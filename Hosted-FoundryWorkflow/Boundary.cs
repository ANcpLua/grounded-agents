// Copyright (c) Microsoft. All rights reserved.

namespace HostedFoundryWorkflow;

/// <summary>
/// Parse, don't validate: all environment configuration is converted into typed values once,
/// at the process edge. No other file reads an environment variable, so a half-configured
/// process is not a reachable state past this type.
/// </summary>
public sealed record FoundryBoundary
{
    public required Uri ProjectEndpoint { get; init; }
    public required ModelDeploymentName Deployment { get; init; }
    public required AgentName ProductExpert { get; init; }

    public static FoundryBoundary FromEnvironment() => new()
    {
        ProjectEndpoint = new Uri(Require("AZURE_AI_PROJECT_ENDPOINT"), UriKind.Absolute),
        Deployment = new ModelDeploymentName(Require("AZURE_AI_MODEL_DEPLOYMENT_NAME")),
        ProductExpert = new AgentName(
            Environment.GetEnvironmentVariable("AGENT_NAME") ?? "product-expert-agent"),
    };

    private static string Require(string name) =>
        Environment.GetEnvironmentVariable(name) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException($"{name} is not set.");
}

public readonly record struct ModelDeploymentName
{
    public string Value { get; }

    public ModelDeploymentName(string value) =>
        Value = !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("Model deployment name must be non-empty.", nameof(value));
}

public readonly record struct AgentName
{
    public string Value { get; }

    public AgentName(string value) =>
        Value = !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException("Agent name must be non-empty.", nameof(value));
}

public readonly record struct CustomerQuery
{
    public string Text { get; }

    public CustomerQuery(string text) =>
        Text = !string.IsNullOrWhiteSpace(text)
            ? text
            : throw new ArgumentException("A customer query must be non-empty.", nameof(text));
}
