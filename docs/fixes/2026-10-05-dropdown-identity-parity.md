# Dropdown identity parity

The shared dropdown contract now reads text and muted text directly from the
identity tokens used by the main, employee portal and platform layouts. Previously
it read design-system aliases that were absent in the platform layout, producing
a different fallback palette. Field and popup use the same input surface; option,
border, focus and scrollbar colors derive from the shared slate-blue brand tokens.
Both light and dark themes retain the same geometry and interaction states.

Portalled option panels and their triggers share an explicit font stack rather
than inheriting different page/body fonts. The onboarding review's higher-specificity
enhanced-select overrides were removed so it no longer uses a different surface.
Widths, native form values, single/multiple selection and filtering are unchanged.

Verification: Release build (zero warnings/errors), dropdown contract tests (5/5),
full unit suite, restore, dependency vulnerability audit and whitespace check.
No authenticated production E2E or database changes. Graphify update was attempted
but its installed launcher points to a missing script; source was inspected directly.
