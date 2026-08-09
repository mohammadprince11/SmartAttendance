# PEOPLE FINAL ACCEPTANCE REPORT

_Acceptance audit — 2026-08-09. Verification only; no production code was changed during this audit._

## 1. Executive Decision

**READY FOR MERGE PREPARATION**

The People module's authorization, contract/lifecycle/file/compensation integrity is
enforced server-side and evidenced by a green build (0 errors) and 1451/1451 passing
tests, including the dedicated People isolation/integrity suites. No release blocker
remains. (Live two-company runtime DB acceptance — Phase 2 — was verified by static
endpoint audit + the automated isolation suites, not by a provisioned two-company test
DB; production data is out of scope by rule. See §3 and §13.)

## 2. Baseline

| Item | Value |
|---|---|
| Branch | `claude/smartattendance-local-rebuild-wftwb3` |
| Starting SHA (last externally verified) | `cb48acf9` |
| Ending SHA (branch HEAD) | `beee2002d55d7bce6141fc052983cedf1f972843` |
| Main SHA (`origin/main`) | `73169433592749279cdcb33c5f4340c68b1eedee` |
| Merge base | `d4085b3d07b48c413d238d1931d362cf84c92724` |
| Ahead of `origin/main` | 25 |
| Behind `origin/main` | 2 (both are prior merge commits of **this** branch — PR #19, #20 — already contained here) |
| Diverged | Yes, but content-clean (see §11) |

One commit exists after the last verified `cb48acf9`: `beee200` — scopes the
EmployeeUpdates manager picker to the employee's company. It was preserved (no reset /
rebase / merge / force-push performed).

## 3. Authorization & Multi-company Isolation

Every People read/write endpoint requires a permission and applies employee/company
scope **server-side, inside SQL before any TOP/Take**, as the intersection
`People Permission Scope ∩ Access Role Scope`. Crafted IDs are rejected by
`EmployeeCompanyGuard.CanAccessEmployeeAsync` / `CanAccessOwnedRowAsync` (fail-closed)
before any mutation.

| Surface | Company A allowed | Company B blocked | Result |
|---|---|---|---|
| Employees Index | ✅ | ✅ SQL scope intersection before paging | PASS |
| Employee Lookup | ✅ | ✅ dual `ApplyPeopleDataScope` before `Take`, no N+1, `take` clamped 1–200 | PASS |
| Employee Profile | ✅ | ✅ `ViewProfile` scope | PASS |
| Employee Edit | ✅ | ✅ scope + owned-row guard | PASS |
| EmployeeUpdates | ✅ | ✅ list scoped in SQL before limit; manager picker scoped to employee's company (`beee200`) | PASS |
| Compensation | ✅ (with `ViewCompensation`) | ✅ separate permission, not granted by compatibility | PASS |
| Contracts (list/detail) | ✅ | ✅ `e.CompanyId` list filter; `FindContractAsync` scoped | PASS |
| Lifecycle / history | ✅ | ✅ owned-row guard on EndService/Rehire | PASS |
| Files (download) | ✅ | ✅ `ViewProfile` + Access-Role scope intersection on target employee | PASS |
| Legacy `/uploads` file | ✅ via controller | ✅ raw static serving removed; only authenticated controller resolves | PASS |
| TemporaryHeads | ✅ | ✅ scoped listing + scoped writes | PASS |
| Import | ✅ | ✅ effective employee scope; no unauthorized org creation | PASS |
| Export | ✅ | ✅ scoped; salary gated by `ViewCompensation` | PASS |
| Dashboard | ✅ | ✅ every KPI filtered by authorized employees | PASS |

Verification basis: static endpoint audit (Phase 1) plus the passing automated suites
`EmployeeListScopeIntersectionTests`, `EmployeeLookupScopeBeforeLimitTests`,
`EmployeeUpdatesScopeBeforeLimitTests`, `EmployeeImportScopeTests`,
`LookupScopeFilterTests`, `PeopleCompatibilityAccessTests`,
`LegacyUploadsStaticServingGuardTests`.

