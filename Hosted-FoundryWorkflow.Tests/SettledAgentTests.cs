// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedFoundryWorkflow.Tests;

public sealed class SettledAgentTests
{
    private static readonly McpServerToolCallContent Retrieval =
        new("call-1", "knowledge_base_retrieve", "knowledge-base");

    [Fact]
    public async Task RunAsync_ApprovalGatedTool_ApprovesUnderThePolicyAndReturnsTheSettledTranscript()
    {
        var first = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new ToolApprovalRequestContent("approval-1", Retrieval)]),
        ]);
        var followUp = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant, [Retrieval, new TextContent("Contoso offers seven tents.")]),
        ]);
        var inner = new ScriptedAgent(first, followUp);
        var agent = new SettledAgent(inner, ApprovalPolicy.AllowAllMcp("trusted knowledge tool"));

        AgentResponse response = await agent.RunAsync(
            "What types of tents does Contoso offer?",
            cancellationToken: TestContext.Current.CancellationToken);

        // The gate sees the final text and the executed tool call, not the bare approval request.
        Assert.Equal("Contoso offers seven tents.", response.Text);
        Assert.Contains(
            response.Messages.SelectMany(message => message.Contents),
            content => content is McpServerToolCallContent { Name: "knowledge_base_retrieve" });

        // Round one carried the query; round two carried the policy's approval.
        Assert.Equal(2, inner.ReceivedRuns.Count);
        Assert.Equal("What types of tents does Contoso offer?", Assert.Single(inner.ReceivedRuns[0]).Text);
        ToolApprovalResponseContent approval = Assert.IsType<ToolApprovalResponseContent>(
            Assert.Single(Assert.Single(inner.ReceivedRuns[1]).Contents));
        Assert.True(approval.Approved);
    }

    [Fact]
    public async Task RunAsync_DeniedByPolicy_ReturnsTheRefusalAndNoToolCall()
    {
        var first = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant, [new ToolApprovalRequestContent("approval-1", Retrieval)]),
        ]);
        var followUp = new AgentResponse([new ChatMessage(ChatRole.Assistant, "I cannot help with that.")]);
        var inner = new ScriptedAgent(first, followUp);
        var agent = new SettledAgent(inner, ApprovalPolicy.DenyAll("no tools here"));

        AgentResponse response = await agent.RunAsync("anything", cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("I cannot help with that.", response.Text);
        Assert.DoesNotContain(
            response.Messages.SelectMany(message => message.Contents),
            content => content is McpServerToolCallContent);
        Assert.False(Assert.IsType<ToolApprovalResponseContent>(
            Assert.Single(Assert.Single(inner.ReceivedRuns[1]).Contents)).Approved);
    }

    [Fact]
    public async Task RunAsync_WithACallerSession_SettlesInOneRoundWhenNothingIsPending()
    {
        var settled = new AgentResponse(
        [
            new ChatMessage(ChatRole.Assistant, [Retrieval, new TextContent("the answer")]),
        ]);
        var inner = new ScriptedAgent(settled);
        var agent = new SettledAgent(inner, ApprovalPolicy.AllowAllMcp("trusted knowledge tool"));
        AgentSession session = await agent.CreateSessionAsync(TestContext.Current.CancellationToken);

        AgentResponse response = await agent.RunAsync("q", session, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("the answer", response.Text);
        Assert.Same(settled.Messages[0], Assert.Single(response.Messages));
        Assert.Single(inner.ReceivedRuns);
    }

    [Fact]
    public async Task RunStreamingAsync_IsNotSupported()
    {
        var agent = new SettledAgent(new ScriptedAgent(), ApprovalPolicy.DenyAll("no tools here"));

        NotSupportedException error = await Assert.ThrowsAsync<NotSupportedException>(async () =>
        {
            await foreach (AgentResponseUpdate _ in agent.RunStreamingAsync(
                               "q", cancellationToken: TestContext.Current.CancellationToken))
            {
            }
        });

        Assert.Equal("SettledAgent settles approvals round by round; it does not stream.", error.Message);
    }
}
