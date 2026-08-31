// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedFoundryWorkflow.Tests;

public sealed class ApprovalPolicyTests
{
    /// <summary>A case outside the union, so the closing guard can be driven rather than assumed.</summary>
    private sealed record UnknownPolicy : ApprovalPolicy;

    [Fact]
    public void Decide_CaseOutsideTheUnion_Throws()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new UnknownPolicy().Decide("foundry-iq", "search", isMcp: true));

        Assert.Equal("Unreachable: ApprovalPolicy union is closed.", error.Message);
    }

    [Fact]
    public void Decide_AllowServers_ListedServer_Approves()
    {
        ApprovalDecision decision = ApprovalPolicy.AllowServers("api-specs")
            .Decide("api-specs", "microsoft_docs_search", isMcp: true);

        Assert.True(decision.Approved);
        Assert.Equal("api-specs", decision.Server);
        Assert.Equal("microsoft_docs_search", decision.Tool);
        Assert.Equal("server 'api-specs' is allow-listed", decision.Reason);
    }

    [Fact]
    public void Decide_AllowServers_ListedServerWithDifferentCasing_Approves()
    {
        Assert.True(ApprovalPolicy.AllowServers("api-specs")
            .Decide("API-Specs", "microsoft_docs_search", isMcp: true).Approved);
    }

    [Fact]
    public void Decide_AllowServers_UnlistedServer_Denies()
    {
        ApprovalDecision decision = ApprovalPolicy.AllowServers("api-specs")
            .Decide("evil-server", "exfiltrate", isMcp: true);

        Assert.False(decision.Approved);
        Assert.Equal("server 'evil-server' is not allow-listed; denied by default", decision.Reason);
    }

    [Fact]
    public void Decide_AllowAllMcp_McpCall_Approves()
    {
        ApprovalDecision decision = ApprovalPolicy.AllowAllMcp("trusted knowledge tool")
            .Decide("foundry-iq", "search", isMcp: true);

        Assert.True(decision.Approved);
        Assert.Equal("trusted knowledge tool", decision.Reason);
    }

    [Fact]
    public void Decide_AllowAllMcp_NonMcpCall_Denies()
    {
        ApprovalDecision decision = ApprovalPolicy.AllowAllMcp("trusted knowledge tool")
            .Decide("<non-mcp>", "anything", isMcp: false);

        Assert.False(decision.Approved);
        Assert.Equal("non-MCP tool call; denied by default", decision.Reason);
    }

    [Fact]
    public void Decide_DenyAll_Denies()
    {
        ApprovalDecision decision = ApprovalPolicy.DenyAll("no tools attached")
            .Decide("foundry-iq", "search", isMcp: true);

        Assert.False(decision.Approved);
        Assert.Equal("no tools attached", decision.Reason);
    }

    [Fact]
    public void Decide_Request_McpToolCall_UsesServerAndToolNames()
    {
        var request = new ToolApprovalRequestContent(
            "approval-1", new McpServerToolCallContent("call-1", "microsoft_docs_search", "api-specs"));

        ApprovalDecision decision = ApprovalPolicy.AllowServers("api-specs").Decide(request);

        Assert.True(decision.Approved);
        Assert.Equal("api-specs", decision.Server);
        Assert.Equal("microsoft_docs_search", decision.Tool);
    }

    [Fact]
    public void Decide_Request_UnlistedMcpServer_Denies()
    {
        var request = new ToolApprovalRequestContent(
            "approval-2", new McpServerToolCallContent("call-2", "exfiltrate", "evil-server"));

        Assert.False(ApprovalPolicy.AllowServers("api-specs").Decide(request).Approved);
    }

    [Fact]
    public void Decide_Request_NonMcpToolCall_IsTreatedAsNonMcp()
    {
        var request = new ToolApprovalRequestContent(
            "approval-3", new FunctionCallContent("call-3", "delete_everything"));

        ApprovalDecision decision = ApprovalPolicy.AllowAllMcp("trusted knowledge tool").Decide(request);

        Assert.False(decision.Approved);
        Assert.Equal("<non-mcp>", decision.Server);
        Assert.Equal("non-MCP tool call; denied by default", decision.Reason);
    }

    [Fact]
    public void Decide_Request_McpToolCallWithoutServerName_FallsBackToPlaceholder()
    {
        var request = new ToolApprovalRequestContent(
            "approval-4", new McpServerToolCallContent("call-4", "search", serverName: null));

        ApprovalDecision decision = ApprovalPolicy.AllowServers("api-specs").Decide(request);

        Assert.False(decision.Approved);
        Assert.Equal("<unnamed>", decision.Server);
    }

    [Fact]
    public async Task SettleAsync_AlreadySettledResponse_ReturnsWithoutRunningTheAgentAgain()
    {
        var response = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant,
                [new McpServerToolCallContent("call-1", "search", "foundry-iq"), new TextContent("the answer")]),
        ]);

        var agent = new ScriptedAgent();
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        SettledResponse settled = await ApprovalPolicy
            .AllowAllMcp("trusted knowledge tool")
            .SettleAsync(agent, session, response);

        Assert.Empty(agent.ReceivedRuns);
        Assert.Empty(settled.Decisions);
        Assert.Equal("the answer", settled.Text);
        Assert.Equal(new ToolCallEvidence("search", "foundry-iq"), Assert.Single(settled.Evidence));
    }

    [Fact]
    public async Task SettleAsync_PendingApproval_ApprovesAndAggregatesEvidenceAcrossRounds()
    {
        var request = new ToolApprovalRequestContent(
            "approval-1", new McpServerToolCallContent("call-2", "fetch", "foundry-iq"));

        var followUp = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant,
                [new McpServerToolCallContent("call-2", "fetch", "foundry-iq"), new TextContent("the answer")]),
        ]);

        var agent = new ScriptedAgent(followUp);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var first = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new McpServerToolCallContent("call-1", "search", "foundry-iq")]),
            new ChatMessage(ChatRole.Assistant, [request]),
        ]);

        SettledResponse settled = await ApprovalPolicy
            .AllowAllMcp("trusted knowledge tool")
            .SettleAsync(agent, session, first);

        Assert.Equal("the answer", settled.Text);
        Assert.Equal(
            [new ToolCallEvidence("search", "foundry-iq"), new ToolCallEvidence("fetch", "foundry-iq")],
            settled.Evidence);

        ApprovalDecision decision = Assert.Single(settled.Decisions);
        Assert.True(decision.Approved);
        Assert.Equal("fetch", decision.Tool);

        IReadOnlyList<ChatMessage> sentBack = Assert.Single(agent.ReceivedRuns);
        ToolApprovalResponseContent approval = Assert.IsType<ToolApprovalResponseContent>(
            Assert.Single(Assert.Single(sentBack).Contents));
        Assert.True(approval.Approved);
        Assert.Equal("approval-1", approval.RequestId);
    }

    [Fact]
    public async Task SettleAsync_DeniedApproval_RecordsTheDenialAndSendsBackARejection()
    {
        var request = new ToolApprovalRequestContent(
            "approval-1", new McpServerToolCallContent("call-1", "exfiltrate", "evil-server"));

        var followUp = new AgentResponse([new ChatMessage(ChatRole.Assistant, "I cannot help with that.")]);

        var agent = new ScriptedAgent(followUp);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        SettledResponse settled = await ApprovalPolicy
            .AllowServers("api-specs")
            .SettleAsync(agent, session, new AgentResponse([new ChatMessage(ChatRole.Assistant, [request])]));

        Assert.Equal("I cannot help with that.", settled.Text);
        Assert.Empty(settled.Evidence);

        ApprovalDecision decision = Assert.Single(settled.Decisions);
        Assert.False(decision.Approved);
        Assert.Equal("evil-server", decision.Server);

        ToolApprovalResponseContent approval = Assert.IsType<ToolApprovalResponseContent>(
            Assert.Single(Assert.Single(Assert.Single(agent.ReceivedRuns)).Contents));
        Assert.False(approval.Approved);
    }

    [Fact]
    public async Task SettleAsync_ApprovalsInTwoRounds_RecordsEveryDecision()
    {
        var second = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent(
                    "approval-2", new McpServerToolCallContent("call-2", "fetch", "foundry-iq")),
            ]),
        ]);

        var third = new AgentResponse([new ChatMessage(ChatRole.Assistant, "the answer")]);

        var agent = new ScriptedAgent(second, third);
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        var first = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant,
            [
                new ToolApprovalRequestContent(
                    "approval-1", new McpServerToolCallContent("call-1", "search", "foundry-iq")),
            ]),
        ]);

        SettledResponse settled = await ApprovalPolicy
            .AllowAllMcp("trusted knowledge tool")
            .SettleAsync(agent, session, first);

        Assert.Equal(2, agent.ReceivedRuns.Count);
        Assert.Equal(["search", "fetch"], settled.Decisions.Select(decision => decision.Tool));
        Assert.All(settled.Decisions, decision => Assert.True(decision.Approved));
    }
}
