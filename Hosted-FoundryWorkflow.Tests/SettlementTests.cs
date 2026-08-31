// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Extensions.AI;

namespace HostedFoundryWorkflow.Tests;

public sealed class SettlementTests
{
    [Fact]
    public void Classify_PendingApproval_ReturnsRequiresApprovalCarryingThePendingRequests()
    {
        var request = new ToolApprovalRequestContent(
            "approval-1", new McpServerToolCallContent("call-1", "search", "foundry-iq"));
        var message = new ChatMessage(ChatRole.Assistant, [request]);

        Settlement settlement = Settlement.Classify("partial", [message], []);

        Settlement.RequiresApproval requiresApproval = Assert.IsType<Settlement.RequiresApproval>(settlement);
        Assert.Same(request, Assert.Single(requiresApproval.Pending));
    }

    [Fact]
    public void Classify_PendingApprovalAlongsideCompletedCalls_StillReturnsRequiresApproval()
    {
        var message = new ChatMessage(ChatRole.Assistant,
        [
            new McpServerToolCallContent("call-1", "search", "foundry-iq"),
            new ToolApprovalRequestContent("approval-1", new McpServerToolCallContent("call-2", "fetch", "foundry-iq")),
        ]);

        Assert.IsType<Settlement.RequiresApproval>(Settlement.Classify("partial", [message], []));
    }

    [Fact]
    public void Classify_McpToolCall_SettlesWithServerQualifiedEvidence()
    {
        var message = new ChatMessage(ChatRole.Assistant,
            [new McpServerToolCallContent("call-1", "search", "foundry-iq")]);

        Settlement.Settled settled =
            Assert.IsType<Settlement.Settled>(Settlement.Classify("done", [message], []));

        ToolCallEvidence evidence = Assert.Single(settled.Response.Evidence);
        Assert.Equal("search", evidence.Tool);
        Assert.Equal("foundry-iq", evidence.McpServer);
    }

    [Fact]
    public void Classify_FunctionCall_SettlesWithoutAnMcpServer()
    {
        var message = new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("call-1", "get_weekly_sales")]);

        Settlement.Settled settled =
            Assert.IsType<Settlement.Settled>(Settlement.Classify("done", [message], []));

        ToolCallEvidence evidence = Assert.Single(settled.Response.Evidence);
        Assert.Equal("get_weekly_sales", evidence.Tool);
        Assert.Null(evidence.McpServer);
    }

    [Fact]
    public void Classify_NoToolCalls_SettlesWithTextAndDecisionsCarriedThrough()
    {
        ApprovalDecision[] decisions = [new("foundry-iq", "search", Approved: true, "allow-listed")];
        var message = new ChatMessage(ChatRole.Assistant, "the answer");

        Settlement.Settled settled =
            Assert.IsType<Settlement.Settled>(Settlement.Classify("the answer", [message], decisions));

        Assert.Equal("the answer", settled.Response.Text);
        Assert.Empty(settled.Response.Evidence);
        Assert.Same(decisions, settled.Response.Decisions);
    }

    [Fact]
    public void Classify_MixedToolCalls_OrdersMcpEvidenceBeforeFunctionEvidence()
    {
        var message = new ChatMessage(ChatRole.Assistant,
        [
            new FunctionCallContent("call-1", "get_inventory_levels"),
            new McpServerToolCallContent("call-2", "search", "foundry-iq"),
        ]);

        Settlement.Settled settled =
            Assert.IsType<Settlement.Settled>(Settlement.Classify("done", [message], []));

        Assert.Equal(
            [new ToolCallEvidence("search", "foundry-iq"), new ToolCallEvidence("get_inventory_levels", null)],
            settled.Response.Evidence);
    }
}
