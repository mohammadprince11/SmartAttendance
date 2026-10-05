# Local ZYNORA source consolidation

## Scope

Integrate the uncommitted HR changes from the local SmartAttendance and
zynora-security-fixes checkouts into the current tenant-code codebase. All branch
tips in both source checkouts are already ancestors of the current codebase.
Keep current tenant-code authentication, tenant isolation, notification routing,
and UI updates; do not replace whole files with older copies.

## Integrated changes

- Web password login enforces enabled TOTP or a one-time recovery code.
- Final approvals enqueue durable effect jobs in the approval transaction.
  The existing financial, lifecycle, data-change and attendance effect stores
  retain their own idempotency and calculation rules; a dispatcher retries
  incomplete effects.
- Field decisions are persisted inside that transaction only after company
  ownership, actor authorization and the step claim succeed. Both web and mobile
  callers pass their decisions to the engine instead of writing them first.
- Controlled migrations define employee engagement tables and the effect outbox.
- Additive migrations repair missing employee-update history and company
  localization tables, with matching standalone SQL exports.
- Employee notification colors use shared design-system tokens and safe defaults.
- Retain the older checkout's SQL migration tests and adapt source contracts to
  follow the centralized approval execution path.

## Data and deployment

No production database access, migration execution, runtime deployment or
employee-file upload is part of this consolidation. Local attachments, private
configuration, generated build outputs and query-tool memory remain outside Git.
Runtime schema changes must be applied explicitly with the controlled migrator
on an approved environment before deploying this branch.

## Verification

Restore and Release solution build succeed. The initial full test run found
three source-location contracts that needed adapting to the centralized path;
these are updated without removing the authorization/effect checks.
Final full test run: 2,533 passed, zero failed and 30 skipped (2,563 total).
The skipped integration tests require a separate configured test database.
The disposable SQL suite, including the five new localization/history cases,
ran successfully. No vulnerable packages were reported by the NuGet audit.
These results do not claim browser E2E or production migration verification.
Graphify update could not run because its installed uv launcher fails to resolve
the script path; the existing graph is stale and was not used as merge evidence.
