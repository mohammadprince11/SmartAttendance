# ZYNORA HR — Full Production-Readiness Audit & Hardening Report

**Date:** 2026-08-09 · **Author:** Claude (executing agent) · **Session scope:** Phase 0 audit + Waves for Phases 5, 15, 8.

> ⚠️ This report is honest about scope. The audit prompt spans 30 phases; a large
> share is **DB-GATED** (blocked on a non-production SQL Server) or requires a real
> browser matrix / 10k-scale datasets. This session **confirmed** the top blocker
> hypotheses against current code and **closed three** of them with regression
> tests. The remainder is scoped, prioritized, and explicitly marked below. No
> false green.

---

## 1. Verified baseline

| Fact | Value |
|---|---|
| Branch | `claude/smartattendance-local-rebuild-wftwb3` |
| Start SHA (externally observed) | `6fecb5a88ac0ff7214bf83f417e7c078005cfa07` |
| End SHA (this session) | `3a0b478` |
| origin/main | `56730a86c51a6ba7ac3cbda025f9f08beaea02ba` |
| merge-base(HEAD, main) | `dc91128` |
| Ahead / behind vs origin/main | **19 ahead / 3 behind** (diverged) |
| Branch vs origin/branch | **ahead 3** (the 3 new commits are **unpushed**) |
| Working tree | clean (tracked); untracked = `graphify-out/` tooling only |

The branch had **not advanced** beyond the observed SHA at session start — no newer
commits to preserve. Branch was not reset, not force-pushed, not merged, not deployed.

## 2. Executive decision

**🔴 SYSTEM NOT READY — BLOCKERS REMAIN.**

Three confirmed blockers were closed (Phases 5, 15, 8), but at least one confirmed
finding (Phase 9) remains open, the majority of the audit matrix is not yet audited,
and every SQL-backed A/B / concurrency / 10k acceptance gate is **DB-GATED** and has
therefore **not** been executed. "Production Ready" is not claimed.

## 3. Audit matrix (audited areas only — the rest is NEEDS-AUDIT)

| Area / Phase | Severity | Evidence (current code) | Action | Tests |
|---|---|---|---|---|
| MonthAttendance BuildMonth (5) | 🔴 BLOCKER → **CLOSED** | `BuildMonthAsync(db,y,m)` aggregated `DayAttendances` with no company filter, MERGEd all companies; called scope-less by Payroll `CalculateAsync` | Mandatory `CompanyScope`; CTE + count scoped by `Employees.CompanyId` | `MonthAttendanceBuildScopeTests` (4) |
| API token hot path (15) | 🔴 BLOCKER → **CLOSED** | `ValidateAsync` (every Bearer req) called `EnsureAsync` → `CREATE TABLE` DDL | Ensure schema once at startup; validate = indexed seek | `ApiTokenHotPathTests` (2) |
| Payroll run create (8) | 🔴 BLOCKER → **CLOSED** | `COUNT(1)+1` batch (no lock); run + scope inserts not transactional | One EF tx; `UPDLOCK,HOLDLOCK` range lock | `PayrollRunCreateAtomicityTests` (2) |
| EmployeeAllowances identity (9) | 🟠 HIGH → **OPEN** | Schema has `ItemName` only, **no `SalaryItemId`** (`EmployeeAllowanceSchema.cs:22`); engine joins by name (`PayrollRunStore.cs:463,468`) | Additive `SalaryItemId` migration + ambiguity report + backfill | — |
| LeaveBalances Index/CarryOver (6) | 🟡 NEEDS-VERIFY | Index/CarryOver route through `CompanySelectionContext.Resolve(…companyIds)` (`Index.cshtml.cs:64,119`) — **differs from hypothesis** | Verify `companyIds` is scope-derived + service ownership check | — |
| Attendance import isolation (3) | ⚠️ PREMISE STALE | No class `AttendanceImportService`; raw-record import path must be located | Map real import path before judging | — |
| Prior tenant fixes (2) | 🟡 NEEDS-VERIFY | Memory says closed; several rely on textual guards | Re-verify in code + add SQL A/B | — |
| Phases 4,7,10–14,16–26 | ⚪ NEEDS-AUDIT | Not inspected this session | Study-first per phase | — |

