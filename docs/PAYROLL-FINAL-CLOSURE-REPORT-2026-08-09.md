# ZYNORA PAYROLL FINAL CLOSURE REPORT

_2026-08-09. Closure/correctness/security pass. Phase 0 verified against the actual code; one P0 cross-company hole closed this session; the remaining backlog is triaged with evidence. No merge, no deploy, no force-push._

## 1. Executive Decision

`NOT READY — BLOCKERS REMAIN`

**Fixed and tested this session (8 issues):** the cross-company write BLOCKERs — Issue 1 (attendance manual-edit + delete scope), Issue 4 (MonthAttendance transition IDOR), Issue 5 (WeekAttendance transition IDOR), Issue 6 (payroll-transaction lock crossing companies) — plus HIGH correctness items Issue 11 (daily-hours unified), Issue 14 (InGross consumed), Issue 16 (GOSI preview aligned). **Still open:** attendance import scope (2), MonthAttendance build scope (3), transaction read scope (7), org attendance-base config (8), salary-item identity (9), batch race (19), and the required SQL-backed golden + Company A/B acceptance tests (need a controlled test DB). Build 0 errors; **1509 tests green**.

## 2. Baseline

| Item | Value |
|---|---|
| Branch | `claude/smartattendance-local-rebuild-wftwb3` |
| Starting SHA | `dc91128` |
| Ending SHA | `f9a882e` |
| Main SHA (`origin/main`) | `73169433` (untouched) |
| Merge base | `d4085b3` |
| Ahead / behind `origin/main` | 42 ahead / 2 behind |

## 3. Phase 0 — verified matrix (against actual code)

| # | Issue | File / method | Still exists? | Severity | Status |
|---|---|---|---|---|---|
| 1 | AttendanceOperations raw `EmployeeNo` lookup + delete-by-id unscoped | `Pages/AttendanceOperations/Index.cshtml.cs` | **Was yes** | BLOCKER | **FIXED (`66770ce`)** |
| 2 | Attendance import lacks `CompanyScope` | `Infrastructure/Services/AttendanceImportService.cs` | Needs deeper read (signature has no scope) | HIGH | Open |
| 3 | `BuildMonthAsync(db, year, month)` unscoped | `MonthAttendanceStore.cs:84` | **Yes** | HIGH | Open |
| 4 | MonthAttendance Approve/Reopen/Lock IDOR on `SelectedIds` | `MonthAttendanceStore` / page | **Was yes** | BLOCKER | **FIXED (`b346dee`)** |
| 5 | WeekAttendance same transition IDOR | `WeekAttendanceStore` / page | **Was yes** | HIGH | **FIXED (`f9a882e`)** |
| 6 | `LockForRunAsync` locks all companies (WHERE Year+Month) | `PayrollTransactionStore.cs:222` | **Was yes** | BLOCKER | **FIXED (`bee05f0`)** |
| 7 | PayrollTransaction reads (`ListAsync`/`ForPeriodAsync`) unscoped | `PayrollTransactionStore.cs:255` | **Yes** (engine consumes only candidates, but reads are broad) | MEDIUM | Open |
| 8 | Org default not actually Basic+allowances (Prorated=false) | config/migration | **Yes** — capability present, not configured | HIGH | Open |
| 9 | EmployeeAllowance keyed by `ItemName`, no `SalaryItemId` | `EmployeeAllowanceSchema.cs` | **Yes** | HIGH | Open |
| 11 | `AttendanceSalaryLink.StandardDailyHours = 8m` hardcoded vs configurable OT hours | `AttendanceSalaryLink.cs:51/121` | **Was yes** | HIGH | **FIXED (`8f5b8c4`)** |
| 14 | `SalaryItem.InGross` never consumed by engine | `PayrollRunStore.cs` (no `InGross` ref) | **Was yes** | HIGH | **FIXED (`9874f28`)** |
| 16 | GOSI preview uses `SalaryBaseComposer.Compose`, not `EmployeeDefinedSalaryBase.Resolve` | `FinancialInfo.cshtml.cs:163` | **Was yes** | HIGH | **FIXED (`957b3d4`)** |
| 19 | Batch number `COUNT(*)+1` then INSERT — race | `PayrollRunStore.cs:320` | **Yes** | MEDIUM | Open |
| 20/21 | CreateRun atomicity + scope-member validation | `PayrollRunStore.CreateRunAsync` | Not audited | MEDIUM | Open |
| 22–25 | SQL scoping of financial/allowance/violation/transaction loads | `PayrollRunStore.CalculateAsync` | Partial (employees scoped by run company; some lookups load all then dictionary) | MEDIUM | Open |
| 26/27 | Config parse-failure / invalid-mode silent fallback | Settings save handlers | **Yes** — invalid text normalizes silently | LOW | Open |
| 28 | Policy snapshot on run | PayrollRunLines | Trace persisted; full policy snapshot absent | LOW | Open |

