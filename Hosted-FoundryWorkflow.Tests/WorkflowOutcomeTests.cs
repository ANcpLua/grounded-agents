// Copyright (c) Microsoft. All rights reserved.

namespace HostedFoundryWorkflow.Tests;

/// <summary>
/// The pure record unions that the workflow and its stages hand back. The stages themselves need a
/// live project client and are excluded from coverage; their payload types are not.
/// </summary>
public sealed class WorkflowOutcomeTests
{
    [Fact]
    public void Delivered_CarriesTheGroundedRecommendation()
    {
        var origin = new SettledResponse("the answer", [new ToolCallEvidence("search", "foundry-iq")], []);
        var recommendation = new Grounded<string>("restock the moisturizer", origin.Evidence, origin);

        WorkflowOutcome outcome = new WorkflowOutcome.Delivered(recommendation);

        WorkflowOutcome.Delivered delivered = Assert.IsType<WorkflowOutcome.Delivered>(outcome);
        Assert.Same(recommendation, delivered.Recommendation);
    }

    [Fact]
    public void Stopped_CarriesTheStageAndReason()
    {
        WorkflowOutcome outcome = new WorkflowOutcome.Stopped("product-expert", "answer lacked required evidence");

        WorkflowOutcome.Stopped stopped = Assert.IsType<WorkflowOutcome.Stopped>(outcome);
        Assert.Equal("product-expert", stopped.Stage);
        Assert.Equal("answer lacked required evidence", stopped.Reason);
    }

    [Fact]
    public void ProductAdvice_CarriesTheQueryAndAdvice()
    {
        var query = new CustomerQuery("something for dry skin");
        var advice = new ProductAdvice(query, "try the moisturizer");

        Assert.Equal(query, advice.Query);
        Assert.Equal("try the moisturizer", advice.Advice);
    }

    [Fact]
    public void InventoryFacts_CarriesTheAssessment()
    {
        Assert.Equal("6 in stock", new InventoryFacts("6 in stock").Assessment);
    }
}
