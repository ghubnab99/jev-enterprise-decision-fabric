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

Keep the API key outside source control:

```bash
export DecisionFabric__Provider=TypeSafe
export TYPESAFE_API_KEY="..."
dotnet run --project samples/DecisionFabric.PaymentDisputes.Api
```

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
can subscribe without changing the domain code. Audit logs contain a SHA-256
input fingerprint and decision metadata; the raw customer message is neither
stored nor logged.
