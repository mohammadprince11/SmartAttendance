# ZYNORA People AI OCR — Production Packaging

Target: Ubuntu 24.04 LTS. The OCR process remains a child of the existing
ASP.NET Core People AI hosted service. Do not create a second queue worker or
a second OCR service.

## Runtime package

Published `SmartAttendance.Web` output must contain:

- `PeopleAI/local_ocr_worker.py`
- `PeopleAI/requirements-ocr.txt`

The web service starts the Python worker through `PeopleAIWorker` configuration.

## Install on Ubuntu

Install the Ubuntu prerequisites first:

```bash
sudo apt-get update
sudo apt-get install -y python3 python3-venv
```

CPU is the default production profile:

```bash
sudo mkdir -p /opt/zynora/people-ai /var/lib/zynora/people-ai/tmp
sudo bash scripts/deploy/install-people-ai-ocr-ubuntu.sh cpu
```

For NVIDIA hosts use `cu118` or `cu126` only when the installed driver matches
the official PaddlePaddle runtime requirements.
The installer creates an isolated venv, installs PaddlePaddle 3.2.0 and
the packages from `requirements-ocr.txt`, including PaddleOCR 3.5.0, Pillow
and pypdfium2. PaddleOCR is intentionally held at 3.5.0 while ZYNORA's OCR
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
PeopleAIWorker__PdfMaxPages=20
PeopleAIWorker__PdfRenderDpi=180
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
5. Pillow and pypdfium2 import successfully;
6. PaddleOCR imports successfully;
7. CPU/GPU device selection is valid;
8. OCR models initialize successfully.

A failure stops only People AI queue processing and writes a Critical startup
diagnostic. It does not start consuming jobs in a partially initialized state.
