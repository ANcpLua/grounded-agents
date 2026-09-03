// Copyright (c) Microsoft. All rights reserved.

using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace HostedFoundryWorkflow;

/// <summary>
/// An agent as the workflow actually runs it: every response is driven to settlement under
/// an <see cref="ApprovalPolicy"/> before it is handed back, and what comes back is the settled
/// <see cref="SettledResponse.Transcript"/>. The evaluation gate wraps the stage agents in this,
/// so it scores the same tool calls, decisions and text the runtime saw. Without it a managed
/// agent whose tool needs approval answers the gate with nothing but the approval request.
/// </summary>
public sealed class SettledAgent(AIAgent inner, ApprovalPolicy policy) : DelegatingAIAgent(inner)
{
    protected override async Task<AgentResponse> RunCoreAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        // The policy loop continues the same session across approval rounds, so a run
        // without one gets a session of its own rather than a fresh one per round.
        session ??= await InnerAgent.CreateSessionAsync(cancellationToken);

        AgentResponse first = await InnerAgent.RunAsync(messages, session, options, cancellationToken);
        SettledResponse settled = await policy.SettleAsync(InnerAgent, session, first);

        return new AgentResponse([.. settled.Transcript]);
    }

    // Settlement is a round-by-round loop over complete responses; there is no streaming form of it.
    protected override IAsyncEnumerable<AgentResponseUpdate> RunCoreStreamingAsync(
        IEnumerable<ChatMessage> messages,
        AgentSession? session = null,
        AgentRunOptions? options = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("SettledAgent settles approvals round by round; it does not stream.");
}
