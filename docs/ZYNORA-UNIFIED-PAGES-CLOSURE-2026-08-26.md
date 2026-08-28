# ZYNORA unified pages closure

_2026-08-26 — rendered-page design contract. No business logic, database schema, authorization rule, payroll calculation, attendance calculation or production state changed._

## Result

The canonical ZYNORA visual contract now covers **182/182 routed HTML pages**:

| Surface | Pages | Coverage path |
|---|---:|---|
| Back-office/default shell | 169 | `_Layout.cshtml` |
| Employee portal | 10 | `_EmployeePortalLayout.cshtml` |
| Standalone HTML | 3 | Explicit local contract |
| CSS endpoint | 1 | Excluded: `/theme/current.css` is a stylesheet response, not an HTML page |

This is systemic coverage rather than a hand-maintained per-page allow-list. Any new page inheriting either application shell receives the same contract automatically.

## Architecture

- `zynora-unified-pages.js` preserves all historical classes used by scripts and additively maps rendered elements to canonical `zy-*` classes.
- The adapter covers titles, page headers, fields, selects, text areas, actions, cards, filters, tables, status badges, alerts, tabs, pagination and empty states.
- A `MutationObserver` applies the same mapping to rows, dialogs and validation messages created after initial render.
- Existing managed widgets (calendar, custom select, date/time picker, switch, chip, stepper and pagination internals) retain their dedicated component contract instead of being flattened into generic buttons.
- `data-zy-preserve` is the explicit escape hatch for a future dedicated component.
- `zynora-unified-pages.css` is loaded after page-local and compatibility CSS and owns the final visual rules through canonical selectors and design tokens only.
- Printable document bodies remain document-specific. Their interactive toolbars use the unified contract, so A4/PDF geometry is unchanged.

## Contract rules

- RTL logical properties; no physical left/right layout declarations.
- No direct hex/RGB colors in the final layer.
- One token-driven vocabulary (`zy-*`) owns final controls.
- Original classes are never replaced, preventing JavaScript selector regressions.
- No broad Razor TagHelper registration; the runtime adapter avoids Razor parser incompatibility with historical dynamic attributes.
- New page-local CSS families are ratchet-locked at the existing baseline; future design work extends the canonical contract.

## Verification

| Gate | Result |
|---|---|
| Release build | PASS — 0 errors, 0 warnings |
| Unified-page contract tests | PASS — 6/6 |
| CI-equivalent non-SQL tests | PASS — 1896/1896, no skips |
| JavaScript syntax | PASS (`node --check`) |
| Desktop visual fixture | PASS — canonical classes and computed token styles verified |
| Mobile 390×844 | PASS — RTL, stacked header, no document overflow, table scroll contained |
| Browser console | PASS — no warnings or errors |
| `git diff --check` | PASS |

The local disposable SQL suite could not start because this workstation's LocalDB instance is unavailable. That suite is unrelated to the presentation-only change and remains enforced by its dedicated CI jobs. The previously green CI baseline is not replaced by this local limitation.

## Source debt that intentionally remains

Historical page stylesheets still exist because deleting them wholesale would also delete page-specific layout and widget behavior. They are now compatibility sources beneath one final rendered-page contract. Removing those files is a separate refactor and must be performed page-by-page with snapshot evidence; it is not required for consistent rendered components.
