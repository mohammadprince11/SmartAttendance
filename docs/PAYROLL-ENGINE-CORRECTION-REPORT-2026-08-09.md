# ZYNORA PAYROLL ENGINE CORRECTION REPORT

_2026-08-09. Study-first architecture correction, then full dynamic configuration. All seven golden scenarios implemented and tested; every new base/flag is now user-editable from the UI; double-deduction is guarded. No merge, no deploy, no force-push._

## 1. Baseline

| Item | Value |
|---|---|
| Branch | `claude/smartattendance-local-rebuild-wftwb3` |
| Starting SHA | `42b1549` (People acceptance report) |
| Ending SHA | `032ed75` |
| Commits added | `2940d4a` (attendance base), `0291391` (gosi/tax employee-defined), `f3a0ea7` (unpaid-leave/overtime/divisor), `031b02d` (config UI), `a5790db` (double-deduction guard), `032ed75` (per-allowance tax/gosi + payslip trace), + report commits |
| Main SHA (`origin/main`) | `73169433` (not modified) |

Working HEAD was `beee200` at task start; only my People report `42b1549` sat above it (preserved). No reset/rebase/merge.

## 2. Original Problems (verified against `PayrollRunStore.cs`, not documentation)

- **Attendance**: factor applied to Basic only (`proratedBasic = basic × factor`, L601); every allowance added at full amount (L618). Absence never touched allowances.
- **Allowances**: totalled as one full amount; the `SalaryItem.Prorated` flag existed but was **never consumed** for employee allowances.
- **GOSI**: `EmployeeFinancialInfos.SocialSecuritySalary` was captured/saved but **never read** by payroll; GOSI always computed on full Gross (default `GosiBase = Gross`).
- **Tax**: no current tax-salary concept; `PreviousTaxSalary` is an opening balance. Tax always from composed components.
- **Unpaid leave**: was `unpaidDays × (basic/30)` — Basic-only, hidden `30` (L884). _(now configurable base + divisor)_
- **Overtime**: was `hours × (basic/30/8) × rateFactor` — Basic-only, hidden `30`/`8` (L644). _(now configurable base + divisor)_
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

New pure, tested units total: `AttendanceSalaryBase`, `EmployeeDefinedSalaryBase`, `PayrollEarningBase`, `PayrollRateBasis`, `PayrollDivisorPolicy` — all in infrastructure payroll logic (no duplicated calculation in pages).

## 4. Employee Financial Fields

- `BasicSalary` — read as before.
- Fixed Gross — still implicit (Basic + recurring allowances), no stored column.
- `SocialSecuritySalary` — **now consumed** when `GosiBaseMode = EmployeeDefined`.
- `CurrentTaxSalary` — **new column**; consumed when `TaxBaseMode = EmployeeDefined`. Distinct from `PreviousTaxSalary` (untouched).
- `TaxBaseMode`, `GosiBaseMode` — **new columns**; empty ⇒ `SalaryComponents` (today's behavior).
- Daily/Hourly rate — overtime/unpaid-leave rates now flow through `PayrollDivisorPolicy` (defaults `basic/30`, `/8`); generic daily/hourly for salary-days/leave-encashment still `basic/30`.

Migration `20260809-01-employee-defined-tax-gosi-base`: three nullable columns, idempotent, non-destructive; `PreviousTaxSalary` preserved.

## 5. Attendance Behavior

- Absence/late/early/missing-punch flow through the attendance **factor** (`EmployeeMonthAttendance` → `AttendanceSalaryLink`), unchanged.
- What changed: the factor now applies to **each attendance-sensitive earning component**, not Basic alone. Basic is always sensitive; an allowance is sensitive iff its `SalaryItem.Prorated = true`. Fixed allowances stay full.
- Unpaid-leave deduction now uses the configurable `UnpaidLeaveBase` ÷ divisor (default Basic ÷ 30).

## 6. Allowance Behavior

- **Attendance participation**: `SalaryItem.Prorated` (existing flag, now consumed). Default false ⇒ allowance is attendance-fixed = old behavior.
- **Tax / GOSI eligibility**: per-allowance via `SalaryItem.Taxable` / `GosiEligible`, surfaced as the `TaxableAllowances` / `GosiAllowances` composer components (a profile opts in by swapping base membership); default membership still uses all allowances.
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
| Total tests | **1474** |
| Passed | 1474 |
| Failed | 0 |
| Skipped | 0 |
| `git diff --check` | clean |

## 13. Performance

- No new full-table scans. The attendance-sensitivity map is built once per run from the already-loaded `SalaryItems` list (no per-employee query). Employee-defined base columns ride the existing single `EmployeeFinancialInfos` read.
- Existing company scoping on the run (employees SQL-filtered by `run.CompanyId`) is unchanged.

## 14. Dynamic configuration (all user-editable)

Every new policy and flag is now editable from the UI, matching the no-code ethos:

- **Salary Items** (`/Payroll/SalaryItems`): `Prorated` (attendance), `OvertimeEligible`, `UnpaidLeaveEligible` checkboxes.
- **Payroll Settings** (`/Payroll/Settings` → "أوعية الراتب والمقام"): Overtime base mode, Unpaid-leave base mode, Salary-days basis, Standard daily hours.
- **Employee Financial Info**: `GosiBaseMode`, `TaxBaseMode` selectors + current tax-salary field.

All default to legacy behavior; nothing changes until an operator opts in.

## 15. Remaining Risks

- **DONE** — Per-allowance tax/GOSI eligibility (Phase 2): `SalaryItem.GosiEligible` + `Taxable` drive the new `TaxableAllowances`/`GosiAllowances` composer components; a profile opts in by swapping its base membership. Editable from Salary Items.
- **DONE** — Payslip trace (Phase 15): AttendanceBase/factor + Tax/GOSI base and source persisted per line and shown in the RunDetail payslip ("أثر الاحتساب").
- **LOW** — Live two-company end-to-end payroll run not executed here (the dev server shares the production DB; running migrations/calcs there needs deploy authorization). Compile + 1474 unit tests cover the logic; a real run is part of acceptance testing itself.

`No known payroll release blocker remains; all changes are backward-compatible, opt-in, and operator-configurable.`

## 15. Data Safety

- No production test employee inserted (`TEST-PAY-001` exists only in unit tests).
- No historical payroll destroyed; `CalculateAsync` still rejects Locked/Issued/PayslipSent runs.
- Migration is additive (nullable columns), non-destructive; `PreviousTaxSalary` untouched.
- No merge, no deploy, no force-push.

---

## RELEASE DECISION

`PAYROLL ENGINE READY FOR ACCEPTANCE TESTING`

All six salary bases (Attendance, Tax, GOSI, Overtime, UnpaidLeave, Penalty) compose from explicit, **user-configurable** sources; all seven golden scenarios pass with exact decimals; the three deduction channels are guarded against double-counting; and everything is backward-compatible (**1474/1474 green**, defaults reproduce the old numbers). Every new policy and flag is editable from the UI, so HR can select and verify each base end-to-end. Remaining items (§15) are LOW enhancements, not blockers. Acceptance testing should now be run against a controlled non-production two-company database. Do **not** merge to main, and do **not** deploy — the additive migrations (`20260809-01/02`) apply on the next authorized deploy.
