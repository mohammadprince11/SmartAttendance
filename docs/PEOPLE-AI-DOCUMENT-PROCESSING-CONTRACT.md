# ZYNORA People AI — Document Processing Contract

## Capability dimensions

Upload permission does not imply AI extraction support. Every format is evaluated independently across:

- `CanUpload`
- `CanStore`
- `CanPreview`
- `CanExtractText`
- `HasStructuredExtractor`

The runtime source of truth is `DocumentProcessingContract`.

## Format matrix

| Format | Upload | Store | Preview | Automatic text/OCR | Structured extraction |
|---|---|---|---|---|---|
| PNG | Yes | Yes | **No** | Yes | Depends on document type |
| JPG/JPEG | Yes | Yes | **No** | Yes | Depends on document type |
| WEBP | Yes | Yes | **No** | Yes | Depends on document type |
| PDF | Yes | Yes | **No** | **Yes** | Depends on document type |
| DOC | Yes | Yes | No | **No** | No |
| DOCX | Yes | Yes | No | **Yes** | Depends on document type |
| XLS/XLSX | Yes | Yes | No | **No** | No |
Onboarding preview is currently `CanPreview=false` for every format because no pre-promotion protected preview route exists. Preview capability is deliberately not inferred from browser MIME support.

PDF uses a hybrid automatic extraction pipeline. Pages with a usable PDF text layer are read directly through PDFium without OCR. Pages without a usable text layer are rendered to temporary images and passed through the existing PaddleOCR pipeline. Results are normalized into the same page/line contract consumed by the deterministic structured extractors.

DOCX uses direct Open XML extraction without OCR. The worker reads paragraphs and table-cell text from `word/document.xml` (plus headers/footers when present) and normalizes the result into the same page/line contract consumed by deterministic structured extractors. Legacy binary DOC and Excel formats remain storage/review-only in this phase.

Storage-only DOC/XLS/XLSX files receive `ProcessingStatus=Stored` and are never queued for automatic extraction. A completed `ManualReview/StorageOnly` extraction run is created from the existing company field policies so reviewers can enter fields manually; required fields remain subject to the existing Ready gate. Legacy queued unsupported files are converted to `Stored` without retrying fake extraction.

## Document type capability

Document type configuration and extraction capability are separate concepts.

Current structured extractors:

- `NationalId` — Iraqi National ID deterministic parser.
- `Passport` — TD3 MRZ parser/check digits.
- `CV` — basic Phone and Personal Email only.

Custom types such as Work Permit, Contract, Residence or future company-defined types may be stored/reviewed without a structured extractor. If uploaded as an image, generic OCR text may still run, but no structured fields are claimed unless a real extractor exists.
Full CV Intelligence (education, employment, skills, certificates, languages, etc.) remains deferred to the post-production feature phase. This contract does not label those capabilities as complete.

## Classification contract

The fields have distinct meanings:

- `DeclaredDocumentType`: selected by the user/company workflow.
- `DetectedDocumentType`: produced only from actual deterministic evidence.
- `ClassificationConfidence`: confidence assigned by the deterministic classifier.
- `DetectionMethod`: explains the evidence path.
- mismatch: when declared and detected are both known and differ.

Current deterministic detection:

- Passport: valid/readable TD3 MRZ (`MRZ_TD3`).
- Iraqi National ID: strong Iraqi ID evidence (`IRAQI_NATIONAL_ID_RULES`).
- Otherwise: `DetectedDocumentType` remains null; ZYNORA does not copy the declared type into the detected field.

A declared/detected mismatch creates an open `DOCUMENT_TYPE_MISMATCH` review warning. Human review remains authoritative.
## OCR language contract

Company `EnabledLanguages` is wired into document processing.

- National ID always uses Arabic OCR because its deterministic parser depends on Arabic field labels.
- Passport always uses English OCR because TD3 MRZ is ICAO Latin text.
- Other/bilingual image documents and scanned PDF pages use the company `EnabledLanguages`; `ar,en` runs both OCR models and merges de-duplicated lines. Text-layer PDF pages are read directly and do not require OCR.
- The Python worker caches PaddleOCR engines by language for the life of the worker process and merges de-duplicated OCR lines for bilingual requests.

`PeopleAIWorker__Language` remains the fallback only when the company setting has no supported language.

## Limits and failure behavior

The existing protected-upload contract remains authoritative for extension, binary signature, malware scanning, SHA-256 and maximum upload size. Unsupported legacy Office extraction is not treated as OCR failure: the file remains safely stored for human review instead of entering retry loops.

PDF processing is bounded by `PeopleAIWorker__PdfMaxPages` (default 20, hard-clamped to 1–100) and `PeopleAIWorker__PdfRenderDpi` (default 180, hard-clamped to 120–300). Text-layer pages bypass OCR. Scanned pages are rendered one at a time to temporary JPEG files and deleted immediately after OCR. Password-protected, empty, unreadable and over-limit PDFs return stable error codes and are treated as permanent document failures rather than entering retry loops.

DOCX extraction is bounded by archive-entry count, total uncompressed bytes and compression ratio. Unsafe archive paths, encrypted packages, macro payloads and embedded objects are rejected before XML parsing. The defaults are 2,000 entries, 64 MB total uncompressed content and a 200:1 compression-ratio ceiling for large entries.
