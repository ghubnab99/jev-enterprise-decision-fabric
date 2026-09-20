# Jev Enterprise Decision Fabric

An experimental, production-oriented architecture for using TypeSafe Jev across many semantic decision points without scattering model calls, question text, thresholds and side effects throughout an application.

> Status: first implementation slice. The repository is intentionally private while the evaluation harness and public claims are validated.

## The problem

Fast semantic judgment makes it practical to place System One decisions inside ordinary software control flow. A serious application may eventually contain hundreds of such decisions. Treating each one as an ad hoc API call creates a new form of AI spaghetti: duplicated questions, inconsistent thresholds, invisible model changes, weak observability and unsafe side effects.

This project separates four concerns:

1. **Versioned semantic contracts** — atomic Noul, Choice and Score questions.
2. **Provider execution** — one batched Jev request for questions sharing the same state.
3. **Typed evidence** — probabilities, confidence, model version, usage and latency.
4. **Deterministic policy** — thresholds, independent evidence, linguistic-risk gates, confirmation, human review and System 2 escalation.

See [the architecture direction](docs/architecture.md).

## Public API

Each decision point is a **decision pack**: a versioned contract, a mapping from
domain input to model state, and a deterministic policy. One fabric evaluates
any pack with a single batched request and validates the answers before the
policy runs.

```csharp
builder.Services.AddDecisionFabric(builder.Configuration);   // Fixture or TypeSafe
builder.Services.AddSingleton<PaymentDisputePack>();

var result = await fabric.EvaluateAsync(pack, new PaymentDisputeInput(message), ct);

// result.Outcome        — the pack's typed domain outcome
// result.Evidence       — validated Noul / Choice / Score answers
// result.Model, ContractVersion, PolicyVersion, Duration, Usage
```

Payment disputes and the Agent Action Gate both use this API; the fabric has
no domain-specific code.

## Repository layout

```text
src/
  DecisionFabric.Core/       contracts, answers, decision packs, fabric, evidence validation
  DecisionFabric.TypeSafe/   direct Jev HTTP provider with retry handling
  DecisionFabric.Policy/     deterministic routing around uncertainty
  DecisionFabric.Hosting/    DI registration and fixture/live provider selection
evals/
  DecisionFabric.Evals/      JSON-driven repeatable evaluation runner
  datasets/                  versioned labelled cases
samples/
  DecisionFabric.PaymentDisputes.Api/  end-to-end payment dispute decision API
  DecisionFabric.AgentActionGate.Api/  agent tool-call authorization API
  DecisionFabric.Inspector/            dashboard over recorded evaluation runs
tests/
  DecisionFabric.Tests/      contract, fabric, policy, snapshot and HTTP tests
```

## First evaluation suite

`payment-card-block-negation-v1` makes 140 calls covering:

- exact repeatability;
- contraction versus expanded negation;
- meaning-preserving paraphrases;
- clear positive and negative boundaries;
- missing evidence;
- genuine ambiguity; and
- double-negation stress.

Each request batches three independent decisions against the same state:

- `block_card_requested` — Noul;
- `primary_intent` — Choice; and
- `urgency` — Score.

The runner writes one JSON object per run with the state case, contract version, returned model version, complete typed answers, action-policy decision, usage, latency, expectation result and error details. It also emits an indented JSON report containing per-case decision-band and primary-choice flips plus declared metamorphic comparisons.

The destructive-action gate authorizes an action only when the Noul result is above the positive boundary, the independent Choice result agrees, Choice confidence clears the configured minimum, and no deterministic linguistic-risk signal is present. Anything uncertain or risky requires confirmation; a clear negative is not authorized.

The first thresholds are hypotheses for evaluation—not production guarantees. They must be recalibrated from labelled data.

### First live baseline

The first pinned-model run completed 140/140 API calls without an error. Across
120 assertion-bearing calls, 230 hard assertions passed and none failed. Clear
positive/negative and paraphrase cases separated cleanly; the deliberately hard
double-negation case exposed why a consequential action cannot rely on a Noul
threshold alone.

See [the complete live findings](docs/evaluations/payment-card-block-negation-v1.md),
including latency, token usage, per-case variance, ambiguity behavior and the
resulting policy requirement.

## End-to-end payment dispute sample

[`DecisionFabric.PaymentDisputes.Api`](samples/DecisionFabric.PaymentDisputes.Api)
demonstrates the complete boundary from customer language to typed Jev evidence,
deterministic policy, confirmation and an authorized next action.

