#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ $# -ne 1 || ! -f "$1" ]]; then echo "Kullanım: $0 backups/veritabani-tarihi.dump" >&2; exit 2; fi
if [[ ! -f .env ]]; then echo ".env bulunamadı." >&2; exit 1; fi
set -a; source ./.env; set +a
: "${POSTGRES_DB:?POSTGRES_DB .env içinde tanımlı olmalı}"
: "${POSTGRES_USER:?POSTGRES_USER .env içinde tanımlı olmalı}"
echo "UYARI: Bu işlem $POSTGRES_DB veritabanındaki mevcut nesneleri değiştirebilir/silebilir."
read -r -p "Devam etmek için RESTORE yazın: " confirm
[[ "$confirm" == "RESTORE" ]] || { echo "İptal edildi."; exit 1; }
docker compose exec -T db pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists < "$1"
echo "Geri yükleme komutu tamamlandı. Uygulama ve veri tutarlılığını kontrol edin."
