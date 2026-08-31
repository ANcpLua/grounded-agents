// Copyright (c) Microsoft. All rights reserved.

// This sample connects to a Foundry-managed agent that has Foundry IQ configured in the
// Microsoft Foundry portal. The local app retrieves that existing agent by name, keeps a
// stateful AgentSession, and handles MCP approval requests returned by the Foundry IQ tool.

using Azure.AI.Projects;
using Azure.AI.Projects.Agents;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

string projectEndpoint = GetRequiredEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT");
string agentName = GetRequiredEnvironmentVariable("AGENT_NAME");

// WARNING: DefaultAzureCredential is convenient for development but requires careful consideration in production.
// In production, consider using a specific credential such as ManagedIdentityCredential.
var credential = new DefaultAzureCredential();
var projectClient = new AIProjectClient(new Uri(projectEndpoint), credential);

ProjectsAgentRecord agentRecord = await projectClient.AgentAdministrationClient.GetAgentAsync(agentName);
AIAgent agent = projectClient.AsAIAgent(agentRecord);
AgentSession session = await agent.CreateSessionAsync();

Console.WriteLine($"Connected to agent: {agent.Name}");
Console.WriteLine("Type a product question, or quit.");
Console.WriteLine();

while (true)
{
    Console.Write("You: ");
    string? input = Console.ReadLine();

    if (input is null)
    {
        // End of input stream (Ctrl+D, or exhausted piped input). Without this guard,
        // IsNullOrWhiteSpace(null) is true and the loop `continue`s forever, spinning the
        // CPU on a closed stdin instead of exiting.
        break;
    }

    if (string.IsNullOrWhiteSpace(input))
    {
        continue;
    }

    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase))
    {
        break;
    }

    try
    {
        AgentResponse response = await agent.RunAsync(input, session);
        response = await ResolveApprovalRequestsAsync(agent, session, response);

        Console.WriteLine();
        Console.WriteLine($"Agent: {response.Text}");
        Console.WriteLine();
    }
    catch (Exception ex)
    {
        Console.WriteLine();
        Console.WriteLine($"Error: {ex.Message}");
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
            Console.WriteLine();
            Console.WriteLine("[Approval required]");

            if (approvalRequest.ToolCall is McpServerToolCallContent mcpCall)
            {
                Console.WriteLine($"Server: {mcpCall.ServerName}");
                Console.WriteLine($"Tool: {mcpCall.Name}");

                if (mcpCall.Arguments is not null)
                {
                    foreach (KeyValuePair<string, object?> argument in mcpCall.Arguments)
                    {
                        Console.WriteLine($"{argument.Key}: {argument.Value}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"Tool call: {approvalRequest.ToolCall}");
            }

            Console.Write("Approve this action? (yes/no): ");
            string? approvalInput = Console.ReadLine();
            bool approved = approvalInput?.Equals("yes", StringComparison.OrdinalIgnoreCase) is true
                || approvalInput?.Equals("y", StringComparison.OrdinalIgnoreCase) is true;

            approvalResponses.Add(new ChatMessage(
                ChatRole.User,
                [approvalRequest.CreateResponse(approved)]));
        }

        response = await agent.RunAsync(approvalResponses, session);
    }
}

static string GetRequiredEnvironmentVariable(string name)
{
    return Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"{name} is not set.");
}
