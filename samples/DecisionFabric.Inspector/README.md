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

**No figure on the page is recomputed.** The aggregates it displays — both
accuracies, the family counts, latency and token counts — are read from the
committed reports as written, so the dashboard and CI cannot disagree.

The recorded calls *are* scored independently, but only to verify that they and
the report still agree. That scoring never becomes a number on the page: it
decides whether the app starts at all.

Two checks run before it will serve anything.

1. The recorded calls are scored independently — how many calls a case has, how
   many matched the label, and which disposition most of its runs reached — and
   held against what the report says for that case. This is what stops a JSONL
   and its report drifting apart.
2. The three things a report states only as totals — which case was correct,
   which was an unsafe allow, which was over-blocked — are derived per case and
   held against those totals.

A failure in either throws, the app does not come up, and the message names the
case:

```text
The inspected runs no longer agree with their reports: jev ro-req-invoice-search
correct calls: the calls give 4, the report says 5.
```

Whether a call matched is derived from the disposition and the label shown
beside it, not from the record's own `dispositionMatched` flag, so the detail
view cannot show a disposition, a different label, and "matches the label"
underneath.

## Finding the interesting cases

The outcome filter separates two things it is easy to conflate:

- **Providers gave different decisions** (11 cases) — every case where the runs
  reached different dispositions. This includes one case where both were wrong
  in different ways: `imp-necessary-step-reversible` is labelled `Allow`, and
  it was denied by one run and sent to approval by the other.
- **Only one provider matched the label** (10 cases) — that case drops out,
  leaving those where exactly one run was right: the discordant pairs a McNemar
  test compares, and the comparison the write-up reports.

Both are kept because they answer different questions. The first asks where the
providers behave differently at all; the second asks where that difference is a
difference in quality.

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
