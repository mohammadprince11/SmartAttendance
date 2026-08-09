# ZYNORA Design System — Migration Status

_2026-08-09. Living tracker so new pages adopt the canonical `zy-*` components instead of adding more legacy classes._

## Authoritative CSS ownership (as loaded in `_Layout.cshtml`)

| File | Role |
|---|---|
| `zynora-theme-contract.css` | semantic brand/status tokens (`--brand-*`, `--status-*`) |
| `zynora-design-tokens.css` + `zynora-tokens.css` | design tokens (spacing/type/radius/control sizes, `--zy-*`) |
| `zynora-design-system.css` | **canonical components** (`.zy-*`) |
| `nexora-brand.css` | application shell / navigation |
| `zynora-legacy-bridge.css` | temporary legacy page bridge |
| `app.css`, `nexora-*`, page CSS | **legacy generations — to be retired progressively** |

> Debt: `zynora-design-tokens.css` and `zynora-tokens.css` both exist (token duplication) — consolidate later; do not add a third.

## Canonical components available (already built)

`zy-page-header` · `zy-card` (+`--hover`) · `zy-btn` (`--primary/--secondary/--danger/--ghost/--sm/--lg`) · `zy-field` (+`--invalid`) · `zy-label`/`zy-input`/`zy-select`/`zy-textarea` · `zy-help`/`zy-error` · `zy-table`/`zy-table-wrap` · `zy-badge` (+variants) · `zy-tabs`/`zy-tab` · `zy-filter-bar` · `zy-modal`/`zy-drawer` · `zy-empty` · `zy-pagination` · `zy-check`/`zy-toggle`.

**Added 2026-08-09** (this pass): `zy-alert` (+`-success/-warning/-danger/-info`), `zy-setting-row` (responsive grid), `zy-actions`.

**Still missing / to add:** `zy-page`/`zy-page-title`/`zy-page-subtitle`, `zy-card-header`/`zy-card-body`, `zy-radio-card`, `zy-toolbar`.

## Page migration status

Legend: **Modern** (born on `zy-*`) · **Migrated** (converted) · **Legacy‑Compatible** (works via bridge) · **Needs Migration**.

| Page | Status | Notes |
|---|---|---|
| /Payroll/Settings | Needs Migration | ×8 copy + mobile overflow **fixed**; still uses `ps-*` + inline `<style>` — canonical refactor pending |
| /Payroll/SalaryItems | Needs Migration | P0 |
| /Employees/FinancialInfo | Needs Migration | P0; GOSI preview corrected |
| /AttendanceOperations | Needs Migration | P0 |
| /MonthAttendance | Needs Migration | P0 |
| /Payroll (Runs) | Needs Migration | P0 |
| /Payroll/RunDetail | Legacy‑Compatible | P0; payslip trace added |
| /Holidays/{Create,Edit,Delete} | Needs Migration | P1 |
| /Devices/{Create,Edit,Delete} | Needs Migration | P1 |
| /LeaveRequests/{Create,…} | Needs Migration | P1 |
| /AttendanceRecords/{Create,…} | Needs Migration | P1 |
| remaining CRUD | Needs Migration | P2 |

## Rule for new work

Do **not** add `ps-*`, `.form-card`, `.form-group`, page‑level `<style>`, or hard‑coded brand/status colors on business pages. Use the `zy-*` components above; extend `zynora-design-system.css` (from tokens) if a component is genuinely missing.

## Blocked this session

- Visual‑regression + a11y automation (Phases 33/34): Playwright/Axe MCP servers disconnected.
- Full per‑page migration: large multi‑session effort; started with canonical‑component gap‑fill + Payroll Settings defect fixes.
