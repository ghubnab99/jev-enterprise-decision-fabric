# Payment Dispute Intelligence API

This sample turns Jev evidence into an explicit enterprise authorization flow.
It never performs a real card action. `blockCard` means that the decision has
crossed the demonstrated authorization boundary and may be handed to a separate,
authenticated card-management integration.

## Run without credentials

Fixture mode is the default and uses recorded values from the evaluation suite:

```bash
dotnet run --project samples/DecisionFabric.PaymentDisputes.Api
```

OpenAPI is available at `http://localhost:<port>/openapi/v1.json`.

```bash
curl -X POST http://localhost:<port>/api/payment-disputes/triage \
  -H "Content-Type: application/json" \
  -d '{"customerMessage":"I don'\''t want you not to block my card."}'
```

The double negation produces positive model evidence but is routed to
`awaitingConfirmation` with `requestCustomerConfirmation`.

## Run against TypeSafe

This sample resolves its key from configuration (`DecisionFabric:TypeSafeApiKey`)
or the `TYPESAFE_API_KEY` environment variable. Keep the key in the gitignored
`.secrets/typesafe.key` file and hand it to one process only, so it never enters
your shell history or your machine-wide environment — see
[live configuration in the root README](../../README.md#optional-running-against-live-jev)
for how to create that file.

```bash
DecisionFabric__Provider=TypeSafe \
  TYPESAFE_API_KEY="$(cat .secrets/typesafe.key)" \
  dotnet run --project samples/DecisionFabric.PaymentDisputes.Api
```

```powershell
$env:DecisionFabric__Provider = "TypeSafe"
$env:TYPESAFE_API_KEY = (Get-Content -Raw .secrets\typesafe.key).Trim()
dotnet run --project samples\DecisionFabric.PaymentDisputes.Api
Remove-Item Env:TYPESAFE_API_KEY
```

## Policy configuration

The card-block gate thresholds are validated configuration, not code:

```json
"DecisionFabric": {
  "Policies": {
    "PaymentDisputeGate": {
      "NegativeAtOrBelow": 0.25,
      "PositiveAtOrAbove": 0.75,
      "MinimumIntentConfidence": 0.8
    }
  }
}
```

A missing, out-of-range or inconsistent value stops the application at startup.
Every response and audit event carries a `policyVersion` fingerprint of the
effective values, for example `payment-dispute-gate/sha256:a0e9832f7842`.

## Confirmation boundary

The confirmation endpoint accepts an opaque reference supplied by a trusted,
authenticated upstream channel:

```bash
curl -X POST http://localhost:<port>/api/payment-disputes/decisions/<decision-id>/confirm \
  -H "Content-Type: application/json" \
  -d '{"confirmationReference":"demo-authenticated-channel-reference"}'
```

This in-memory sample validates the policy transition but does not authenticate
customers or call a card processor. Production systems must verify the reference
and caller before invoking this boundary.

## Observability and privacy

The sample emits `ActivitySource` traces and `Meter` instruments under
`DecisionFabric.PaymentDisputes`. An OpenTelemetry SDK or Azure Monitor exporter
can subscribe without changing the domain code. The shared fabric additionally
emits provider-level activities and metrics under `DecisionFabric`. Audit logs
contain a SHA-256 input fingerprint, model, contract and policy versions, and —
for the `confirmed` event — the trusted confirmation reference; the raw customer
message is neither stored nor logged.
