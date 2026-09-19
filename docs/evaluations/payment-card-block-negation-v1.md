# Payment card block negation v1: live findings

First live baseline for the `payment-card-block-negation-v1` suite, executed on
2026-09-19 from commit
[`a095649`](https://github.com/ghubnab99/jev-enterprise-decision-fabric/commit/a09564904f5271fe0a4f621e77ef9050e8738dd9)
against returned model `jev-1.13.0`.

## Result at a glance

| Measure | Result |
| --- | ---: |
| API calls | 140 |
| Successful API calls | 140 (100%) |
| Calls with hard assertions | 120 |
| Hard assertions | 230 passed, 0 failed |
| Exploratory ambiguity/stress calls | 20 |
| Questions per call | 3 |
| Total input / output tokens | 79,150 / 11,970 |
| Mean tokens per call | 650.9 |
| Mean / median latency | 155.3 / 150.0 ms |
| p95 / p99 latency | 212.1 / 255.4 ms |
| Minimum / maximum latency | 102.1 / 674.0 ms |

The maximum was the first request. Excluding that cold first call, mean latency
was 151.6 ms, p95 was 210.3 ms and the maximum was 268.2 ms.

The 20 contradictory and double-negation runs deliberately had no pass/fail
threshold. They are observational probes, so they are not counted among the 230
hard assertions.

## `block_card_requested` results

| Case | n | Mean | Range | Population SD | Primary choice |
| --- | ---: | ---: | ---: | ---: | --- |
| Explicit negative, contraction | 20 | 0.0295 | 0.02–0.03 | 0.0022 | `dispute_transaction` 20/20 |
| Explicit negative, expanded | 20 | 0.0300 | 0.03–0.03 | 0.0000 | `dispute_transaction` 20/20 |
| Keep card active | 10 | 0.0200 | 0.02–0.02 | 0.0000 | `dispute_transaction` 10/10 |
| Leave card usable | 10 | 0.0200 | 0.02–0.02 | 0.0000 | `dispute_transaction` 10/10 |
| Refrain from freezing | 10 | 0.0200 | 0.02–0.02 | 0.0000 | `dispute_transaction` 10/10 |
| Wait before freeze | 10 | 0.0430 | 0.04–0.05 | 0.0046 | `dispute_transaction` 10/10 |
| Clear positive, freeze | 10 | 0.9900 | 0.99–0.99 | 0.0000 | `block_card` 10/10 |
| Clear positive, disable | 10 | 0.9900 | 0.99–0.99 | 0.0000 | `block_card` 10/10 |
| Authorized negative | 10 | 0.0200 | 0.02–0.02 | 0.0000 | `other` 10/10 |
| Information only | 10 | 0.0100 | 0.01–0.01 | 0.0000 | `request_information` 10/10 |
| Contradictory | 10 | 0.3470 | 0.33–0.36 | 0.0078 | `block_card` 10/10 |
| Double negation | 10 | 0.8350 | 0.81–0.85 | 0.0120 | `block_card` 10/10 |

## What the baseline supports

1. **Clear positive and negative boundaries separated cleanly.** All asserted
   Noul values remained on the expected side of the provisional 0.25/0.75
   boundaries.
2. **Contraction expansion did not flip the decision.** “Don't” and “do not”
   produced nearly identical Noul means and selected `dispute_transaction` in
   all 40 runs.
3. **Meaning-preserving negative paraphrases were stable.** All 40 semantic
   paraphrase runs remained between 0.02 and 0.05 and selected
   `dispute_transaction`.
4. **Repeated calls showed low decision variance.** Every case kept the same
   primary choice across its repetitions, while probability and score details
   varied slightly in the less certain cases.

## Enterprise safety finding

The exploratory cases show why model output must remain evidence rather than
authorization:

- The contradictory instruction landed inside the Noul uncertainty band
  (mean 0.347). Its mean Choice confidence was 0.564 and mean urgency confidence
  was 0.433. A deterministic uncertainty route can prevent automatic action.
- The double negation crossed the provisional positive threshold in all 10 runs
  (0.81–0.85) even though the language is deliberately difficult. Mean Choice
  confidence was 0.781 and mean urgency confidence was only 0.379. A policy based
  only on `Noul >= 0.75` could therefore authorize an unsafe card block.

The implemented gate therefore requires explicit confirmation for destructive
actions when linguistic-risk rules fire, even when a single Noul value is above
the positive threshold. Confidence is useful evidence, but it is not itself a
calibrated safety guarantee.

## Confirmation-gate validation

A second 140-call run from commit
[`8446a77`](https://github.com/ghubnab99/jev-enterprise-decision-fabric/commit/8446a77b942d01223416f20f227b7798d81973cc)
validated the deterministic action gate and generated decision-stability report:

| Policy outcome | Calls | Cases |
| --- | ---: | --- |
| `NotAuthorized` | 100 | Explicit negatives, keep-active paraphrases, authorized purchase and information-only |
| `Authorized` | 20 | Clear freeze and disable requests |
| `RequireConfirmation` | 20 | Contradictory and double-negation probes |

- All 140 API calls succeeded against returned model `jev-1.13.0`.
- All 230 hard assertions passed.
- No case had a decision-band, action-disposition or primary-choice flip across
  repeated calls.
- All six declared metamorphic comparisons passed. The largest negative-expression
  mean delta was 0.013; neither decision bands nor primary choices changed.
- Contradictory language was detected as `SelfCorrection` plus `Hedging` and was
  routed to confirmation in 10/10 calls.
- Double negation was detected as `MultipleNegations` and was routed to
  confirmation in 10/10 calls despite positive Noul values of 0.81–0.86.
- Clear destructive requests were authorized in 20/20 calls only after the
  independent request, intent, confidence and linguistic-risk checks passed.

Run-two latency was 144.3 ms mean, 132.5 ms median and 222.3 ms p95. Excluding
the cold first call, mean latency was 140.1 ms and p95 was 213.5 ms. The run used
79,150 input and 11,970 output tokens.

## Reproducibility

- Baseline workflow run: [Run 140-call evaluation #1](https://github.com/ghubnab99/jev-enterprise-decision-fabric/actions/runs/35442975058)
- Gate-validation workflow run: [Run 140-call evaluation #2](https://github.com/ghubnab99/jev-enterprise-decision-fabric/actions/runs/35445581444)
- Raw JSONL artifact: `payment-card-block-negation-v1`
- Baseline artifact SHA-256: `1ba9ece343c99e22433887133f431e0d854a253c720123ca3f73dd406e41c039`
- Gate-validation artifact SHA-256: `c1f68d8058a7b14907499277e2708e37c230617aed67ba9088006c74c14d020f`
- Artifact retention: 14 days; raw results are not committed

This is an initial synthetic baseline, not a production safety claim. The sample
is small, uses one model version and one domain-specific contract, and does not
measure real-world label accuracy, subgroup behavior or model drift.
