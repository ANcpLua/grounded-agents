# Hosted-FoundryWorkflow

This is the third chapter after [`Hosted-FoundryIQ`](../Hosted-FoundryIQ/README.md) and
[`Hosted-FoundryMcpTools`](../Hosted-FoundryMcpTools/README.md). It composes both into one
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
|---|---|
| Reading text out of a response with pending approvals | `SettledResponse` has no public constructor; the only producers are `Settlement.Classify` (which refuses while approvals are pending) and the `ApprovalPolicy.SettleAsync` loop that drives every request to a recorded decision. |
| A "grounded" answer without retrieval proof | `Grounded<T>` is only produced by `Grounding.Require`, which demands that the tool-call evidence satisfies the stage's `ToolRequirement`; its constructor rejects empty evidence. |
| Consolidating unproven claims | `ConsolidateAsync(Grounded<ProductAdvice>, Grounded<InventoryFacts>)` — the signature is the invariant. There is no overload taking raw strings. |
| Approving an unexpected tool call | `ApprovalPolicy` is a closed union (`AllowServers` / `AllowAllMcp` / `DenyAll`) that denies by default; every decision is recorded and exported as telemetry. |
| Running half-configured | `FoundryBoundary.FromEnvironment` parses all configuration into typed values at the process edge; no other file reads an environment variable. |
| Skipping a pipeline stage | The stage order is enforced by the types: each stage's input is the previous stage's grounded output. |

By design, these are rejected — the first two at compile time, the last at the constructor:

```csharp
consolidator.ConsolidateAsync(adviceText, inventoryText);   // CS1503: strings are not Grounded<T>
requiresApproval.Response.Text;                              // CS1061: only Settlement.Settled has a Response
new Grounded<ProductAdvice>(advice, [], settled);            // throws (ctor is internal; evidence must be non-empty)
```

## Observability

`WorkflowTelemetry` exports OpenTelemetry traces for the workflow spans
(`retail-ops.workflow` -> `product-expert` / `inventory-analyst` / `consolidator`) plus the
Agent Framework and Azure SDK activity sources. Each stage span carries the boundary facts:
the evidence tool list, approval totals, per-decision events, and a grounded/ungrounded/failed
verdict. With `APPLICATIONINSIGHTS_CONNECTION_STRING` set, traces go to Application Insights;
without it they print to the console so runs are inspectable offline.

## Run

Prerequisites are the previous two chapters: the `product-expert-agent` with its Foundry IQ
knowledge base from `Hosted-FoundryIQ`, and a deployed chat model.

```bash
export AZURE_AI_PROJECT_ENDPOINT="https://<account>.services.ai.azure.com/api/projects/<project>"
export AZURE_AI_MODEL_DEPLOYMENT_NAME="gpt-4.1"
# optional: export AGENT_NAME="product-expert-agent"
# optional: export APPLICATIONINSIGHTS_CONNECTION_STRING="..."
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

Offline and credential-free: every boundary guard is driven into its illegal state and must
reject it — empty configuration, evidence-free grounding, partial tool evidence, unlisted MCP
servers, and pending approvals that must refuse to settle.

## Modes

```text
dotnet run -- "<question>"   # full workflow (default question when omitted)
dotnet run -- selftest       # offline boundary-guard proof
dotnet run -- --server       # stdio inventory MCP server (spawned internally by the analyst)
```
