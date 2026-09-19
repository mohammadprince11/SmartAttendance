# ZYNORA People AI OCR — Production Packaging

Primary server target: Ubuntu 24.04 LTS. Windows production is also
supported by the versioned watchdog launcher. The OCR process remains a child
of the existing ASP.NET Core People AI hosted service. Do not create a second
queue worker or a second OCR service.

## Runtime package

Published `SmartAttendance.Web` output must contain:

- `PeopleAI/local_ocr_worker.py`
- `PeopleAI/requirements-ocr.txt`

The web service starts the Python worker through `PeopleAIWorker` configuration.

## Windows production

The canonical Windows watchdog is:

`scripts/deploy/Start-Zynora-Windows.ps1`

It keeps the ASP.NET Core process supervised, captures web stdout/stderr, and
sets `PADDLE_PDX_CACHE_HOME=C:\ZynoraRuntime\PeopleAI\paddlex-cache`.
Keep the isolated Python environment under
`C:\ZynoraRuntime\PeopleAI\.venv` and the OCR temp directory under
`C:\ZynoraRuntime\PeopleAI\tmp`. The PaddleX cache must not depend on the
interactive user's `~\.paddlex` directory because scheduled-task tokens can
have different effective access during model initialization.

## Install on Ubuntu

Install the Ubuntu prerequisites first:

```bash
sudo apt-get update
sudo apt-get install -y python3 python3-venv libreoffice-writer libreoffice-calc
```

CPU is the default production profile:

```bash
sudo mkdir -p /opt/zynora/people-ai /var/lib/zynora/people-ai/tmp /var/lib/zynora/people-ai/paddlex-cache
sudo bash scripts/deploy/install-people-ai-ocr-ubuntu.sh cpu
```

For NVIDIA hosts use `cu118` or `cu126` only when the installed driver matches
the official PaddlePaddle runtime requirements.
The installer creates an isolated venv, installs PaddlePaddle 3.2.0 and
the packages from `requirements-ocr.txt`, including PaddleOCR 3.5.0, Pillow,
pypdfium2 and olefile. LibreOffice Writer/Calc provide isolated legacy DOC/XLS conversion. PaddleOCR is intentionally held at 3.5.0 while ZYNORA's OCR
contract remains PP-OCRv5; upgrading the OCR model/runtime is a separate
regression-tested change. PDFium is used for direct PDF text extraction and
per-page rendering before OCR fallback.

Then it executes:

```bash
local_ocr_worker.py --preflight
```

Preflight initializes PaddleOCR and its required models. Deployment must not
proceed if preflight fails.

## systemd environment

Copy `scripts/deploy/zynora-people-ai.env.example` outside the repository and
reference it from the existing ZYNORA web service:

```ini
[Service]
EnvironmentFile=/etc/zynora/people-ai.env
```

The production service user must have read access to the published application
and read/write access to:

```text
/var/lib/zynora/people-ai/tmp
/var/lib/zynora/people-ai/paddlex-cache
<AppContentRoot>/App_Data/ProtectedPeopleAssets
```
Recommended runtime configuration:

```text
PeopleAIWorker__Enabled=true
PeopleAIWorker__PythonExecutable=/opt/zynora/people-ai/.venv/bin/python
PeopleAIWorker__ScriptPath=PeopleAI/local_ocr_worker.py
PeopleAIWorker__Device=auto
PeopleAIWorker__Language=ar
PeopleAIWorker__TempDirectory=/var/lib/zynora/people-ai/tmp
PADDLE_PDX_CACHE_HOME=/var/lib/zynora/people-ai/paddlex-cache
PeopleAIWorker__PdfMaxPages=20
PeopleAIWorker__PdfRenderDpi=180
PeopleAIWorker__DocxMaxEntries=2000
PeopleAIWorker__DocxMaxUncompressedMegabytes=64
PeopleAIWorker__DocxMaxCompressionRatio=200
PeopleAIWorker__XlsxMaxEntries=5000
PeopleAIWorker__XlsxMaxUncompressedMegabytes=64
PeopleAIWorker__XlsxMaxCompressionRatio=200
PeopleAIWorker__XlsxMaxSheets=50
PeopleAIWorker__XlsxMaxRowsPerSheet=5000
PeopleAIWorker__XlsxMaxCellsPerSheet=50000
PeopleAIWorker__LibreOfficeExecutable=/usr/bin/libreoffice
PeopleAIWorker__OfficeConversionTimeoutSeconds=90
```

`Device=auto` uses GPU only when the installed PaddlePaddle wheel exposes an
available CUDA device; otherwise it uses CPU. `Device=gpu` fails startup when
CUDA is unavailable rather than silently falling back.

## Startup contract

Before queue processing starts, ZYNORA now verifies:

1. protected People AI storage is writable;
2. Python can be started;
3. the worker script exists;
4. the configured temp directory is writable;
5. the PaddleX cache directory is writable when `PADDLE_PDX_CACHE_HOME` is configured;
6. Pillow, pypdfium2 and olefile import successfully;
7. LibreOffice converter is resolvable;
8. PaddleOCR imports successfully;
9. CPU/GPU device selection is valid;
10. OCR models initialize successfully.

A failure stops only People AI queue processing and writes a Critical startup
diagnostic. It does not start consuming jobs in a partially initialized state.
