#!/usr/bin/env bash
# Veritabanını geri yükler. Aynı zaman damgalı uploads ve dpkeys yedekleri yanındaysa onları da yükler.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ $# -ne 1 || ! -f "$1" ]]; then echo "Kullanım: $0 backups/veritabani-tarihi.dump" >&2; exit 2; fi
if [[ ! -f .env ]]; then echo ".env bulunamadı." >&2; exit 1; fi
set -a; source ./.env; set +a
: "${POSTGRES_DB:?POSTGRES_DB .env içinde tanımlı olmalı}"
: "${POSTGRES_USER:?POSTGRES_USER .env içinde tanımlı olmalı}"

DIR="$(dirname "$1")"
STAMP="$(basename "$1" .dump)"; STAMP="${STAMP##*-}"
UPLOADS="$DIR/uploads-$STAMP.tar.gz"
KEYS="$DIR/dpkeys-$STAMP.tar.gz"

echo "UYARI: Bu işlem $POSTGRES_DB veritabanındaki mevcut nesneleri değiştirebilir/silebilir."
[[ -f "$UPLOADS" ]] && echo "UYARI: Yüklenen görseller $UPLOADS ile değiştirilecek."
[[ -f "$KEYS" ]] && echo "UYARI: Kimlik anahtarları $KEYS ile değiştirilecek ve web yeniden başlatılacak."
read -r -p "Devam etmek için RESTORE yazın: " confirm
[[ "$confirm" == "RESTORE" ]] || { echo "İptal edildi."; exit 1; }

docker compose exec -T db pg_restore -U "$POSTGRES_USER" -d "$POSTGRES_DB" --clean --if-exists < "$1"
if [[ -f "$UPLOADS" ]]; then
  docker compose exec -T web sh -c 'find /app/wwwroot/uploads -mindepth 1 -delete && tar -C /app/wwwroot/uploads -xzf -' < "$UPLOADS"
fi
if [[ -f "$KEYS" ]]; then
  docker compose exec -T web sh -c 'find /var/dpkeys -mindepth 1 -delete && tar -C /var/dpkeys -xzf -' < "$KEYS"
  docker compose restart web
fi
echo "Geri yükleme komutu tamamlandı. Uygulama ve veri tutarlılığını kontrol edin."
