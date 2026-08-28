# ZYNORA approvals, settings and navigation closure

_2026-08-26 — implementation slice based on the live Kayan comparison. This change does not add runtime DDL, alter payroll/attendance formulas, or touch production data._

## Outcome

- Added first-class **Approvals** and **Settings** groups to the main navigation instead of scattering admin links at the root.
- Added a permission-aware global command search. It indexes only navigation links that the current server-rendered identity is allowed to see, supports Arabic normalization, keyboard selection and `Ctrl/Cmd + K`, and builds results with DOM APIs rather than `innerHTML`.
- Added an Admin-only Settings hub that routes to the existing sources of truth for company, identity, access roles, audit, HR, attendance, payroll, devices and integrations.
- Added approval reports with company-scope enforcement, parameterized filters, operational KPIs, type breakdown, request detail and safe shared XLSX/CSV export.
- Added the frozen approval path to each request detail card so the current, completed, future, returned and escalated steps are visible.
- Linked approval templates and the existing time-bound delegation manager from the new Approvals group using a stable anchor.
- Exposed Audit Logs through Settings and restricted the route to Admin. The former HR Manager compatibility prefix was removed because the legacy audit table has no `CompanyId` and must not be presented as tenant-safe.
- Added `Setup.AuditLogs` to the central page-grant catalog and mapped both `/Settings` and `/AuditLogs` through the central route catalog.

## Deliberate boundaries

The live Kayan menu also exposes external committees, reusable committee groups, custom requester fields, separate self-service/admin screening, currency exchange and several classification catalogs. They are not represented as decorative links here:

- External committees/groups require a controlled schema plus workflow authorization semantics; storing a name without making it participate in decisions would be misleading.
- Separate requester/admin screening requires a durable request-source field populated by every request writer; the current shared request table does not carry that provenance.
- Currency exchange requires a defined payroll/accounting consumer and effective-date rules before an admin CRUD screen is safe.

Those are schema/business-capability waves, not navigation work. This slice exposes only implemented behavior and establishes the navigation/reporting foundation for them.

## Verification

| Gate | Result |
|---|---|
| Release build | PASS — 0 errors, 0 warnings |
| CI-equivalent non-SQL tests | PASS — 1902/1902, no skips |
| Focused navigation/security/approval tests | PASS — included in the 1902 suite |
| JavaScript syntax | PASS (`node --check`) |
| NuGet vulnerable packages | PASS — none found in all solution projects |
| `git diff --check` | PASS |

The workstation's LocalDB instance remains unavailable, so disposable SQL integration suites were not rerun locally. They remain separate CI gates. `graphify` was not installed in the environment, so no graph refresh was possible.