## 4. Closed findings

### Phase 5 — MonthAttendance BuildMonth tenant scope (`40c2eb3`)
- **Root cause:** `BuildMonthAsync` CTE selected `DayAttendances` with only date/`IsAnalyzed` filters and MERGEd every employee row; `PayrollRunStore.CalculateAsync` invokes it automatically → calculating Company A's payroll silently rebuilt Company B/C monthly attendance.
- **Files:** `MonthAttendanceStore.cs`, `PayrollRunStore.cs`, `Pages/MonthAttendance/Index.cshtml.cs`.
- **Invariant:** `BuildMonthAsync` requires `CompanyScope`; fail-closed on `DeniedAll`; aggregate source **and** returned count constrained by `Employees.CompanyId` before the MERGE. Payroll passes the run's company scope; the page passes the user's effective scope.
- **Regression:** `MonthAttendanceBuildScopeTests` — signature guard + double scoped-predicate guard + fail-closed.
- **Result:** build ✅ · 4/4 ✅. Full A/B before/after: **DB-GATED**.

### Phase 15 — API token validation hot path (`9778a11`)
- **Root cause:** `ValidateAsync` (per Bearer request) ran `EnsureAsync` DDL (`IF OBJECT_ID('ApiTokens') IS NULL CREATE TABLE…`).
- **Files:** `Program.cs`, `Infrastructure/Api/ApiTokenStore.cs`.
- **Invariant:** schema ensured once at startup inside the app-locked `SqlSchemaMigrator` scope (concurrency-safe); validation is a bounded seek on unique `UX_ApiTokens_Hash`, then `RevokedAt/ExpiresAt` on one row.
- **Regression:** `ApiTokenHotPathTests` — no `EnsureAsync` in validate body; startup ensures schema; revoke/expiry predicates retained.
- **Result:** build ✅ · 2/2 ✅.

### Phase 8 — Payroll run creation atomicity & concurrency (`3a0b478`)
- **Root cause:** batch number `COUNT(1)+1` without a lock (concurrent creates → duplicate numbers); run insert + scope-member insert in two calls with no transaction (failure → orphan `Draft` with no members → downstream reads as "all employees").
- **Files:** `PayrollRunStore.cs`.
- **Invariant:** one EF transaction wraps allocation + both inserts (auto-rollback → no orphan/partial); sequence allocated under `UPDLOCK, HOLDLOCK` range lock → unique, gap-free numbers.
- **Regression:** `PayrollRunCreateAtomicityTests` — transaction + range-lock guards.
- **Result:** build ✅ · 2/2 ✅. Real 20-way parallel test: **DB-GATED**.

## 5. Remaining findings (precise)

| Severity | Path | Effect | Why unresolved | Required next action |
|---|---|---|---|---|
| 🟠 HIGH | `EmployeeAllowanceSchema.cs`, `PayrollRunStore.cs:463` | Renaming a salary item can change payroll math (allowance matched by `ItemName`) | Needs additive migration + ambiguity report + backfill; too risky to rush in an exhausted context on a production-shared DB | New session: inventory duplicate names, generate ambiguity report, add `SalaryItemId NULL`, deterministic backfill, resolve engine by id |
| 🟠 HIGH | Payroll CreateRun (8, remainder) | Scope employees not verified to belong to run `CompanyId` inside the store | Out of this wave's minimal scope | Add in-store validation that every scope employee exists/active/belongs to `CompanyId` |
| 🟡 MED | `Pages/LeaveBalances/Index.cshtml.cs` | Possible scope leak on Index/CarryOver | `CompanySelectionContext` guard present; `companyIds` source + `LeaveCarryoverService` ownership not yet confirmed | Trace `companyIds` provenance; add A/B tests |
| 🟡 MED | Attendance raw-record import | Cross-tenant `EmployeeNo` import risk (Phase 3/4) | Real import path not yet located; audited class name is stale | Locate path; verify scope + DoS controls |
| ⚪ | Phases 7,10,11,21 (perf/10k) | Unmeasured scale behavior | DB-GATED + synthetic 10k datasets required | Non-production SQL + benchmark harness |
| ⚪ | Phases 16,17 (files, CSP) | Malware scan + XSS hardening not audited | Not inspected | Study-first |
| ⚪ | Phases 18–20 (design system + a11y) | Token-contract + browser QA not done | Requires browser matrix | Foundation-first, then P0 pages |
| ⚪ | Phases 22–26 (observability, audit log, CI gates) | Not audited | — | Study-first |

