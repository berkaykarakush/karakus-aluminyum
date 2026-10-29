#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ ! -f .env ]]; then echo ".env bulunamadı; proje kökünde .env oluşturun." >&2; exit 1; fi
set -a; source ./.env; set +a
: "${POSTGRES_DB:?POSTGRES_DB .env içinde tanımlı olmalı}"
: "${POSTGRES_USER:?POSTGRES_USER .env içinde tanımlı olmalı}"
mkdir -p backups
OUT="backups/${POSTGRES_DB}-$(date -u +%Y%m%dT%H%M%SZ).dump"
docker compose exec -T db pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc > "$OUT"
echo "Yedek oluşturuldu: $OUT"
