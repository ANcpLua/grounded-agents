// Copyright (c) Microsoft. All rights reserved.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Azure.Monitor.OpenTelemetry.Exporter;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace HostedFoundryWorkflow;

/// <summary>
/// Workflow telemetry: one ActivitySource whose spans carry the boundary facts —
/// which stage ran, what evidence it produced, and every approval decision.
/// Exports to Application Insights when APPLICATIONINSIGHTS_CONNECTION_STRING is set,
/// otherwise to the console so traces are inspectable offline.
/// </summary>
public static class WorkflowTelemetry
{
    public const string SourceName = "RetailOpsWorkflow";

    public static readonly ActivitySource Source = new(SourceName);

    // Builds live exporters (console or Application Insights), so it is composed at startup
    // rather than under test.
    [ExcludeFromCodeCoverage]
    public static TracerProvider Create()
    {
        // Azure SDK ActivitySources are still behind an experimental switch.
        AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);

        TracerProviderBuilder builder = Sdk.CreateTracerProviderBuilder()
            .SetResourceBuilder(ResourceBuilder.CreateDefault().AddService("retail-ops-workflow"))
            .AddSource(SourceName)
            .AddSource("Microsoft.Agents.AI*")
            .AddSource("Experimental.Microsoft.Extensions.AI*")
            .AddSource("Azure.*");

        return Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")
                is { Length: > 0 } connectionString
            ? builder.AddAzureMonitorTraceExporter(options => options.ConnectionString = connectionString).Build()
            : builder.AddConsoleExporter().Build();
    }

    public static void RecordSettled(this Activity? activity, SettledResponse settled)
    {
        activity?.SetTag("workflow.evidence.tools", string.Join(",", settled.Evidence.Select(item =>
            item.McpServer is null ? item.Tool : $"{item.McpServer}/{item.Tool}")));
        activity?.SetTag("workflow.approvals.total", settled.Decisions.Count);
        activity?.SetTag("workflow.approvals.denied", settled.Decisions.Count(decision => !decision.Approved));

        foreach (ApprovalDecision decision in settled.Decisions)
        {
            activity?.AddEvent(new ActivityEvent("approval.decision", tags: new ActivityTagsCollection
            {
                ["server"] = decision.Server,
                ["tool"] = decision.Tool,
                ["approved"] = decision.Approved,
                ["reason"] = decision.Reason,
            }));
        }
    }

    public static void RecordOutcome<T>(this Activity? activity, StageOutcome<T> outcome)
    {
        switch (outcome)
        {
            case StageOutcome<T>.Success success:
                activity?.SetTag("workflow.stage.verdict", "grounded");
                activity.RecordSettled(success.Result.Origin);
                break;
            case StageOutcome<T>.Ungrounded ungrounded:
                activity?.SetTag("workflow.stage.verdict", "ungrounded");
                activity?.SetTag("workflow.stage.missing", ungrounded.Missing.ToString());
                activity?.SetStatus(ActivityStatusCode.Error, "answer lacked required tool evidence");
                break;
            case StageOutcome<T>.Failed failed:
                activity?.SetTag("workflow.stage.verdict", "failed");
                activity?.SetStatus(ActivityStatusCode.Error, failed.Reason);
                break;
        }
    }
}
