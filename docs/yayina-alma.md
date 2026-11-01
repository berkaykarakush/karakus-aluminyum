# Yayına alma

## İlk kurulum

1. `www` ve kök alan adı için A kaydını sunucunun IP'sine yönlendirin. Caddy sertifikayı bu kayıtlar çözümlenince alabilir.
2. Sunucuda yalnızca 22, 80 ve 443 portlarını açın.
3. Şifreleri içeren ortam dosyasını sunucuya `.env` olarak kopyalayın ve `chmod 600 .env` uygulayın. Şablon: `.env.example`.
4. `docker compose up -d --build` ile başlatın. `.env` içindeki `COMPOSE_PROFILES=production` Caddy'yi de çalıştırır.
5. `https://www.karakusaluminyum.com/health` adresinin `healthy` döndüğünü doğrulayın.

Admin şifresi yalnızca hesap ilk oluşturulurken okunur. Hesap oluştuktan sonra `.env` içinde değiştirmek etkisizdir.

## Yedekleme

`scripts/backup-postgres.sh` veritabanını, yüklenen görselleri ve kimlik anahtarlarını `backups/` klasörüne yazar. Yerel kopyalar `BACKUP_KEEP_DAYS` gün saklanır.

Günlük çalıştırmak için sunucuda `crontab -e` ile ekleyin (proje yolunu kendi yolunuzla değiştirin):

```
0 3 * * * cd /opt/karakus-aluminyum && scripts/backup-postgres.sh >> backups/backup.log 2>&1
```

Sunucu arızasına karşı yedeğin başka yerde de bulunması gerekir: rclone kurup bir hedef tanımlayın ve `.env` içinde `BACKUP_REMOTE` değerini verin.

Geri yükleme: `scripts/restore-postgres.sh backups/<veritabani>-<tarih>.dump`. Aynı tarihli görsel ve anahtar yedekleri de birlikte yüklenir.
