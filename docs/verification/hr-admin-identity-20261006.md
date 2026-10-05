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

## Clipping and alignment follow-up

The text-bearing zyp-move links now use their content width rather than the
legacy 28px icon width. Employee picker containers allow the entire 44px
controls to display, overriding shell sizing without changing picker behavior.
Empty document rows retain table-cell/colspan layout and remove the legacy
minimum-height dead space. Request status filters use a compact flex row;
category creation has a separate RTL-aligned action footer.

Validation: 30 additional full-shell browser fixtures across dark/light and
390/900/1800 widths, with visibility checks after shell readiness and entrance
animations; all pass. The 72 previous identity fixtures and 2682 unit tests
also pass (30 skipped). Release build has no errors/warnings; configured NuGet
sources report no vulnerable packages. No handlers, field attributes, database
logic, production records or runtime settings changed.

The Graphify CLI was retried but its launcher refers to a missing script under
the user bin directory. The graph remains unrefreshed; source was verified directly.
