// Copyright (c) Microsoft. All rights reserved.

using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace HostedFoundryWorkflow;

/// <summary>
/// The stdio inventory MCP server hosted by this executable (spawned with --server),
/// backed by a small mock inventory.
/// </summary>
[ExcludeFromCodeCoverage(Justification = "Hosts a stdio server that blocks until the transport closes.")]
public static class InventoryMcpServer
{
    public static async Task RunAsync()
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
