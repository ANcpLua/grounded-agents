// Copyright (c) ANcpLua. Licensed under the MIT License.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace ANcpLua.Agents.Evaluation;

/// <summary>
/// A fluent, fail-closed evaluation suite that composes deterministic local checks, LLM-judged quality,
/// and any other <see cref="IAgentEvaluator"/> stage into a single agent run with a single gate.
/// </summary>
/// <remarks>
/// Every scorer here is delegated to a shipped library — <see cref="LocalEvaluator"/>/<see cref="EvalChecks"/>
/// for offline checks and an MEAI <see cref="IEvaluator"/> for judged quality. The suite adds only what no
/// single library provides: one composition over one run, and a gate that cannot silently pass (see
/// <see cref="SuiteReport.Succeeded"/>).
/// <para>
/// Server-side stages that need a preview dependency (for example Azure AI Foundry) are not referenced
/// here; plug them in through <see cref="Stage"/> so this package's dependency closure stays stable.
/// </para>
/// </remarks>
public sealed class EvaluationSuite
{
    private readonly string _name;
    private readonly List<EvalCheck> _localChecks = [];
    private readonly HashSet<string> _localCheckNames = new(StringComparer.Ordinal);
    private readonly List<QualityEvaluator> _quality = [];
    private readonly List<(string Stage, IAgentEvaluator Evaluator)> _extraStages = [];

    private AIAgent? _agent;
    private string[] _queries = [];
    private EvalItem[]? _items;
    private string[]? _expectedOutputs;
    private ExpectedToolCall[][]? _expectedToolCalls;
    private IConversationSplitter? _splitter;
    private int _repetitions = 1;

    private EvaluationSuite(string name) => this._name = name;

    /// <summary>Starts a new suite.</summary>
    /// <param name="name">A display name used in the report and gate messages.</param>
    public static EvaluationSuite Create(string name) => new(name);

    // ---- source: choose exactly one ----

    /// <summary>Runs the agent against the queries once, then scores every stage over that run.</summary>
    public EvaluationSuite Agent(AIAgent agent, params string[] queries)
    {
        this._agent = agent ?? throw new ArgumentNullException(nameof(agent));
        this._queries = queries;
        return this;
    }

    /// <summary>Scores pre-built items directly, without invoking an agent (no credentials required).</summary>
    public EvaluationSuite Items(params EvalItem[] items)
    {
        this._items = items;
        return this;
    }

    // ---- agent-mode options ----

    /// <summary>Stamps ground-truth expected outputs onto each query's item (agent mode), one per query.</summary>
    public EvaluationSuite Expecting(params string[] expectedOutputs)
    {
        this._expectedOutputs = expectedOutputs;
        return this;
    }

    /// <summary>
    /// Stamps per-query expected tool calls onto each query's item (agent mode), one array per query.
    /// Pair with <see cref="Check"/>(<see cref="EvalChecks.ToolCallArgsMatch"/>) to assert them.
    /// </summary>
    public EvaluationSuite ExpectingToolCalls(params ExpectedToolCall[][] expectedToolCallsPerQuery)
    {
        this._expectedToolCalls = expectedToolCallsPerQuery;
        return this;
    }

    /// <summary>Applies a conversation splitter to every item (agent mode).</summary>
    public EvaluationSuite SplitBy(IConversationSplitter splitter)
    {
        this._splitter = splitter;
        return this;
    }

    /// <summary>Runs each query <paramref name="times"/> times to measure consistency (agent mode).</summary>
    public EvaluationSuite Repeat(int times)
    {
        if (times < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(times), times, "Repeat count must be >= 1.");
        }

