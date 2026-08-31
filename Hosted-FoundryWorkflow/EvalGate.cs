// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using ANcpLua.Agents.Evaluation;
using Azure.AI.Projects;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;

namespace HostedFoundryWorkflow;

/// <summary>
/// The fail-closed evaluation gate over the workflow's stage agents. The suite expectations
/// are the same invariants the runtime boundary enforces: the inventory gate's
/// <c>ToolExpectation.AllOf</c> is literally the <see cref="InventoryAnalyst"/>'s
/// <c>ToolRequirement.AllTools</c>, and the product-expert gate demands positive tool-call
/// evidence just like <c>ToolRequirement.AnyMcp</c>. What the types prevent at runtime,
/// the gate proves on a dataset — non-zero exit unless every stage ran and passed.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Runs the evaluation suites against live agents.")]
public static class EvalGate
{
    public static async Task<int> RunAsync(AIProjectClient projectClient, FoundryBoundary boundary)
    {
        bool withFoundry = Environment.GetEnvironmentVariable("AI_EVAL_FOUNDRY") is "1" or "true";

        Console.WriteLine("=== product-expert gate ===");
        ProductExpert expert = await ProductExpert.ConnectAsync(projectClient, boundary.ProductExpert);

        EvaluationSuite expertSuite = EvaluationSuite.Create("product-expert-gate")
            .Agent(
                expert.Agent,
                "What types of tents does Contoso offer?",
                "Tell me about which backpacks are available in XL.")
            .Check(EvalChecks.NonEmpty())
            // Mirrors ToolRequirement.AnyMcp: red unless the knowledge tool was really called.
            .ExpectTools(ToolExpectation.Present());

        if (withFoundry)
        {
            expertSuite = expertSuite.Foundry(
                projectClient, boundary.Deployment.Value, FoundryEvals.Relevance, FoundryEvals.Groundedness);
        }

        int expertExit = await expertSuite.GateAsync();

        Console.WriteLine();
        Console.WriteLine("=== inventory-analyst gate ===");
        await using InventoryAnalyst analyst =
            await InventoryAnalyst.ConnectAsync(projectClient, boundary.Deployment);

        EvaluationSuite analystSuite = EvaluationSuite.Create("inventory-analyst-gate")
            .Agent(
                analyst.Agent,
                "Which products should we restock this week?",
                "Is anything a candidate for clearance?")
            .Check(EvalChecks.NonEmpty())
            // Mirrors ToolRequirement.AllTools: both retrievals must be positively evidenced.
            .ExpectTools(ToolExpectation.AllOf("get_inventory_levels", "get_weekly_sales"));

        if (withFoundry)
        {
            analystSuite = analystSuite.Foundry(
                projectClient, boundary.Deployment.Value, FoundryEvals.Relevance, FoundryEvals.Coherence);
        }

        int analystExit = await analystSuite.GateAsync();

        Console.WriteLine();
        Console.WriteLine(expertExit == 0 && analystExit == 0
            ? "eval gate: green — every stage ran and every item passed."
            : "eval gate: RED — see the breakdown above.");

        return expertExit == 0 && analystExit == 0 ? 0 : 1;
    }
}
