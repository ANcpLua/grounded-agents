// Copyright (c) ANcpLua. Licensed under the MIT License.

using System.Text;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace ANcpLua.Agents.Evaluation;

/// <summary>The fail-closed verdict for a single evaluated item.</summary>
public enum ItemStatus
{
    /// <summary>Every metric carried a determination and none failed.</summary>
    Pass,

    /// <summary>At least one metric positively reported failure.</summary>
    Fail,

    /// <summary>
    /// The engine returned no usable determination — an empty result, an un-interpreted or null
    /// numeric score, an empty response, or a provider error. Indeterminate is never a pass.
    /// </summary>
    Error,
}

/// <summary>One provider's contribution to a suite run.</summary>
/// <param name="Stage">Stage name (for example <c>local</c>, <c>RelevanceEvaluator</c>, <c>foundry</c>).</param>
/// <param name="Ran">
/// <see langword="false"/> when the stage was requested but could not execute (it threw — bad
/// credentials, network, timeout, a server-side failure). A requested-but-unran stage fails the gate;
/// it is never skipped silently, and the sibling stages' verdicts are still reported.
/// </param>
/// <param name="Result">The native results, or <see langword="null"/> when <paramref name="Ran"/> is false.</param>
/// <param name="FailureReason">
/// A one-line reason the stage did not run (exception type and message), or <see langword="null"/> when
/// it ran. This is what appears in <see cref="SuiteReport.AssertOrThrow"/> and the run summary.
/// </param>
/// <param name="FailureDetail">
/// The full diagnostic for the failure — stack trace and inner exceptions. Printed once, under the
/// stage's <c>[STAGE NOT RUN]</c> line, and deliberately kept out of the one-line summary.
/// </param>
public sealed record StageReport(
    string Stage,
    bool Ran,
    AgentEvaluationResults? Result,
    string? FailureReason = null,
    string? FailureDetail = null);

/// <summary>
/// Fail-closed aggregate over every stage of an <see cref="EvaluationSuite"/> run.
/// </summary>
/// <remarks>
/// This report deliberately does not trust the engine's own <see cref="AgentEvaluationResults.Passed"/>
/// count, which reads a numeric metric without an <see cref="EvaluationMetricInterpretation"/> as a pass.
/// It re-derives every verdict with the inverse rule — <em>a pass requires a positive, non-failed
/// determination on every metric</em> — so an un-scored, empty, or errored item can never count as a
/// pass, and <see cref="Succeeded"/> is true only when there is real, green evidence for every item.
/// </remarks>
public sealed class SuiteReport
{
    private readonly record struct ItemLine(int StageIndex, ItemStatus Status, string Query, EvaluationResult Result);

    private readonly List<StageReport> _stages;
    private readonly List<ItemLine> _lines = [];

    internal SuiteReport(string name, IEnumerable<StageReport> stages)
    {
        this.Name = name;
        this._stages = stages.ToList();

        // Lines are keyed by stage INDEX, not name — two stages may share a display name
        // (for example the same evaluator type at two thresholds).
        for (int s = 0; s < this._stages.Count; s++)
        {
            if (this._stages[s].Result is not { } result)
            {
                continue;
            }

            for (int i = 0; i < result.Items.Count; i++)
            {
                EvalItem? input = result.InputItems is { } inputs && i < inputs.Count ? inputs[i] : null;
                EvalItemResult? detail = result.DetailedItems is { } details && i < details.Count ? details[i] : null;
                this._lines.Add(new ItemLine(s, Classify(result.Items[i], input, detail), input?.Query ?? $"item {i + 1}", result.Items[i]));
            }
        }
    }

    /// <summary>Gets the suite name.</summary>
    public string Name { get; }

    /// <summary>Gets the native, per-stage results for callers that need the raw metrics.</summary>
    public IReadOnlyList<StageReport> Stages => this._stages;

    /// <summary>Gets whether every requested stage actually executed.</summary>
    public bool AllStagesRan => this._stages.All(s => s.Ran);

    /// <summary>Gets the total number of evaluated items across all stages.</summary>
    public int Total => this._lines.Count;