        this._repetitions = times;
        return this;
    }

    // ---- stages ----

    /// <summary>Adds one or more deterministic local checks (for example <see cref="EvalChecks.NonEmpty"/>).</summary>
    public EvaluationSuite Check(params EvalCheck[] checks)
    {
        foreach (var check in checks)
        {
            ArgumentNullException.ThrowIfNull(check);
            this.AddLocalCheck(check, name: null);
        }

        return this;
    }

    /// <summary>Adds a named custom check over the item.</summary>
    /// <remarks>
    /// The predicate may return a bare <see cref="bool"/> or a <c>(bool Passed, string Reason)</c> tuple;
    /// both convert to <see cref="CheckOutcome"/>. Prefer the tuple — every built-in check reports a
    /// reason, and a bare <see langword="false"/> is the one verdict in the report that cannot say why.
    /// </remarks>
    /// <param name="name">The check name, shown in the report. Must be unique within the suite.</param>
    /// <param name="predicate">Returns the verdict, and optionally the reason shown in the report.</param>
    public EvaluationSuite Custom(string name, Func<EvalItem, CheckOutcome> predicate)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(predicate);
        return this.AddLocalCheck(
            item =>
            {
                var outcome = predicate(item);
                string reason = string.IsNullOrWhiteSpace(outcome.Reason)
                    ? outcome.Passed ? "Passed" : "Failed"
                    : outcome.Reason;
                return new EvalCheckResult(outcome.Passed, reason, name);
            },
            name);
    }

    /// <summary>Adds a fail-closed tool-call expectation.</summary>
    public EvaluationSuite ExpectTools(ToolExpectation expectation)
    {
        ArgumentNullException.ThrowIfNull(expectation);
        return this.AddLocalCheck(expectation.ToCheck(), name: null);
    }

    /// <summary>Adds an LLM-judged quality stage that fails closed below <paramref name="minScore"/>.</summary>
    public EvaluationSuite Quality(IEvaluator evaluator, ChatConfiguration judge, double minScore)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(judge);
        this._quality.Add(new QualityEvaluator(evaluator, judge, minScore));
        return this;
    }

    /// <summary>
    /// Adds an arbitrary <see cref="IAgentEvaluator"/> as a named stage over the same single run.
    /// </summary>
    /// <remarks>
    /// This is the extension seam for evaluators this package does not depend on — server-side providers
    /// such as Azure AI Foundry, or your own batch evaluator. The stage participates in the gate exactly
    /// like the built-in ones: it must run, and every item it scores must positively pass.
    /// </remarks>
    /// <param name="name">The stage name shown in the report.</param>
    /// <param name="evaluator">The evaluator to run over the suite's items.</param>
    public EvaluationSuite Stage(string name, IAgentEvaluator evaluator)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentNullException.ThrowIfNull(evaluator);
        this._extraStages.Add((name, evaluator));
        return this;
    }

    // ---- run + gate ----

    /// <summary>
    /// Runs every stage, prints the breakdown, and returns the process exit code — <c>0</c> only when the
    /// run <see cref="SuiteReport.Succeeded"/>. Prefer this over <see cref="RunAsync"/>: it makes the
    /// verdict the return value, so a failing run cannot be computed and then silently dropped.
    /// </summary>
    /// <remarks>
    /// This method does not throw for a misconfigured suite either. A builder mistake that
    /// <see cref="RunAsync"/> reports as an exception is printed here and returned as exit code <c>1</c>,
    /// so the gate is the only exit path. Cancellation still propagates: a cancelled run has no verdict.
    /// </remarks>
    /// <param name="writer">Where to write the breakdown; defaults to <see cref="Console.Out"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<int> GateAsync(TextWriter? writer = null, CancellationToken cancellationToken = default)
    {
        SuiteReport report;
        try
        {
            report = await this.RunAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // A suite that could not even be assembled is a red gate, not a crash. Printing the
            // reason and returning 1 keeps the promise the whole type makes: the verdict IS the
            // return value. RunAsync still throws, for callers who want the exception.
            (writer ?? Console.Out).WriteLine(
                $"=== {this._name} ==={Environment.NewLine}  [SUITE NOT RUN] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}  -> {this._name}: the gate fails.{Environment.NewLine}");
            return 1;
        }

        report.Print(writer);
        return report.ToExitCode();
    }

    /// <summary>
    /// Executes every configured stage over a single run and returns the fail-closed report for
    /// inspection. Most callers want <see cref="GateAsync"/> instead — a bare report is a verdict you can
    /// forget to act on.
    /// </summary>
    public async Task<SuiteReport> RunAsync(CancellationToken cancellationToken = default)
    {
        this.Validate();

        var providers = this.BuildProviders();
        if (providers.Count == 0)
        {
            throw new InvalidOperationException(
                $"Suite '{this._name}' has no evaluators. Add Check, Custom, ExpectTools, Quality, or Stage.");
        }

        var stages = this._agent is not null
            ? await this.RunAgentModeAsync(providers, cancellationToken).ConfigureAwait(false)
            : await this.RunItemsModeAsync(providers, cancellationToken).ConfigureAwait(false);

        return new SuiteReport(this._name, stages);
    }

    /// <summary>
    /// Registers a local check behind a fault-isolation guard and rejects a duplicate name.
    /// </summary>
    /// <remarks>
    /// The guard is the difference between one bad predicate costing one item and costing the whole
    /// stage. <see cref="LocalEvaluator"/> invokes checks directly, so an exception out of a check
    /// aborts the batch — every sibling item's verdict, including the real failures you needed to see,
    /// is lost and the stage is reported only as "did not run". Converting the throw into a per-item
    /// failed <see cref="EvalCheckResult"/> keeps the run fail-closed <em>and</em> legible.
    /// </remarks>
    private EvaluationSuite AddLocalCheck(EvalCheck check, string? name)
    {
        if (name is not null && !this._localCheckNames.Add(name))
        {
            throw new InvalidOperationException(
                $"Suite '{this._name}': duplicate check name '{name}'. Names key the report's metrics, so the " +
                "second registration would silently replace the first. Give each check a distinct name.");
        }

        string fallbackName = name ?? $"check_{this._localChecks.Count + 1}";
        this._localChecks.Add(item =>
        {
            try
            {
                return check(item);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new EvalCheckResult(false, $"check threw {ex.GetType().Name}: {ex.Message}", fallbackName);
            }
        });

        return this;
    }

    /// <summary>
    /// Rejects builder combinations that would otherwise do nothing silently: two sources, no source, or
    /// an agent-only option set in items mode. A no-op you can write is a landmine; this makes it loud.
    /// </summary>
    private void Validate()
    {
        if (this._agent is not null && this._items is not null)
        {
            throw new InvalidOperationException($"Suite '{this._name}': call either Agent(...) or Items(...), not both.");
        }

        if (this._agent is null && this._items is null)
        {
            throw new InvalidOperationException($"Suite '{this._name}': no source. Call Agent(...) or Items(...).");
        }

        if (this._items is null)
        {
            return;
        }

        var agentOnly = new List<string>();
        if (this._expectedOutputs is not null) { agentOnly.Add(nameof(this.Expecting)); }
        if (this._expectedToolCalls is not null) { agentOnly.Add(nameof(this.ExpectingToolCalls)); }
        if (this._splitter is not null) { agentOnly.Add(nameof(this.SplitBy)); }
        if (this._repetitions != 1) { agentOnly.Add(nameof(this.Repeat)); }

        if (agentOnly.Count > 0)
        {
            throw new InvalidOperationException(
                $"Suite '{this._name}': {string.Join("/", agentOnly)} {(agentOnly.Count == 1 ? "applies" : "apply")} only to Agent(...) mode " +
                $"and would be ignored with Items(...). Build the EvalItems the way you need them — stamp ExpectedOutput/ExpectedToolCalls/Splitter " +
                "on each item, or repeat them yourself — or switch to Agent(...).");
        }
    }

    private List<(string Stage, IAgentEvaluator Evaluator)> BuildProviders()
    {
        var providers = new List<(string Stage, IAgentEvaluator Evaluator)>();

        if (this._localChecks.Count > 0)
        {
            providers.Add(("local", new LocalEvaluator([.. this._localChecks])));
        }

        foreach (var quality in this._quality)
        {
            providers.Add((quality.Name, quality));
        }

        providers.AddRange(this._extraStages);

        return providers;
    }

    private async Task<List<StageReport>> RunAgentModeAsync(
        List<(string Stage, IAgentEvaluator Evaluator)> providers,
        CancellationToken cancellationToken)
    {
        if (this._queries.Length == 0)
        {
            throw new InvalidOperationException($"Suite '{this._name}' was given an agent but no queries.");
        }

        // Bootstrap: run the agent exactly ONCE through the framework with a zero-check
        // LocalEvaluator and harvest the framework-built items from InputItems — tools
        // extraction, splitter/expected-output/expected-tool-call stamping, and repetitions
        // all stay delegated to the engine. The bootstrap's per-item results are empty by
        // construction and deliberately discarded; the real providers are then scored
        // per stage below so one failing stage cannot destroy its siblings' verdicts.
        var bootstrap = await this._agent!.EvaluateAsync(
            this._queries,
            new LocalEvaluator(),
            this._name,
            expectedOutput: this._expectedOutputs,
            expectedToolCalls: this._expectedToolCalls,
            splitter: this._splitter,
            numRepetitions: this._repetitions,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var items = bootstrap.InputItems
            ?? throw new InvalidOperationException($"Suite '{this._name}': the engine returned no input items for the run.");

        return await this.RunStagesAsync(providers, items, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<StageReport>> RunItemsModeAsync(
        List<(string Stage, IAgentEvaluator Evaluator)> providers,
        CancellationToken cancellationToken)
    {
        if (this._items is not { Length: > 0 } items)
        {
            throw new InvalidOperationException($"Suite '{this._name}' was given an empty item set.");
        }

        return await this.RunStagesAsync(providers, items, cancellationToken).ConfigureAwait(false);
    }

    private async Task<List<StageReport>> RunStagesAsync(
        List<(string Stage, IAgentEvaluator Evaluator)> providers,
        IReadOnlyList<EvalItem> items,
        CancellationToken cancellationToken)
    {
        var stages = new List<StageReport>(providers.Count);
        foreach (var (stage, evaluator) in providers)
        {
            try
            {
                var result = await evaluator.EvaluateAsync(items, this._name, cancellationToken).ConfigureAwait(false);
                stages.Add(new StageReport(stage, Ran: true, result));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // The caller cancelled the suite; that is not a stage failure. (An HTTP
                       // timeout also surfaces as OperationCanceledException but with our token
                       // un-cancelled — that one falls through and is recorded below.)
            }
            catch (Exception ex)
            {
                // Deliberate fault-isolation boundary, not error hiding: whatever a provider
                // throws (auth, network, server-side failure) is recorded loudly — the short
                // reason lands in the Summary/AssertOrThrow message, the full detail prints once
                // under [STAGE NOT RUN], and the gate is forced to 1 — while the sibling stages'
                // verdicts survive. Suite misconfiguration still throws before this loop.
                stages.Add(new StageReport(
                    stage,
                    Ran: false,
                    Result: null,
                    FailureReason: $"{ex.GetType().Name}: {ex.Message}",
                    FailureDetail: ex.ToString()));
            }
        }

        return stages;
    }
}
