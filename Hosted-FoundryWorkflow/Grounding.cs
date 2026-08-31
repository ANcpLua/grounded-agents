// Copyright (c) Microsoft. All rights reserved.

namespace HostedFoundryWorkflow;

/// <summary>
/// What a stage must prove before its answer counts as grounded, as a closed union.
/// </summary>
public abstract record ToolRequirement
{
    // Internal rather than private so the test project (the only friend assembly) can construct a
    // case outside the union and prove the closing guard in Grounding.Require actually fires.
    internal ToolRequirement() { }

    /// <summary>Every named tool must appear in the evidence.</summary>
    public sealed record AllOf(IReadOnlyList<string> Tools) : ToolRequirement;

    /// <summary>At least one MCP-server-backed call must appear (e.g. a Foundry IQ retrieval).</summary>
    public sealed record AnyMcpCall : ToolRequirement;

    public static ToolRequirement AllTools(params string[] tools) => new AllOf(tools);

    public static ToolRequirement AnyMcp() => new AnyMcpCall();
}

/// <summary>
/// A value that carries the tool-call evidence that produced it. The constructor is
/// internal and asserts non-empty evidence: "grounded, but with no proof" cannot be built.
/// The only producer is <see cref="Grounding.Require{T}"/>.
/// </summary>
public sealed class Grounded<T>
{
    internal Grounded(T value, IReadOnlyList<ToolCallEvidence> evidence, SettledResponse origin)
    {
        if (evidence.Count == 0)
        {
            throw new ArgumentException("Grounded values require non-empty evidence.", nameof(evidence));
        }

        Value = value;
        Evidence = evidence;
        Origin = origin;
    }

    public T Value { get; }
    public IReadOnlyList<ToolCallEvidence> Evidence { get; }
    public SettledResponse Origin { get; }
}

/// <summary>Closed outcome union for a workflow stage. Callers must handle every case.</summary>
public abstract record StageOutcome<T>
{
    private protected StageOutcome() { }

    public sealed record Success(Grounded<T> Result) : StageOutcome<T>;

    /// <summary>The agent answered, but the evidence does not satisfy the requirement.
    /// The answer text is retained for diagnostics yet is typed as unusable downstream.</summary>
    public sealed record Ungrounded(string Answer, ToolRequirement Missing) : StageOutcome<T>;

    public sealed record Failed(string Reason) : StageOutcome<T>;
}

public static class Grounding
{
    /// <summary>
    /// The single gate between a settled response and a grounded value: project the
    /// response into <typeparamref name="T"/> only when the evidence satisfies the
    /// requirement; otherwise the outcome is <see cref="StageOutcome{T}.Ungrounded"/>.
    /// </summary>
    public static StageOutcome<T> Require<T>(
        SettledResponse response,
        ToolRequirement requirement,
        Func<SettledResponse, T> project)
    {
        bool satisfied = requirement switch
        {
            ToolRequirement.AllOf(var tools) => tools.All(tool =>
                response.Evidence.Any(item => string.Equals(item.Tool, tool, StringComparison.OrdinalIgnoreCase))),
            ToolRequirement.AnyMcpCall => response.Evidence.Any(item => item.McpServer is not null),
            _ => throw new InvalidOperationException("Unreachable: ToolRequirement union is closed."),
        };

        return satisfied
            ? new StageOutcome<T>.Success(new Grounded<T>(project(response), response.Evidence, response))
            : new StageOutcome<T>.Ungrounded(response.Text, requirement);
    }
}
