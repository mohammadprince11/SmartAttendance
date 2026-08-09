# ZYNORA HR — Codex Production Closure Report

Date: 2026-08-09  
Working branch: `agent/codex-production-closure`  
Decision: **SYSTEM NOT READY — BLOCKERS REMAIN**

This pass changed only the isolated task branch. It did not merge or rebase `main`, deploy, change repository settings, or access a production database. Database tests created uniquely named disposable LocalDB databases and dropped them after each case.

## 1. Git baseline

| Field | Value |
|---|---|
| Start SHA | `94b0080f7d276ac2984052e98b1309caf1549b76` |
| End SHA (tested implementation; report commits follow) | `2d15480e7afe4af235cb1922de99bba9462011b4` |
| Observed `origin/main` SHA | `56730a86c51a6ba7ac3cbda025f9f08beaea02ba` |
| Ahead of `origin/main` at tested implementation SHA | 33 commits |
| Behind `origin/main` | 3 commits |
| Merge base | `dc91128873ba3f0dc5a19e73b8a09dfbf2a04fa4` |

The feature branch had not advanced beyond the supplied start SHA. Work continued from that feature head on the isolated branch. No reset, rebase, merge, force-push, or push was performed.

## 2. Governance

The role conflict in `AGENTS.md` and `docs/AI-DEVELOPMENT-RULES.md` is resolved. Both now state that the agent explicitly assigned by the user may implement, a review-only agent remains read-only, the current prompt determines the role, and two implementation agents must not modify one worktree concurrently.

All existing controls remain: no production database, no direct `main` push, no deployment, tenant isolation, financial regression testing, privacy, and controlled schema migration. No secondary implementation agent was used.

## 3. Findings matrix

