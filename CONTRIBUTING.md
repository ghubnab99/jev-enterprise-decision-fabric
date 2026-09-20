# Contributing

Thanks for looking. This is a small experimental repository with one unusual
property: **its claims are its main output.** The code exists to produce
measurements that hold up, so the rules below are mostly about evidence rather
than style.

## Getting set up

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download). Nothing else,
and no provider key:

```bash
dotnet restore JevEnterpriseDecisionFabric.sln
dotnet build JevEnterpriseDecisionFabric.sln --configuration Release -warnaserror
dotnet test JevEnterpriseDecisionFabric.sln --configuration Release --no-build
```

Everything in the test suite runs against committed fixtures and recorded runs.
A live provider is needed only to produce a *new* recorded run.

## What CI requires

The same three steps as the workflow, and all three must pass:

1. **Build with zero warnings.** CI builds with `-warnaserror` and the analyzer
   level is `latest-recommended`; a new warning fails the build.
2. **All tests pass.**
3. **Every dataset validates.** The workflow loops over `evals/datasets/*.json`
   with `--dry-run`, so a dataset added later cannot skip validation.

## Evidence rules

These are not negotiable, because getting them wrong would make the benchmark
worse than useless.

- **Never edit an expected label to match what a model answered.** If a label is
  wrong, fix it as a labelling decision, record it in the dataset's
  `annotationHistory`, and say which runs predate the change.
- **Preserve raw artifacts.** Recorded JSONL is append-only history. Do not
  regenerate a run to make a check pass; find out why the check fails.
- **Report both denominators.** Per-case accuracy leads, with the aggregation
  method stated. Call-weighted accuracy is reported beside it and described as a
  property of the repetition scheme, never as an estimate of production traffic.
- **Say that repeated calls measure stability** and are not independent evidence
  of accuracy.
- **Keep the three layers apart** — raw classification, policy transformation,
  executable decision — and attribute each error to the layer it came from. Do
  not blame a model for a contract or policy gap.
- **Give full provenance for any provider leg**: exact model id, effort setting,
  run dates, token usage, caching assumptions, and the pricing source with the
  date it was checked. Where pricing is not published, write "pricing not
  publicly available" — never "free" or "cheaper".
- **Show the denominator for every percentage**, including per family.
- **Do not tune a policy to improve a result** and report it as the same
  experiment. A policy change is a new experiment needing its own dataset
  review.
- **State what a run does not establish.** A sample this size rarely
  establishes superiority, and saying so is the point.

## Changing a dataset

Datasets are versioned. Prefer adding `agent-action-gate-v2` over editing v1 in
place; a committed result must stay reproducible against the dataset it ran on.
`rebuild-report` re-derives every decision from the recorded answers and refuses
to proceed if one no longer reproduces — if that fires, stop and work out why.

## Commits and pull requests

- Small, atomic commits. The subject uses `feat:`, `fix:`, `test:`, `docs:`,
  `ci:`, `refactor:` or `chore:`, and the body explains *why*, not what the
  diff already shows.
- Do not rewrite pushed history. A wrong commit is corrected by a later one.
- One concern per pull request, with a description that says what changed and
  what it does not cover.
- If a change affects a published number, say which number and why it moved.

## Running against live Jev

Only needed to record a new run:

```bash
mkdir -p .secrets && printf '%s' 'YOUR_KEY' > .secrets/typesafe.key
```

`.secrets/` is gitignored and the runner reads it before the environment, so a
benchmark key never has to be exported machine-wide. Never commit a key, never
paste one into an issue, and do not export one globally for convenience.

## Licensing of contributions

By contributing, you agree that your contributions will be licensed under the
MIT License.

## Reporting problems

Bugs and questions belong in an issue. Anything exploitable goes through the
private channel in [SECURITY.md](SECURITY.md) instead.
