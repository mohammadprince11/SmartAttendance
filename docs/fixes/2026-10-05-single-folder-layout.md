# Single-folder workstation layout

The maintained source checkout is `C:\SmartAttendance`.

Local machine data is excluded from Git under `local-runtime/`:

- `portal/`: the existing published web application, its local configuration,
  certificates, data-protection keys and uploaded/protected files.
- `ocr/PeopleAI/`: Python OCR environment, model cache and temporary files.
- `database-backups/`: 39 existing database backup files retained unchanged.
  The latest file timestamp is September 25, 2026; this is not a fresh backup.
- `preserved-unique/`: deduplicated historical content and a private path/hash
  index. The previous `artifacts/` and `local-runtime/legacy-archive/` folders
  were recycled after validating all 124,523 original-file mappings. Matching
  current files are referenced; 14,106 unique files were copied independently.
  Archive content decreased from 13.75 GiB to approximately 2.9 GiB plus the
  index. This is not a standalone backup of referenced current files.

Deployment, handover and development launch defaults now use these paths.
No deployment, SQL query, database backup creation or restore is performed by
the folder consolidation. Published binaries are preserved, not rebuilt from
the current source commit.

For another Windows computer, copy the whole folder privately. Install the
required .NET, SQL Server and Python versions first. A Python virtual environment
is machine-specific: recreate it using the project's dependency instructions
and point OCR configuration to the recreated interpreter. Restore an appropriate
database backup explicitly after checking its age and intended database.
Review certificate and data-protection portability separately; copying files
does not prove that machine-bound keys will decrypt on another computer.

Verification: SHA256 equality for 1,382 published files and 25,343 OCR files
before local path edits; the six edited runtime files are compared against
their originals with only the documented path/cache substitutions allowed.
PowerShell syntax, JSON parsing, new runtime path existence and imports of
Paddle/PaddleOCR pass. No live production startup or database connection is
performed, so this is not a production health-check result.

Do not upload `local-runtime/` or private artifacts to Git or a public archive.

Generated `graphify-out/` analysis is no longer tracked in Git. Existing local
analysis files are retained and can be regenerated; this cleanup does not rewrite
Git history or remove product source, tests, migrations or documentation.
