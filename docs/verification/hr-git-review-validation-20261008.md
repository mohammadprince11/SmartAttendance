# Git review validation

## Source grouping

- First commit: previously published isolated application source.
- Follow-up: typed approval-template conditions and their explicit schema migration, not deployed.
- Test harness fixes: compare identical field subsets when HEAD already contains user articles; locate the designer section after the article section rather than matching an earlier header branch.
- Original dirty worktree preserved. No SQL, migrations, new deployment or main-branch writes are part of this upload.
- Runtime data, configuration, database cleanup artifacts, screenshots and workstation-specific deployment logs are excluded.

## Checks

- Solution restore and Release build succeeded with zero warnings/errors.
- .NET: 2909 passed, 34 integration tests skipped, zero failed.
- Package vulnerability audit: no vulnerable packages reported by the configured NuGet sources.
- Offline synthetic UI checks: typed conditions, approval navigation, disciplinary sidebar/loading/workspace/save/designer/PDF/articles, People AI, HR layout and reference selectors.
- Git whitespace check passes, retaining upstream vendor license bytes through a scoped whitespace attribute.
- Staged additions were scanned for common API-token/private-key patterns. This is a limited pattern scan, not an exhaustive security audit.

Dedicated DB integration tests remain required before approval/deployment of typed conditions. Uploading this draft branch does not assert production readiness or tenant isolation certification.
