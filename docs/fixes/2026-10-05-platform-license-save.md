# Platform license save feedback and identity colours

## Scope
- Isolate the license form's ModelState from independent renewal, domain and customer-profile form errors. Keep submitted-field conversion errors and global errors intact.
- Show every license validation error in the license panel; explain invalid dates, limits, modules and stale version tokens in Arabic.
- Show success/failure feedback next to the license form and redirect back to its fragment after saving. Rehydrate other panels after rejected submissions without replacing submitted license values.
- Platform colours consume the existing ZYNORA theme contract and design tokens. Use slate-blue actions, consistent dark surfaces and semantic status colours; remove independent teal gradients. No inline styles or new literal colours.

## Safety
Authorization, antiforgery, tenant identifiers, optimistic row-version checks and database update logic are unchanged. No license values, customer data, schema or migrations were modified for testing.

## Verification
Regression tests cover unrelated-form isolation, preserving binding/global errors and attempted values. Full Release build, tests, restore, package vulnerability audit and whitespace check are required before publication. Database-dependent tests remain skipped without the dedicated integration environment; no production form submission is used as an E2E test.

Graphify update was attempted but its installed uv trampoline cannot resolve its script. Existing graph is stale; implementation was verified against source directly.
