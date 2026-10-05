# Employee profile: missing update-history tables

## Cause

The profile timeline reads `EmployeeUpdateBatches` before a user opens the
Employee Updates page. Older numbered migrations only upgraded the table when
it already existed. They could therefore be recorded as applied on a fresh
database with both `EmployeeUpdateBatches` and `EmployeeUpdateChanges` missing.

## Repair

Migration `20260902-01-employee-update-history` creates the two legacy tables
with their existing column definitions, adds the four optional columns to older
batch tables, and adds indexes for employee history and batch details. It is
transactional and safe to repeat. Existing published migrations are unchanged.

The registered SQL is also exported as
`database/migrations/20260902-01-employee-update-history.sql`; a contract test keeps
the export identical to the registered migration. The normal migration runner
records its ID in `__SchemaMigrations`. Running the standalone export does not
write that ledger; a subsequent runner invocation can safely replay and record it.

No employee records, update values, attachment paths, compensation values, or
payroll/attendance calculations are changed. No new request-time schema creation
or exception-swallowing fallback was added to the profile.

## Verification

- Disposable SQL tests cover a fresh database, legacy-column upgrades, repeated
  execution, and preservation of existing batches and change rows.
- A profile-source contract prevents adding request-time table creation here.
- Apply and verify on a dedicated non-production database before release.

Local verification on 2026-09-02: Release build succeeded with zero warnings;
2,021 tests passed and 28 environment-dependent tests were skipped in the
non-ProductionClosureSql group. The two new disposable SQL tests and three
migration contracts also passed. On the local inspection database, the new
migration was recorded and the exact profile timeline SQL for the reported
synthetic employee executed successfully. Browser-level profile verification
still requires an authenticated session.

## Rollback

This is an additive schema repair. Reverting application code does not require
dropping the history tables or columns. Retain them and their data; any destructive
schema rollback requires a separately reviewed plan and backup.
