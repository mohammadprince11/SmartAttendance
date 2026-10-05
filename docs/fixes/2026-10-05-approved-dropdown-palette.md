# Approved dropdown reference palette

The user approved the Employee Edit screenshot as the exact dropdown reference:
field `#111b2a`, popup `#0f1a29`, selected/focused option `#20374f`, control border
`#33506a`, focus border `#6f8da8`. These defaults are defined only in the shared
dropdown contract. Light mode retains its readable identity-token surfaces.

The previous source-order fix was insufficient: Employee Create/Edit page CSS
uses ID and `:has()` selectors with greater specificity than the shared rules.
The visual contract now uses an important cascade layer, which outranks legacy
unlayered important declarations without another list of page-specific overrides.
Behavioral visibility/position/z-index/disabled rules remain outside this layer.
The searchable direct-manager control and its option rows now use the same palette.
No selection, filtering, validation, data or authorization behavior changed.

`scripts/tests/test-dropdown-palette.cjs` runs a disposable, network-blocked
browser document using real component/page CSS. Twenty computed-style scenarios
cover dark/light, RTL, mobile/desktop, Employee Create/Edit, filters, Positions,
Onboarding Review, searchable/portal variants and native multi-select backgrounds.
An additional later high-specificity important rule verifies the original cascade
failure cannot recur. Focus state and exact approved RGB values are asserted.
Uses installed Edge by default (`DROPDOWN_BROWSER_CHANNEL` can override).
Requires Playwright through the existing Node dependency environment. No production
server, account or database is contacted. Release build and contract tests also run.
