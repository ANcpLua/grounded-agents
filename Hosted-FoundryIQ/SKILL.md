# Hosted Foundry IQ C# Guide

Use this skill when maintaining or extending the `Hosted-FoundryIQ` sample guide.

## Purpose

This sample teaches a reader to configure a Foundry IQ-backed agent in the Microsoft Foundry portal, then invoke that managed agent from a local C# console app. It includes the console implementation, but it is not a code-first Foundry IQ provisioning sample.

## Workflow

1. Keep the sample located at `samples/04-hosting/FoundryHostedAgents/responses/Hosted-FoundryIQ`.
2. Verify current C# API examples against nearby Foundry samples before changing code snippets.
3. Use Rider 2026.2 for local IDE, terminal, and project workflow wording.
4. Use the managed-agent pattern:

```csharp
var projectClient = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());
ProjectsAgentRecord agentRecord = await projectClient.AgentAdministrationClient.GetAgentAsync(agentName);
AIAgent agent = projectClient.AsAIAgent(agentRecord);
```

5. Preserve session-based invocation:

```csharp
AgentSession session = await agent.CreateSessionAsync();
AgentResponse response = await agent.RunAsync(userInput, session);
```

6. Preserve MCP approval handling with `ToolApprovalRequestContent`, `McpServerToolCallContent`, `ChatMessage`, and `approvalRequest.CreateResponse(approved)`.
7. Keep the portal setup steps for Foundry IQ knowledge base creation and agent attachment.
8. Keep secrets out of the guide. Use placeholders and environment variables only.
9. Do not add cleanup steps unless explicitly requested.

## Validation

Validate by building `HostedFoundryIQ.csproj`, reading the rendered Markdown, and checking that the C# snippets match the checked-in implementation and current local sample patterns. Do not add live-service tests for this sample.