    /// <summary>Gets the number of items that positively passed.</summary>
    public int Passed => this._lines.Count(l => l.Status == ItemStatus.Pass);

    /// <summary>Gets the number of items that positively failed.</summary>
    public int Failed => this._lines.Count(l => l.Status == ItemStatus.Fail);

    /// <summary>Gets the number of items the engine could not determine (treated as not-passed).</summary>
    public int Errored => this._lines.Count(l => l.Status == ItemStatus.Error);

    /// <summary>
    /// Gets whether the run is clean: every requested stage ran, there was at least one item, and every
    /// item positively passed. This is the single source of truth for the gate.
    /// </summary>
    public bool Succeeded => this.AllStagesRan && this.Total > 0 && this.Failed == 0 && this.Errored == 0;

    /// <summary>Returns <c>0</c> only when the run <see cref="Succeeded"/>; otherwise <c>1</c>. Use as a process exit code.</summary>
    public int ToExitCode() => this.Succeeded ? 0 : 1;

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> unless the run <see cref="Succeeded"/>. The message
    /// names every failing item and the metric that failed it.
    /// </summary>
    /// <remarks>
    /// This is the entry point for a test framework, and it is the one path where <see cref="Print"/> is
    /// never called — so the exception message carries the breakdown itself rather than a bare tally that
    /// sends you back to the console output you do not have.
    /// </remarks>
    /// <param name="message">Optional custom failure message, used instead of the generated one.</param>
    public void AssertOrThrow(string? message = null)
    {
        if (!this.Succeeded)
        {
            throw new InvalidOperationException(message ?? this.FailureMessage());
        }
    }

    /// <summary>Writes a human-readable breakdown of every stage, item, and metric.</summary>
    /// <param name="writer">Target writer; defaults to <see cref="Console.Out"/>.</param>
    public void Print(TextWriter? writer = null)
    {
        writer ??= Console.Out;
        writer.WriteLine($"=== {this.Name} ===");

        for (int s = 0; s < this._stages.Count; s++)
        {
            var stage = this._stages[s];
            if (!stage.Ran)
            {
                writer.WriteLine($"  [STAGE NOT RUN] {stage.Stage} — {stage.FailureReason ?? "requested but did not execute"}; the gate fails.");

                // The full diagnostic appears here, once, indented. The summary line below carries
                // only the one-line reason: a stack trace printed twice is noise, not evidence.
                if (stage.FailureDetail is { Length: > 0 } detail)
                {
                    foreach (var detailLine in detail.Split('\n'))
                    {
                        writer.WriteLine($"      {detailLine.TrimEnd('\r')}");
                    }
                }

                continue;
            }

            writer.WriteLine($"  -- {stage.Stage} --");
            foreach (var line in this._lines.Where(l => l.StageIndex == s))
            {
                writer.WriteLine($"    [{line.Status.ToString().ToUpperInvariant()}] {Truncate(line.Query)}");
                foreach (var metric in line.Result.Metrics.Values)
                {
                    writer.WriteLine($"        {FormatMetric(metric)}");
                }
            }
        }

        foreach (var stage in this._stages)
        {
            if (stage.Result?.ReportUrl is { } url)
            {
                writer.WriteLine($"  report ({stage.Stage}): {url}");
            }
        }

        writer.WriteLine($"  -> {this.Summary()}");
        writer.WriteLine();
    }

    private string Summary()
    {
        string core = $"{this.Name}: {this.Passed} passed, {this.Failed} failed, {this.Errored} errored of {this.Total}";
        if (!this.AllStagesRan)
        {
            var unran = this._stages
                .Where(s => !s.Ran)
                .Select(s => s.FailureReason is null ? s.Stage : $"{s.Stage} ({s.FailureReason})");
            core += $"; stages that did not run: {string.Join(", ", unran)}";
        }

        return core;
    }