## 4. Contract Engine

All business mutations route through the canonical `ContractRegisterStore`
(`Add`/`Update`/`Delete`). Callers: `Contracts/Index.cshtml.cs` and
`Employees/Profile.Panels.cshtml.cs` — no independent Profile write engine remains
(`ContractWritePathConsolidationTests`, `ContractDeleteCanonicalTests`).

- **Add** — transactional; when `makeCurrent`, strips `IsCurrent` from the employee's
  other contracts in the same transaction (single-current invariant); history preserved.
- **Update** — targets one contract; promotion to current de-currents siblings in the
  same transaction; attachment written only when a new file is supplied (no accidental
  erase).
- **Delete** — `DeleteContractAsync`: scope checked **before** mutation via owned-row
  guard; `expectedEmployeeId` ownership check; soft-delete `IsDeleted = 1` (no physical
  DELETE); transactional; **idempotent** (already-deleted returns early, before the
  transaction, so no double promotion). Deleting the current contract promotes the newest
  remaining valid contract to current and syncs `Employee.ContractType` /
  `Employee.ContractEndDate`; deleting the last contract clears both flattened fields.
  Deleted rows remain in the table for audit/history.

## 5. Employee Lifecycle

- **EndService** — transactional; separated from Administrative Delete.
- **Rehire** — `SET XACT_ABORT ON` + explicit transaction; idempotent via
  `IF EXISTS (... IsActive = 0)` guard, which prevents double-submit from creating
  duplicate rehire rows or inflating `RehireCount`.
