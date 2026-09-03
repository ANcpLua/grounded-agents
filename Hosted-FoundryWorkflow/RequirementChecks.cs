// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;

namespace HostedFoundryWorkflow;

/// <summary>
/// The evaluation gate's expectations are the runtime's <see cref="ToolRequirement"/>s, applied
/// to an item's conversation with the same evidence extraction the boundary uses. MCP-server
/// calls count, which the framework's own tool-call checks do not see.
/// </summary>
public static class RequirementChecks
{
    public static EvalCheck Satisfies(ToolRequirement requirement) => item =>
    {
        List<ToolCallEvidence> evidence = Settlement.EvidenceOf(item.Conversation);
        bool satisfied = requirement.IsSatisfiedBy(evidence);

        string seen = evidence.Count == 0
            ? "no tool calls"
            : string.Join(", ", evidence.Select(call =>
                call.McpServer is null ? call.Tool : $"{call.McpServer}/{call.Tool}"));

        return new EvalCheckResult(
            satisfied,
            satisfied
                ? $"{requirement.Describe()} — satisfied by {seen}"
                : $"{requirement.Describe()} — not satisfied; saw {seen}",
            "tool_requirement");
    };
}
