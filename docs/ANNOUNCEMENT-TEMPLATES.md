# Dynamic announcement templates

Entry: Engagement → Announcements → Templates, designs and preview (`/Engagement/Studio`).

- Company-owned library; administrator creates/edits templates and uploads/disables designs.
- Eleven initial occasions, including separate newborn boy/girl, birthday, welcome,
  marriage, condolences, holiday range, monthly award, work anniversary and retirement.
- Builder supports text/date/month/number fields, required/optional, arbitrary language codes,
  translated titles/bodies, and multiple selected designs. A company's template override
  replaces only that company's default. Disabled defaults do not reappear.
- Variables use `{fieldKey}`; `{years}` is completed years from `joiningDate` in Baghdad.
  Unknown placeholders, duplicate fields/languages, invalid dates/ranges and long texts fail validation.
- Dates in the template are event dates, not publication dates. Draft is the publication default.
- Images are stored in the database, not a public upload path. Only raster PNG/JPEG,
  up to 5 MiB and bounded raster dimensions. Employee image access requires tenant,
  company, published wall and recipient-snapshot ownership. Disabled designs remain available
  to already-published announcements. Picture contents are not editable; replace/upload an artwork.
- Fit, crop anchor, and text placement are immutable announcement snapshots, as are all rendered
  language versions. Later template edits never rewrite old posts.
- Arabic/English starter texts are included. Other languages, including Kurdish, can be added
  and maintained through the builder; there is no machine translation. Field values are supplied
  by the author and are not automatically translated.
- UI labels, dynamic controls, messages, default occasion names and field labels use the system
  dictionary. Built-in title/body source keys are registered in compiled resources. Add a language
  in Settings → Dictionary and translate both texts while retaining placeholders: complete, valid
  translations automatically become available for unsaved built-in templates. Company-authored
  template content stays in its scoped builder, not the global dictionary. Existing posts remain immutable.
- Per-company audience is default; optional individual selection is checked against that company.
  AnnouncementService enforces existing create/publish permissions. Template/image library writes
  are administrator-only. Duplicate publication uses the existing unique TranslationGroupId.

## Database change (not applied to production)

EF migration: `20261009160617_AddDynamicAnnouncementTemplates`.
Reviewed canonical deployment export: `AnnouncementStudioSchema`, registered in
`SqlSchemaMigrator` under `20261009-01-dynamic-announcement-templates`.
It touches only AnnouncementContents and the two new AnnouncementStudio tables.
Use the existing explicitly-approved database deployment workflow and backup confirmation.
Do not apply the entire EF migration chain to legacy production: its snapshot already has
pre-existing drift. The new snapshot changes are intentionally limited to this feature.
No per-request schema creation or runtime auto-repair has been added.

The EF Down path removes the new library and presentation snapshots; it deliberately does
not shrink LanguageCode, which could lose new-language content. Back up and plan rollback
before invoking any destructive migration. Prefer rolling back application code while retaining
the additive schema.

## Verification

Build/test locally. Production data and deployment are out of scope. Unit tests cover required
and optional fields, strict date parsing, ranges, completed-year calculations, new-language
support, schemas, safe replacement, image signature/dimension checks, and snapshot matching.
Dedicated SQL/browser verification is recorded separately; unit tests do not prove full tenant isolation.

### Local verification — 2026-10-09

- Release solution build: zero warnings/errors.
- Full regression run: 2,935 passed, 34 skipped (unrelated tests need their own integration fixtures).
- Focused feature run after adding recipient-image isolation: 28 passed, including five dedicated LocalDB tests.
- Actual JavaScript tested in isolated headless Chromium: date controls, preserved posted values,
  language switch, safe text, holiday ranges, layout switch and custom-language serialization passed.
  This is a DOM smoke test, not a full authenticated application E2E or a visual pixel review.
- NuGet vulnerability scan: no known vulnerable packages reported from configured sources.
- JavaScript syntax and Git whitespace checks passed.
- SQL tests use only the named CodexAnnouncementTests20261009 instance and newly generated synthetic catalogs.
  No production database was read, migrated or modified.
- Graphify update was attempted with installed and bundled interpreters, but its local launcher
  returned no graph artifact. Dependencies were checked in source; no updated graph is claimed.
- Starter artwork reuses existing Zynora designs. Upload your own designs for distinct occasion artwork;
  no bitmap editor or automatic translation of field values is included.

### Review handoff

Dictionary follow-up: 104 matching new English/Kurdish resource keys; UI catalog lookups
cover server markup and dynamic controls. Only relevant script keys/field labels are sent
to the browser. User-entered values and company template texts are not globally registered.
The new-language and placeholder tests passed: focused localization/studio run 123 passed;
final full run 2,938 passed, 34 skipped, zero failed. Release solution build had zero warnings/errors.
Headless DOM checks also passed translated controls/messages and dictionary-defined direction.
These are local checks, not production verification. Deployment still awaits review/base approval.

The isolated feature branch starts at the user's existing local commit `72a20565`.
The remote source branch `feature/separate-company-onboarding-20261009` is absent and the
local base is 122 commits ahead of the cached remote main. A Draft PR was therefore not opened:
pushing that base would also publish unrelated local work. Select/publish a reviewed base or
port this feature to the intended integration branch before creating the PR. No main push,
merge, production deployment, or production migration occurred.
