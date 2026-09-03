// Copyright (c) Microsoft. All rights reserved.

// Retail-ops multi-agent workflow over Microsoft Foundry:
//   product-expert (Foundry-managed, Foundry IQ knowledge base)
//     -> inventory-analyst (local stdio inventory MCP tools)
//       -> consolidator (pure synthesis over grounded facts)
//
// The boundary types make the illegal states unrepresentable:
// unsettled responses have no readable text, grounded values cannot exist without
// tool-call evidence, and consolidation cannot be invoked with unproven inputs.

using System.Diagnostics.CodeAnalysis;
using Azure.AI.Projects;
using Azure.Identity;
using HostedFoundryWorkflow;

if (args is ["--server"])
{
    await InventoryMcpServer.RunAsync();
    return 0;
}

if (args is ["selftest"])
{
    return Selftest.Run();
}

using var tracing = WorkflowTelemetry.Create();

FoundryBoundary boundary = FoundryBoundary.FromEnvironment();

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential such as ManagedIdentityCredential.
var projectClient = new AIProjectClient(boundary.ProjectEndpoint, new DefaultAzureCredential());

if (args is ["eval"])
{
    return await EvalGate.RunAsync(projectClient, boundary);
}

var query = new CustomerQuery(args is [var text, ..]
    ? string.Join(' ', args)
    : "I need something for dry skin and sun protection for a hiking weekend. What should I buy, and is it in stock?");

Console.WriteLine($"Query: {query.Text}");
Console.WriteLine();

WorkflowOutcome outcome = await RetailOpsWorkflow.RunAsync(projectClient, boundary, query);

switch (outcome)
{
    case WorkflowOutcome.Delivered delivered:
        Console.WriteLine("=== Consolidated recommendation ===");
        Console.WriteLine(delivered.Recommendation.Value);
        Console.WriteLine();
        Console.WriteLine("Evidence:");
        foreach (ToolCallEvidence evidence in delivered.Recommendation.Evidence)
        {
            Console.WriteLine($"  - {(evidence.McpServer is null ? evidence.Tool : $"{evidence.McpServer}/{evidence.Tool}")}");
        }

        return 0;

    case WorkflowOutcome.Stopped stopped:
        Console.WriteLine($"Workflow stopped at {stopped.Stage}: {stopped.Reason}");
        return 1;

    default:
        throw new InvalidOperationException("Unreachable: WorkflowOutcome union is closed.");
}

/// <summary>The synthesized top-level entry point; process composition, not unit-testable logic.</summary>
[ExcludeFromCodeCoverage(Justification = "Process composition: credentials, network and console I/O.")]
internal sealed partial class Program;

