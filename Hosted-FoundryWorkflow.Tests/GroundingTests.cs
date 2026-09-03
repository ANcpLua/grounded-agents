// Copyright (c) Microsoft. All rights reserved.

namespace HostedFoundryWorkflow.Tests;

public sealed class GroundingTests
{
    /// <summary>A case outside the union, so the closing guard can be driven rather than assumed.</summary>
    private sealed record UnknownRequirement : ToolRequirement;

    private static SettledResponse Settled(params ToolCallEvidence[] evidence) =>
        new("the answer", evidence, [], []);

    [Fact]
    public void Require_RequirementOutsideTheUnion_Throws()
    {
        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => Grounding.Require(Settled(), new UnknownRequirement(), settled => settled.Text));

        Assert.Equal("Unreachable: ToolRequirement union is closed.", error.Message);
    }

    [Fact]
    public void Describe_NamesEveryCase()
    {
        Assert.Equal(
            "all of get_inventory_levels, get_weekly_sales",
            ToolRequirement.AllTools("get_inventory_levels", "get_weekly_sales").Describe());
        Assert.Equal("any MCP-server call", ToolRequirement.AnyMcp().Describe());

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(
            () => new UnknownRequirement().Describe());
        Assert.Equal("Unreachable: ToolRequirement union is closed.", error.Message);
    }

    [Fact]
    public void IsSatisfiedBy_IsTheSameRuleRequireUses()
    {
        ToolCallEvidence[] evidence = [new("search", "foundry-iq"), new("get_inventory_levels", null)];

        Assert.True(ToolRequirement.AnyMcp().IsSatisfiedBy(evidence));
        Assert.True(ToolRequirement.AllTools("GET_INVENTORY_LEVELS").IsSatisfiedBy(evidence));
        Assert.False(ToolRequirement.AllTools("get_inventory_levels", "get_weekly_sales").IsSatisfiedBy(evidence));
        Assert.False(ToolRequirement.AnyMcp().IsSatisfiedBy([new("get_inventory_levels", null)]));
    }

    [Fact]
    public void Grounded_EmptyEvidence_Throws()
    {
        SettledResponse origin = Settled();

        ArgumentException error = Assert.Throws<ArgumentException>(
            () => new Grounded<string>("the answer", [], origin));

        Assert.Equal("evidence", error.ParamName);
    }

    [Fact]
    public void Grounded_NonEmptyEvidence_CarriesValueEvidenceAndOrigin()
    {
        SettledResponse origin = Settled(new ToolCallEvidence("search", "foundry-iq"));

        var grounded = new Grounded<string>("the answer", origin.Evidence, origin);

        Assert.Equal("the answer", grounded.Value);
        Assert.Same(origin.Evidence, grounded.Evidence);
        Assert.Same(origin, grounded.Origin);
    }

    [Fact]
    public void Require_AllOfSatisfied_ReturnsSuccessCarryingEvidenceAndOrigin()
    {
        SettledResponse response = Settled(
            new ToolCallEvidence("get_inventory_levels", null),
            new ToolCallEvidence("get_weekly_sales", null));

        StageOutcome<int> outcome = Grounding.Require(
            response,
            ToolRequirement.AllTools("get_inventory_levels", "get_weekly_sales"),
            settled => settled.Text.Length);

        StageOutcome<int>.Success success = Assert.IsType<StageOutcome<int>.Success>(outcome);
        Assert.Equal("the answer".Length, success.Result.Value);
        Assert.Same(response, success.Result.Origin);
        Assert.Equal(2, success.Result.Evidence.Count);
    }

    [Fact]
    public void Require_AllOfSatisfiedWithDifferentCasing_ReturnsSuccess()
    {
        StageOutcome<string> outcome = Grounding.Require(
            Settled(new ToolCallEvidence("GET_WEEKLY_SALES", null)),
            ToolRequirement.AllTools("get_weekly_sales"),
            settled => settled.Text);

        Assert.IsType<StageOutcome<string>.Success>(outcome);
    }

    [Fact]
    public void Require_AllOfPartiallySatisfied_ReturnsUngroundedWithAnswerAndRequirement()
    {
        ToolRequirement requirement = ToolRequirement.AllTools("get_inventory_levels", "get_weekly_sales");

        StageOutcome<string> outcome = Grounding.Require(
            Settled(new ToolCallEvidence("get_inventory_levels", null)),
            requirement,
            settled => settled.Text);

        StageOutcome<string>.Ungrounded ungrounded = Assert.IsType<StageOutcome<string>.Ungrounded>(outcome);
        Assert.Equal("the answer", ungrounded.Answer);
        Assert.Same(requirement, ungrounded.Missing);
    }

    [Fact]
    public void Require_AnyMcpSatisfied_ReturnsSuccess()
    {
        StageOutcome<string> outcome = Grounding.Require(
            Settled(new ToolCallEvidence("search", "foundry-iq")),
            ToolRequirement.AnyMcp(),
            settled => settled.Text);

        StageOutcome<string>.Success success = Assert.IsType<StageOutcome<string>.Success>(outcome);
        Assert.Equal("the answer", success.Result.Value);
    }

    [Fact]
    public void Require_AnyMcpWithOnlyLocalTools_ReturnsUngrounded()
    {
        StageOutcome<string> outcome = Grounding.Require(
            Settled(new ToolCallEvidence("some_local_function", null)),
            ToolRequirement.AnyMcp(),
            settled => settled.Text);

        Assert.IsType<StageOutcome<string>.Ungrounded>(outcome);
    }

    [Fact]
    public void Failed_CarriesTheReason()
    {
        Assert.Equal("no session", new StageOutcome<string>.Failed("no session").Reason);
    }
}
