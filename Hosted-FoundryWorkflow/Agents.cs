// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics.CodeAnalysis;
using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Foundry;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;

namespace HostedFoundryWorkflow;

/// <summary>Facts produced by the product-expert stage; only constructible via grounding.</summary>
public sealed record ProductAdvice(CustomerQuery Query, string Advice);

/// <summary>Facts produced by the inventory stage; only constructible via grounding.</summary>
public sealed record InventoryFacts(string Assessment);

/// <summary>
/// The Foundry-managed product expert (Foundry IQ knowledge base attached in the portal).
/// The wrapper never leaks a raw AgentResponse: callers receive a StageOutcome whose
/// success case proves a knowledge retrieval happened.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Requires a live Foundry-managed agent.")]
public sealed class ProductExpert
{
    private readonly AIAgent _agent;

    private ProductExpert(AIAgent agent) => _agent = agent;

    /// <summary>Same-assembly seam for the evaluation gate; the boundary types stay authoritative at runtime.</summary>
    internal AIAgent Agent => _agent;

    public static async Task<ProductExpert> ConnectAsync(AIProjectClient projectClient, AgentName name)
    {
        ProjectsAgentRecord record = await projectClient.AgentAdministrationClient.GetAgentAsync(name.Value);
        return new ProductExpert(projectClient.AsAIAgent(record));
    }

    public async Task<StageOutcome<ProductAdvice>> AdviseAsync(CustomerQuery query)
    {
        AgentSession session = await _agent.CreateSessionAsync();
        AgentResponse response = await _agent.RunAsync(query.Text, session);

        SettledResponse settled = await ApprovalPolicy
            .AllowAllMcp("managed agent's only tool is the Foundry IQ knowledge base")
            .SettleAsync(_agent, session, response);

        return Grounding.Require(
            settled,
            ToolRequirement.AnyMcp(),
            result => new ProductAdvice(query, result.Text));
    }
}

/// <summary>
/// The inventory analyst: a model-backed agent whose only tools are the local stdio
/// inventory MCP server hosted by this same executable. Its success case proves both
/// inventory levels and weekly sales were actually fetched.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Requires a live model deployment and a spawned stdio MCP server.")]
public sealed class InventoryAnalyst : IAsyncDisposable
{
    private static readonly ToolRequirement Requirement =
        ToolRequirement.AllTools("get_inventory_levels", "get_weekly_sales");

    private readonly AIAgent _agent;
    private readonly McpClient _mcpClient;

    private InventoryAnalyst(AIAgent agent, McpClient mcpClient)
    {
        _agent = agent;
        _mcpClient = mcpClient;
    }

    /// <summary>Same-assembly seam for the evaluation gate; the boundary types stay authoritative at runtime.</summary>
    internal AIAgent Agent => _agent;

    public static async Task<InventoryAnalyst> ConnectAsync(AIProjectClient projectClient, ModelDeploymentName deployment)
    {
        McpClient mcpClient = await McpClient.CreateAsync(
            new StdioClientTransport(new()
            {
                Name = "inventory-server",
                Command = "dotnet",
                Arguments = [typeof(InventoryAnalyst).Assembly.Location, "--server"],
            }));

        IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();

        AIAgent agent = projectClient.AsAIAgent(
            model: deployment.Value,
            instructions: """
                You are an inventory analyst for a retail store.

                Guidelines:
                - Always call get_inventory_levels and get_weekly_sales before answering.
                - Recommend restock when inventory is below 10 and weekly sales are above 15.
                - Recommend clearance when inventory is above 20 and weekly sales are below 5.
                - Answer with the stock and sales numbers you retrieved, per product.
                """,
            name: "inventory-analyst",
            tools: [.. mcpTools.Cast<AITool>()]);

        return new InventoryAnalyst(agent, mcpClient);
    }

    public async Task<StageOutcome<InventoryFacts>> AssessAsync(Grounded<ProductAdvice> advice)
    {
        AgentSession session = await _agent.CreateSessionAsync();
        AgentResponse response = await _agent.RunAsync(
            $"""
             A product expert gave a customer this advice:

             {advice.Value.Advice}

             Report current stock and weekly sales for the products involved (or the closest
             matches we carry), plus any restock or clearance recommendation.
             """,
            session);

        SettledResponse settled = await ApprovalPolicy
            .DenyAll("local MCP function tools execute directly; approval requests are unexpected here")
            .SettleAsync(_agent, session, response);

        return Grounding.Require(settled, Requirement, result => new InventoryFacts(result.Text));
    }

    public ValueTask DisposeAsync() => _mcpClient.DisposeAsync();
}

/// <summary>
/// The consolidator is pure synthesis: no tools, and its signature is the invariant —
/// it cannot be invoked without both grounded inputs, so a recommendation based on
/// unproven product or inventory claims is not a representable call.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Requires a live model deployment.")]
public sealed class Consolidator
{
    private readonly AIAgent _agent;

    private Consolidator(AIAgent agent) => _agent = agent;

    public static Consolidator Create(AIProjectClient projectClient, ModelDeploymentName deployment) => new(
        projectClient.AsAIAgent(
            model: deployment.Value,
            instructions: """
                You are a retail operations copilot. You receive verified product advice and
                verified inventory facts. Write one consolidated recommendation for the store:
                what to tell the customer, and what operational action (restock, clearance,
                none) the store should take. Do not invent products or numbers.
                """,
            name: "retail-ops-consolidator"));

    public async Task<StageOutcome<string>> ConsolidateAsync(
        Grounded<ProductAdvice> advice,
        Grounded<InventoryFacts> inventory)
    {
        AgentSession session = await _agent.CreateSessionAsync();
        AgentResponse response = await _agent.RunAsync(
            $"""
             Customer question:
             {advice.Value.Query.Text}

             Verified product advice (evidence: {Describe(advice.Evidence)}):
             {advice.Value.Advice}

             Verified inventory facts (evidence: {Describe(inventory.Evidence)}):
             {inventory.Value.Assessment}
             """,
            session);

        SettledResponse settled = await ApprovalPolicy
            .DenyAll("the consolidator has no tools; any approval request is an illegal state")
            .SettleAsync(_agent, session, response);

        // The consolidator's grounding is its typed inputs, not tool calls of its own.
        return settled.Decisions.Any(decision => !decision.Approved)
            ? new StageOutcome<string>.Failed("consolidator attempted a tool call and was denied")
            : new StageOutcome<string>.Success(new Grounded<string>(
                settled.Text,
                [.. advice.Evidence, .. inventory.Evidence],
                settled));
    }

    private static string Describe(IReadOnlyList<ToolCallEvidence> evidence) =>
        string.Join(", ", evidence.Select(item =>
            item.McpServer is null ? item.Tool : $"{item.McpServer}/{item.Tool}"));
}
