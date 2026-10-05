# Uniform tenant detail section spacing

The customer profile, domain, administrator, license, subscription and audit blocks previously touched or used inconsistent separate margins. Wrap these six sibling cards in a page-scoped grid with `gap: var(--zy-space-4)` (24px from existing ZYNORA tokens). Reset only direct-card block margins so subscription/audit spacing is not added twice. Keep card interior layout, form boundaries, handlers and data unchanged. The rule is responsive without a separate mobile override and uses logical sizing.

Verification: Razor Release build and whitespace checks pass. No data writes or production form submissions are required. Graphify's installed trampoline remains unavailable; the actual Razor structure and stylesheet were inspected directly.
