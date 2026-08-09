# ZYNORA PAYROLL ENGINE CORRECTION REPORT

_2026-08-09. Study-first architecture correction. Scope this session (per decision): **core salary-base engine + golden tests first**; Settings/FinancialInfo UI reorg and the remaining two golden scenarios (unpaid-leave, overtime) are planned, not yet implemented. No merge, no deploy, no force-push._

## 1. Baseline

| Item | Value |
|---|---|
| Branch | `claude/smartattendance-local-rebuild-wftwb3` |
| Starting SHA | `42b1549` (People acceptance report) |
| Ending SHA | `0291391` |
| Commits added | `2940d4a` (attendance base), `0291391` (gosi/tax employee-defined) |
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
| `OvertimeBase`, `UnpaidLeaveBase` | **planned** | to reuse the same per-component model + central divisor |

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

Unchanged this session. Current: `hours × (basic/30/8) × rateFactor`; rate factors by day context already configurable. `OvertimeBase` + central divisor/daily-hours planned.

## 10. Golden Employee Results (`TEST-PAY-001`: Basic 400,000 + Allowances 1,600,000)

| Scenario | Result | Status |
|---|---|---|
| 1 — full attendance | AttendanceAdjusted = FixedGross = **2,000,000** | ✅ tested |
| 2 — 2 absent / 26, Basic+allowances sensitive | AttendanceBase 2,000,000; adjusted **1,846,153.85** (old was 1,969,230.77) | ✅ tested |
| 3 — Housing (prorate) 1.5M + Phone (fixed) 100k | **1,853,846.15** (Housing 1,384,615.38 + Phone full) | ✅ tested |
| 4 — GOSI EmployeeDefined 400,000 | employee **20,000**, company **48,000** | ✅ tested |
| 5 — Tax EmployeeDefined 400,000 | tax **4,500** (test profile), ≠ tax on 2M | ✅ tested |
| 6 — Unpaid leave 1 day, base 2M/30 = 66,666.67 | — | ⏳ planned |
| 7 — Overtime base variants | — | ⏳ planned |

Full end-to-end line integration test (all effects combined) requires a SQL-backed fixture; existing payroll tests are pure, so golden scenarios are asserted at the pure-helper level with exact decimals.

## 11. Regression Tests

- Payroll-related suites present and passing, including new `AttendanceSalaryBaseTests` (6) and `EmployeeDefinedSalaryBaseTests` (5), alongside existing `SalaryBaseComposerTests`, `AttendanceSalaryLinkTests`, `PayrollProfileResolverTests`, `PayrollFormulaVariablesTests`, `PayrollCompanyIsolationTests`.
- No existing test was removed or weakened; the 1451 prior tests remain green (defaults reproduce old numbers).

## 12. Full Build/Test

| Metric | Value |
|---|---|
| Release build | **0 errors** |
| Total tests | **1462** |
| Passed | 1462 |
| Failed | 0 |
| Skipped | 0 |
| `git diff --check` | clean |

## 13. Performance

- No new full-table scans. The attendance-sensitivity map is built once per run from the already-loaded `SalaryItems` list (no per-employee query). Employee-defined base columns ride the existing single `EmployeeFinancialInfos` read.
- Existing company scoping on the run (employees SQL-filtered by `run.CompanyId`) is unchanged.

## 14. Remaining Risks

- **MEDIUM** — Unpaid-leave and overtime bases still Basic-only with hidden `/30`,`/8`. Not a regression (unchanged), but the architecture goal is incomplete until they and a central divisor policy land.
- **MEDIUM** — New base modes (`TaxBaseMode`/`GosiBaseMode`) have **no UI writer yet** (FinancialInfo/Settings reorg deferred). The engine reads them; until a screen sets them, the org cannot opt in through the UI (columns default to composed = safe).
- **LOW** — Per-allowance tax/GOSI eligibility (Phase 2 full model) not implemented; tax/GOSI still use aggregate base membership.
- **LOW** — Double-deduction audit (Phase 18) and payslip calculation trace (Phase 15) not yet done.

`No known payroll release blocker introduced; all changes are backward-compatible and opt-in.`

## 15. Data Safety

- No production test employee inserted (`TEST-PAY-001` exists only in unit tests).
- No historical payroll destroyed; `CalculateAsync` still rejects Locked/Issued/PayslipSent runs.
- Migration is additive (nullable columns), non-destructive; `PreviousTaxSalary` untouched.
- No merge, no deploy, no force-push.

---

## RELEASE DECISION

`PAYROLL ENGINE NOT READY — BLOCKERS REMAIN`

Not because anything shipped is unsafe — the two delivered corrections are backward-compatible, tested (1462/1462), and opt-in — but because the **architecture correction is intentionally incomplete** for this session's agreed scope: unpaid-leave and overtime bases, the central divisor policy, the Settings/FinancialInfo UI that lets the org actually select the new modes, and golden scenarios 6–7 remain. Acceptance testing of the full engine should wait until those land. Do **not** merge to main.
