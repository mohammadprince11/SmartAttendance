# Published HR changes review

This branch captures the application source from the isolated release published on 2026-10-08, without runtime configuration, uploaded documents, employee data, database backups or screenshots.

## Published changes

- Notification event routing, automatic generation, durable mail outbox and next-day end-service account restrictions.
- Approval template module/audience navigation, HR settings identity, People AI workspace organization and reference lookup bindings.
- User-authored disciplinary articles, form/PDF preview, lightweight tab loading, scaled paper preview and direct sidebar entry.
- Existing authorization/company scoping is preserved. Migration definitions require explicit controlled application; this Git upload runs no SQL or migrations.
- Vendored Mozilla PDF.js is included with original upstream license files and matching worker/resources. License whitespace is intentionally preserved.

## Verification

The exact isolated published source passed a Release build, 2868 .NET tests and 7 direct-sidebar contract checks. 34 integration tests were skipped because their dedicated environment was unavailable. Earlier synthetic disciplinary checks covered workspace navigation, upload redirects, designer layout and PDF rendering. No authenticated production E2E was run.

## Review limitations

This is a draft review, not permission to merge or apply migrations. The separate follow-up commit for typed approval conditions is not part of the published version and needs review before deployment. Existing local screenshots and database-maintenance artifacts are intentionally not uploaded.
