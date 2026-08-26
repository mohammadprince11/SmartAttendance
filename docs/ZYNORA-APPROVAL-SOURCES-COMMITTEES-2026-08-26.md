# ZYNORA — Request sources, decision history, and approval committees

Date: 2026-08-26

Branch: `agent/zynora-completion-baseline`

Draft PR: `#44`

## Delivered

- Every known `SelfServiceRequests` writer now records an explicit source:
  `SelfService`, `Admin`, or `Legacy`.
- Existing rows are migrated to `Legacy`; the migration does not infer historical
  provenance from usernames or request types.
- The approvals center and approval reports filter by request source while retaining
  company-scope guards and parameterized SQL.
- Request cards show the complete decision history, including actor, time, notes,
  and delegation provenance. History is loaded in one scoped batch for the visible
  requests.
- Admin-only committee administration supports create, edit/reactivate, and soft
  deactivate for:
  - reusable internal committee groups with company users;
  - external committees with contact metadata.
- Approval templates accept internal groups and external committees as first-class
  step types.
- Internal committee members are copied to `ApprovalRequestStepMembers` when the
  workflow starts. Later group edits do not alter an in-flight request.
- A frozen internal member can decide the step directly or through an active approval
  delegation. Admin and HR Manager retain the documented override.
- Current-step notifications and SLA reminders target all frozen group members.
  External steps notify HR Manager for administrative recording.
- Pending request deletion removes frozen step members before workflow steps, keeping
  referential integrity intact.

## Controlled migrations

- `20260826-22-approval-request-source`
- `20260826-23-approval-committees`

No committee page or request handler creates or alters schema at runtime.

## Verification

| Gate | Result |
|---|---|
| Release build | PASS — 0 warnings, 0 errors |
| CI-equivalent non-SQL tests | PASS — 1909/1909 |
| Disposable ProductionClosure SQL suite | PASS — 27/27 |
| Legacy disposable SQL integration suite | PASS — 30/30 |
| NuGet known-vulnerability audit | PASS — no vulnerable packages |
| `git diff --check` | PASS |

The SQL committee acceptance test verifies company-scope rejection, membership
snapshot persistence after a group edit, authorization of the frozen member, final
approval, and stored request source. Every SQL database used by verification had a
disposable guarded name and was removed by the test fixture or bootstrap teardown.

## Deliberate semantics

- An internal committee step is an **any-member** decision: the first authorized
  frozen member decision resolves that step. Parallel steps remain available when
  multiple independent decisions are required.
- An external committee has no local login identity. HR Manager or Admin records its
  decision in ZYNORA, preserving the actor, time, and notes in approval history.
- Soft-deactivated committees remain referentially valid for historical templates and
  request snapshots but cannot be selected for a newly saved active template.
