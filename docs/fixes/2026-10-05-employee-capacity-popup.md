# Employee creation capacity preflight

The directory's Add Employee action checks the current authenticated tenant's
license capacity on every click and shows an RTL dialog when the limit is full.
Usage includes non-deleted employees across the tenant's companies, not the
filtered directory result. No limit is hardcoded to ten.

The capacity endpoint checks employee-create permission, uses a tenant claim
rather than a supplied tenant ID and disables response caching. Missing license
or tenant fails closed. Direct Create GET/POST requests check the same capacity
before loading the form or consuming an employee number. The existing atomic
database capacity trigger remains the final concurrency guard; its limit error
is translated to a friendly message instead of an unhandled exception.

No license values, employee data or database schema are changed.

Verification: boundary-policy unit tests; JavaScript interaction harness
(`node scripts/tests/test-employee-capacity.cjs`); Release build and full test suite.
Graphify update could not run because the installed uv launcher points to a
missing script; current source was inspected directly instead.

Deployment follow-up: stop the exact detached single-folder PowerShell watchdog
tree before replacing binaries, and preserve both manual Start-Zynora launchers.
The existing task's wscript action detaches the watchdog, so stopping the task
alone does not stop its restart loop. Verified PowerShell syntax and stop order;
the scoped watchdog stop was exercised during this deployment.
