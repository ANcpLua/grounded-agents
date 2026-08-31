// Copyright (c) ANcpLua. Licensed under the MIT License.

using Azure.AI.Projects;
using Microsoft.Agents.AI.Foundry;

namespace ANcpLua.Agents.Evaluation;

/// <summary>
/// Adds an Azure AI Foundry server-side evaluation stage to a suite.
/// </summary>
/// <remarks>
/// This lives in the scaffolded project rather than in the <c>ANcpLua.Agents.Evaluation</c> package on
/// purpose. <c>Microsoft.Agents.AI.Foundry</c> ships on a preview channel and drags
/// <c>Azure.AI.Projects</c> with it; a stable package that referenced it would push that closure onto
/// every consumer, including the ones who only ever run offline checks. <c>EvaluationSuite.Stage</c> is
/// the seam that keeps the dependency here, where the project already opted into it.
/// <para>
/// This file is template-local and is <em>not</em> covered by <c>eng/sync-evaluation-sources.sh</c>.
/// </para>
/// </remarks>
public static class FoundryStage
{
    /// <summary>Adds a Foundry server-side evaluation stage over the suite's single run.</summary>
    /// <param name="suite">The suite to extend.</param>
    /// <param name="client">The Foundry project client.</param>
    /// <param name="model">The judge model deployment name.</param>
    /// <param name="evaluators">The builtin evaluator names, for example <c>FoundryEvals.Relevance</c>.</param>
    public static EvaluationSuite Foundry(
        this EvaluationSuite suite,
        AIProjectClient client,
        string model,
        params string[] evaluators)
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrEmpty(model);
        return suite.Stage("foundry", new FoundryEvals(client, model, evaluators));
    }
}