The ASP.NET Core API includes:

- fixture mode using recorded evaluation values, requiring no credentials;
- opt-in live TypeSafe mode;
- `notAuthorized`, `awaitingConfirmation`, `authorizedByPolicy` and
  `authorizedByConfirmation` states;
- an idempotent confirmation transition;
- redacted audit events containing an input SHA-256 fingerprint rather than raw
  customer text;
- `ActivitySource` and `Meter` instrumentation for OpenTelemetry subscribers;
- OpenAPI output and end-to-end HTTP tests; and
- no real card integration or side effect.

Run the zero-credential demo:

```bash
dotnet run --project samples/DecisionFabric.PaymentDisputes.Api
```

See [the sample guide](samples/DecisionFabric.PaymentDisputes.Api/README.md) for
fixture requests, live configuration and the confirmation boundary.

## Agent Action Gate sample

[`DecisionFabric.AgentActionGate.Api`](samples/DecisionFabric.AgentActionGate.Api)
decides whether an AI agent's proposed tool call may run automatically, needs
human approval or is denied. It proves the pack abstraction in a second domain
with a different input shape and a non-destructive route, using the same fabric,
hosting registration and fixture provider. Its fixtures are synthetic.

```bash
dotnet run --project samples/DecisionFabric.AgentActionGate.Api
```

## Decision Inspector

[`DecisionFabric.Inspector`](samples/DecisionFabric.Inspector) reads the
recorded evaluation runs and shows one case at a time in three layers: the raw
answers a provider gave, the gate reasons those answers triggered, and the
disposition against the label. It needs no provider key and makes no network
calls. The aggregates it displays are republished from the committed reports
rather than recomputed; the recorded calls are scored independently only to
verify the two still agree, and a disagreement stops the app starting rather
than changing a number on the page.

```bash
dotnet run --project samples/DecisionFabric.Inspector
```

![Decision Inspector showing the ten cases where one run was right and the other wrong](docs/images/decision-inspector-discordant-cases.jpg)

## Run locally

Requires the .NET 10 SDK.

```bash
dotnet restore JevEnterpriseDecisionFabric.sln
dotnet test JevEnterpriseDecisionFabric.sln --configuration Release
dotnet run --project evals/DecisionFabric.Evals -- --dry-run
```

For a live evaluation, keep the key outside source control:

```bash
export TYPESAFE_API_KEY="..."
dotnet run --project evals/DecisionFabric.Evals -- \
  --dataset evals/datasets/payment-card-block-negation-v1.json \
  --output artifacts/results/payment-card-block-negation-v1.jsonl \
  --report artifacts/results/payment-card-block-negation-v1.report.json
```

PowerShell:

```powershell
$env:TYPESAFE_API_KEY = "..."
dotnet run --project evals/DecisionFabric.Evals -- --dataset evals/datasets/payment-card-block-negation-v1.json
```

Alternatively, add `TYPESAFE_API_KEY` as a GitHub Actions repository secret and manually run the `live-evaluation` workflow. The secret is injected only into the evaluation step. Raw JSONL and the machine-readable stability report are retained as a private workflow artifact for 14 days.

## Model and API contract

The provider calls `POST https://api.typesafe.ai/v1/systemone` directly and pins `jev-1.13.0`. It does not depend on an unofficial SDK. The requested model, actual returned model and semantic-contract version remain separate so upgrades can be evaluated deliberately.

Primary references:

- [TypeSafe API reference](https://docs.typesafe.ai/api)
- [TypeSafe primitives](https://docs.typesafe.ai/primitives)
- [TypeSafe model versioning](https://docs.typesafe.ai/models)

## Safety posture

- No API keys, customer data or result artifacts belong in Git.
- A model answer is evidence, not authorization for a side effect.
- Uncertain results route to confirmation, human review or System 2.
- The payment cases are synthetic and contain no personal or cardholder data.

## Near-term milestones

- Record live Jev fixtures and a labelled evaluation dataset for the Agent Action Gate.
- Run a Jev versus structured-output LLM comparison on the same datasets.
- Add adversarial multilingual and punctuation variants to the risk-gate suite.
- Add a small decision-inspection dashboard for both samples.
- Encode the architecture as a coding-agent skill after the abstractions are evidence-backed.

This is independent experimental work and is not an official TypeSafe AI project.
