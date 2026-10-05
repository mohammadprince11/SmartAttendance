# HR administration page identity

Scope: Forms/Submissions, Contracts, Contracts/Movements, EmployeeDocuments,
Documents/Requests, CompanyDocuments, Documents/Templates (including Badge),
Acknowledgments/Tracking, and HrSettings.

The shared page-scoped stylesheet replaces legacy gradients, fixed colors,
inconsistent spacing and fixed-width layouts with the existing theme and
dropdown contracts. Tables scroll inside their own wrapper on narrow screens;
settings use a responsive card grid and document upload/list panels stack.
Existing handlers, authorization, fields, confirmations, uploads and condition
builder wiring are unchanged. No database or runtime configuration changes.

Validation:

- Release solution build: no warnings/errors.
- Unit suite: 2682 passed, 30 skipped, none failed.
- NuGet vulnerability scan: none reported by the configured sources.
- Synthetic browser fixtures: nine pages, dark/light, 320/390/900/1440 pixels;
  palette, spacing, no page overflow, fields, tables, settings grid, native
  checkbox/file selection, shared dropdown selection and employee picker widths.
- Static comparison against the preceding commit checks form field and handler
  attributes on all nine views.

Browser fixtures do not query application databases or production employee data.
They are not a production end-to-end test. Production publishing is a separate
operation requiring explicit approval.

Graphify could not run: its installed uv trampoline fails to canonicalize the
script path. Source files were inspected directly; graph output was not refreshed.