| Item | Severity | Original defect | Current status | Principal files changed | Test evidence |
|---|---|---|---|---|---|
| 0.1 governance | P0 | Vendor-specific implementation/review assignment | CLOSED | `AGENTS.md`; `docs/AI-DEVELOPMENT-RULES.md` | Source review; full suite |
| Known: MonthAttendance build | P0 | Risk of unscoped aggregate source | ALREADY CLOSED — NO CHANGE | None | Current-source trace; full suite |
| Known: API token validation | P0 | Risk of DDL on bearer validation | ALREADY CLOSED — NO CHANGE | None | Current-source trace; full suite |
| P0-1 attendance import | P0 | Global employee-number and attendance lookup | CLOSED | import interface/scope/service; Attendance Operations page; employee composite unique index | SQL A/B test and security unit tests |
| P0-2 staging token | P0 | Filename GUID without ownership boundary | CLOSED | `AttendanceImportStagingStore.cs`; Attendance Operations page; `Program.cs` | token actor/company/expiry/replay/path tests |
| P0-3 leave authorization | P0 | Page treated every company as allowed; service trusted bare ID | CODE CLOSED; SQL ACCEPTANCE INCOMPLETE | Leave Balances page; `LeaveCarryoverService.cs` | scope unit tests pass; mandatory real SQL malicious carryover remains |
| P1-1 run sequence | P1 | `COUNT + 1`, deletion reuse, no unique invariant | CLOSED | `PayrollRunStore.cs`; `SqlSchemaMigrator.cs` | SQL deletion-hole and 20-way concurrency test |
| P1-2 run company | P1 | New run could have null/mixed company | CLOSED | Payroll Runs page/store | targeted tests and full suite |
| P1-3 scope employee validation | P1 | Store trusted supplied employee IDs | CLOSED | Payroll run/scope stores | invalid/cross-company tests and SQL scope persistence |
| P1-4 run UI employee list | P1 | Global employee/organization picker | CLOSED | Payroll Runs Razor/PageModel | source/behavior tests and full suite |
| P1-5 allowance identity | P1 | `ItemName` was authoritative identity | CLOSED | allowance entity/configuration, financial/profile UI, payroll store, migrations | rename regression plus SQL matched/unmatched/duplicate/FK audit |
| P1-6 transaction tenant contract | P1 | Read/write/bulk methods lacked a store-level scope contract | CLOSED | `PayrollTransactionStore.cs` and all callers | targeted scope tests and SQL malicious A→B read/write/delete/bulk test |
| P1-7 transaction reference | P1 | `COUNT + 1` reused/de-duplicated poorly | CLOSED | transaction store; schema migrator | 20-way SQL allocation, deletion/no-reuse, unique index |
| P1-8 import resources | P1 | Unbounded archive/XML/group/error/time behavior | PARTIAL | import limits/service; upload limits | limit/security unit tests; XLSX still materializes XML/list/DataTable |
| P1-9 leave N+1 | P1 | Per-employee calculator/query loop | CODE CLOSED; BENCHMARK OPEN | `LeaveCarryoverService.cs` | set-based unit tests; required 1k/3k/10k measurements not run |
| P1-10 payroll candidates | P1 | Full financial tables loaded before candidate filtering | CODE CLOSED; BENCHMARK OPEN | `PayrollRunStore.cs` and scoped callers | query regression tests; required 10k timing not run |
| UI-1 primary token | P1 | Incorrect brand text token on primary button | CLOSED | Payroll Settings Razor | UI source regression tests |
| UI-2 financial theme | P1 | Near-white text on white cards in light mode | CODE CLOSED; VISUAL ACCEPTANCE OPEN | Employee Financial Info Razor | source regression tests; no browser/contrast measurement |
| UI-3 mojibake | P1 | Corrupt user-visible Attendance Operations strings | CLOSED | Attendance Operations PageModel; encoding guard | encoding test passes |
| UI-4 design foundation | P1 | Competing scopes/tokens/load order/`!important` cascade | OPEN | No broad rewrite attempted | No browser-backed acceptance |
| Wave 5 A/B acceptance | P0 | Mandatory malicious-operation matrix absent | PARTIAL | `ProductionClosureSqlTests.cs` | 4 real SQL cases pass; required operations remain |
| Payroll golden | P0 | No full deterministic persisted calculation oracle | PARTIAL | Existing `AttendanceSalaryBaseTests.cs` only | attendance-base arithmetic passes; full run/components oracle absent |
| CI/release governance | P1 | No explicit SQL-backed required check | CODE CLOSED; REMOTE STATE OPEN | `.github/workflows/ci.yml` | workflow source updated; GitHub run/protection not changed or observed |

## 4. Tenant isolation

The disposable SQL fixture creates Company A and Company B with the same `EmployeeNo = 10001`.

- Attendance preview under Company A resolved only the A employee.
- Attendance import wrote one A attendance row and left the B employee's row count unchanged.
- A number existing only in B was returned as unresolved and imported zero rows, preventing an existence leak.
- A-scoped payroll transaction listing did not return B data.
- A-scoped attempts to create for, delete, or bulk-delete a B transaction failed; the mixed-company bulk delete was atomic and both rows remained.
- The SQL suite passed 4/4 with no skips.

This does not complete the mandatory A/B operation matrix. Month/week lifecycle operations, real SQL leave operations, and payroll calculation/lock operations remain unproved and are P0 acceptance blockers.

## 5. Payroll integrity

### Run sequence and scope

`PayrollRunSequences` allocates monotonically under `UPDLOCK, HOLDLOCK`; `PayrollRuns.BatchNo` has a database unique index. After creating 1, 2, 3 and deleting 2, SQL allocated 4. Twenty parallel creates produced 20 distinct run IDs, 20 distinct batch numbers, 20 scope members, and zero orphan scope members.

New runs require an explicit permitted company. Employee IDs are validated inside the transaction against existence, deletion/active state, and run company. The UI pickers are company-filtered before materialization.

### Salary item identity

`EmployeeAllowances.SalaryItemId` is authoritative and FK-backed; `ItemName` is a display/history snapshot. The controlled migration blocks duplicate salary-item names and unmatched historic allowance names rather than guessing. The disposable audit produced: total 1, uniquely matched 1, unmatched 0, duplicate names 0, FK present 1. The rename regression confirms policy flags remain linked by ID.

