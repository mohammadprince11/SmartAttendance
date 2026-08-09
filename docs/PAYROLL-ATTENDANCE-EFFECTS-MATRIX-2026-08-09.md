# ZYNORA — Attendance → Payroll Effects Matrix

_2026-08-09. How each attendance state affects pay, on which base, by which formula, and from which configuration source. No attendance status alters pay through an undocumented formula._

## How attendance reaches payroll

Punches → `DayAttendances` (analyzed) → `EmployeeMonthAttendance` (monthly rollup: `WorkDays`, `PresentDays`, `AbsentDays`, `UnpaidLeaveDays`, `WorkedHours`) → `AttendanceSalaryLink.Evaluate` produces a single **factor** → the factor prorates each **attendance-sensitive** earning component (`AttendanceSalaryBase`).

The factor mode is user-selected (`Payroll.AttendanceLink`): **Lenient** (no data ⇒ pay full, declared), **Strict** (no data ⇒ not paid), **PresentDays** (present ÷ work days), **Hours** (worked ÷ expected). Extra absence weight comes from `Payroll.AbsenceDeductionDays` (1 = day-for-day).

## Matrix

| Attendance status | Payroll effect | Salary base | Formula | Configuration source |
|---|---|---|---|---|
| **Present** | Full pay for the day | AttendanceBase | counted in `PresentDays`/`WorkedHours` → factor 1 for that day | — |
| **Absent** | Reduces the attendance factor | AttendanceBase (Basic + attendance-sensitive allowances) | `factor = (workDays − absentDays)/workDays − extra`; `extra = absentDays×(AbsenceDeductionDays−1)/workDays` | `Payroll.AttendanceLink`, `Payroll.AbsenceDeductionDays` |
| **Late** | Only via the chosen factor mode (Hours/PresentDays lower it); optionally a disciplinary penalty | AttendanceBase (factor); PenaltyBase (penalty) | factor as above; penalty per `DisciplinaryPenaltyRules` | attendance mode + violation rule (separate, see double-deduction guard) |
| **Early leave** | Same as Late — through the factor mode and/or a penalty | AttendanceBase / PenaltyBase | as above | attendance mode + violation rule |
| **Missing punch** | Depends on how it lands in the monthly rollup (reduces present/worked hours) | AttendanceBase | via `PresentDays`/`WorkedHours` in the factor | `Payroll.AttendanceLink` mode |
| **Paid leave** | No deduction (counts as worked) | — | not subtracted | leave type = paid |
| **Unpaid leave** | Post-gross deduction of the unpaid days | UnpaidLeaveBase (Basic or Basic + eligible allowances) ÷ divisor | `unpaidDays × (UnpaidLeaveBase ÷ divisor)` | `Payroll.UnpaidLeaveBaseMode`, `Payroll.SalaryDaysBasis`; SalaryItem `UnpaidLeaveEligible` |
| **Rest day** | No effect by itself | — | excluded from work-days where a penalty divisor requests it | — |
| **Holiday** | No effect by itself | — | as rest day | — |
| **Work on rest day** | Overtime earning | OvertimeBase ÷ divisor ÷ dailyHours | `hours × hourlyRate × restDayFactor` | `Payroll.OvertimeBaseMode`, divisor/hours, shift rest-day rate factor |
| **Work on holiday** | Overtime earning | OvertimeBase ÷ divisor ÷ dailyHours | `hours × hourlyRate × holidayFactor` | overtime base + shift holiday rate factor |

## Double-counting boundary

A single event (e.g. lateness) may touch up to three **independent** channels — attendance proration (inside gross), a disciplinary penalty, and a manual deduction. Each is a distinct payslip component (`Kind` = Basic vs Penalty vs Deduction) and is applied **once**; the same event is deducted twice only when an operator deliberately configures two channels. Guarded by `PayrollDoubleDeductionTests`.

## Explainability

Every line persists its trace (`AttendanceBase`, `AttendanceFactor`, `TaxBase`/source, `GosiBase`/source) and its `Kind`-tagged components, shown in the RunDetail payslip under "أثر الاحتساب" — so each dinar traces to a base, a factor, a policy, and a source without recomputation.
