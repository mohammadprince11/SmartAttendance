# ZYNORA PAYROLL ENGINE CORRECTION REPORT

_2026-08-09. Study-first architecture correction. Scope this session (per decision): **core salary-base engine + golden tests first**. All seven golden scenarios are now implemented and tested; the Settings/FinancialInfo UI that lets the org select the new modes/flags is still deferred. No merge, no deploy, no force-push._

## 1. Baseline

| Item | Value |
|---|---|
| Branch | `claude/smartattendance-local-rebuild-wftwb3` |
| Starting SHA | `42b1549` (People acceptance report) |
| Ending SHA | `f3a0ea7` |
| Commits added | `2940d4a` (attendance base), `0291391` (gosi/tax employee-defined), `dc769f2` (report), `f3a0ea7` (unpaid-leave/overtime/divisor) |
| Main SHA (`origin/main`) | `73169433` (not modified) |

Working HEAD was `beee200` at task start; only my People report `42b1549` sat above it (preserved). No reset/rebase/merge.

## 2. Original Problems (verified against `PayrollRunStore.cs`, not documentation)

- **Attendance**: factor applied to Basic only (`proratedBasic = basic × factor`, L601); every allowance added at full amount (L618). Absence never touched allowances.
- **Allowances**: totalled as one full amount; the `SalaryItem.Prorated` flag existed but was **never consumed** for employee allowances.
- **GOSI**: `EmployeeFinancialInfos.SocialSecuritySalary` was captured/saved but **never read** by payroll; GOSI always computed on full Gross (default `GosiBase = Gross`).
- **Tax**: no current tax-salary concept; `PreviousTaxSalary` is an opening balance. Tax always from composed components.
- **Unpaid leave**: `unpaidDays × (basic/30)` — Basic-only, hidden `30` (L884). _(not yet changed)_
- **Overtime**: `hours × (basic/30/8) × rateFactor` — Basic-only, hidden `30`/`8` (L644). _(not yet changed)_
- **Penalties**: already correct/configurable via `BasePoolJson` + `WorkDaysBasis` — the model the others should follow. _(unchanged, by design)_

## 3. New Salary Base Architecture

| Base | Status | Source of truth |
|---|---|---|
| `TaxBase` | existed | `SalaryBaseComposer` membership per profile |
| `GosiBase` | existed | `SalaryBaseComposer` (default Gross) |
| **AttendanceBase** | **new (this session)** | per-component: Basic (always sensitive) + allowances sensitive iff `SalaryItem.Prorated` |
| **Employee-defined Tax/GOSI base** | **new (this session)** | `EmployeeDefinedSalaryBase`: `EmployeeDefined` mode → employee value; else composed |
| `PenaltyBase` | existed | `BasePoolJson` + `WorkDaysBasis` |
| **OvertimeBase, UnpaidLeaveBase** | **new (this session)** | `PayrollEarningBase` (Basic vs Basic+eligible allowances) ÷ `PayrollDivisorPolicy` |
| **Divisor policy** | **new (this session)** | `PayrollDivisorPolicy` owns `Payroll.SalaryDaysBasis` (Fixed30) + `Payroll.StandardDailyHours` (8) |

New pure, tested units total: `AttendanceSalaryBase`, `EmployeeDefinedSalaryBase`, `PayrollEarningBase`, `PayrollRateBasis`, `PayrollDivisorPolicy`.

New pure, tested units: `AttendanceSalaryBase`, `EmployeeDefinedSalaryBase`. Both live in infrastructure payroll logic (no duplicated calculation in pages).

## 4. Employee Financial Fields