### Transactions

Read, period, save, bulk save, delete, lock, bulk delete, and lock-state APIs now require `CompanyScope`. Employee and bulk IDs are validated before mutation. `PayrollTransactionSequences` and a unique reference index prevent deletion reuse and parallel duplicates. The SQL test generated 20 unique references and verified the next reference exceeded the deleted maximum.

### Golden employee

The existing `TEST-PAY-001` arithmetic test verifies a 400,000 basic salary plus 1,600,000 attendance-eligible allowance yields a 2,000,000 attendance base, including exact absence proration examples. It does not yet assert every requested tax/GOSI/overtime/deduction/net field or compare persisted `PayrollRunLines` and components to the calculated values. The full SQL golden test therefore remains open.

## 6. Attendance import

Every normal import is bound to exactly one validated company. A restricted single-company user is auto-selected; a multi-company user must select explicitly. Preview and final import share the same scope, mapping, and duplicate-building path. Employee identity is `(CompanyId, EmployeeNo)` in both application lookup and the database unique invariant. Existing attendance is filtered in SQL through employees in the selected company.

Staging uses ASP.NET Data Protection time-limited tokens bound to actor and company. Claims are checked for expiration and path containment, final import claims the file once, successful/failed finalization cleans it, and abandoned files are cleaned by the registered background path.

Configured limits are: 256 MiB compressed upload, 1 GiB decompressed total, 512 MiB per ZIP entry, 100:1 compression ratio, 1,000,000 worksheet rows, 500,000 unique employee-day groups, 250,000 shared strings, 200 reported errors, 600-second processing deadline, and 120-second bulk-copy timeout. Cancellation flows through the import. Bulk insert now supplies required audit fields.

Residual issue: XLSX parsing still materializes shared-string/worksheet XML, row collections, aggregates, and a final `DataTable`. Limits bound exposure, but the requested forward-only XML/batched bulk design is not complete.

## 7. Leave

The Leave Balances company selector now derives options from the effective `CompanyScope`, and out-of-scope requested company IDs fail closed. Carryover accepts the scope at the service boundary and validates company ownership before financial mutation.

Carryover was changed from per-employee calculation calls to bulk employee, source-balance, approved-request, and target-balance loading with one transactional save. Unit tests cover the authorization and query shape. No real SQL A→B carryover acceptance case and no 1,000/3,000/10,000 employee measurements were executed, so those claims remain open.

## 8. Design

- Payroll Settings consumes canonical `zy-btn zy-btn--primary` styling.
- Employee Financial Info uses semantic theme tokens for cards, text, notes, controls, headings, and errors.
- Corrupt Attendance Operations strings were replaced with UTF-8 Arabic and a source guard catches common mojibake markers.
- No broad design-system migration was attempted. `_Layout.cshtml`, `wwwroot/css/app.css`, attendance page styles, competing `zy-scope` assumptions, late component CSS, and widespread legacy `!important` rules still form cascade debt.

No browser rendering or measured WCAG contrast run was available. Visual correctness is therefore not marked accepted.

## 9. Performance

Only measured values are reported:

| Operation | Observed elapsed time |
|---|---:|
| `dotnet restore SmartAttendance.slnx` | 3.0 s |
| Release solution build | 30.18 s |
| Full 1,529-test suite | 12 s test duration (16.8 s command wall time) |
| Four-test disposable SQL acceptance filter | 10 s test duration (41 s command wall time including build) |
| NuGet vulnerability audit | 19.7 s |

No 1,000/3,000/10,000 leave-carryover benchmark and no 10,000-employee payroll benchmark or phase timings were run. No throughput or memory claim is made for those workloads.

## 10. Build and test output

