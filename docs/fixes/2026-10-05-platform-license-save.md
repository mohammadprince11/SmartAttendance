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

## Follow-up: selected plan rejected by browser
The subsequent screenshot exposed a client-side error for the valid `Custom` option. The original `StringLength(MinimumLength = 2)` produces a jQuery `rangelength` rule. For a select, jQuery counts selected options (one), not code characters, so every single choice is rejected before POST. Replace that attribute with an equivalent full-string regular expression (2–60 characters), retaining Required validation. This validates the selected code on both client and server without disabling other validation.

Tests use actual MVC validation adapters to verify emitted regex/required rules without option-count length rules. The Node harness exercises the shipped jQuery/unobtrusive rule implementations, reproducing the old rejection and verifying Custom, preset values and length boundaries. No real license save is used for testing.
