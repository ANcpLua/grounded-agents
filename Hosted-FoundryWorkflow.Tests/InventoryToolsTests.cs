// Copyright (c) Microsoft. All rights reserved.

using System.Text.Json;

namespace HostedFoundryWorkflow.Tests;

public sealed class InventoryToolsTests
{
    private static readonly string[] Products =
        ["Moisturizer", "Shampoo", "Body Spray", "Sunscreen", "Face Wash", "Lip Balm"];

    [Fact]
    public void GetInventoryLevels_ReturnsAStockCountForEveryProduct()
    {
        Dictionary<string, int> inventory = Deserialize(InventoryTools.GetInventoryLevels());

        Assert.Equal(Products.Order(), inventory.Keys.Order());
        Assert.All(inventory.Values, count => Assert.True(count >= 0));
        Assert.Equal(6, inventory["Moisturizer"]);
        Assert.Equal(4, inventory["Lip Balm"]);
    }

    [Fact]
    public void GetWeeklySales_ReturnsAUnitCountForEveryProduct()
    {
        Dictionary<string, int> sales = Deserialize(InventoryTools.GetWeeklySales());

        Assert.Equal(Products.Order(), sales.Keys.Order());
        Assert.All(sales.Values, count => Assert.True(count >= 0));
        Assert.Equal(24, sales["Moisturizer"]);
        Assert.Equal(2, sales["Face Wash"]);
    }

    [Fact]
    public void InventoryAndSales_CoverTheSameProducts()
    {
        Assert.Equal(
            Deserialize(InventoryTools.GetInventoryLevels()).Keys.Order(),
            Deserialize(InventoryTools.GetWeeklySales()).Keys.Order());
    }

    private static Dictionary<string, int> Deserialize(string json)
    {
        Dictionary<string, int>? parsed = JsonSerializer.Deserialize<Dictionary<string, int>>(json);
        Assert.NotNull(parsed);
        return parsed;
    }
}
