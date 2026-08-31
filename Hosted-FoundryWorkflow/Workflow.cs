// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Azure.AI.Projects;

namespace HostedFoundryWorkflow;

/// <summary>Closed outcome union for the whole workflow run.</summary>
public abstract record WorkflowOutcome
{
    private WorkflowOutcome() { }

    public sealed record Delivered(Grounded<string> Recommendation) : WorkflowOutcome;

    public sealed record Stopped(string Stage, string Reason) : WorkflowOutcome;
}

/// <summary>
/// The retail-ops pipeline. The stage order is not a convention — it is the only order the
/// types allow: AssessAsync needs a Grounded&lt;ProductAdvice&gt;, ConsolidateAsync needs both
/// grounded values, and grounded values only fall out of settled, evidence-checked responses.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Requires a live Foundry project client.")]
public static class RetailOpsWorkflow
{
    public static async Task<WorkflowOutcome> RunAsync(AIProjectClient projectClient, FoundryBoundary boundary, CustomerQuery query)
    {
        using Activity? root = WorkflowTelemetry.Source.StartActivity("retail-ops.workflow");
        root?.SetTag("workflow.query", query.Text);

        Grounded<ProductAdvice> advice;
        using (Activity? span = WorkflowTelemetry.Source.StartActivity("retail-ops.product-expert"))
        {
            ProductExpert expert = await ProductExpert.ConnectAsync(projectClient, boundary.ProductExpert);
            StageOutcome<ProductAdvice> outcome = await expert.AdviseAsync(query);
            span.RecordOutcome(outcome);

            if (outcome is not StageOutcome<ProductAdvice>.Success adviceSuccess)
            {
                return Stop(root, "product-expert", outcome);
            }

            advice = adviceSuccess.Result;
        }

        Grounded<InventoryFacts> inventory;
        using (Activity? span = WorkflowTelemetry.Source.StartActivity("retail-ops.inventory-analyst"))
        {
            await using InventoryAnalyst analyst =
                await InventoryAnalyst.ConnectAsync(projectClient, boundary.Deployment);
            StageOutcome<InventoryFacts> outcome = await analyst.AssessAsync(advice);
            span.RecordOutcome(outcome);

            if (outcome is not StageOutcome<InventoryFacts>.Success inventorySuccess)
            {
                return Stop(root, "inventory-analyst", outcome);
            }

            inventory = inventorySuccess.Result;
        }

        using (Activity? span = WorkflowTelemetry.Source.StartActivity("retail-ops.consolidator"))
        {
            Consolidator consolidator = Consolidator.Create(projectClient, boundary.Deployment);
            StageOutcome<string> outcome = await consolidator.ConsolidateAsync(advice, inventory);
            span.RecordOutcome(outcome);

            if (outcome is not StageOutcome<string>.Success consolidated)
            {
                return Stop(root, "consolidator", outcome);
            }

            root?.SetTag("workflow.verdict", "delivered");
            return new WorkflowOutcome.Delivered(consolidated.Result);
        }
    }

    private static WorkflowOutcome.Stopped Stop<T>(Activity? root, string stage, StageOutcome<T> outcome)
    {
        string reason = outcome switch
        {
            StageOutcome<T>.Ungrounded ungrounded => $"answer lacked required evidence ({ungrounded.Missing})",
            StageOutcome<T>.Failed failed => failed.Reason,
            _ => "unexpected stage outcome",
        };

        root?.SetTag("workflow.verdict", "stopped");
        root?.SetStatus(ActivityStatusCode.Error, $"{stage}: {reason}");
        return new WorkflowOutcome.Stopped(stage, reason);
    }
}