    /// <summary>
    /// Builds the assertion message: the summary tally, then one line per failing item naming the stage,
    /// the query, and every metric that failed or could not be determined.
    /// </summary>
    private string FailureMessage()
    {
        var builder = new StringBuilder(this.Summary());

        foreach (var line in this._lines.Where(l => l.Status != ItemStatus.Pass))
        {
            string stage = this._stages[line.StageIndex].Stage;
            builder.Append(Environment.NewLine)
                .Append("  [").Append(line.Status.ToString().ToUpperInvariant()).Append("] ")
                .Append(stage).Append(" / ").Append(Truncate(line.Query));

            foreach (var metric in line.Result.Metrics.Values.Where(IsNotGreen))
            {
                builder.Append(Environment.NewLine).Append("      ").Append(FormatMetric(metric));
            }
        }

        foreach (var stage in this._stages.Where(s => !s.Ran))
        {
            builder.Append(Environment.NewLine)
                .Append("  [STAGE NOT RUN] ").Append(stage.Stage)
                .Append(" — ").Append(stage.FailureReason ?? "requested but did not execute");
        }

        return builder.ToString();
    }

    private static bool IsNotGreen(EvaluationMetric metric) =>
        metric.Interpretation is not { Failed: false }
        || metric is BooleanMetric { Value: not true }
        || metric is NumericMetric { Value: null };

    /// <summary>
    /// Classifies one item with the inverted, fail-closed rule: a pass requires a positive non-failed
    /// determination on every metric; anything indeterminate is an error, never a pass.
    /// </summary>
    /// <remarks>
    /// Order matters. A positively-asserted failure outranks every indeterminacy except a provider-level
    /// error, because a check that <em>did</em> render a verdict is evidence and an item that merely
    /// could not be scored is not. In particular an empty response that fails a <c>NonEmpty</c> check is
    /// a real red — reporting it as <see cref="ItemStatus.Error"/> would dress a genuine failure up as a
    /// broken harness.
    /// </remarks>
    private static ItemStatus Classify(EvaluationResult result, EvalItem? input, EvalItemResult? detail)
    {
        // A provider-reported per-item error wins outright: the scoring itself did not complete, so
        // no metric on this item can be trusted, including one that claims failure.
        if (detail is { IsError: true })
        {
            return ItemStatus.Error;
        }

        // An empty or padded result carries no determination.
        if (result.Metrics.Count == 0)
        {
            return ItemStatus.Error;
        }

        bool anyFailed = false;
        bool anyIndeterminate = false;

        foreach (var metric in result.Metrics.Values)
        {
            anyFailed |= metric.Interpretation?.Failed == true;

            switch (metric)
            {
                // A boolean is self-determining: a non-true value fails even when an
                // interpretation claims otherwise. (The engine applies the same dual check;
                // trusting the interpretation alone would make this gate weaker than the engine.)
                case BooleanMetric boolean:
                    anyFailed |= boolean.Value != true;
                    break;

                // A numeric score must exist and carry a determination to pass. A null score is
                // un-scored even when an interpretation says not-failed — exactly what the
                // bundled quality evaluators produce for an unparseable judge reply.
                case NumericMetric numeric:
                    anyIndeterminate |= numeric.Value is null || metric.Interpretation is null;
                    break;

                // Any other metric type has no self-determining value; without an
                // interpretation it is indeterminate.
                default:
                    anyIndeterminate |= metric.Interpretation is null;
                    break;
            }
        }

        if (anyFailed)
        {
            return ItemStatus.Fail;
        }

        // No metric positively failed, so an unusable response means nothing was really scored.
        bool unusableResponse = input is not null && string.IsNullOrWhiteSpace(input.Response);
        return anyIndeterminate || unusableResponse ? ItemStatus.Error : ItemStatus.Pass;
    }

    private static string FormatMetric(EvaluationMetric metric)
    {
        string value = metric switch
        {
            NumericMetric numeric => numeric.Value?.ToString() ?? "none",
            BooleanMetric boolean => boolean.Value?.ToString() ?? "none",
            _ => "—",
        };

        string verdict = metric.Interpretation is { } interpretation
            ? interpretation.Failed == true ? "FAIL" : "pass"
            : "no-determination";

        string reason = string.IsNullOrWhiteSpace(metric.Reason) ? string.Empty : $" — {metric.Reason}";
        return $"{metric.Name}: {value} [{verdict}]{reason}";
    }

    private static string Truncate(string text, int max = 88) =>
        text.Length <= max ? text : text[..max] + "…";
}
