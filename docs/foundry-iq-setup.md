# Foundry IQ setup

The Product Expert stage of [Hosted-FoundryWorkflow](../Hosted-FoundryWorkflow/README.md)
requires a Foundry agent with a Foundry IQ knowledge base. This guide creates both in the
Microsoft Foundry portal: a project, an agent named `product-expert-agent`, and a Foundry IQ
knowledge base backed by Azure AI Search and sample Contoso product PDFs. The workflow then
retrieves and invokes the agent by name.

> Foundry IQ is in preview. The setup below intentionally uses the Microsoft Foundry portal for
> the knowledge base and tool connection.

## Prerequisites

- An Azure subscription with permission to create Foundry, Azure AI Search, and Storage resources.
- Basic familiarity with the Microsoft Foundry portal.

## Create a Foundry project

1. Open the [Microsoft Foundry portal](https://ai.azure.com) and sign in.
2. Turn on the New Foundry experience if the portal asks.
3. Select **Create a new project**.
4. Name the project, for example `agent-iq-lab`.
5. Configure the project:

| Setting | Value |
|---|---|
| Foundry resource | Create a new resource or select an existing one |
| Subscription | Your Azure subscription |
| Resource group | Create or select a resource group |
| Location | Any available region with the model quota you need |

6. Select **Create** and wait for the project home page.

If model quota is unavailable in your selected region later, create the project in another supported region.

## Create the agent

1. In the project home page, open **Build**.
2. On the **Agents** tab, select **Create agent**.
3. Name the agent `product-expert-agent`.
4. Use the default deployed chat model if the portal creates one, or select a deployed chat model such as `gpt-4.1`.
5. In the agent instructions, paste:

```text
You are a helpful AI assistant for Contoso, specializing in outdoor camping and hiking products.

You must ALWAYS search the knowledge base to answer questions about our products or product catalog.
Provide detailed, accurate information and always cite your sources.

If you don't find relevant information in the knowledge base, say so clearly.
```

6. Save the agent.

## Configure Foundry IQ

Foundry IQ uses Azure AI Search as the retrieval layer. In this guide, the portal creates or connects the Foundry IQ objects, and the workflow calls the finished agent.

### Create or connect the search resource

1. In the agent page, open the **Knowledge** section.
2. Select **Add** > **Connect to Foundry IQ**.
3. Select **Connect to an AI Search resource**.
4. Create a new Azure AI Search resource if you do not already have one.

Use these settings:

| Setting | Value |
|---|---|
| Resource name | A globally unique search service name |
| Subscription | Your Azure subscription |
| Resource group | Same resource group as the Foundry project |
| Region | Same region as the Foundry project |
| Pricing tier | Free if available, otherwise Basic |

### Upload the sample product PDFs

1. Download the sample Contoso product files:

```text
https://github.com/MicrosoftLearning/mslearn-ai-agents/raw/main/Labfiles/04-integrate-agent-with-foundry-iq/data/contoso-products.zip
```

2. Extract the zip. It should contain three PDF product documents.
3. Open the [Azure portal](https://portal.azure.com).
4. Create a Storage account in the same subscription, resource group, and region as the Foundry project.

Use these settings:

| Setting | Value |
|---|---|
| Primary service | Azure Blob Storage or Azure Data Lake Storage |
| Performance | Standard |
| Redundancy | Locally redundant storage (LRS) |

5. Open the Storage account.
6. Upload the three PDFs to a new blob container named `contosoproducts`.

### Allow the search service connection

1. Open the Azure AI Search service that the portal created or connected.
2. In **Security + networking** > **Keys**, set API access control to **Both** if the Foundry portal setup requires key-based access.
3. Leave the Azure portal tab open on the Search keys page.

### Create the Foundry IQ knowledge base

1. Return to the Foundry portal and refresh.
2. In the Knowledge page, select **Create a knowledge base**.
3. Choose **Azure Blob Storage** as the knowledge source and select **Connect**.
4. Configure the knowledge source:

| Setting | Value |
|---|---|
| Name | `ks-contosoproducts` |
| Description | `Contoso product catalog items` |
| Storage account | The Storage account created above |
| Container | `contosoproducts` |
| Authentication type | API key if you followed the key-based path |
| Content extraction mode | `minimal` |
| Embedding model | The available embedding deployment, usually `text-embedding-3-small` |
| Chat completions model | The available chat deployment, usually `gpt-4.1` |

5. Select **Create**.
6. On the knowledge base page, keep the defaults unless your tenant requires different retrieval settings.
7. Select **Save knowledge base**.
8. Refresh until the knowledge source status is active.

### Add Foundry IQ to the agent

1. Return to **Build** > **Agents** and open `product-expert-agent`.
2. In the playground, find the **Knowledge** section.
3. Add Foundry IQ.
4. Select the connection and knowledge base you created.
5. Save the agent.

If the portal asks for Search authentication:

1. Open the **Manage** link next to the Knowledge connection.
2. Select the connected Azure AI Search resource.
3. Edit authentication.
4. Copy one Search key from the Azure portal and save it in the Foundry dialog.

## Test the agent in the portal

Before running the workflow, verify that the portal agent retrieves from Foundry IQ.

Try:

```text
What types of tents does Contoso offer?
```

```text
Tell me about which backpacks are available in XL.
```

```text
What camping accessories are available?
```

Expected behavior:

- Answers contain specific Contoso product details.
- Answers include citations or source references when the service returns them.
- The agent says when it cannot find relevant knowledge.
- Product questions trigger the Foundry IQ knowledge tool.

Copy these values before leaving the portal:

| Value | Where to find it |
|---|---|
| Agent name | The name you created, `product-expert-agent` |
| Project endpoint | Project home page or project settings, usually `https://<account>.services.ai.azure.com/api/projects/<project>` |

Use them for `AZURE_AI_PROJECT_ENDPOINT` and, if you chose a different name, `AGENT_NAME` when
you run the workflow.

## Troubleshooting

| Problem | Check |
|---|---|
| Authentication fails | Run `az login` and confirm the selected subscription contains the Foundry project. |
| Agent cannot be found | Confirm `AGENT_NAME` exactly matches the Foundry agent name. |
| 403 from Foundry | Confirm your identity has access to the Foundry project and the parent Foundry resource. |
| Foundry IQ returns no product details | Confirm the knowledge source status is active and the agent has the Foundry IQ knowledge base attached. |
| No citations appear | Retest in the portal playground; citation shape can vary by model and Foundry IQ response. |

## References

- [Connect a Foundry IQ knowledge base to Foundry Agent Service](https://learn.microsoft.com/en-us/azure/foundry/agents/how-to/foundry-iq-connect)
- [Create a knowledge base in Azure AI Search](https://learn.microsoft.com/en-us/azure/search/agentic-retrieval-how-to-create-knowledge-base)
