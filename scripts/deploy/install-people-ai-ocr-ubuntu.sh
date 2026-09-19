#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-cpu}"
VENV_DIR="${ZYNORA_PEOPLE_AI_VENV:-/opt/zynora/people-ai/.venv}"

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
PACKAGE_ROOT="${ZYNORA_PEOPLE_AI_PACKAGE_ROOT:-$REPO_ROOT/SmartAttendance.Web/PeopleAI}"
WORKER="$PACKAGE_ROOT/local_ocr_worker.py"
REQUIREMENTS="$PACKAGE_ROOT/requirements-ocr.txt"

if ! command -v python3 >/dev/null 2>&1; then
  echo "python3 is required." >&2
  exit 1
fi

if command -v libreoffice >/dev/null 2>&1; then
  LIBREOFFICE_EXECUTABLE="$(command -v libreoffice)"
elif command -v soffice >/dev/null 2>&1; then
  LIBREOFFICE_EXECUTABLE="$(command -v soffice)"
else
  echo "LibreOffice Writer/Calc is required for DOC/XLS extraction." >&2
  exit 1
fi

if [[ ! -f "$WORKER" || ! -f "$REQUIREMENTS" ]]; then
  echo "People AI worker package files are missing." >&2
  exit 1
fi
python3 -m venv "$VENV_DIR"
PYTHON="$VENV_DIR/bin/python"

"$PYTHON" -m pip install --upgrade pip setuptools wheel

case "$MODE" in
  cpu)
    PADDLE_INDEX="https://www.paddlepaddle.org.cn/packages/stable/cpu/"
    PADDLE_PACKAGE="paddlepaddle==3.2.0"
    ;;
  cu118)
    PADDLE_INDEX="https://www.paddlepaddle.org.cn/packages/stable/cu118/"
    PADDLE_PACKAGE="paddlepaddle-gpu==3.2.0"
    ;;
  cu126)
    PADDLE_INDEX="https://www.paddlepaddle.org.cn/packages/stable/cu126/"
    PADDLE_PACKAGE="paddlepaddle-gpu==3.2.0"
    ;;
  *)
    echo "Usage: $0 [cpu|cu118|cu126]" >&2
    exit 2
    ;;
esac
"$PYTHON" -m pip install "$PADDLE_PACKAGE" -i "$PADDLE_INDEX"
"$PYTHON" -m pip install -r "$REQUIREMENTS"

export PEOPLE_AI_OCR_DEVICE="${PEOPLE_AI_OCR_DEVICE:-auto}"
export PEOPLE_AI_OCR_LANGUAGE="${PEOPLE_AI_OCR_LANGUAGE:-ar}"
export PEOPLE_AI_OCR_TEMP_DIRECTORY="${PEOPLE_AI_OCR_TEMP_DIRECTORY:-/var/lib/zynora/people-ai/tmp}"
export PADDLE_PDX_CACHE_HOME="${PADDLE_PDX_CACHE_HOME:-/var/lib/zynora/people-ai/paddlex-cache}"
export PEOPLE_AI_LIBREOFFICE_EXECUTABLE="${PEOPLE_AI_LIBREOFFICE_EXECUTABLE:-$LIBREOFFICE_EXECUTABLE}"
export PEOPLE_AI_OFFICE_CONVERSION_TIMEOUT_SECONDS="${PEOPLE_AI_OFFICE_CONVERSION_TIMEOUT_SECONDS:-90}"

mkdir -p "$PEOPLE_AI_OCR_TEMP_DIRECTORY" "$PADDLE_PDX_CACHE_HOME"

echo "Running People AI OCR preflight..."
"$PYTHON" "$WORKER" --preflight

cat <<EOF
People AI OCR runtime installed successfully.
PythonExecutable=$PYTHON
WorkerPath=$WORKER
Device=$PEOPLE_AI_OCR_DEVICE
Language=$PEOPLE_AI_OCR_LANGUAGE
TempDirectory=$PEOPLE_AI_OCR_TEMP_DIRECTORY
PaddleCache=$PADDLE_PDX_CACHE_HOME
LibreOffice=$PEOPLE_AI_LIBREOFFICE_EXECUTABLE
OfficeConversionTimeoutSeconds=$PEOPLE_AI_OFFICE_CONVERSION_TIMEOUT_SECONDS
EOF
