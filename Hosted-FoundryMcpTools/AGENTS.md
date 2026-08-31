# Hosted-FoundryMcpTools Agent Instructions

These instructions apply only to `samples/04-hosting/FoundryHostedAgents/responses/Hosted-FoundryMcpTools`.

## Scope

- This folder is a follow-up chapter after `Hosted-FoundryIQ` with a checked-in C# console implementation.
- Keep it as a separate sample. Do not merge it into the Foundry IQ chapter.
- Use Rider 2026.2 for all IDE and terminal wording.
- Keep the guide C# and .NET only. Do not add other language runtime setup or starter files.
- Do not add resource cleanup or deletion sections unless the user explicitly asks for them.

## MCP rules

- For remote Microsoft Learn MCP, prefer `HostedMcpServerTool` with `HostedMcpServerToolApprovalMode.AlwaysRequire` so approval handling remains visible.
- For local custom MCP tools, use the C# MCP SDK with `Host.CreateApplicationBuilder`, `AddMcpServer`, `WithStdioServerTransport`, and explicit `WithTools<T>()`.
- Keep the sample differentiated from upstream microsoft/agent-framework's `dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-McpTools`: that upstream sample demonstrates hosted-agent deployment with Microsoft Learn MCP; this sample is the course-oriented Rider follow-up and includes a custom local inventory stdio MCP server.
- In stdio server mode, route logs to stderr. Do not write diagnostic text to stdout from the server.
- Use `McpClient.CreateAsync` with `StdioClientTransport` for local child-process MCP servers.
- Expose inventory data through `[McpServerTool]` methods and connect discovered `McpClientTool` instances to Agent Framework as `AITool`.

## Documentation rules

- Keep secrets out of examples. Use environment variables and placeholders.
- Keep package commands current with local repo samples and official C# MCP SDK guidance.
- If SDK APIs change, verify snippets against local source before editing the guide.
- Do not create tests for this sample unless a future change adds deterministic local behavior that can be verified without live Foundry resources.