## 6. Tenant A/B evidence

**DB-GATED.** No SQL-backed A/B before/after test was executed — the only available
SQL instance is `localhost/SmartAttendance`, the production-shared DB (guardrail:
never run acceptance tests against production). The Phase 5 fix is guarded by
source/signature/predicate unit tests only; full A/B (Company A build must not alter
Company B monthly rows, incl. through `PayrollRunStore.CalculateAsync`) requires a
disposable non-production SQL database and is listed as the top DB-GATED item.

## 7. Payroll golden results

**Not run this session** (Phase 12/13 not audited). Prior work (see
`payroll-salary-base-architecture` memory / earlier reports) indicates the 6
configurable bases + golden scenarios exist; this must be **re-verified**, not assumed.

## 8. Performance

**Not measured.** Phases 7/10/11/21 are DB-GATED and need synthetic 1k/3k/10k datasets.
No before/after timings or query counts were produced. Do not claim performance PASS.

## 9. Security

- **API token:** hot-path DDL removed (Phase 15); token remains opaque, hashed at rest, expiring, revocable, stamp-checked.
- **CSP / uploads / package audit:** not audited this session (Phases 16/17). `dotnet list package --vulnerable` not run this session.
- **Authorization:** three isolation/atomicity blockers closed; remainder per §5.

## 10. Database

- **Migrations created:** none (Phase 5/15/8 fixes are code-only; no schema change).
- **Schema ownership:** Phase 14 (consolidating DDL out of hot paths) not done; Phase 15 removed **one** DDL hot path (token validation) by moving the ensure to startup.
- **Backfill:** none.

## 11. UI QA

**Not performed** (Phases 18–20). No browser viewport/dark/light/RTL/Axe testing this
session. No visual PASS claimed.

## 12. Build / test results

```
Release build:            ✅ succeeded, 0 errors (warnings pre-existing)
New unit tests:           ✅ 8/8 passed (Phase 5: 4, Phase 15: 2, Phase 8: 2)
Full unit suite:          not re-run this session (baseline was 1511 green)
Integration (SQL) tests:  DB-GATED — not run (production-shared DB)
Tenant A/B tests:         DB-GATED — not run
Concurrency tests:        DB-GATED — not run
E2E / browser:            not run
NuGet vulnerability audit: not run this session
git diff --check:         ✅ clean
```

## 13. Git status

```
Branch: claude/smartattendance-local-rebuild-wftwb3  (ahead of origin/branch by 3, UNPUSHED)
HEAD:   3a0b478
3a0b478 fix(payroll): make payroll-run creation atomic and concurrency-safe (Phase 8)
9778a11 fix(api): remove DDL from the API-token validation hot path (Phase 15)
40c2eb3 fix(attendance): scope MonthAttendance BuildMonth to the caller's companies (Phase 5)
6fecb5a (session start)

git diff --stat 6fecb5a..HEAD:
 8 files changed, 238 insertions(+), 18 deletions(-)
```

## 14. Deployment recommendation

- **Do not merge / deploy yet** — decision is 🔴 NOT READY.
- Before any merge to `main`: close Phase 9, complete the NEEDS-VERIFY items (6, 3, 2), and — critically — **stand up a disposable non-production SQL database** so the DB-GATED A/B / concurrency / 10k gates can actually run. Until those execute, tenant isolation and payroll concurrency are guarded only by unit-level source tests, not proven end-to-end.
- The 3 commits are safe, self-contained, and unpushed. Pushing them (no merge) is a reasonable next step to preserve the work; merge and deploy remain separate human decisions.

---

### Guardrails honored this session
No merge to `main` · no deploy · no force-push · no production-data access · no
destructive migration · no business-formula change · no skipped test marked pass ·
no acceptance test run against the production database.
