# Hosted Foundry MCP Tools C# Guide

Use this skill when maintaining or extending the `Hosted-FoundryMcpTools` sample guide.

## Purpose

This sample is the second chapter after `Hosted-FoundryIQ`. It teaches how to extend a Foundry-backed Agent Framework app with MCP tools using C# and Rider 2026.2, and includes the checked-in console implementation.

## Workflow

1. Keep the sample located at `samples/04-hosting/FoundryHostedAgents/responses/Hosted-FoundryMcpTools`.
2. Keep the guide separate from the Foundry IQ chapter and link to it only as the prerequisite chapter.
3. Use Rider 2026.2 for IDE, terminal, and project creation instructions.
4. For remote MCP, use this pattern:

```csharp
AITool tool = new HostedMcpServerTool("api-specs", "https://learn.microsoft.com/api/mcp")
{
    AllowedTools = ["microsoft_docs_search"],
    ApprovalMode = HostedMcpServerToolApprovalMode.AlwaysRequire,
};
```

5. For local MCP, use this server pattern:

```csharp
builder.Services.AddMcpServer(options =>
    options.ServerInfo = new Implementation { Name = "Inventory", Version = "1.0.0" })
.WithStdioServerTransport()
.WithTools<InventoryTools>();
```

6. For local MCP clients, use this pattern:

```csharp
await using McpClient client = await McpClient.CreateAsync(
    new StdioClientTransport(new()
    {
        Name = "inventory-server",
        Command = "dotnet",
        Arguments = [assemblyPath, "--server"],
    }));
```

7. Preserve the approval loop for remote MCP calls and the `AgentSession` chat loop for local inventory questions.
8. Keep it differentiated from upstream microsoft/agent-framework's `dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-McpTools` sample by preserving the custom local inventory stdio MCP server.
9. Do not add resource cleanup instructions unless explicitly requested.

## Validation

Validate by building `HostedFoundryMcpTools.csproj`, reading the Markdown, searching for non-C# runtime/editor drift, and checking Markdown whitespace. Do not add live-service tests for this sample.
