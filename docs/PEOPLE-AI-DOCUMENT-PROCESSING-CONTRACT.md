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
| PDF | Yes | Yes | **No** | **No** | No |
| DOC/DOCX | Yes | Yes | No | **No** | No |
| XLS/XLSX | Yes | Yes | No | **No** | No |
Onboarding preview is currently `CanPreview=false` for every format because no pre-promotion protected preview route exists. Preview capability is deliberately not inferred from browser MIME support.

PDF is intentionally storage/review-only in this production closure. The current local worker has no PDF renderer, text-PDF parser, scanned-PDF page renderer, page-limit enforcement or per-page OCR pipeline. Therefore ZYNORA does not advertise PDF as automatically extractable.

Office documents are also storage/review-only. There is no DOC/DOCX/XLS/XLSX parser or conversion pipeline in the existing architecture.

Storage-only files receive `ProcessingStatus=Stored` and are never queued for OCR. A completed `ManualReview/StorageOnly` extraction run is created from the existing company field policies so reviewers can enter fields manually; required fields remain subject to the existing Ready gate. Known structured document types also create an explicit unsupported-extraction review issue. Legacy queued unsupported files are converted to `Stored` by the worker without retrying fake extraction.

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
- Other/bilingual image documents use the company `EnabledLanguages`; `ar,en` runs both models and merges de-duplicated OCR lines.
- The Python worker caches PaddleOCR engines by language for the life of the worker process and merges de-duplicated OCR lines for bilingual requests.

`PeopleAIWorker__Language` remains the fallback only when the company setting has no supported language.

## Limits and failure behavior

The existing protected-upload contract remains authoritative for extension, binary signature, malware scanning, SHA-256 and maximum upload size. Unsupported extraction is not treated as OCR failure: the file remains safely stored for human review instead of entering retry loops.

PDF automatic extraction can be introduced later only when a real contract exists for text PDFs, scanned PDFs, multi-page rendering, page limits, per-page OCR, corrupted PDF behavior and timeout accounting.
