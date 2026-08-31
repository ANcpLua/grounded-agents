// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using ANcpLua.Agents.Testing.Diagnostics;

namespace HostedFoundryWorkflow.Tests;

/// <summary>
/// One class so the <see cref="ActivityListener"/> registrations do not overlap: the null-activity
/// cases below depend on no listener being attached to the workflow source at that moment.
/// </summary>
public sealed class TelemetryTests
{
    private static SettledResponse Settled(
        IReadOnlyList<ToolCallEvidence> evidence,
        IReadOnlyList<ApprovalDecision> decisions) => new("the answer", evidence, decisions);

    private static Activity Record(Action<Activity?> record)
    {
        using var collector = new ActivityCollector(WorkflowTelemetry.SourceName);

        using (Activity? activity = WorkflowTelemetry.Source.StartActivity("retail-ops.stage"))
        {
            Assert.NotNull(activity);
            record(activity);
        }

        return collector.FindSingle("retail-ops.stage");
    }

    [Fact]
    public void RecordSettled_EvidenceAndDecisions_AreTaggedOnTheActivity()
    {
        Activity activity = Record(span => span.RecordSettled(Settled(
            [new ToolCallEvidence("search", "foundry-iq"), new ToolCallEvidence("get_weekly_sales", null)],
            [
                new ApprovalDecision("foundry-iq", "search", Approved: true, "allow-listed"),
                new ApprovalDecision("evil-server", "exfiltrate", Approved: false, "denied by default"),
            ])));

        activity
            .AssertTag("workflow.evidence.tools", "foundry-iq/search,get_weekly_sales")
            .AssertTag("workflow.approvals.total", 2)
            .AssertTag("workflow.approvals.denied", 1)
            .AssertHasEvent("approval.decision");

        Assert.Equal(2, activity.Events.Count(activityEvent => activityEvent.Name == "approval.decision"));
    }

    [Fact]
    public void RecordSettled_NoDecisions_TagsZeroCountsAndAddsNoEvents()
    {
        Activity activity = Record(span => span.RecordSettled(Settled([], [])));

        activity
            .AssertTag("workflow.evidence.tools", string.Empty)
            .AssertTag("workflow.approvals.total", 0)
            .AssertTag("workflow.approvals.denied", 0);

        Assert.Empty(activity.Events);
    }

    [Fact]
    public void RecordOutcome_Success_TagsGroundedAndRecordsTheOriginEvidence()
    {
        SettledResponse origin = Settled([new ToolCallEvidence("search", "foundry-iq")], []);

        Activity activity = Record(span => span.RecordOutcome(
            new StageOutcome<string>.Success(new Grounded<string>("the answer", origin.Evidence, origin))));

        activity
            .AssertTag("workflow.stage.verdict", "grounded")
            .AssertTag("workflow.evidence.tools", "foundry-iq/search")
            .AssertStatus(ActivityStatusCode.Unset);
    }

    [Fact]
    public void RecordOutcome_Ungrounded_TagsTheMissingRequirementAndFailsTheSpan()
    {
        ToolRequirement requirement = ToolRequirement.AnyMcp();

        Activity activity = Record(span => span.RecordOutcome(
            new StageOutcome<string>.Ungrounded("the answer", requirement)));

        activity
            .AssertTag("workflow.stage.verdict", "ungrounded")
            .AssertTag("workflow.stage.missing", requirement.ToString())
            .AssertStatus(ActivityStatusCode.Error);

        Assert.Equal("answer lacked required tool evidence", activity.StatusDescription);
    }

    [Fact]
    public void RecordOutcome_Failed_TagsTheReasonAsTheSpanStatus()
    {
        Activity activity = Record(span => span.RecordOutcome(new StageOutcome<string>.Failed("no session")));

        activity
            .AssertTag("workflow.stage.verdict", "failed")
            .AssertStatus(ActivityStatusCode.Error);

        Assert.Equal("no session", activity.StatusDescription);
    }

    [Fact]
    public void RecordSettled_NullActivity_DoesNotThrow()
    {
        Activity? activity = null;

        activity.RecordSettled(Settled(
            [new ToolCallEvidence("search", "foundry-iq")],
            [new ApprovalDecision("foundry-iq", "search", Approved: true, "allow-listed")]));
    }

    [Fact]
    public void RecordOutcome_NullActivity_DoesNotThrowForAnyCase()
    {
        Activity? activity = null;
        SettledResponse origin = Settled([new ToolCallEvidence("search", "foundry-iq")], []);

        activity.RecordOutcome(
            new StageOutcome<string>.Success(new Grounded<string>("the answer", origin.Evidence, origin)));
        activity.RecordOutcome(new StageOutcome<string>.Ungrounded("the answer", ToolRequirement.AnyMcp()));
        activity.RecordOutcome(new StageOutcome<string>.Failed("no session"));
    }

    [Fact]
    public void SourceName_MatchesTheActivitySourceName()
    {
        Assert.Equal("RetailOpsWorkflow", WorkflowTelemetry.SourceName);
        Assert.Equal(WorkflowTelemetry.SourceName, WorkflowTelemetry.Source.Name);
    }
}
