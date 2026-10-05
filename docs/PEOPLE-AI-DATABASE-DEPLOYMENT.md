# ZYNORA People AI — Controlled Database Deployment

## Deployment model

People AI schema is no longer created by a runtime self-healing method.
`PeopleAiSchema.VerifyAsync` is verification-only and never executes DDL.

The canonical foundation migration is:

```text
20260919-01-people-ai-foundation
```

It is registered in `SqlSchemaMigrator` and recorded in:

```text
dbo.__SchemaMigrations
```

Production startup does not apply DDL. It verifies the People AI schema and
fails closed when the controlled migration has not been deployed.
## Current and target versions

Do not infer the current production version from source control. Read the
migration ledger on the target database:

```bash
dotnet run --project tools/SmartAttendance.DatabaseMigrator \
  -c Release -- --status
```

`--status` prints the target server/database, applied count, every pending
migration ID, and whether the People AI foundation is `APPLIED` or `PENDING`.

Target People AI schema version for this closure:

```text
20260919-01-people-ai-foundation
```

Migration order is the order defined by `SqlSchemaMigrator.Migrations`.
The People AI foundation is appended after the existing controlled migrations.
## Data impact

The People AI foundation is additive/idempotent:

- creates missing People AI tables, indexes and foreign keys;
- adds missing original-verification/expiry columns to OnboardingDocuments;
- does not DROP or TRUNCATE tables;
- does not remove columns;
- does not narrow existing column types.

After schema deployment, `DatabaseDeployment.ApplyAsync` seeds missing
company People AI defaults and performs the existing legacy identity bootstrap
from Employees into EmployeeIdentityDocuments.

Those data operations are repeat-safe and happen only after schema verification.

## Backup requirement

Before `--apply`, take a database backup/snapshot and retain its immutable
reference. The migrator refuses to apply without both:

```text
ZYNORA_DATABASE_MIGRATION_CONFIRM=APPLY
ZYNORA_DATABASE_MIGRATION_BACKUP_REFERENCE=<backup/snapshot reference>
```
## Apply procedure

1. Take and verify the backup/snapshot.
2. Set the target connection string through environment configuration.
3. Run `--status` and review every pending migration ID.
4. Set the explicit confirmation and backup reference.
5. Run `--apply`.
6. Run `--status` again.
7. Confirm People AI foundation is `APPLIED`.
8. Keep `DatabaseMigrations__ApplyOnStartup=false`.
9. Restart the ZYNORA web service.
10. Production startup then verifies schema only; it performs no DDL.

Example:

```bash
export ConnectionStrings__DefaultConnection='...'
export ZYNORA_DATABASE_MIGRATION_CONFIRM='APPLY'
export ZYNORA_DATABASE_MIGRATION_BACKUP_REFERENCE='snapshot-20260919-before-people-ai'

dotnet run --project tools/SmartAttendance.DatabaseMigrator \
  -c Release -- --apply
```
## Rollback policy

There is intentionally no automatic destructive DOWN migration for this
foundation. The migration is additive, so rolling the application binaries
back does not require removing the new People AI tables.

If schema/data rollback is required because deployment validation fails:

1. stop the ZYNORA service;
2. restore the exact backup/snapshot referenced before `--apply`;
3. deploy the previous application release;
4. verify the restored migration ledger;
5. restart service only after verification.

Do not manually DROP People AI tables or remove columns from production as a
rollback shortcut.

## Production startup contract

`DatabaseMigrations__ApplyOnStartup=true` is rejected in Production.
The web host calls verification only. A missing/incomplete People AI schema
stops startup with an explicit instruction to run controlled migrations.
