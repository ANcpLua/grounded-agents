# Hosted-FoundryMcpTools

This is the follow-up chapter to [`Hosted-FoundryIQ`](../Hosted-FoundryIQ/README.md).
The first chapter connects a Foundry-managed agent to a Foundry IQ knowledge base.
This chapter extends the same Foundry development path with Model Context Protocol (MCP) tools from C#.

The sample is placed under `samples/04-hosting/FoundryHostedAgents/responses/Hosted-FoundryMcpTools`.
Both exercises use Microsoft Foundry project endpoints and Agent Framework response agents.
The local development workflow is done in Rider 2026.2.

The repository already contains the implementation in `HostedFoundryMcpTools.csproj` and `Program.cs`. The app has two user-facing modes:

```bash
dotnet run -- remote
dotnet run -- inventory
```

This sample complements upstream microsoft/agent-framework's
`dotnet/samples/04-hosting/FoundryHostedAgents/responses/Hosted-McpTools` sample,
which demonstrates hosted-agent deployment patterns with Microsoft Learn MCP.
This follow-up course keeps the local Rider workflow from the lab and adds a custom inventory stdio MCP server.
Readers can see both a remote hosted MCP tool and their own local MCP tools in one C# implementation.

## What you build

- A Foundry project with a deployed chat model.
- A local C# console app that gives a Foundry-backed agent access to the Microsoft Learn MCP server.
- The same local C# console app hosting inventory tools as a stdio MCP server and connecting those tools to a Foundry-backed agent.
- Approval handling for MCP tool calls.
- A stateful console chat loop for inventory questions.

## Prerequisites

