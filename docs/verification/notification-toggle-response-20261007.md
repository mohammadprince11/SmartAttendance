# Notification toggle response

Baseline: cece923a. Scope: NotificationCenter only, plus its scoped status CSS
and regression tests. No deployment or remote write is included.

The switch now previews its desired state immediately while showing a live
Arabic saving status and a progress cursor. Per-form request locking prevents
duplicate submissions without disabling the focused switch. Detail controls
are inert until the save completes, preventing an overlapping details save.

Fetch sends an explicit desired boolean with the existing antiforgery form
data and same-origin credentials. The Admin-authorized handler validates the
JSON request and returns the saved boolean without redirecting to a full-page
GET. Non-JavaScript submissions retain their existing redirect behavior.
Setting an explicit state avoids inverting twice when retrying the same state.

Failed HTTP, rejected network, redirected login and invalid JSON responses
restore the previous visual state and show a verification/retry message.
There is no automatic second POST: a lost response may follow a successful
write, so users are told to refresh to verify. This is not a claim of
transaction-level exactly-once processing or changed tenant authorization.

Verification:

- Release solution build: zero warnings and errors.
- Existing unit suite: 2682 passed, 30 dedicated integration fixtures skipped,
  zero failed. Three new request validation tests passed without a DB provider.
- 54 synthetic full-shell browser fixtures passed. Notification checks include
  a deliberately pending response, immediate visibility, progress cursor,
  retained focus, duplicate-submit suppression, desired-state request body,
  keyboard operation and rollback on the four failure types above.
- All nine original form contracts remain unchanged. No production data used;
  network traffic is mocked/blocked in browser fixtures.
- The first targeted test build collided with the running unit test DLL lock;
  after the suite completed, the targeted build/test passed.

Graphify mapping was verified against current source. Incremental graph update
was attempted but the installed launcher still references a missing script.
