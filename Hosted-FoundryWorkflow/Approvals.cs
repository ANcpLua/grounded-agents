// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedFoundryWorkflow;

/// <summary>One observed tool invocation. <see cref="McpServer"/> is null for local function tools.</summary>
public sealed record ToolCallEvidence(string Tool, string? McpServer);

public sealed record ApprovalDecision(string Server, string Tool, bool Approved, string Reason);

/// <summary>
/// A response with zero pending approvals. This type has no public constructor:
/// the only producers are <see cref="Settlement.Classify"/> (which refuses to build one
/// while an approval is pending) and the <see cref="ApprovalPolicy"/> loop that drives
/// every pending request to a recorded decision. Reading text out of an unsettled
/// response is therefore not a representable state.
/// </summary>
public sealed class SettledResponse
{
    internal SettledResponse(
        string text,
        IReadOnlyList<ToolCallEvidence> evidence,
        IReadOnlyList<ApprovalDecision> decisions)
    {
        Text = text;
        Evidence = evidence;
        Decisions = decisions;
    }

    public string Text { get; }
    public IReadOnlyList<ToolCallEvidence> Evidence { get; }
    public IReadOnlyList<ApprovalDecision> Decisions { get; }
}

/// <summary>Closed classification of an agent response: settled, or blocked on approvals.</summary>
public abstract record Settlement
{
    private Settlement() { }

    public sealed record Settled(SettledResponse Response) : Settlement;

    public sealed record RequiresApproval(IReadOnlyList<ToolApprovalRequestContent> Pending) : Settlement;

    public static Settlement Classify(
        string text,
        IEnumerable<ChatMessage> messages,
        IReadOnlyList<ApprovalDecision> decisions)
    {
        List<AIContent> contents = [.. messages.SelectMany(message => message.Contents)];

        List<ToolApprovalRequestContent> pending = [.. contents.OfType<ToolApprovalRequestContent>()];
        if (pending.Count > 0)
        {
            return new RequiresApproval(pending);
        }

        return new Settled(new SettledResponse(text, EvidenceOf(contents), decisions));
    }

    /// <summary>Tool-call evidence carried by a set of contents; MCP calls keep their server name.</summary>
    internal static List<ToolCallEvidence> EvidenceOf(IEnumerable<ChatMessage> messages) =>
        EvidenceOf([.. messages.SelectMany(message => message.Contents)]);

    private static List<ToolCallEvidence> EvidenceOf(List<AIContent> contents) =>
    [
        .. contents.OfType<McpServerToolCallContent>()
            .Select(call => new ToolCallEvidence(call.Name, call.ServerName)),
        .. contents.OfType<FunctionCallContent>()
            .Select(call => new ToolCallEvidence(call.Name, McpServer: null)),
    ];
}

/// <summary>
/// Deny-by-default approval policy as a closed union. There is no way to approve a call
/// that the policy does not positively allow, and every decision is recorded on the
/// settled response so downstream stages and telemetry carry the audit trail.
/// </summary>
public abstract record ApprovalPolicy
{
    // Internal rather than private so the test project (the only friend assembly) can construct a
    // case outside the union and prove the closing guard below actually fires. Consumers of the
    // package still cannot extend it.
    internal ApprovalPolicy() { }

    private sealed record AllowServersPolicy(IReadOnlySet<string> Servers) : ApprovalPolicy;

    private sealed record AllowAllMcpPolicy(string Justification) : ApprovalPolicy;

    private sealed record DenyAllPolicy(string Justification) : ApprovalPolicy;

    public static ApprovalPolicy AllowServers(params string[] servers) =>
        new AllowServersPolicy(servers.ToHashSet(StringComparer.OrdinalIgnoreCase));

    /// <summary>For managed agents whose only attached tools are trusted (e.g. a Foundry IQ knowledge tool).</summary>
    public static ApprovalPolicy AllowAllMcp(string justification) => new AllowAllMcpPolicy(justification);

    /// <summary>For agents that must not call tools at all; any approval request is an illegal state.</summary>
    public static ApprovalPolicy DenyAll(string justification) => new DenyAllPolicy(justification);

    public ApprovalDecision Decide(ToolApprovalRequestContent request)
    {
        (string server, string tool, bool isMcp) = request.ToolCall is McpServerToolCallContent mcp
            ? (mcp.ServerName ?? "<unnamed>", mcp.Name ?? "<unnamed>", true)
            : ("<non-mcp>", request.ToolCall?.ToString() ?? "<unknown>", false);

        return Decide(server, tool, isMcp);
    }

    /// <summary>The pure decision core; exposed for the offline selftest.</summary>
    internal ApprovalDecision Decide(string server, string tool, bool isMcp)
    {
        return this switch
        {
            AllowServersPolicy(var servers) when servers.Contains(server) =>
                new ApprovalDecision(server, tool, Approved: true, $"server '{server}' is allow-listed"),
            AllowServersPolicy => new ApprovalDecision(
                server, tool, Approved: false, $"server '{server}' is not allow-listed; denied by default"),
            AllowAllMcpPolicy(var why) when isMcp =>
                new ApprovalDecision(server, tool, Approved: true, why),
            AllowAllMcpPolicy => new ApprovalDecision(
                server, tool, Approved: false, "non-MCP tool call; denied by default"),
            DenyAllPolicy(var why) => new ApprovalDecision(server, tool, Approved: false, why),
            _ => throw new InvalidOperationException("Unreachable: ApprovalPolicy union is closed."),
        };
    }

    /// <summary>
    /// Drives an agent run to settlement: while approvals are pending, decide each one
    /// under this policy and continue the run. The returned <see cref="SettledResponse"/>
    /// aggregates the evidence and decisions of every round.
    /// </summary>
    public async Task<SettledResponse> SettleAsync(AIAgent agent, AgentSession session, AgentResponse response)
    {
        List<ToolCallEvidence> evidence = [];
        List<ApprovalDecision> decisions = [];

        while (true)
        {
            // Only the current round can hold a *pending* request: anything decided in an earlier
            // round is already settled and survives here as evidence, not as a reason to loop again.
            if (Settlement.Classify(response.Text, response.Messages, decisions) is Settlement.Settled settled)
            {
                return new SettledResponse(
                    settled.Response.Text, [.. evidence, .. settled.Response.Evidence], decisions);
            }

            evidence.AddRange(Settlement.EvidenceOf(response.Messages));

            List<ChatMessage> approvalResponses = [];

            foreach (ToolApprovalRequestContent request in response.Messages
                         .SelectMany(message => message.Contents)
                         .OfType<ToolApprovalRequestContent>())
            {
                ApprovalDecision decision = Decide(request);
                decisions.Add(decision);
                approvalResponses.Add(new ChatMessage(ChatRole.User, [request.CreateResponse(decision.Approved)]));
            }

            response = await agent.RunAsync(approvalResponses, session);
        }
    }
}
