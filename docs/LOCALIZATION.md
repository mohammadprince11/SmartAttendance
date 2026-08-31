# Zynora localization

Zynora supports Arabic (`ar-IQ`), English (`en-US`), and Kurdish Sorani
(`ckb-IQ`). Arabic and Kurdish use RTL; English uses LTR.

## How it works

- ASP.NET Core request localization selects culture from the protected culture
  cookie, with a query-string provider available for diagnostics.
- `/Culture/Set` accepts only registered culture codes, writes the standard
  ASP.NET culture cookie, and redirects only to a validated local URL.
- Razor pages use `IStringLocalizer<SharedResource>` through the injected `T`
  helper. Arabic source text is the resource key and the Arabic fallback.
- The exact-text compatibility bridge translates known text and accessible
  attributes in legacy Razor/JavaScript screens, including content added after
  load. It never uses `innerHTML`, never changes user data, and does not store
  language state in `localStorage`.
- `zynora-direction.css` applies the final RTL/LTR contract after legacy page
  styles so older hard-coded RTL rules cannot override English layout.

## Adding or changing a translation

Add the same key to both files:

- `SmartAttendance.Web/Resources/SharedResource.en-US.resx`
- `SmartAttendance.Web/Resources/SharedResource.ckb-IQ.resx`

For new Razor markup, render text as `@T["Arabic source key"]`. For values
inside JavaScript, serialize `T[key].Value` with `JsonSerializer` rather than
building a JavaScript string by hand.

The `LocalizationContractTests` suite checks catalog parity, non-empty values,
supported cultures, cookie safety, dynamic direction, and the legacy bridge.

## Adding another language

1. Add the culture to `ZynoraSupportedCultures` with its native display name
   and direction.
2. Add `SharedResource.<culture>.resx` with the complete key set.
3. Extend the catalog-parity test to include the new file.
4. Verify the login page and authenticated shell at desktop and mobile widths.
