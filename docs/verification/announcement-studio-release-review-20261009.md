# Announcement studio release review — 2026-10-09

User authorized implementation, review, publication and the feature schema migration.
No production database operations, restart, application replacement or publication occurred.

## Review and corrections

- Scoped company access and employee recipient image ownership checked in source.
- Controlled additive schema, immutable presentation snapshots, draft default and retry identifier checked.
- Fixed built-in dictionary overrides for existing language aliases (en-US updates en); immutable Arabic source keys and invalid-translation rollback retained.
- Added regression coverage and 55 matching English/Sorani translations for validation messages and scanner-normalized placeholders.
- Graphify launcher remains unavailable; no refreshed graph is claimed.

## Release integration

The deployed Web assembly SHA256 matches local-runtime/publish-yearly-leave-accrual-20261009, not the feature branch's older base.
An isolated local release snapshot was prepared in work/announcement-studio-release-source-20261009 from work/yearly-leave-accrual-release-source-20261009.
Feature-only patches were applied, preserving published leave/import/name changes and excluding the unrelated pending typed approval migration.
Resource conflicts reused existing English translations instead of replacing the complete English catalog; bilingual core parity and scanner completeness were corrected and verified.
This snapshot is not a Git merge and has not been pushed. Do not publish the 122 unrelated local commits to remote main.

## Verification

- Integrated Release build: zero warnings and errors.
- Audited synthetic regression allowlist from the prior release plus studio/dictionary tests: 3,097 passed, zero failed/skipped.
- Five studio SQL integration tests use a dedicated LocalDB instance and synthetic catalogs, never production connection strings.
- Existing actual-script headless DOM smoke passed. This is not authenticated live E2E or pixel review.
- Initial integrated run exposed two localization failures; both were repaired and the full release allowlist rerun passed.
- Production CheckOnly ran read-only; it neither stops the site nor accesses SQL.

## Publication gate

CheckOnly reported Operations:EnforceProductionReadiness=false. Read-only presence checks found no owner-acceptance reference, offsite-backup path/heartbeat, health monitor/alert URL, RPO/RTO, or malware scanner endpoint.
No readiness enforcement setting was changed and no existing bypass was used to publish.
Publication requires safe production readiness setup and backup verification first. The feature migration must remain explicitly scoped; do not apply the entire historical EF migration chain.
