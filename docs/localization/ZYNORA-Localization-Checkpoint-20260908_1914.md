# ZYNORA HR — Localization Checkpoint

**Date:** 2026-09-08 19:14 (Iraq, UTC+3)
**Branch:** `fix/full-localization-cleanup-20260906`
**Checkpoint base:** `43a6bc42a930fe1d79e4f24fcfe29cb87b456980`
**Status:** P0–P5 completed and pushed; post-P5 audit reviewed; P6 not started yet.

## Current localization progress

| Stage | Arabic findings |
|---|---:|
| Initial deep audit | 11,097 |
| After P3 | 600 |
| After P4 | 281 |
| After P5 | 201 |

Overall reduction from the initial audit to post-P5 is approximately **98.2%**.

Post-P5 unique Arabic fragments: **112**.

## Completed work

### P0 — Standalone shell structure
- Localized standalone Employee Portal and Verify shells.
- Removed fixed `ar/rtl` assumptions at the HTML shell level.

### P1 — High-volume business-data localization
- Localized major organization, shift, leave-balance, geolocation, and related business-data surfaces.

### P2 — Runtime localization coverage
- Expanded runtime localization to cover accessibility and UI attributes including `placeholder`, `title`, `aria-label`, `aria-description`, `alt`, tooltip/data-title attributes, and supported input button values.
- Preserved textarea/user-entered content.

### P3 — Residual business-data leaks
- Reduced visible Arabic employee/company/department/position leakage on major English-mode pages.
- Employee Portal business display and demo identity handling improved.

### P4 — Business alias catalog + composed runtime text
- Added authenticated and company-scoped `/Culture/BusinessCatalog`.
- Added visible-only business aliases for runtime translation.
- Improved nested/composed/template runtime translation.
- Added English/Kurdish catalog parity for new UI keys.

### P5 — LTR dictionary quality guard
Commit: `43a6bc42a930fe1d79e4f24fcfe29cb87b456980`

- Rejects contaminated LTR dictionary overrides that still contain Arabic-script remnants.
- Keeps RTL target languages unaffected.
- Added whitespace-normalized exact runtime lookup.
- Updated localization contract tests to validate behavior instead of P4 formatting details.
- Validation completed successfully:
  - focused P5 tests: 36/36 passed
  - full SmartAttendance.Tests: 2161 total, 0 failed, 2144 passed, 17 skipped

## Post-P5 audit status

- Arabic findings: **201**
- Unique Arabic fragments: **112**
- Runtime localization fallback states: **0**
- Successfully visited pages are structurally `en-US / ltr`.
- The recurring structural-failure count is tied to known Edit/Delete routes returning HTTP 404 without IDs, not a localization direction regression.

## Main remaining areas

Largest remaining concentrations observed in the post-P5 audit:

- `DisciplinaryRules` — about 27 findings
- `Payroll/Settings` — about 12
- `Settings/Dictionary` — about 12
- `PositionLevels` — about 8
- `AttendanceSettings` — about 7
- `PositionCategories` — about 6
- `UserAccess` — about 6
- `Positions` — about 5

## Important classification of the remaining 201

The remaining findings are not all translation defects.

### 1. Intended Arabic/native-language content
Examples include native language labels such as `العربية` shown intentionally in language-management UI. These must not be force-translated when the UI intentionally displays a language's native name.

### 2. Accessibility naming defects
Some generated `aria-label` values collect large surrounding blocks of text or entire selector contents. This can expose long Arabic business-data sequences in the audit even when visible UI text is otherwise localized.

Central candidates for P6 review:
- `wwwroot/js/zynora-select-system.js`
- `wwwroot/js/zynora-ux-guards.js`

The fix should derive concise accessible names from explicit labels/legends/headings and avoid using whole container text as a fallback.

### 3. Remaining business/reference data
Examples:
- Position Categories
- Position Levels
- some employee selectors
- some evaluation/leave-balance selectors

These should be localized through the business-data/reference-data model, not through blind static dictionary substitutions.

### 4. Genuine static/composed UI text
Remaining genuine UI strings are concentrated mainly in:
- Disciplinary Rules / Form Designer
- Payroll Settings
- Attendance Settings
- User Access
- selected report/payroll/import surfaces

## P6 plan

P6 should be a targeted final-cleanup batch, not another broad localization rewrite.

1. Fix centralized accessibility-name generation so `aria-label` values stay concise and do not absorb whole containers or option lists.
2. Localize remaining Position Category / Position Level display values through the correct business/reference-data path.
3. Clean the genuine static/composed UI strings remaining in Disciplinary Rules, Payroll Settings, Attendance Settings, User Access, and small residual surfaces.
4. Preserve intentional Arabic/native-language content and user/business source data where translation would be incorrect.
5. Run Release build, focused localization/accessibility tests, full SmartAttendance.Tests, then rerun the V6 English UI Arabic audit.

## Definition of done for localization

- No unintended Arabic UI leakage in English mode.
- Intentional native-language/business content explicitly classified and excluded from false-positive localization defects.
- Runtime localization fallback remains 0.
- English pages remain `en-US / ltr`.
- Full regression suite remains green.

---

This checkpoint records the exact localization state before starting P6 so future changes can be compared against a stable, documented baseline.