- JetBrains Rider 2026.2 with .NET support.
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- [Azure CLI](https://learn.microsoft.com/cli/azure/install-azure-cli), signed in with `az login`.
- Git, if you want to keep your local course work in source control.
- An Azure subscription with access to Microsoft Foundry.

## Create or reuse the Foundry project

You can reuse the Foundry project from `Hosted-FoundryIQ` or create a new one.

1. Open the [Microsoft Foundry portal](https://ai.azure.com).
2. Create a project, or open the project from the previous chapter.
3. Deploy a chat model such as `gpt-4.1` or another model available in your region.
4. Copy the project endpoint from the project home page or settings page. It usually has this shape:

```text
https://<account>.services.ai.azure.com/api/projects/<project>
```

5. In Rider 2026.2, open the integrated terminal and set:

```bash
export AZURE_AI_PROJECT_ENDPOINT="https://<account>.services.ai.azure.com/api/projects/<project>"
export AZURE_AI_MODEL_DEPLOYMENT_NAME="gpt-4.1"
```

6. Authenticate:

```bash
az login
```

## Run mode 1: Connect to the Microsoft Learn MCP server

This mode uses a hosted MCP tool. The model provider calls the remote MCP server, and the local C# app handles approval requests before the call is allowed to continue.

The checked-in `Program.cs` uses this core pattern:

```csharp
// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable MEAI001

using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

string projectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("AZURE_AI_MODEL_DEPLOYMENT_NAME is not set.");

var projectClient = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());

AITool learnMcpTool = new HostedMcpServerTool(
    serverName: "api-specs",
    serverAddress: "https://learn.microsoft.com/api/mcp")
{
    AllowedTools = ["microsoft_docs_search"],
    ApprovalMode = HostedMcpServerToolApprovalMode.AlwaysRequire,
};

AIAgent agent = projectClient.AsAIAgent(
    model: deploymentName,
    instructions: """
        You are a helpful developer assistant.
        Use the Microsoft Learn MCP tool when the user asks for current Microsoft documentation,
        Azure CLI commands, SDK details, or platform guidance.
        """,
    name: "docs-mcp-agent",
    tools: [learnMcpTool]);

AgentSession session = await agent.CreateSessionAsync();

const string prompt = "Give me the Azure CLI commands to create an Azure Container App with a managed identity.";
Console.WriteLine($"User: {prompt}\n");

AgentResponse response = await agent.RunAsync(prompt, session);
response = await ResolveApprovalRequestsAsync(agent, session, response);

Console.WriteLine($"\nAgent: {response.Text}");

static async Task<AgentResponse> ResolveApprovalRequestsAsync(
    AIAgent agent,
    AgentSession session,
    AgentResponse response)
{
    while (true)
    {
        List<ToolApprovalRequestContent> approvalRequests = response.Messages
            .SelectMany(message => message.Contents)
            .OfType<ToolApprovalRequestContent>()
            .ToList();

        if (approvalRequests.Count == 0)
        {
            return response;
        }

        var approvalResponses = new List<ChatMessage>();

        foreach (ToolApprovalRequestContent approvalRequest in approvalRequests)
        {
            Console.WriteLine("[Approval required]");

            if (approvalRequest.ToolCall is McpServerToolCallContent mcpCall)
            {
                Console.WriteLine($"Server: {mcpCall.ServerName}");
                Console.WriteLine($"Tool: {mcpCall.Name}");
            }
            else
            {
                Console.WriteLine($"Tool call: {approvalRequest.ToolCall}");
            }

            bool approved = approvalRequest.ToolCall is McpServerToolCallContent mcp
                && string.Equals(mcp.ServerName, "api-specs", StringComparison.OrdinalIgnoreCase);

            Console.WriteLine(approved ? "Approved expected MCP call.\n" : "Denied unexpected tool call.\n");

            approvalResponses.Add(new ChatMessage(
                ChatRole.User,
                [approvalRequest.CreateResponse(approved)]));
        }

        response = await agent.RunAsync(approvalResponses, session);
    }
}
```

### Run remote mode

```bash
dotnet run -- remote
```

Expected behavior:

- The app sends the prompt to a Foundry-backed agent.
- The agent requests approval to call the Microsoft Learn MCP server.
- The app approves only the expected `api-specs` MCP server call.
- The final answer includes Azure CLI guidance grounded in Microsoft Learn documentation.

Change the `prompt` constant in `Program.cs` to ask other documentation questions, for example:

```text
How do I configure revision mode for Azure Container Apps?
```

```text
What are the required Azure CLI commands to enable managed identity on an existing Container App?
```

## Run mode 2: Create a local inventory MCP server in C#

This mode creates a local MCP server with inventory tools, then connects those tools to a Foundry-backed agent. The same console app runs in two modes:

- Normal mode: starts the MCP server as a child process, discovers tools, and runs the agent.
- `--server` mode: runs the stdio MCP server and exposes the inventory tools.

The checked-in `Program.cs` uses this core pattern:

```csharp
// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

if (args.Length > 0 && args[0] == "--server")
{
    await RunMcpServerAsync();
    return;
}

string projectEndpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT")
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is not set.");
string deploymentName = Environment.GetEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME")
    ?? throw new InvalidOperationException("AZURE_AI_MODEL_DEPLOYMENT_NAME is not set.");

string assemblyPath = typeof(Program).Assembly.Location;

await using McpClient mcpClient = await McpClient.CreateAsync(
    new StdioClientTransport(new()
    {
        Name = "inventory-server",
        Command = "dotnet",
        Arguments = [assemblyPath, "--server"],
    }));

IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
Console.WriteLine($"Connected to inventory MCP server with tools: {string.Join(", ", mcpTools.Select(tool => tool.Name))}");

var projectClient = new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());

AIAgent agent = projectClient.AsAIAgent(
    model: deploymentName,
    instructions: """
        You are an inventory assistant for a retail store.

        Guidelines:
        - Recommend restock when inventory is below 10 and weekly sales are above 15.
        - Recommend clearance when inventory is above 20 and weekly sales are below 5.
        - Use the available inventory MCP tools before giving inventory, sales, restock, or clearance recommendations.
        """,
    name: "inventory-agent",
    tools: [.. mcpTools.Cast<AITool>()]);

AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine("Type an inventory question, or quit.\n");

while (true)
{
    Console.Write("You: ");
    string? input = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    AgentResponse response = await agent.RunAsync(input, session);
    Console.WriteLine($"\nAgent: {response.Text}\n");
}

static async Task RunMcpServerAsync()
{
    var builder = Host.CreateApplicationBuilder();

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

    builder.Services.AddMcpServer(options =>
        options.ServerInfo = new Implementation { Name = "Inventory", Version = "1.0.0" })
    .WithStdioServerTransport()
    .WithTools<InventoryTools>();

    await builder.Build().RunAsync();
}

#pragma warning disable CA1812
[McpServerToolType]
internal sealed class InventoryTools
#pragma warning restore CA1812
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    [McpServerTool(Name = "get_inventory_levels")]
    [Description("Get current inventory levels for every product.")]
    public static string GetInventoryLevels()
    {
        Dictionary<string, int> inventory = new()
        {
            ["Moisturizer"] = 6,
            ["Shampoo"] = 8,
            ["Body Spray"] = 28,
            ["Sunscreen"] = 14,
            ["Face Wash"] = 32,
            ["Lip Balm"] = 4,
        };

        return JsonSerializer.Serialize(inventory, JsonOptions);
    }

    [McpServerTool(Name = "get_weekly_sales")]
    [Description("Get units sold this week for every product.")]
    public static string GetWeeklySales()
    {
        Dictionary<string, int> sales = new()
        {
            ["Moisturizer"] = 24,
            ["Shampoo"] = 19,
            ["Body Spray"] = 3,
            ["Sunscreen"] = 18,
            ["Face Wash"] = 2,
            ["Lip Balm"] = 21,
        };

        return JsonSerializer.Serialize(sales, JsonOptions);
    }
}
```

### Run inventory mode

```bash
dotnet run -- inventory
```

Try:

```text
Show me the current inventory levels for all products.
```

```text
Are there any products that should be restocked?
```

```text
Which products would you recommend for clearance?
```

```text
What are the best sellers this week?
```

Type `quit` to exit.

Expected behavior:

- The app starts its own MCP server as a child process.
- The MCP client lists `get_inventory_levels` and `get_weekly_sales`.
- The agent invokes those tools when inventory or sales data is needed.
- Follow-up questions keep context through the `AgentSession`.

## How the two MCP patterns differ

| Pattern | Transport | Tool execution owner | Best for |
|---|---|---|---|
| Remote hosted MCP tool | HTTPS to `https://learn.microsoft.com/api/mcp` | Model provider after app approval | Public or service-hosted tools |
| Local stdio MCP server | Child process stdin/stdout | Local C# app | Local tools, local services, development-only workflows |

## Troubleshooting

| Problem | Check |
|---|---|
| `AZURE_AI_PROJECT_ENDPOINT is not set` | Set it in Rider's terminal before `dotnet run -- remote` or `dotnet run -- inventory`. |
| `AZURE_AI_MODEL_DEPLOYMENT_NAME is not set` | Set it to the Foundry model deployment name. |
| Authentication fails | Run `az login` in Rider's terminal and confirm the selected subscription has access to the Foundry project. |
| Remote MCP answer does not mention documentation | Confirm the remote MCP approval was approved and the server name was `api-specs`. |
| Local MCP server does not start | Confirm the project builds and that the app launches itself with `dotnet <assembly> --server`. |
| Local MCP protocol errors | Make sure server-mode logging goes to stderr. Anything written to stdout outside MCP framing corrupts stdio transport. |
| Rate or quota errors | Wait and retry, or use a model deployment with available quota. |

## References

- [Microsoft Agent Framework provider for Microsoft Foundry](https://learn.microsoft.com/en-us/agent-framework/agents/providers/microsoft-foundry)
- [Model Context Protocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [Microsoft Learn MCP server](https://learn.microsoft.com/api/mcp)
