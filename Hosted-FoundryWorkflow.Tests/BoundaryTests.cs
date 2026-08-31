// Copyright (c) Microsoft. All rights reserved.

namespace HostedFoundryWorkflow.Tests;

/// <summary>
/// All environment-variable driven assertions live in this one class: xunit runs the tests of a
/// single class sequentially, which keeps the process-wide variables from being raced.
/// </summary>
public sealed class BoundaryTests : IDisposable
{
    private const string EndpointVariable = "AZURE_AI_PROJECT_ENDPOINT";
    private const string DeploymentVariable = "AZURE_AI_MODEL_DEPLOYMENT_NAME";
    private const string AgentVariable = "AGENT_NAME";

    private readonly string? _savedEndpoint = Environment.GetEnvironmentVariable(EndpointVariable);
    private readonly string? _savedDeployment = Environment.GetEnvironmentVariable(DeploymentVariable);
    private readonly string? _savedAgent = Environment.GetEnvironmentVariable(AgentVariable);

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(EndpointVariable, _savedEndpoint);
        Environment.SetEnvironmentVariable(DeploymentVariable, _savedDeployment);
        Environment.SetEnvironmentVariable(AgentVariable, _savedAgent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void CustomerQuery_EmptyText_Throws(string text)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new CustomerQuery(text));
        Assert.Equal("text", error.ParamName);
    }

    [Fact]
    public void CustomerQuery_NonEmptyText_StoresText()
    {
        Assert.Equal("dry skin", new CustomerQuery("dry skin").Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ModelDeploymentName_EmptyValue_Throws(string value)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new ModelDeploymentName(value));
        Assert.Equal("value", error.ParamName);
    }

    [Fact]
    public void ModelDeploymentName_NonEmptyValue_StoresValue()
    {
        Assert.Equal("gpt-4o-mini", new ModelDeploymentName("gpt-4o-mini").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void AgentName_EmptyValue_Throws(string value)
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => new AgentName(value));
        Assert.Equal("value", error.ParamName);
    }

    [Fact]
    public void AgentName_NonEmptyValue_StoresValue()
    {
        Assert.Equal("product-expert-agent", new AgentName("product-expert-agent").Value);
    }

    [Fact]
    public void FromEnvironment_AllVariablesSet_ParsesEveryValue()
    {
        Environment.SetEnvironmentVariable(EndpointVariable, "https://contoso.services.ai.azure.com/api/projects/demo");
        Environment.SetEnvironmentVariable(DeploymentVariable, "gpt-4o-mini");
        Environment.SetEnvironmentVariable(AgentVariable, "custom-expert");

        FoundryBoundary boundary = FoundryBoundary.FromEnvironment();

        Assert.Equal(new Uri("https://contoso.services.ai.azure.com/api/projects/demo"), boundary.ProjectEndpoint);
        Assert.Equal("gpt-4o-mini", boundary.Deployment.Value);
        Assert.Equal("custom-expert", boundary.ProductExpert.Value);
    }

    [Fact]
    public void FromEnvironment_AgentNameUnset_UsesDefaultAgentName()
    {
        Environment.SetEnvironmentVariable(EndpointVariable, "https://contoso.services.ai.azure.com/api/projects/demo");
        Environment.SetEnvironmentVariable(DeploymentVariable, "gpt-4o-mini");
        Environment.SetEnvironmentVariable(AgentVariable, null);

        Assert.Equal("product-expert-agent", FoundryBoundary.FromEnvironment().ProductExpert.Value);
    }

    [Fact]
    public void FromEnvironment_EndpointUnset_Throws()
    {
        Environment.SetEnvironmentVariable(EndpointVariable, null);
        Environment.SetEnvironmentVariable(DeploymentVariable, "gpt-4o-mini");

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(FoundryBoundary.FromEnvironment);

        Assert.Equal($"{EndpointVariable} is not set.", error.Message);
    }

    [Fact]
    public void FromEnvironment_DeploymentUnset_Throws()
    {
        Environment.SetEnvironmentVariable(EndpointVariable, "https://contoso.services.ai.azure.com/api/projects/demo");
        Environment.SetEnvironmentVariable(DeploymentVariable, null);

        InvalidOperationException error =
            Assert.Throws<InvalidOperationException>(FoundryBoundary.FromEnvironment);

        Assert.Equal($"{DeploymentVariable} is not set.", error.Message);
    }
}
