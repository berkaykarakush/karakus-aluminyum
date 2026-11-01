#!/usr/bin/env bash
# Veritabanı, yüklenen görseller ve kimlik anahtarlarını yedekler. Web ve db konteynerleri çalışıyor olmalıdır.
set -euo pipefail
umask 077
cd "$(dirname "$0")/.."
if [[ ! -f .env ]]; then echo ".env bulunamadı; proje kökünde .env oluşturun." >&2; exit 1; fi
set -a; source ./.env; set +a
: "${POSTGRES_DB:?POSTGRES_DB .env içinde tanımlı olmalı}"
: "${POSTGRES_USER:?POSTGRES_USER .env içinde tanımlı olmalı}"
KEEP_DAYS="${BACKUP_KEEP_DAYS:-14}"
REMOTE="${BACKUP_REMOTE:-}"
if [[ -n "$REMOTE" ]] && ! command -v rclone >/dev/null; then echo "BACKUP_REMOTE tanımlı ama rclone kurulu değil." >&2; exit 1; fi

mkdir -p backups
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
DB_OUT="backups/${POSTGRES_DB}-${STAMP}.dump"
UPLOADS_OUT="backups/uploads-${STAMP}.tar.gz"
KEYS_OUT="backups/dpkeys-${STAMP}.tar.gz"
# Yarım kalan yedek, sağlam yedek gibi görünmesin diye dosyalar doğrulanınca yerine taşınır.
trap 'rm -f "$DB_OUT.tmp" "$UPLOADS_OUT.tmp" "$KEYS_OUT.tmp"' EXIT

docker compose exec -T db pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc > "$DB_OUT.tmp"
docker compose exec -T db pg_restore --list < "$DB_OUT.tmp" > /dev/null
docker compose exec -T web tar -C /app/wwwroot/uploads -czf - . > "$UPLOADS_OUT.tmp"
gzip -t "$UPLOADS_OUT.tmp"
docker compose exec -T web tar -C /var/dpkeys -czf - . > "$KEYS_OUT.tmp"
gzip -t "$KEYS_OUT.tmp"

mv "$DB_OUT.tmp" "$DB_OUT"
mv "$UPLOADS_OUT.tmp" "$UPLOADS_OUT"
mv "$KEYS_OUT.tmp" "$KEYS_OUT"
echo "Yedek oluşturuldu: $DB_OUT, $UPLOADS_OUT, $KEYS_OUT"

if [[ -n "$REMOTE" ]]; then
  rclone copy backups "$REMOTE" --include "*-${STAMP}.*"
  echo "Sunucu dışına kopyalandı: $REMOTE"
fi

find backups -maxdepth 1 -type f \( -name "${POSTGRES_DB}-*.dump" -o -name 'uploads-*.tar.gz' -o -name 'dpkeys-*.tar.gz' \) -mtime "+${KEEP_DAYS}" -delete
