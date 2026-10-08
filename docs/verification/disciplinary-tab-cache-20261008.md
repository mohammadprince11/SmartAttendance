# Disciplinary tab navigation — local verification

Date: 2026-10-08. Not deployed or pushed as part of this change.

## Change

- Reuse authorized GET section snapshots within the current document for up to 30 seconds, with at most four entries and a 2 MB per-entry limit.
- Preserve scope/filter query parameters in cache keys; normalize the default library tab and categoryId=0.
- Display the loading status only after 250 ms rather than flashing it on every switch.
- Invalidate snapshots on workspace POST attempts, hidden document, and page exit. No browser-persistent storage or POST caching.
- Keep unsaved-change confirmation, native POST/antiforgery handling, asset allowlists, and designer disposal/initialization.

## Verification

Offline synthetic Playwright checks against the actual workspace script:

- Tab cache: 43 checks at 390 and 1440 px.
- Workspace navigation: 35 checks.
- Save design: 15 checks.
- Loading/layout: 38 checks.
- Designer: 24 checks.
- PDF preview: 23 checks.

Total: 178 checks passed. JavaScript syntax and git diff whitespace checks passed.

These tests do not establish live authenticated navigation timings or production behavior. First visits, expired snapshots, and navigation after writes still fetch fresh content. Existing unrelated local files were preserved.

Graphify was consulted for source tracing but its local launcher failed with `uv trampoline failed to canonicalize script path`; source inspection was used as fallback. No database changes, runtime replacement, Git commit, or remote push occurred.