/// <summary>
/// Offline proof that the boundary guards fire. No credentials, no network:
/// every guard that must reject an illegal state is driven into that state.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Console entry point; the same guards are covered by the test project.")]
internal static class Selftest
{
    public static int Run()
    {
        int failures = 0;

        Check(ref failures, "empty query => throws",
            () => Throws(() => _ = new CustomerQuery("   ")));

        Check(ref failures, "empty deployment name => throws",
            () => Throws(() => _ = new ModelDeploymentName("")));

        Check(ref failures, "missing environment => throws, no half-configured boundary",
            () =>
            {
                string? saved = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
                Environment.SetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT", null);
                try
                {
                    return Throws(() => _ = FoundryBoundary.FromEnvironment());
                }
                finally
                {
                    Environment.SetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT", saved);
                }
            });

        Check(ref failures, "grounded value without evidence => not constructible",
            () => Throws(() => _ = new Grounded<string>(
                "answer", [], new SettledResponse("answer", [], [], []))));

        Check(ref failures, "AllOf requirement with partial evidence => Ungrounded",
            () =>
            {
                var settled = new SettledResponse(
                    "answer",
                    [new ToolCallEvidence("get_inventory_levels", null)],
                    [],
                    []);

                StageOutcome<string> outcome = Grounding.Require(
                    settled,
                    ToolRequirement.AllTools("get_inventory_levels", "get_weekly_sales"),
                    response => response.Text);

                return outcome is StageOutcome<string>.Ungrounded;
            });

        Check(ref failures, "AnyMcp requirement with only local tools => Ungrounded",
            () =>
            {
                var settled = new SettledResponse(
                    "answer",
                    [new ToolCallEvidence("some_local_function", null)],
                    [],
                    []);

                return Grounding.Require(settled, ToolRequirement.AnyMcp(), response => response.Text)
                    is StageOutcome<string>.Ungrounded;
            });

        Check(ref failures, "satisfied requirement => Success carries the evidence",
            () =>
            {
                var settled = new SettledResponse(
                    "answer",
                    [new ToolCallEvidence("search", "foundry-iq")],
                    [],
                    []);

                return Grounding.Require(settled, ToolRequirement.AnyMcp(), response => response.Text)
                    is StageOutcome<string>.Success { Result.Evidence.Count: 1 };
            });

        Check(ref failures, "allow-list policy denies unlisted server by default",
            () => ApprovalPolicy.AllowServers("api-specs")
                .Decide("evil-server", "exfiltrate", isMcp: true) is { Approved: false });

        Check(ref failures, "allow-list policy approves listed server",
            () => ApprovalPolicy.AllowServers("api-specs")
                .Decide("api-specs", "microsoft_docs_search", isMcp: true) is { Approved: true });

        Check(ref failures, "allow-all-mcp policy still denies non-MCP calls",
            () => ApprovalPolicy.AllowAllMcp("trusted knowledge tool")
                .Decide("<non-mcp>", "anything", isMcp: false) is { Approved: false });

        Check(ref failures, "deny-all policy denies everything",
            () => ApprovalPolicy.DenyAll("no tools attached")
                .Decide("api-specs", "microsoft_docs_search", isMcp: true) is { Approved: false });

        Check(ref failures, "pending approval => response is not settled",
            () =>
            {
                var pending = new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.Assistant,
                    [new Microsoft.Extensions.AI.ToolApprovalRequestContent(
                        "approval-1",
                        new Microsoft.Extensions.AI.McpServerToolCallContent("call-1", "search", "foundry-iq"))]);

                return Settlement.Classify("partial", [pending], []) is Settlement.RequiresApproval;
            });

        Check(ref failures, "no pending content => settles with extracted evidence",
            () =>
            {
                var completed = new Microsoft.Extensions.AI.ChatMessage(
                    Microsoft.Extensions.AI.ChatRole.Assistant,
                    [new Microsoft.Extensions.AI.McpServerToolCallContent("call-1", "search", "foundry-iq")]);

                return Settlement.Classify("done", [completed], [])
                    is Settlement.Settled { Response.Evidence: [{ Tool: "search", McpServer: "foundry-iq" }] };
            });

        Check(ref failures, "full policy path denies unlisted server on real content",
            () =>
            {
                var request = new Microsoft.Extensions.AI.ToolApprovalRequestContent(
                    "approval-2",
                    new Microsoft.Extensions.AI.McpServerToolCallContent("call-2", "exfiltrate", "evil-server"));

                return ApprovalPolicy.AllowServers("api-specs").Decide(request) is { Approved: false };
            });

        Console.WriteLine();
        Console.WriteLine(failures == 0
            ? "selftest: all boundary guards fired correctly."
            : $"selftest: {failures} guard(s) did NOT fire.");

        return failures == 0 ? 0 : 1;
    }

    private static void Check(ref int failures, string name, Func<bool> guard)
    {
        bool passed;
        try
        {
            passed = guard();
        }
        catch
        {
            passed = false;
        }

        Console.WriteLine($"  [{(passed ? "OK" : "FAIL")}] {name}");
        if (!passed)
        {
            failures++;
        }
    }

    private static bool Throws(Action action)
    {
        try
        {
            action();
            return false;
        }
        catch (ArgumentException)
        {
            return true;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
