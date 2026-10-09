# Employee engagement source handoff

Scope: announcement template studio/library, free announcements, company-scoped employee/position selection, multilingual previews and poll content, draft/publication options, announcement details/edit/soft-delete, compact four-column announcement list, poll lifecycle/privacy, modal layering, and removal of redundant Recognition tab. Historical Recognition URL falls back to the announcement list.

The authoring language follows the system UI; additional translations are manual and per-field. Missing user-language content falls back to the base content. Poll option IDs and votes remain shared across translations.

## Verification and deployment boundary

Full solution restore/build succeeded locally. Targeted tests were run throughout implementation, including 19 announcement layout/navigation/classification tests and 10 poll lifecycle/translation tests. The broad suite reports an English/Kurdish catalog key-parity failure: new engagement keys are not all present in the Kurdish resource. SQL integration cases requiring a dedicated connection were skipped. This is a Draft handoff, not a production-ready certification.

Previously verified on isolated staging port 5098. This handoff transfers source only: do not publish/restart the original runtime or apply migrations as an implicit part of copying source. No production data or schema is changed. Existing original-checkout notification/end-service changes must be preserved.

## Controlled database requirements

Review the EF AddDynamicAnnouncementTemplates migration and AnnouncementStudioSchema controlled SQL export, then the exported poll lifecycle and poll translations migrations under database/migrations. Never apply them automatically to production. Back up the intended database, verify migration history and environment, obtain separate deployment approval, and execute through the controlled migrator.

## Remaining review caveats

- Poll audience creation stores target IDs while older employee/API audience evaluation has legacy name/value matching; full audience parity needs a separate review before deployment.
- Aggregate confidential results are hidden below five participants, but participation records are retained for duplicate-vote prevention; this is not database-level anonymous voting.
- The feature branch inherits other already-committed project prerequisites; review its base before merging. Do not merge straight to main without review.
- Graphify launcher was unavailable (uv trampoline path error); source checks were used, and no graph refresh is claimed.

Screenshots, local deployment logs, clipboard content and private/local runtime settings are excluded from the source commit. Old packaged assets are retained for backward compatibility; the new built-in art is added without deleting old runtime assets.
