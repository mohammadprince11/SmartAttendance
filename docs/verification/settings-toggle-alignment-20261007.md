# Settings toggle alignment — 2026-10-07

## Scope

- Notification rule headers use two columns: title at inline-start, switch at inline-end (left in RTL).
- Routine pending/success announcements remain in the polite live region but are visually hidden. Empty status spans no longer paint legacy pills or consume a layout column.
- Save failures remain visible in a full-width row; switch position does not change.
- Shared option rows (currently NoticePeriod) align their segmented control at inline-end, including narrow viewports.
- User explicitly approved details autosave and publishing. The Save button is now a noscript fallback only; input/change events trigger a 600ms debounce, with per-rule serialization and deduplication. Latest edits queue behind acknowledged requests; uncertain failures do not retry automatically. Pending/failed edits warn before navigating away.
- UpdateRule retains the normal POST fallback and adds a JSON acknowledgement with binding/value/existence guards. Empty supervisor is allowed. Toggle requests cannot overlap a pending details request.
- No database, authorization or calculation changes. No production records accessed.

## Verification

- Release solution build: 0 warnings, 0 errors.
- Unit suite: 2690 passed, 30 skipped integration tests, 0 failed.
- Synthetic browser fixtures: 54 passed across 9 settings views, dark/light and 390/900/1800 widths. Includes long-title alignment, empty/pending/success status clipping, visible error/rollback, repeated-submit guard and shared option-row alignment.
- Browser tests additionally cover serialized details saves, last-edit-wins, unchanged-value deduplication, unsaved-exit warning and recovery after connection failure. All 9 baseline form contracts preserved; git diff --check passed.
- Configured NuGet sources: no vulnerable packages reported across 10 projects.
- Graphify CLI query/reflect/update unavailable due broken uv trampoline/script path. Existing graph inspected read-only; current markup/CSS verified directly. Graph not refreshed.

## Publication

Publishing approved explicitly by the user for this operation. No remote push or PR merge authorized by this request.
