using KarakusAluminyum.Web.Models.Documents;

namespace KarakusAluminyum.Web.Services.Documents;

// Kurumsal sunumun başlangıç içeriği. Sıra, kurumsal teklif dosyalarındaki alışılmış akıştır:
// kapak, içindekiler, firma, neden biz, hizmetler, süreç, markalar, referanslar, projeler, garanti, iletişim.
// Rakam ve taahhüt içeren metinler bilerek genel tutuldu; panelden firmanın gerçek bilgileriyle güncellenir.
public static class PresentationSeed
{
    public static List<PresentationSection> Sections()
    {
        var order = 0;
        PresentationSection Section(string kind, string eyebrow, string title, string? body = null, string? image = null, params (string Title, string Text)[] items) => new()
        {
            Kind = kind, Eyebrow = eyebrow, Title = title, Body = body, ImagePath = image, SortOrder = order++,
            ItemsJson = DocumentJson.Serialize(items.Select(i => new PresentationItem { Title = i.Title, Text = i.Text }).ToList())
        };

        return new()
        {
            Section(PresentationKinds.Cover, "Kurumsal Tanıtım ve Teklif Dosyası", "Mimari alüminyum ve cam sistemlerinde projeye özel çözümler.",
                "Alüminyum doğrama, cam ve kapı sistemlerinde ölçüden montaja kadar projenizin yanındayız.", "/images/hero-villa.webp"),
            Section(PresentationKinds.Contents, "Dosya içeriği", "İçindekiler",
                "Bu dosyada firmamızı, hizmetlerimizi, çalışma şeklimizi ve tamamladığımız işlerden örnekleri bulabilirsiniz."),
            Section(PresentationKinds.About, "Hakkımızda", "{{kurulus_yili}}'den bu yana {{sehir}}'de, projeye odaklı çalışıyoruz.",
                "Karakuş Alüminyum, {{kurulus_yili}} yılından bu yana {{sehir}}'de mimari alüminyum doğrama, cam ve kapı sistemleri alanında hizmet veriyor. Konut, ticari yapı ve şantiye projelerinde sistem seçiminden imalata, montajdan teslimata kadar süreci tek elden yürütüyoruz.\n\nHer proje farklı ölçülerle ve farklı beklentilerle başlar. Açıklıktan donanıma, camdan montaj detayına kadar sistemi projenizin ihtiyaçlarına göre birlikte değerlendiriyoruz.",
                "/images/aluminyum-dograma.webp"),
            Section(PresentationKinds.WhyUs, "Neden Karakuş Alüminyum", "Her projede aynı özeni gösteriyoruz.", null, null,
                ("Projeye göre değerlendirme", "Her yapının ölçüsü, cephesi ve kullanım amacı farklıdır. Sistem önerimizi bu koşulları inceledikten sonra oluşturuyoruz."),
                ("Tek elden süreç", "Ölçü, çizim, imalat ve montaj aynı ekip tarafından takip edilir; tek muhatapla çalışırsınız."),
                ("Açık iletişim", "Ürün seçiminden uygulamaya kadar seçenekleri gerekçeleriyle paylaşıyor, kararları sizinle birlikte veriyoruz."),
                ("Uygulama öncesi netlik", "İmalata geçmeden önce ölçüleri ve detayları netleştiriyor, süreci onaylanan kararlar üzerinden planlıyoruz."),
                ("Geniş marka seçeneği", "Projenin bütçesine ve teknik beklentisine uygun profil, cam ve donanım markalarıyla çalışıyoruz."),
                ("Şantiye düzenine uyum", "İmalat ve montaj takvimini şantiye iş programına göre planlıyoruz.")),
            Section(PresentationKinds.Services, "Hizmetlerimiz", "Alüminyum, cam ve kapı sistemlerinde hizmet veriyoruz.",
                "Konuttan ticari yapıya, projenin ihtiyacına göre sistem öneriyor; imalat ve montajını üstleniyoruz."),
            Section(PresentationKinds.Process, "Çalışma süreci", "İlk görüşmeden teslimata kadar süreci birlikte yürütüyoruz.", null, null,
                ("Görüşme ve keşif", "Projenizi, kullanım amacını ve beklentilerinizi dinliyor, yerinde inceliyoruz."),
                ("Ölçü ve çizim", "Açıklıkları ölçüyor, pozları çiziyor ve sistem detaylarını belirliyoruz."),
                ("Sistem ve teklif", "Uygun sistem seçeneklerini ve fiyat teklifimizi paylaşıyoruz."),
                ("İmalat", "Onaylanan çizim ve ölçülere göre imalatı yapıyoruz."),
                ("Montaj", "Şantiye programına uygun olarak montajı ekibimizle gerçekleştiriyoruz."),
                ("Teslim", "Sistemleri kontrol edip çalışır halde teslim ediyoruz.")),
            Section(PresentationKinds.Brands, "Çalıştığımız sistemler", "Güvenilir profil ve sistem markalarıyla çalışıyoruz.",
                "Marka seçimini projenin bütçesine ve teknik beklentilerine göre birlikte yapıyoruz."),
            Section(PresentationKinds.References, "Referanslarımız", "Bize güvenen firmalar."),
            Section(PresentationKinds.Projects, "Projelerimiz", "Uygulamalarımızdan örnekler."),
            Section(PresentationKinds.Quality, "Kalite ve garanti", "Uygulamanın arkasında duruyoruz.",
                "Kullandığımız malzemeleri ve yaptığımız işi teslimden sonra da takip ediyoruz.", "/images/kapi-ve-pencere-sistemleri.webp",
                ("Malzeme garantisi", "Kullanılan profil, cam ve donanımlarda üretici firmaların garanti koşulları geçerlidir."),
                ("İşçilik", "Montaj ve işçilik garantisinin kapsamı ve süresi teklif ve sözleşmede ayrıca belirtilir."),
                ("Kesin ölçü", "İmalat, yerinde alınan kesin ölçülere göre yapılır."),
                ("Temiz teslim", "Montaj sonrası çalışma alanı toparlanır, sistemler kontrol edilerek teslim edilir.")),
            Section(PresentationKinds.Contact, "İletişim", "Projenizi birlikte değerlendirelim.",
                "Teklifimizle ilgili sorularınız ve revizyon talepleriniz için bize her zaman ulaşabilirsiniz.", "/images/cephe-koyu.webp")
        };
    }
}
