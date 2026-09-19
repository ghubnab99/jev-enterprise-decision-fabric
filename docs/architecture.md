# Architecture direction

The Decision Fabric separates probabilistic semantic judgment from deterministic application policy.

```mermaid
flowchart TD
    A[Application state] --> B[Versioned decision contract]
    B --> C[Jev provider: one batched request]
    C --> D[Typed probabilities, choices and scores]
    D --> E[Independent evidence and risk signals]
    E --> F[Deterministic authorization policy]
    F --> G[Execute, confirm, review or refuse]
```

## Invariants

1. Application code does not scatter raw Jev JSON or prompt strings across controllers and handlers.
2. Questions are versioned semantic contracts.
3. Questions sharing the same state are evaluated in one request unless a real dependency requires another request.
4. Probabilistic output never performs a consequential side effect directly.
5. Thresholds and fallback behavior belong to deterministic policy and are tuned against labelled evaluations.
6. The requested model version, returned model version, contract version, latency and full distribution are observable and replayable.
7. A consequential action requires agreeing evidence and must fail closed under uncertainty, confidence shortfall or linguistic risk.
8. Meaning-preserving variants are evaluated as declared metamorphic relations; threshold and primary-choice flips are explicit report data.

## Current slice

The first slice provides provider-neutral contracts, a direct Jev HTTP provider, deterministic Noul routing, a destructive-action confirmation gate, and a JSON-driven evaluation runner with decision-flip and metamorphic reporting. The 140-run payment-negation suite supplies the first evidence baseline; payment and agent-safety samples build on these boundaries.
