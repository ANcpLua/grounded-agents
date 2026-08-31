// Copyright (c) ANcpLua. Licensed under the MIT License.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace ANcpLua.Agents.Evaluation;

/// <summary>
/// Bridges a Microsoft.Extensions.AI.Evaluation <see cref="IEvaluator"/> (for example
/// <c>RelevanceEvaluator</c> or <c>CoherenceEvaluator</c>) onto the Agent Framework's
/// <see cref="IAgentEvaluator"/> seam — and, unlike the framework's own bridge, turns every numeric
/// score into a hard pass/fail against the caller's <c>minScore</c>.
/// </summary>
/// <remarks>
/// <para>
/// The framework already ships this bridge as <c>MeaiEvaluatorAdapter</c>, but that type is
/// <see langword="internal"/>, so a consumer cannot construct it. Re-expressing its ~10-line core here
/// is therefore the only public path to mixing an MEAI evaluator into a single multi-evaluator run.
/// </para>
/// <para>
/// The reason it is worth re-expressing rather than calling the dedicated
/// <c>AIAgent.EvaluateAsync(queries, IEvaluator, ChatConfiguration, …)</c> overload is fail-closed
/// scoring. The bundled quality evaluators do interpret their own scores (failing below a hardcoded
/// 4.0), but their interpretation marks an <em>unparseable</em> (null) score as not-failed — an
/// un-scored pass. <see cref="EvaluateAsync"/> therefore re-derives a definite
/// <see cref="EvaluationMetricInterpretation.Failed"/> on every metric: a numeric score must exist and
/// reach the caller's <c>minScore</c> (making the bar configurable), a boolean verdict must be
/// <see langword="true"/> regardless of any pre-existing interpretation, and any metric type this
/// evaluator cannot threshold fails closed.
/// </para>
/// </remarks>
public sealed class QualityEvaluator : IAgentEvaluator
{
    private readonly IEvaluator _evaluator;
    private readonly ChatConfiguration _judge;
    private readonly double _minScore;

    /// <summary>Initializes a new instance of the <see cref="QualityEvaluator"/> class.</summary>
    /// <param name="evaluator">The MEAI evaluator to run (the scorer).</param>
    /// <param name="judge">The judge model the evaluator uses to score responses.</param>
    /// <param name="minScore">The inclusive minimum score an item must reach to pass.</param>
    public QualityEvaluator(IEvaluator evaluator, ChatConfiguration judge, double minScore)
    {
        ArgumentNullException.ThrowIfNull(evaluator);
        ArgumentNullException.ThrowIfNull(judge);
        this._evaluator = evaluator;
        this._judge = judge;
        this._minScore = minScore;
    }

    /// <inheritdoc />
    public string Name => this._evaluator.GetType().Name;

    /// <inheritdoc />
    public async Task<AgentEvaluationResults> EvaluateAsync(
        IReadOnlyList<EvalItem> items,
        string evalName = "Quality Eval",
        CancellationToken cancellationToken = default)
    {
        var results = new List<EvaluationResult>(items.Count);

        foreach (var item in items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Mirror the framework's internal adapter: score the query messages against the captured
            // response (or a synthesized one when evaluating bare query/response items).
            var (queryMessages, _) = item.Split();
            var response = item.RawResponse
                ?? new ChatResponse(new ChatMessage(ChatRole.Assistant, item.Response));

            var result = await this._evaluator.EvaluateAsync(
                queryMessages,
                response,
                this._judge,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            ApplyThreshold(result, this._minScore);
            results.Add(result);
        }

        return new AgentEvaluationResults(this.Name, results, inputItems: items);
    }

    /// <summary>
    /// Stamps a definite pass/fail interpretation onto every metric so the gate can never read an
    /// un-scored — or un-thresholdable — metric as a pass.
    /// </summary>
    private static void ApplyThreshold(EvaluationResult result, double minScore)
    {
        foreach (var metric in result.Metrics.Values)
        {
            bool failed = metric switch
            {
                // The score must exist and clear the bar. The bundled evaluators' own
                // interpretation marks an unparseable (null) score as not-failed; here a null
                // score always fails, at any threshold.
                NumericMetric numeric => numeric.Value is not double value || value < minScore,

                // A boolean verdict is re-derived from its own value — a pre-existing
                // interpretation is never trusted over the value itself.
                BooleanMetric boolean => boolean.Value != true,

                // minScore cannot evaluate any other metric type; it fails closed. Gate
                // string/categorical verdicts with Check/Custom or a dedicated evaluator.
                _ => true,
            };

            metric.Interpretation = new EvaluationMetricInterpretation
            {
                Rating = failed ? EvaluationRating.Unacceptable : EvaluationRating.Good,
                Failed = failed,
            };
        }
    }
}