| Gate | Result |
|---|---|
| Restore | PASS; all projects up to date |
| Release build errors | 0 |
| Release build warnings | 24 |
| Unit/integration total | 1,529 |
| Passed | 1,529 |
| Failed | 0 |
| Skipped | 0 |
| SQL integration | PASS, 4/4 real disposable LocalDB cases |
| Tenant A/B | PASS for attendance import and payroll transactions; required broader matrix incomplete |
| Concurrency | PASS for 20 payroll runs and 20 transaction references |
| NuGet audit | PASS; no vulnerable packages in all seven projects |
| `git diff --check` | PASS |

The build warnings are existing nullability/analyzer findings, primarily in Web PageModels/report infrastructure plus two FormBuilder test nullability warnings and one xUnit analyzer warning. They did not fail the build, but should be burned down separately.

## 11. CI state

`.github/workflows/ci.yml` now contains these PR gates:

- restore and Release build;
- `git diff --check` against the `origin/main` merge base;
- unit tests excluding the SQL fixture;
- a distinct Windows job named `SQL Server Acceptance`, using LocalDB and the real tenant/concurrency fixture;
- NuGet transitive vulnerability audit;
- existing optional Playwright E2E path.

Local execution is green; this report does not claim that GitHub Actions has run this branch. An authorized repository administrator must configure `main` to require at least `Build & Unit Tests`, `SQL Server Acceptance`, and `NuGet Vulnerability Audit`, require a pull request and up-to-date branch, dismiss stale approvals, block force pushes/deletions, and require conversation resolution. No repository setting was changed in this pass.

## 12. Remaining blockers

| Severity | Exact path | Reason | Required next action |
|---|---|---|---|
| P0 | `SmartAttendance.Tests/ProductionClosureSqlTests.cs` | SQL A/B coverage omits MonthAttendance build; month/week approve, reopen, and lock; Leave Balances index/carryover; payroll calculate and lock | Extend the disposable fixture with each malicious A→B operation and assert B before/after equality and zero measurable reads |
| P0 | `SmartAttendance.Tests/AttendanceSalaryBaseTests.cs`; `SmartAttendance.Web/Infrastructure/Hrms/PayrollRunStore.cs` | `TEST-PAY-001` covers attendance-base arithmetic only, not the complete deterministic calculation and persisted lines/components | Add a disposable SQL golden test with deterministic test tax/GOSI profiles and exact assertions for every requested field and persisted component |
| P1 | `SmartAttendance.Infrastructure/Services/AttendanceImportService.cs` | XLSX path remains materialized rather than forward-only/batched | Replace `XDocument`/row-list/final-DataTable flow with bounded `XmlReader` parsing and batch bulk copies; retain all current limits |
| P1 | `SmartAttendance.Web/Infrastructure/Hrms/LeaveCarryoverService.cs` | Set-based implementation has no required 1k/3k/10k evidence | Add disposable SQL benchmark fixtures, capture query count, elapsed time, allocations, and publish actual results |
| P1 | `SmartAttendance.Web/Infrastructure/Hrms/PayrollRunStore.cs` | Candidate scoping is fixed but no 10k monthly payroll phase timings exist | Add a reproducible 10k synthetic benchmark and record phase/query/memory timings |
| P1 | `SmartAttendance.Web/Pages/Employees/FinancialInfo.cshtml`; `SmartAttendance.Web/Pages/Shared/_Layout.cshtml`; `SmartAttendance.Web/wwwroot/css/app.css` | Source tokens changed, but browser/WCAG acceptance and cascade consolidation are incomplete | Run light/dark browser smoke and automated contrast checks, then incrementally remove competing scope/load-order/legacy override debt |
| P1 | `.github/workflows/ci.yml` | Workflow has not run remotely and branch protection is not configured | Push/open a PR under authorization, observe all checks including patch hygiene, then have an authorized administrator enable the documented required checks |
| P2 | Web PageModels/report infrastructure and `SmartAttendance.Tests/FormBuilderTests.cs` | Release build still emits 24 compiler/analyzer warnings | Triage and remove warnings in a separate focused pass without changing payroll/attendance behavior |

Until the P0 database-operation matrix and complete persisted payroll golden test pass, the only supportable decision is:

**SYSTEM NOT READY — BLOCKERS REMAIN**
