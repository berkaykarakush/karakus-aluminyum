# Eklenecek Özellikler
  1. ✅ https://ercom.com.tr/tr/urunler/ercom-smart/ercom-smart-enterprise-otomasyon programının tüm özelliklerinin projeye dahil edilmesi gerekiyor.
     - Yapıldı (PR #16, #17): sipariş/üretim takibi, seri kataloğu ve metraj, stoktan düşme, barkodlu etiketler ve istasyon takibi, barkod yazıcıları (ZPL/TSPL), kesim makinesi CSV aktarımı, fiyat listeleri ve döviz, tedarikçiye alış taslağı, kapasite.
  2. ✅ Tüm dosyalarda vs. tarih anlık olarak basılsın ancak tarih seçme seçeneği de olmalı.
  3. ✅ Ayrıca tüm çizimlerde vs. anlık olarak tarih basılsın ancak tarih değiştirme seçeneği de olmalı.
  4. ✅ Servis formu süreçleri için bitmiş projelerde servis talepleri, servis formu gibi müşteri bilgileri ve gerekli bilgilerin yer aldığı pdf yazdırma seçenği olan formlar olmalıdır.
  5. ✅ Projede takvim özelliği olmalı böylece hangi personel hangi gün hani projede çalışıyor takip edebileceğiz ayrıca hangi projede kaç gün kaç kişi çalışmış gibi metrikleri de takip edebileceğiz.
  6. ✅ Admin panelinde tasarım olarak eksikler var. Tasarımı gözden geçir ve daha modern sade hale getir.
  7. Daha fazla özellik önerin varsa buraya ekle.

     **Öncelik 1: yapılan işleri tamamlayan eksikler** (PR #18)
     - ✅ 7.1 Servis formunda kullanılan parçalar stoktan düşsün; ücretli servis tek tıkla satış faturası taslağına dönüşsün.
     - ✅ 7.2 Personel takviminden işçilik maliyeti: adam-gün × günlük ücret (yalnızca Admin/Muhasebe görür), proje analizine otomatik işçilik gideri.
     - ✅ 7.3 Profil sistemi (çizim) fiyatları da EUR/USD girilebilsin ve güncel kurla TL'ye çevrilsin (şu an döviz yalnızca stok kartlarında).
     - ✅ 7.4 Kritik stoğun altına düşen kartlar için otomatik malzeme siparişi önerisi (tedarikçiye alış taslağı).
     - ✅ 7.5 Montaj ekipleri (taşeronlar dahil) de personel takviminde görünsün.

     **Öncelik 2: müşteriyle iletişim**
     - ✅ 7.6 Müşteriye otomatik WhatsApp/SMS/e-posta: sipariş üretime girdi, sevk tarihi, montaj günü hatırlatması, servis ziyareti saati.
       - Yapıldı (PR #20): e-posta + WhatsApp (olay başına onaylı Meta şablonu gerekir); SMS kullanıcı kararıyla eklenmedi. Ayarlar: Site ayarları > Müşteri bildirimleri.
     - ✅ 7.7 Müşteri takip sayfası: QR/bağlantı ile sipariş durumunu görme ve doğrudan servis talebi açma.
       - Yapıldı (PR #21): /takip/{anahtar}; siparişte QR ve paylaşım, teslim tutanağında QR, e-postalarda bağlantı.
     **Kalan maddeler: B2C önceliğine göre yeniden sıralandı (24.11.2026)**
     İşlerin çoğu bireysel müşteri (ev sahibi) olduğu için öncelik; talebi teklife, teklifi işe çeviren adımlara, ödeme takibine ve satış sonrası tekrar işe verildi. Atölye içi maddeler müşteriyi doğrudan etkilemediği için sona alındı.

     **A: satış ve teklif (ciroya doğrudan etki)**
     - ✅ 7.11 Keşif / ölçü formu: sahada alınan ölçüler doğrudan çizim listesine aktarılsın. *(B2C'de her iş keşifle başlar; öne alındı)*
       - Ek: web talebinden tek tıkla müşteri kartı + keşif randevusu; müşteriye randevu bildirimi (7.6 altyapısı).
       - Yapıldı (PR #22): /yonetim/kesif; web talebinde "Keşif randevusu oluştur", telefonda ölçü girişi, çizim listesine aktarma, keşif formu.
     - ✅ 7.18 *(yeni)* Teklifin müşteri tarafından online onayı: QR'lı teklif sayfasında "Kabul ediyorum"; kabul edilince sipariş kendiliğinden açılsın.
       - Yapıldı (PR #22): SMS olmadığı için doğrulama kodu yerine ad soyad + onay kutusu; kanıt olarak zaman, IP, tarayıcı, tutar ve belge özeti saklanır.
     - ✅ 7.19 *(yeni)* Ödeme planı: kapora, ara ödeme, kalan bakiye ve vade hatırlatması; kalan tutar takip sayfasında görünsün.
       - Yapıldı (PR #22): siparişte ödeme planı, tahsilatı siparişe bağlama, vade hatırlatması (banka bilgisiyle), /yonetim/odemeler.
     - ✅ 7.15 Teklif kayıp nedeni (fiyat, süre, rakip) ve satış dönüşüm raporu; müşteri kaynağına göre (Instagram, Google, tavsiye, tabela) dönüşüm.
       - Yapıldı (PR #22): reddedilen teklifte kayıp nedeni ve rakip; Raporlar > Satış dönüşümü (huni, kaynak, kayıp nedenleri).
     - ✅ 7.23 *(yeni, küçük)* Yeni cari kartı varsayılan olarak "Bireysel" açılsın (şu an "Firma"); bireyselde vergi no yerine T.C. kimlik no alanı.
       - Yapıldı (PR #22).

     **B: satış sonrası ve tavsiye**
     - ✅ 7.8 Garanti bitmeden önce yıllık bakım hatırlatması ve tek tıkla bakım servis talebi. *(ev sahibinde tekrar iş kaynağı)*
       - Yapıldı (PR #22): yıldönümünde bakım + garanti bitmeden 30 gün önce hatırlatma; bağlantı takip sayfasında bakım talebini hazır açar.
     - ✅ 7.20 *(yeni)* Teslimden birkaç gün sonra Google yorum isteği (e-posta / WhatsApp); açık eksik veya servis talebi olan işe gönderilmesin.
       - Yapıldı (PR #22): Site ayarları > Müşteri bildirimleri > Google yorum bağlantısı yazılınca çalışır.
     - 7.10 Telefonda servis formu: fotoğraf ekleme ve ekranda müşteri imzası (kâğıtsız servis).
     - 7.21 *(yeni)* Bitmiş işin önce/sonra fotoğraflarını müşteri izniyle galeriye ve sosyal medya modülüne tek tıkla aktarma.

     **C: yasal ve altyapı**
     - 7.14 e-Arşiv / e-Fatura için GİB onaylı entegratör bağlantısı. *(bireysel müşteriye kesilen e-Arşiv fatura öncelikli)*
     - 7.22 *(yeni)* KVKK: bireysel müşteri verisi için silme / anonimleştirme talebi ve pazarlama iletişimi (yorum isteği, kampanya) için ayrı açık rıza kaydı.
     - 7.16 Veritabanı yedeğini panelden indirme ve yedek durumunu gösterme.
     - 7.17 Rol bazlı yetki ayrıntısı (ör. atölye kullanıcısı yalnızca barkod okut ve takvimi görsün).

     **D: atölye (müşteriyi doğrudan etkilemez, ihtiyaç oldukça)**
     - 7.9 Cam siparişini camcıya e-posta ile gönderme (cam listesi PDF ekli).
     - 7.12 Zebra Browser Print ile tarayıcıdan USB veya atölye ağındaki yazıcıya doğrudan etiket basma (sunucu atölye ağına erişemediği için).
     - 7.13 Kesim makinelerine özel dosya biçimleri (Özgenç, Kaban, Murat, Elumatec, Yılmaz): makinelerden örnek dosya gerekir.
  8. ✅ Projelerde cam çizim listesi, balkon korkulukları çizim listesi, doğrama çizim listesi gibi listeler mevcut olacak ancak mesela Cam kırıldı, kapı kolu eksik gibi eksik olan kısımları nasıl yazmak takip etmek gerekir.
     - Yapıldı (PR #19): siparişte "Eksikler ve kusurlar" bölümü (poz, tür, adet, sorumlu, hedef tarih; Bekleniyor / Giderildi), tüm işler için /yonetim/eksikler listesi, sorumluya göre basılan eksik listesi, açık eksiklerin teslim tutanağına yazılması.
