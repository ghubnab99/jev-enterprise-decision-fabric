# Agent Action Gate

This sample decides whether an AI agent's proposed tool call may run
automatically, needs human approval or is denied. It uses the same
`IDecisionFabric` as the payment dispute sample; only the decision pack differs.

It never executes a tool. `allow` means the call crossed the demonstrated
policy boundary and may be handed to a separate, authenticated executor.

## Run without credentials

```bash
dotnet run --project samples/DecisionFabric.AgentActionGate.Api
```

```bash
curl -X POST http://localhost:<port>/api/agent-actions/evaluate \
  -H "Content-Type: application/json" \
  -d '{"userInstruction":"Clean up the Q3 folder.","toolName":"drive.delete_folder","toolArguments":"{\"folder\":\"Q3\",\"recursive\":true}"}'
```

Fixture answers are **synthetic** (reported model `fixture/synthetic`); unlike
the payment fixtures they were not recorded from a live Jev run. Any
unrecognized instruction/tool pair falls back to uncertain evidence and routes
to `requireApproval`.

| Instruction | Tool | Disposition |
| --- | --- | --- |
| Summarize the open invoices for ACME. | `crm.search_invoices` | `allow` (read-only) |
| Rename the Q3 folder to Q3-final. | `drive.rename_folder` | `allow` (requested, reversible, in scope) |
| Clean up the Q3 folder. | `drive.delete_folder` | `requireApproval` (irreversible, uncertain request) |
| Summarize the open invoices for ACME. | `email.send` | `deny` (not requested) |

## Run against TypeSafe

```bash
export DecisionFabric__Provider=TypeSafe
export TYPESAFE_API_KEY="..."
dotnet run --project samples/DecisionFabric.AgentActionGate.Api
```

## Policy

Thresholds live in `DecisionFabric:Policies:AgentActionGate` and are validated at
startup. In order:

1. A confidently read-only action is allowed.
2. A user-request probability at or below the negative boundary is denied.
3. Otherwise approval is required if the request is uncertain, the impact is
   irreversible or external, impact confidence is low, scope expansion exceeds
   the configured maximum, or the user instruction carries linguistic risk.
4. A requested, reversible, in-scope action with no risk signal is allowed.

The response reports a `policyVersion` fingerprint of these parameters, and
never echoes the user instruction or tool arguments.
