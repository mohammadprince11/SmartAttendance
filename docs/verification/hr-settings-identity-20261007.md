# HR settings identity alignment

Scope: NoticePeriod, SelfServiceSettings, TerminationReasons, NotificationCenter,
Lookups, ApprovalTemplates, RequestTypes, LeavePolicies and PeopleAI.

The views opt into a page-scoped identity bridge: existing theme/dropdown tokens,
flat surfaces, uniform card spacing, consistent buttons and controls, responsive
form grids and contained table scrolling. The approval drawer retains its open/
close lifecycle and uses a 200ms transition. Native multi-selects remain tall;
notice-period segmented radios retain keyboard access. Lookup creation has a
separate action footer, and termination-reason fields have visible labels.

No handlers, field names, validation constraints, authorization, JavaScript
business behavior, database logic, calculation rules or runtime settings change.
Notification switches additionally expose their current state through
role="switch" and aria-checked, updated after the existing successful POST.
Static attribute comparison uses commit 97d19f71 as the immutable baseline.

Verification:

- Release solution build: zero warnings and errors.
- Unit tests: 2682 passed, 30 skipped, zero failed. Database integration tests
  remain skipped without their dedicated fixture; production data was not used.
- 54 full-shell synthetic browser fixtures: all nine views, dark/light themes,
  390/900/1800px; visibility, palette, overflow, field height, hidden POST values,
  native checkboxes, shared dropdowns, segmented radios and approval drawer.
- Follow-up: all 54 fixtures passed again with 52x28px capsule switches,
  contained 22px thumbs, RTL on/off travel and centered segmented labels.
  The actual notification inline script is exercised with mocked successful
  fetch responses; mouse activation and keyboard Space update aria-checked
  and the existing detail visibility. Release build passed again with no
  warnings or errors. No real notification setting was changed.
- 30 preceding clipping regression fixtures passed.
- 72 preceding HR administration identity fixtures passed.
- Configured NuGet sources reported no vulnerable packages.

Fixtures use invented data and block network requests. They are not a production
end-to-end test. Publishing requires explicit approval for this operation.

Graphify query located HR settings source nodes; the graph is treated as stale
and checked against source. Incremental update was attempted but the installed
launcher references a missing script in the user bin directory. No refreshed
graph is claimed.
