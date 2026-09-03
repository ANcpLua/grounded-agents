// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedFoundryWorkflow.Tests;

public sealed class RequirementChecksTests
{
    private static EvalItem Item(params AIContent[] assistantContents) => new(
        "the question",
        "the answer",
        [new ChatMessage(ChatRole.User, "the question"), new ChatMessage(ChatRole.Assistant, assistantContents)]);

    [Fact]
    public void Satisfies_AnyMcp_CountsAnMcpServerCall()
    {
        EvalCheckResult result = RequirementChecks.Satisfies(ToolRequirement.AnyMcp())(
            Item(new McpServerToolCallContent("call-1", "knowledge_base_retrieve", "knowledge-base"),
                new TextContent("the answer")));

        Assert.True(result.Passed);
        Assert.Equal("tool_requirement", result.CheckName);
        Assert.Equal("any MCP-server call — satisfied by knowledge-base/knowledge_base_retrieve", result.Reason);
    }

    [Fact]
    public void Satisfies_AnyMcp_LocalFunctionCallIsNotEnough()
    {
        EvalCheckResult result = RequirementChecks.Satisfies(ToolRequirement.AnyMcp())(
            Item(new FunctionCallContent("call-1", "get_weekly_sales")));

        Assert.False(result.Passed);
        Assert.Equal("any MCP-server call — not satisfied; saw get_weekly_sales", result.Reason);
    }

    [Fact]
    public void Satisfies_AllTools_RequiresEveryNamedTool()
    {
        EvalCheck check = RequirementChecks.Satisfies(
            ToolRequirement.AllTools("get_inventory_levels", "get_weekly_sales"));

        Assert.True(check(Item(
            new FunctionCallContent("call-1", "get_inventory_levels"),
            new FunctionCallContent("call-2", "get_weekly_sales"))).Passed);

        EvalCheckResult partial = check(Item(new FunctionCallContent("call-1", "get_inventory_levels")));
        Assert.False(partial.Passed);
        Assert.Equal(
            "all of get_inventory_levels, get_weekly_sales — not satisfied; saw get_inventory_levels",
            partial.Reason);
    }

    [Fact]
    public void Satisfies_NoToolCallsAtAll_SaysSo()
    {
        EvalCheckResult result = RequirementChecks.Satisfies(ToolRequirement.AnyMcp())(
            Item(new TextContent("answered from memory")));

        Assert.False(result.Passed);
        Assert.Equal("any MCP-server call — not satisfied; saw no tool calls", result.Reason);
    }
}
