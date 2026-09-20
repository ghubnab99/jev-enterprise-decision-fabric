# Security policy

## What this project is

Experimental research code published as an evidence baseline. It is **not
production software**: there is no authentication, authorization, rate limiting,
tenancy or persistence in any sample, and none of them performs a real side
effect. Please do not deploy it as-is, and do not rely on it to gate anything
that matters without your own controls in front of it.

## Supported versions

| Version | Supported |
| --- | --- |
| `main` | Yes, best effort |
| v0.1.x | Yes, best effort |
| Anything earlier | No |

This is maintained by one person alongside other work. Expect a best-effort
response, not a service level.

## Reporting a vulnerability

Report privately through GitHub: **Security → Advisories → Report a
vulnerability** on this repository. That opens a private channel visible only to
the maintainer.

Please do not open a public issue for anything exploitable, and please do not
include real credentials, customer data or personal information in a report — a
redacted reproduction is enough.

A useful report says what an attacker can do, the smallest steps that show it,
and which commit you tested. An acknowledgement should arrive within a week.

## In scope

- Anything that leaks a provider key, or causes one to be written to disk, logs,
  audit events or an HTTP response.
- Path traversal or arbitrary file read through the inspector's configuration or
  its endpoints.
- A way to make a gate return `Allow` for input the policy should have blocked,
  where the cause is the gate implementation rather than a model answer.
- Injection into the evaluation runner or the recorded artifacts it writes.
- Dependency vulnerabilities that are actually reachable from this code.

## Out of scope

- A model classifying something differently from the label. That is benchmark
  accuracy, discussed openly in
  [the evaluation](docs/evaluations/agent-action-gate-v1.md), not a
  vulnerability.
- The samples having no authentication. That is stated, intended, and the reason
  they are samples.
- Prompt injection that changes a *model answer* without defeating the
  deterministic policy. The architecture assumes model answers are untrusted
  evidence; the `injection-resistance` family measures exactly this.
- Denial of service against a sample you are running locally.

## Handling keys and data

- Provider keys belong in `.secrets/` (gitignored) or in a CI secret, never in
  source, configuration, tests or recorded artifacts.
- Do not set a benchmark key as a persistent user- or machine-scoped
  environment variable: every process started afterwards inherits it, including
  tools that resolve credentials from the environment on their own. An
  `export` is narrower but still reaches the current shell environment and its
  child processes. The runner reads the key file before the environment so
  neither is necessary.
- Every committed evaluation case is synthetic. Do not add real customer
  language, personal data or cardholder data to a dataset, a fixture or an
  issue.
- Recorded runs deliberately store model answers and decisions, not the
  instruction text that produced them.
