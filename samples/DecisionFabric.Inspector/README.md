# Decision Inspector

A report tells you how often the gate was right. It does not tell you why any
one decision came out the way it did. The inspector reads the recorded runs and
shows a single case in the three layers the evaluation write-up keeps apart:

1. the raw answers the provider gave, with their probability mass;
2. the gate's reading of those answers, with the reasons it recorded;
3. the disposition that came out, against the label.

```bash
dotnet run --project samples/DecisionFabric.Inspector
```

Then open the address it prints. No provider key is needed and nothing is
fetched over the network: every figure comes from files already in the
repository.

![The overview, with both recorded runs side by side](../../docs/images/decision-inspector-overview.jpg)

## What it reads

| Source | Used for |
| --- | --- |
| `evals/datasets/agent-action-gate-v1.json` | the instruction, the proposed call, the label, answer-level expectations, the gate boundaries and the annotation rules |
| `evals/runs/*.report.json` | every aggregate: both accuracies, confusion matrices, per-family counts, latency, tokens |
| `evals/runs/*.jsonl` | the individual calls: answers, gate reasons, disposition, duration, usage |

**The inspector recomputes no published figure.** Accuracy, unsafe allows,
over-blocks, latency and token counts are read from the committed reports as
written, so the dashboard and CI cannot disagree.

Three things a report states only as a total — which case was correct, which was
an unsafe allow, which was over-blocked — are derived per case from the recorded
calls. Those derivations are checked against the report's own totals at startup.
If they disagree the app refuses to start and names the mismatch:

```text
The inspected runs no longer agree with their reports: jev correct cases:
the calls give 100, the report says 101.
```

## Finding the interesting cases

The outcome filter separates two things it is easy to conflate:

- **Legs reached different dispositions** (11 cases) — the runs disagree,
  including where both are wrong in different ways.
- **One leg right, another wrong** (10 cases) — the discordant set a McNemar
  test runs on, which is the comparison the write-up reports.

![The ten cases where one leg was right and the other wrong](../../docs/images/decision-inspector-discordant-cases.jpg)

Also available: any leg wrong, unsafe allow, over-block, changed across runs,
repeated cases, a family, and free text over the case id, instruction and tool.

## Reading a case

`irr-req-revoke-access` is the case the write-up calls a contract gap rather
than a model failure, and the inspector makes that visible: both providers
classified the revocation as `reversible_change`, and only the 0.80 confidence
floor separated Claude's escalation from Jev's unattended allow.

![One case, both runs, in three layers](../../docs/images/decision-inspector-case-detail.jpg)

## Endpoints

| Endpoint | Returns |
| --- | --- |
| `GET /api/archive` | suite, contract, gate boundaries, annotation rules, one summary per run, per-family counts |
| `GET /api/cases` | one row per case; `family`, `outcome` and `query` narrow it |
| `GET /api/cases/{caseId}` | the case in full, with every call each run made against it |

An unknown `outcome` is a 400 naming the accepted values; an unknown case is a
404.

## Configuration

`DecisionFabric:Inspector` in `appsettings.json`, validated at startup:

```json
{
  "ArchiveRoot": "../../evals",
  "DatasetPath": "datasets/agent-action-gate-v1.json",
  "Legs": [
    { "Id": "jev", "Label": "TypeSafe Jev",
      "Report": "runs/agent-action-gate-jev.report.json",
      "Calls": "runs/agent-action-gate-jev.jsonl" }
  ]
}
```

`ArchiveRoot` is resolved against the content root; the other paths are
resolved against it. Add a leg to compare another run — the case table and the
case detail grow a column each.

## What it is not

It does not run a provider, score anything or write anything. To produce a new
run use the evaluation runner, and to rebuild a report from calls already
recorded use its `rebuild-report` mode; the inspector only reads what those
leave behind.
