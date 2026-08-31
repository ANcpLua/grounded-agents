// Copyright (c) Microsoft. All rights reserved.

using ANcpLua.Agents.Testing.Agents;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedFoundryWorkflow.Tests;

/// <summary>
/// Replays a fixed script of agent responses and records the messages handed to each run,
/// so the approval round-trip can be asserted without a live service.
/// </summary>
internal sealed class ScriptedAgent(params AgentResponse[] script) : FakeAgentBase
{
    private readonly Queue<AgentResponse> _script = new(script);

    /// <summary>The message list passed to every <c>RunAsync</c> call, in order.</summary>
    public List<IReadOnlyList<ChatMessage>> ReceivedRuns { get; } = [];

    protected override Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ReceivedRuns.Add([.. messages]);
        return Task.FromResult(_script.Dequeue());
    }

    // The policy loop only ever drives non-streaming runs.
    protected override IAsyncEnumerable<AgentResponseUpdate> StreamResponseAsync(
        IEnumerable<ChatMessage> messages,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("ScriptedAgent does not stream.");
}
