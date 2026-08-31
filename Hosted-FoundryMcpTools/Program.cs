// Copyright (c) Microsoft. All rights reserved.

// This sample is the C# follow-up to Hosted-FoundryIQ. It demonstrates two MCP paths:
// 1. A remote hosted MCP tool that lets the provider call the Microsoft Learn MCP server.
// 2. A local stdio MCP server that exposes inventory tools from this same executable.

#pragma warning disable MEAI001 // HostedMcpServerTool is experimental.

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

string mode = args.Length > 0 ? args[0] : "remote";

if (mode.Equals("inventory", StringComparison.OrdinalIgnoreCase))
{
    await RunInventoryMcpClientAsync();
}
else if (mode.Equals("remote", StringComparison.OrdinalIgnoreCase))
{
    await RunRemoteMcpClientAsync();
}
else
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- remote");
    Console.WriteLine("  dotnet run -- inventory");
}

static async Task RunRemoteMcpClientAsync()
{
    string deploymentName = GetRequiredEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME");
    AIProjectClient projectClient = CreateProjectClient();

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
    Console.WriteLine($"User: {prompt}");
    Console.WriteLine();

    AgentResponse response = await agent.RunAsync(prompt, session);
    response = await ResolveApprovalRequestsAsync(agent, session, response);

    Console.WriteLine();
    Console.WriteLine($"Agent: {response.Text}");
}

static async Task RunInventoryMcpClientAsync()
{
    string deploymentName = GetRequiredEnvironmentVariable("AZURE_AI_MODEL_DEPLOYMENT_NAME");
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

    AIProjectClient projectClient = CreateProjectClient();

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

    Console.WriteLine("Type an inventory question, or quit.");
    Console.WriteLine();

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
        Console.WriteLine();
        Console.WriteLine($"Agent: {response.Text}");
        Console.WriteLine();
    }
}

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

            Console.WriteLine(approved ? "Approved expected MCP call." : "Denied unexpected tool call.");
            Console.WriteLine();

            approvalResponses.Add(new ChatMessage(
                ChatRole.User,
                [approvalRequest.CreateResponse(approved)]));
        }

        response = await agent.RunAsync(approvalResponses, session);
    }
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

static AIProjectClient CreateProjectClient()
{
    string projectEndpoint = GetRequiredEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");

    // WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
    // In production, consider using a specific credential such as ManagedIdentityCredential.
    return new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential());
}

static string GetRequiredEnvironmentVariable(string name)
{
    return Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is not set.");
}

#pragma warning disable CA1812 // Discovered by MCP SDK via [McpServerToolType] attribute.
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
