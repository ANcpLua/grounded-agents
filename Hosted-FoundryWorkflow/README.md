# Hosted-FoundryWorkflow

This project composes a Foundry IQ knowledge agent and an MCP tool agent into one
multi-agent retail-ops workflow, and designs the Foundry boundary so that the illegal states
of an agent pipeline are unrepresentable in the type system.

```text
CustomerQuery
  -> ProductExpert   (Foundry-managed agent, Foundry IQ knowledge base)   => Grounded<ProductAdvice>
  -> InventoryAnalyst (local stdio inventory MCP tools)                    => Grounded<InventoryFacts>
  -> Consolidator     (pure synthesis, no tools)                           => WorkflowOutcome
```

## The invariants

| Illegal state | Why it cannot be represented |
| --- | --- |
| Reading text out of a response with pending approvals | `SettledResponse` has no public constructor; the only producers are `Settlement.Classify` (which refuses while approvals are pending) and the `ApprovalPolicy.SettleAsync` loop that drives every request to a recorded decision. |
| A "grounded" answer without retrieval proof | `Grounded<T>` is only produced by `Grounding.Require`, which demands that the tool-call evidence satisfies the stage's `ToolRequirement`; its constructor rejects empty evidence. |
| Consolidating unproven claims | `ConsolidateAsync(Grounded<ProductAdvice>, Grounded<InventoryFacts>)`, the signature is the invariant. There is no overload taking raw strings. |
| Approving an unexpected tool call | `ApprovalPolicy` is a closed union (`AllowServers` / `AllowAllMcp` / `DenyAll`) that denies by default; every decision is recorded and exported as telemetry. |
| Running half-configured | `FoundryBoundary.FromEnvironment` parses all configuration into typed values at the process edge; no other file reads an environment variable. |
| Skipping a pipeline stage | The stage order is enforced by the types: each stage's input is the previous stage's grounded output. |

By design, these are rejected, the first two at compile time, the last at the constructor:

```csharp
consolidator.ConsolidateAsync(adviceText, inventoryText);   // CS1503: strings are not Grounded<T>
requiresApproval.Response.Text;                              // CS1061: only Settlement.Settled has a Response
new Grounded<ProductAdvice>(advice, [], settled);            // throws (ctor is internal; evidence must be non-empty)
```

## Observability

`WorkflowTelemetry` publishes the `RetailOpsWorkflow` activity source and exports the workflow
spans (`retail-ops.workflow` -> `retail-ops.product-expert` / `retail-ops.inventory-analyst` /
`retail-ops.consolidator`) plus the Agent Framework and Azure SDK activity sources. Each stage
span carries the boundary facts: the evidence tool list, approval totals, per-decision events,
and a grounded/ungrounded/failed verdict. With `APPLICATIONINSIGHTS_CONNECTION_STRING` set,
traces go to Application Insights through the Azure Monitor exporter; without it they print to
the console so runs are inspectable offline.

## Run

The workflow requires a Foundry agent with a Foundry IQ knowledge base: the `product-expert-agent`,
set up with [docs/foundry-iq-setup.md](../docs/foundry-iq-setup.md), and a deployed chat model.

```bash
export AZURE_AI_PROJECT_ENDPOINT="https://<account>.services.ai.azure.com/api/projects/<project>"
export AZURE_AI_MODEL_DEPLOYMENT_NAME="gpt-4.1"
# optional: export AGENT_NAME="product-expert-agent"
# optional: export APPLICATIONINSIGHTS_CONNECTION_STRING="..."
# on a developer machine: skip the managed-identity probe, use the CLI login directly
export AZURE_TOKEN_CREDENTIALS=AzureCliCredential
az login

dotnet run -- "I need something for dry skin and sun protection. What should I buy, and is it in stock?"
```

The process exits `0` only when the workflow delivers a consolidated recommendation whose
every stage was settled and grounded; any ungrounded answer, denied tool call, or failed
stage stops the pipeline with exit `1` and an error span.

## Selftest

```bash
dotnet run -- selftest
```

Offline and credential-free: 14 checks drive every boundary guard into its illegal state and
require it to reject, empty configuration, evidence-free grounding, partial tool evidence,
unlisted MCP servers, and pending approvals that must refuse to settle. The same guards are
covered by the [test project](../Hosted-FoundryWorkflow.Tests/).

## Evaluation gate

```bash
dotnet run -- eval
```

Runs a fail-closed evaluation suite against the two tool-using stage agents, and it evaluates
them the way the workflow runs them:

- Each agent is wrapped in a `SettledAgent`, which drives every response to settlement under the
  stage's own `ApprovalPolicy` and returns the settled transcript. A managed agent whose knowledge
  tool requires approval would otherwise answer the gate with nothing but the approval request.
- Each suite's expectation is the stage's own `ToolRequirement` (`ProductExpert.Requirement`,
  `InventoryAnalyst.Requirement`), scored over that transcript by `RequirementChecks.Satisfies`.
  It uses the boundary's evidence extraction, so MCP-server calls such as the Foundry IQ
  `knowledge_base_retrieve` count; the framework's built-in tool-call checks only see local
  function calls.

Setting `AI_EVAL_FOUNDRY=1` adds Foundry server-side evaluators (relevance, groundedness,
coherence). The process exits `0` only when every stage ran and every item passed.

## Modes

```text
dotnet run -- "<question>"   # full workflow (default question when omitted)
dotnet run -- selftest       # offline boundary-guard proof
dotnet run -- eval           # evaluation gate over the stage agents
dotnet run -- --server       # stdio inventory MCP server (spawned internally by the analyst)
```

## Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| `managed_identity_all_sources_unavailable`, process aborts after about 75 s | `DefaultAzureCredential` probed the instance metadata endpoint and treated the timeout as a failure instead of falling through to the CLI. Set `AZURE_TOKEN_CREDENTIALS=AzureCliCredential`. |
| `HTTP 429 rate_limit_exceeded` during `eval` | The gate runs four agent turns plus the workflow's tool rounds in quick succession. Raise the chat deployment's tokens-per-minute capacity or wait a minute. |
| `Workflow stopped at product-expert` | The stage's evidence has no MCP-server call. Check that the agent has the knowledge base attached as an MCP tool; the span's `workflow.evidence.tools` tag lists what was actually called. |
| `Workflow stopped at inventory-analyst` | The model called only one of the two inventory tools. That is the gate working as designed; tune the instructions in `Agents.cs` and rerun `eval`. |
