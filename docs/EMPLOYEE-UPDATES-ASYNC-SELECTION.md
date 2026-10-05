# Employee updates: in-page employee selection

Selection no longer auto-submits the GET form. The page-specific script requests the
existing authenticated EmployeeUpdates GET using same-origin credentials and
no-store caching. No new authorization path, database query, schema change or
financial formula is introduced.

Only employee content, tab links and batch count are replaced. The shell, shared
picker and confirmation dialog remain mounted. The canonical URL is updated via
replaceState. Server-rendered POST identities and anti-forgery tokens come from the
new response, not from previous employee forms. Global select/calendar observers
continue initializing inserted controls; field highlighting and confirmation use
delegation.

An aborted/versioned request cannot overwrite a later choice. Redirects, errors and
employee-ID mismatches are rejected. Previous content is hidden/inert during loads
and after errors, and submission is blocked until loading succeeds. Typing an
unresolved code suspends editing. Unsaved edits require confirmation before switching;
cancellation restores the loaded picker and existing edits. Errors show an explicit
retry button; no automatic navigation fallback discards edits.

Verification uses synthetic browser fixtures only, not production accounts/data.
Covered: no default selection/navigation, out-of-order responses, failure/retry,
wrong identity, fresh tokens, empty selection, dirty cancellation, dynamic field
highlighting/confirmation, blocked stale submissions, actual shared-picker code
lookup, RTL layout and sidebar regressions. Publishing requires separate approval.