- `BasicSalary` — read as before.
- Fixed Gross — still implicit (Basic + recurring allowances), no stored column.
- `SocialSecuritySalary` — **now consumed** when `GosiBaseMode = EmployeeDefined`.
- `CurrentTaxSalary` — **new column**; consumed when `TaxBaseMode = EmployeeDefined`. Distinct from `PreviousTaxSalary` (untouched).
- `TaxBaseMode`, `GosiBaseMode` — **new columns**; empty ⇒ `SalaryComponents` (today's behavior).
- Daily/Hourly rate — still `basic/30`, `/8` (central divisor policy planned).

Migration `20260809-01-employee-defined-tax-gosi-base`: three nullable columns, idempotent, non-destructive; `PreviousTaxSalary` preserved.

## 5. Attendance Behavior

- Absence/late/early/missing-punch flow through the attendance **factor** (`EmployeeMonthAttendance` → `AttendanceSalaryLink`), unchanged.
- What changed: the factor now applies to **each attendance-sensitive earning component**, not Basic alone. Basic is always sensitive; an allowance is sensitive iff its `SalaryItem.Prorated = true`. Fixed allowances stay full.
- Paid leave / unpaid leave day handling unchanged this session (unpaid-leave base is planned).

## 6. Allowance Behavior

- **Attendance participation**: `SalaryItem.Prorated` (existing flag, now consumed). Default false ⇒ allowance is attendance-fixed = old behavior.
- **Tax / GOSI eligibility**: still at the base-membership (aggregate) level via `SalaryBaseComposer`; per-allowance tax/GOSI split not changed this session.
- EmployeeAllowance inherits the SalaryItem policy by `ItemName` (free-text match, case-insensitive).

## 7. Tax

- Base source: composed `TaxBase`, **or** `CurrentTaxSalary` when `TaxBaseMode = EmployeeDefined` and value > 0.
- Exemption then progressive brackets (`ComputeTax`), unchanged.
- Golden: base 400,000, test profile (exemption 250,000; 3% then 5%) ⇒ (400,000−250,000)×3% = **4,500** (proven ≠ tax on 2,000,000).

## 8. GOSI

- Base source: composed `GosiBase` (default Gross), **or** `SocialSecuritySalary` when `GosiBaseMode = EmployeeDefined` and value > 0.
- Employee/company rates + ceiling from `GosiProfile` (configurable; Iraq 5%/12% is seeded config flagged "needs accountant confirmation", not hard-coded).
- Golden: base 400,000 × 5% = **20,000** employee, × 12% = **48,000** company (was computed on 2,000,000).

## 9. Overtime

`hours × hourlyRate × rateFactor`, where `hourlyRate` now derives from `OvertimeBase ÷ divisor ÷ dailyHours` via `PayrollEarningBase` + `PayrollDivisorPolicy`. Default (`OvertimeBaseMode = Basic`, Fixed30, 8h) reproduces `basic/30/8` exactly; day-context rate factors unchanged. Divisor and daily-hours are now owned by an explicit policy, not inline literals.

## 10. Golden Employee Results (`TEST-PAY-001`: Basic 400,000 + Allowances 1,600,000)

| Scenario | Result | Status |
|---|---|---|
| 1 — full attendance | AttendanceAdjusted = FixedGross = **2,000,000** | ✅ tested |
| 2 — 2 absent / 26, Basic+allowances sensitive | AttendanceBase 2,000,000; adjusted **1,846,153.85** (old was 1,969,230.77) | ✅ tested |
| 3 — Housing (prorate) 1.5M + Phone (fixed) 100k | **1,853,846.15** (Housing 1,384,615.38 + Phone full) | ✅ tested |
| 4 — GOSI EmployeeDefined 400,000 | employee **20,000**, company **48,000** | ✅ tested |
| 5 — Tax EmployeeDefined 400,000 | tax **4,500** (test profile), ≠ tax on 2M | ✅ tested |
| 6 — Unpaid leave 1 day, Basic+allowances base 2M/30 | **66,666.67** (default Basic-only = 13,333.33) | ✅ tested |
| 7 — Overtime 8h × 1.5 | Basic base **20,000**; Basic+allowances base **100,000** | ✅ tested |

Full end-to-end line integration test (all effects combined) requires a SQL-backed fixture; existing payroll tests are pure, so golden scenarios are asserted at the pure-helper level with exact decimals.

## 11. Regression Tests

- Payroll-related suites present and passing, including new `AttendanceSalaryBaseTests` (6), `EmployeeDefinedSalaryBaseTests` (5) and `PayrollBasePolicyTests` (6), alongside existing `SalaryBaseComposerTests`, `AttendanceSalaryLinkTests`, `PayrollProfileResolverTests`, `PayrollFormulaVariablesTests`, `PayrollCompanyIsolationTests`.
- No existing test was removed or weakened; the 1451 prior tests remain green (defaults reproduce old numbers).

## 12. Full Build/Test

| Metric | Value |
|---|---|
| Release build | **0 errors** |
| Total tests | **1468** |
| Passed | 1468 |
| Failed | 0 |
| Skipped | 0 |
| `git diff --check` | clean |

## 13. Performance

- No new full-table scans. The attendance-sensitivity map is built once per run from the already-loaded `SalaryItems` list (no per-employee query). Employee-defined base columns ride the existing single `EmployeeFinancialInfos` read.
- Existing company scoping on the run (employees SQL-filtered by `run.CompanyId`) is unchanged.

## 14. Remaining Risks

- **HIGH** — The new policies (`TaxBaseMode`, `GosiBaseMode`, `OvertimeBaseMode`, `UnpaidLeaveBaseMode`, `SalaryDaysBasis`, `StandardDailyHours`) and the SalaryItem flags (`Prorated`, `OvertimeEligible`, `UnpaidLeaveEligible`) have **no UI writer yet** (Settings/FinancialInfo reorg deferred). The engine reads them correctly; until screens set them, the org can only opt in via direct settings/DB writes. Columns default to the legacy composed/Basic behavior, so this is safe but not yet operator-usable.
- **MEDIUM** — Double-deduction audit (Phase 18: late → factor + violation + manual deduction) not yet done; no regression test proving a single event isn't deducted twice.
- **LOW** — Per-allowance tax/GOSI eligibility (Phase 2 full model) not implemented; tax/GOSI still use aggregate base membership.
- **LOW** — Payslip calculation trace (Phase 15) not yet surfaced; components already carry `Kind`, so the trace is derivable without new tables.

`No known payroll release blocker introduced; all changes are backward-compatible and opt-in.`

## 15. Data Safety

- No production test employee inserted (`TEST-PAY-001` exists only in unit tests).
- No historical payroll destroyed; `CalculateAsync` still rejects Locked/Issued/PayslipSent runs.
- Migration is additive (nullable columns), non-destructive; `PreviousTaxSalary` untouched.
- No merge, no deploy, no force-push.

---

## RELEASE DECISION

`PAYROLL ENGINE NOT READY — BLOCKERS REMAIN`

The **engine correctness core is complete**: all six configurable salary bases (Attendance, Tax, GOSI, Overtime, UnpaidLeave, Penalty) now compose from explicit, configurable sources, all seven golden scenarios pass with exact decimals, and everything is backward-compatible (1468/1468 green, defaults reproduce old numbers). It is **NOT READY for acceptance testing** because the operator cannot yet configure the new policies — the Settings/FinancialInfo UI is deferred (HIGH risk above) — and the double-deduction audit (Phase 18) and payslip trace (Phase 15) remain. Acceptance testing should wait until the configuration UI lands so HR can actually select and verify these bases end-to-end. Do **not** merge to main.
