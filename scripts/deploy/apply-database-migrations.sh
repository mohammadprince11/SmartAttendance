#!/usr/bin/env bash
set -euo pipefail

MODE="${1:---status}"
ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
PROJECT="$ROOT/tools/SmartAttendance.DatabaseMigrator/SmartAttendance.DatabaseMigrator.csproj"

if [[ "$MODE" != "--status" && "$MODE" != "--apply" ]]; then
  echo "Usage: $0 --status|--apply" >&2
  exit 2
fi

if [[ -z "${ConnectionStrings__DefaultConnection:-${ZYNORA_DATABASE_MIGRATION_CONNECTION:-}}" ]]; then
  echo "Database migration connection string is required in the environment." >&2
  exit 1
fi
if [[ "$MODE" == "--apply" ]]; then
  if [[ "${ZYNORA_DATABASE_MIGRATION_CONFIRM:-}" != "APPLY" ]]; then
    echo "Set ZYNORA_DATABASE_MIGRATION_CONFIRM=APPLY before --apply." >&2
    exit 1
  fi

  if [[ -z "${ZYNORA_DATABASE_MIGRATION_BACKUP_REFERENCE:-}" ]]; then
    echo "ZYNORA_DATABASE_MIGRATION_BACKUP_REFERENCE is required before --apply." >&2
    exit 1
  fi
fi

dotnet run \
  --project "$PROJECT" \
  -c Release \
  --no-launch-profile \
  -- "$MODE"