- **Administrative Delete** — distinct `People.AdministrativeDelete` permission, checked
  in-page (defends against the middleware's path-prefix compatibility grant);
  `EmployeeOperationalHistoryGuard` rejects any employee with rows in 20 operational
  tables (attendance/payroll/contracts/leave/loans/requests/documents/violations/EoS) and
  instructs the user to use EndService — **no automatic EndService record is generated**.
  Clean invalid records are soft-deleted (`IsActive=0, IsDeleted=1`) with an audit row.

Suites: `AdministrativeDeletePolicyTests`, `ContractDeleteCanonicalTests`. (No suite is
named `EndService*`/`Rehire*`; their transactional/idempotent behavior was verified by
source inspection — see §13 LOW.)

## 6. Employee Files

- New sensitive uploads use `ProtectedFileService` (Data-Protection-signed tokens);
  `EmployeeFilesController` is the single authenticated download point.
- `Program.cs` uses `app.MapStaticAssets().AllowAnonymous()` and **no `UseStaticFiles`** —
  raw `/uploads/...` sensitive files are no longer publicly served
  (`LegacyUploadsStaticServingGuardTests`).
- Legacy paths resolve only through the controller, gated by `ViewProfile` scope ∩
  Access-Role scope on the target employee. Path traversal is blocked by
  `ProtectedFileStore.TryResolvePhysicalPath` root containment. Missing files fail as
  `NotFound`; no physical path is leaked to the client.

## 7. Compensation

- `People.ViewCompensation` and `People.EditCompensation` are **not** granted implicitly
  by role compatibility (added to `SensitiveCompatibilityDenied` for HR Officer / Branch
  Manager); only Admin / HR Manager or an explicit Access-Role grant carry them.
- Read/write of salary and allowances, and salary values in import/export, are gated by
  these permissions server-side, so a crafted POST cannot bypass the UI.

## 8. Dashboard

- **Scope** — every KPI applies the company employee filter.
- **Probation** — driven by `Probation.*` HR settings (unit/value/basis/extension), not a
  hard-coded 90 days; future-dated hires excluded.
- **Suspended** — real suspended employment state on active employees, **not**
  `IsActive = 0`.
- **New This Month** — `@MonthStart <= HireDate <= @Today` (future hires excluded).
- **Turnover** — `exits × 100 / averageHeadcount`, rounded to 1 decimal; implementation
  matches the displayed definition (`PeopleDashboardMetricsTests`).

## 9. Performance

Scope is applied inside SQL before any limit across Employees Index, Lookup,
EmployeeUpdates, Contracts, TemporaryHeads and the Dashboard; the former per-row
authorization N+1 in Lookup was removed. Remaining hard limits are intentional and safe:

- Lookup `take` clamped to 1–200 with a `take+1` probe for an honest "+N" indicator —
  **intentional UI limit**, applied after scope.
- No `.ToList()`/in-memory pagination before scope was found; no full-directory
  `<select>` loads remain on the audited surfaces.

No performance correctness issue found for 10,000+ employee scale on People surfaces.

## 10. Test Results

| Metric | Value |
|---|---|
| Release build | **0 errors**, 23 warnings (pre-existing nullability, unrelated to People) |
| Total tests | 1451 |
| Passed | 1451 |
| Failed | 0 |
| Skipped | 0 |
| Duration | 11 s |
| `git diff --check` | Clean |

People-specific suites present and passing: `PeopleCompatibilityAccessTests`,
`EmployeeListScopeIntersectionTests`, `EmployeeLookupScopeBeforeLimitTests`,
`EmployeeUpdatesScopeBeforeLimitTests`, `EmployeeImportScopeTests`,
`LookupScopeFilterTests`, `AdministrativeDeletePolicyTests`,
`ContractWritePathConsolidationTests`, `ContractDeleteCanonicalTests`,
`LegacyUploadsStaticServingGuardTests`, `PeopleDashboardMetricsTests`.

## 11. Regression Boundary

This People hardening did **not** change any business calculation logic.

- **Attendance formulas** (lateness, early leave, absence, overtime, processing): **unchanged.**
  The only Attendance edits (`AttendanceRecords/Index`, `AttendanceViewer/Index`) are
  company-scoping of filter dropdown lists — a **security/filtering change**, not a
  calculation change.
- **Payroll formulas** (gross, deductions, missing-punch, absence, overtime, net):
  **unchanged.** No `Pages/Payroll` or `SmartAttendance.Domain` business files were
  modified; the only `SmartAttendance.Application` edits are security types
  (`PeopleDataScope`, `PeoplePermissionCodes`, `EmployeeListQueryViewModel`).
- **Leave entitlement/accrual formulas**: **unchanged.**

## 12. GitHub CI / Protection

- **CI exists**: `.github/workflows/ci.yml` runs Build (Release) + Unit tests (explicit
  project path) + NuGet vulnerability audit on `pull_request`→`main`, `push`→`main`, and
  manual dispatch; an optional Playwright E2E job runs only on dispatch with configured
  secrets and never against production. It performs no deployment.
- **Required status checks / branch protection**: could not be confirmed — `gh` is not
  authenticated in this environment. A local `dotnet test` is **not** a substitute for the
  GitHub-run checks; CI will execute when the PR to `main` is opened/updated.

## 13. Remaining Risks

- **LOW** — EndService/Rehire have no dedicated named test suite. Affected surface:
  Employee lifecycle. Their transaction (`XACT_ABORT`) and idempotency guards were
  verified by source inspection and pass build/compile; recommend adding explicit
  regression tests during merge preparation.
- **LOW** — Branch-protection / required-checks state is unverified (unauthenticated
  `gh`). Affected surface: integration gate. Recommend confirming required checks are
  enabled on `main` before merge.

`No known People-module release blocker remains.`

## 14. Merge Preparation Recommendation

**Do not merge in this task.** For the later integration step:

1. Open/refresh the PR from `claude/smartattendance-local-rebuild-wftwb3` to `main` and let
   `ci.yml` (build + tests + NuGet audit) run — this is the authoritative gate, not local
   testing.
2. Confirm branch protection / required status checks on `main` (needs authenticated `gh`).
3. Optionally add EndService/Rehire regression tests (§13 LOW).
4. The tree merges cleanly today (`git merge-tree` reports no conflicts; the 2 "behind"
   commits are prior merges of this same branch), so no rebase/conflict resolution is
   expected — but re-verify at merge time since `main` can move.
5. Merge and deployment remain a human decision (Mohammad); no deployment is part of this
   work.