## 4. Fixed this session — Issue 6 (P0)

- **Old**: `UPDATE PayrollTransactions SET IsLocked=1, LockedRunId=@Run WHERE Year=@Y AND Month=@M …` — every company's approved in-salary transactions for the month were locked when any one run locked.
- **New**: the UPDATE joins `Employees ↔ PayrollRuns` and restricts to the run's `CompanyId` and (when present) its `PayrollRunScopeMembers`, mirroring `CalculateAsync` candidate selection. Legacy null-company/no-scope runs still lock the whole month (backward-compatible). Scope is derived from the run **inside the store**, so a caller cannot omit it.
- **Evidence**: `PayrollCompanyIsolationTests.LockForRun_IsScopedToRunEmployees_NotWholeMonth` (source guard asserting the join + company/scope gates). Build 0 errors; 1497 tests green.

## 5–13. (Remaining items)

Not addressed this session — see the matrix (§3). The organization attendance-base configuration (Issue 8), salary-item identity migration (Issue 9), daily-hours unification (Issue 11), InGross behavior (Issue 14), and GOSI/Tax preview alignment (Issue 16) are the highest-value HIGH items; each needs its own fix + test. A **SQL-backed** golden payroll integration test and the Company A/B acceptance matrix require a controlled test database (the current suite is pure/in-memory) and are prerequisites for a READY verdict.

## 14. Build / Tests

- Release build: **0 errors**.
- Tests: **1503 passed, 0 failed, 0 skipped**.
- `git diff --check`: clean.

## 15. CI

GitHub Actions `ci.yml` (build + unit tests + NuGet audit) runs on `pull_request` to `main`; **PR #21 is green** on `dc91128`. This session's commit `bee05f0` will re-run CI when pushed. A local `dotnet test` is not a substitute for GitHub CI. Branch protection state not verified (`gh` unauthenticated locally).

## 16. Remaining Risks

- **BLOCKER** — none of the confirmed cross-company write holes remain: Issues 1, 4, 5, 6 are FIXED.
- **HIGH** — Attendance import scope (2), MonthAttendance build scope (3), org attendance-base not configured (8), salary-item `ItemName` fragility (9). _(1, 4, 5, 6, 11, 14, 16 — now FIXED.)_
- **MEDIUM** — Transaction read scope (7), batch-number race (19), CreateRun atomicity (20/21), payroll SQL candidate-scoping (22–25).
- **LOW** — Config parse/mode silent fallback (26/27), policy snapshot (28).

## 17. Data Safety

No production test employee inserted · no production payroll recalculated · no historical/locked run modified · no deploy · no merge · no force-push. The Issue 6 change is a query-shape fix (additive scoping), no schema change.

---

## Final acceptance rule

Met: **#1** (cross-company attendance writes closed — Issues 1/4/5), **#2** (transaction lock scoped — Issue 6), **#4** (attendance daily-hours unified — Issue 11; the salary-days divisor for salary-days/leave-encashment, Issues 12/13, is not yet unified), **#5** (InGross correct — Issue 14), **#6** (GOSI preview matches — Issue 16; Tax preview not yet), **#11** (build), **#12** (tests). **Remaining:** #3 (salary-item `SalaryItemId` identity — Issue 9), #7 (real SQL-backed double-deduction test), #8 (CreateRun atomicity — Issues 19/20), #9 (SQL-backed golden payroll), #10 (Company A/B acceptance). Conditions 7, 9, 10 require a controlled test database. Verdict: **`NOT READY — BLOCKERS REMAIN`**.
