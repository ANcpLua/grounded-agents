# Hosted-FoundryIQ Agent Instructions

These instructions apply only to `samples/04-hosting/FoundryHostedAgents/responses/Hosted-FoundryIQ`.

## Scope

- This folder rewrites the Foundry IQ lab as a C# guide with a checked-in console implementation.
- Keep it in the `FoundryHostedAgents/responses` lane because the guide targets a Foundry-managed agent invoked from C#.
- Keep the guide C# and .NET only. Do not add other language runtime setup or starter files.
- When an editor or local IDE is mentioned, use JetBrains Rider 2026.2. Do not introduce alternate editor wording.
- Do not add cleanup/deletion instructions unless the user explicitly asks for them.

## C# API rules

- Prefer `Azure.AI.Projects`, `Azure.Identity`, and `Microsoft.Agents.AI.Foundry`.
- For a portal-created Foundry agent, retrieve it with `AIProjectClient.AgentAdministrationClient.GetAgentAsync(agentName)` and wrap it with `AIProjectClient.AsAIAgent(...)`.
- Preserve MCP approval handling with `ToolApprovalRequestContent` and `approvalRequest.CreateResponse(...)`.
- Keep Foundry IQ setup portal-managed unless current official Microsoft documentation proves a supported C# provisioning path for the specific Foundry IQ agent connection.

## Documentation rules

- Keep the guide C#-first and local-client-focused.
- Use environment variables for endpoints and names. Do not include real subscription IDs, tenant IDs, keys, tokens, or resource names.
- If package names or API shapes change, verify against local repo samples and official Microsoft documentation before editing snippets.
- Do not create tests for this sample unless a future change adds deterministic local behavior that can be verified without live Foundry resources.
