namespace KarakusAluminyum.Web.Models;

public sealed record ServicePage(
    string Slug,
    string Name,
    string ShortDescription,
    string Intro,
    string Image,
    string SeoTitle,
    string SeoDescription,
    IReadOnlyList<string> Benefits,
    IReadOnlyList<string> Faqs);

public static class ServiceCatalog
{
    public static readonly IReadOnlyList<ServicePage> All = new List<ServicePage>
    {
        new("aluminyum-dograma", 
            "Alüminyum Doğrama",
            "Konut ve ticari projelere uygun alüminyum doğrama çözümleri.",
            "Alüminyum doğrama sistemleri; tasarım, açıklık ölçüsü, kullanım yoğunluğu ve yalıtım hedefleri birlikte değerlendirilerek seçilmelidir. Karakuş Alüminyum ekibiyle projenizin ihtiyaçlarını görüşebilirsiniz.",
            "/images/aluminyum-dograma.png", "Alüminyum Doğrama | Karakuş Alüminyum",
            "İzmir'de alüminyum doğrama çözümleri. Projenize uygun sistem seçenekleri ve uygulama hakkında bilgi alın.",
            new[] { "Projeye göre sistem seçimi", "Modern mimariye uyumlu görünüm", "Kullanım ihtiyacına göre çözüm seçenekleri" },
            new[] { 
                "Alüminyum doğrama nasıl seçilir?|Açıklık ölçüleri, kullanım amacı, ısı ve ses yalıtımı hedefleri ile mimari tasarım birlikte değerlendirilmelidir.", 
                "Teklif için hangi bilgiler gerekir?|Proje konumu, yaklaşık ölçüler, çizimler ve istenen sistem hakkında bilgi paylaşmanız ilk değerlendirmeyi kolaylaştırır." 
                }
            ),
        new("surme-sistemler", 
            "Sürme Sistemler",
            "Geniş açıklıklar ve iç-dış mekân bağlantıları için sürme sistem seçenekleri.",
            "Sürme sistemler, teras, balkon ve bahçe bağlantılarında mekânı daha açık hissettiren çözümler sunabilir. Uygun sistem; kanat ölçüleri, ağırlık, eşik detayı ve kullanım beklentisine göre belirlenmelidir.",
            "/images/surme-sistem.png", "Alüminyum Sürme Sistemler | Karakuş Alüminyum",
            "Alüminyum sürme kapı ve pencere sistemleri hakkında bilgi alın. Projenize uygun seçenekleri değerlendirin.",
            new[] { 
                "Geniş cam açıklıklarına uygun seçenekler", 
                "Mimari çizgiyle uyum", 
                "Projeye göre eşik ve kanat değerlendirmesi" 
            },
            new[] { 
                "Sürme sistem hangi alanlarda kullanılır?|Teras, balkon, bahçe ve geniş açıklık istenen iç-dış mekân bağlantılarında değerlendirilebilir.", 
                "Her sürme sistem aynı performansı sağlar mı?|Hayır. Performans, seçilen sistemin teknik özelliklerine, ölçülere, cam kombinasyonuna ve montaj detaylarına bağlıdır." 
                }
            ),
        new("kapi-pencere-sistemleri", 
            "Kapı ve Pencere Sistemleri",
            "Günlük kullanıma ve mimari tasarıma uygun kapı-pencere çözümleri.",
            "Kapı ve pencere sistemlerinde görünüm kadar açılım tipi, donanım, güvenlik, hava-su sızdırmazlığı ve yalıtım beklentileri de önemlidir. Projeye uygun alternatifleri birlikte değerlendirin.",
            "/images/kapi-pencere-sistemleri.png", 
            "Alüminyum Kapı ve Pencere Sistemleri | Karakuş Alüminyum",
            "Alüminyum kapı ve pencere sistemleri. İzmir'deki projeniz için bilgi ve teklif alın.",
            new[] { 
                "Farklı açılım seçenekleri", 
                "Donanım ve aksesuar alternatifleri", 
                "Projeye göre sistem değerlendirmesi" 
                },
            new[] { 
                "Kapı ve pencere ölçüsü nasıl alınır?|Nihai ölçüler uygulama keşfi ve proje detayları doğrultusunda uzman ekip tarafından teyit edilmelidir.", 
                "Cam seçimi performansı etkiler mi?|Evet. Cam kombinasyonu, profil ve montajla birlikte ısı, ses ve güvenlik performansını etkileyebilir." 
                }
            ),
        new("giydirme-cephe", "Giydirme Cephe Sistemleri",
            "Ticari ve konut projelerinde cam-alüminyum cephe çözümleri.",
            "Giydirme cephe çözümleri mimari görünüm, yapı yüksekliği, rüzgâr yükleri, su tahliyesi, bakım ve ilgili teknik gereklilikler birlikte ele alınarak projelendirilmelidir.",
            "/images/cephe-sistemi.png", "Giydirme Cephe Sistemleri | Karakuş Alüminyum",
            "Cam ve alüminyum giydirme cephe sistemleri hakkında bilgi alın. Projenizin teknik gereksinimlerini görüşün.",
            new[] { 
                "Mimari cepheyle bütünleşen tasarım", 
                "Proje bazlı detay değerlendirmesi", 
                "Teknik gereksinimlere göre sistem seçimi" 
            },
            new[] { 
                "Giydirme cephe hangi yapılarda kullanılır?|Ticari binalar, ofisler ve tasarım gereksinimlerine uygun konut projelerinde kullanılabilir.", "Cephe sistemi nasıl belirlenir?|Mimari proje, statik gereklilikler, rüzgâr yükleri, su tahliyesi, bakım ve performans hedefleri değerlendirilir." }),
        new("cam-balkon", "Cam Balkon",
            "Balkon ve teras alanları için projeye uygun cam çözümleri.",
            "Cam balkon uygulamalarında mekânın geometrisi, kullanım amacı, mevcut yapı ve yerel koşullar dikkate alınmalıdır. Uygulama öncesinde ölçü ve montaj detaylarının yerinde kontrol edilmesi önemlidir.",
            "/images/cam-balkon.png", "Cam Balkon Sistemleri | Karakuş Alüminyum",
            "Cam balkon sistemleri ve uygulama seçenekleri. İzmir'deki projeniz için bilgi alın.",
            new[] { "Alan ölçüsüne göre değerlendirme", "Farklı kullanım senaryoları", "Uygulama detaylarının önceden planlanması" },
            new[] { "Cam balkon her balkona uygulanabilir mi?|Uygunluk; mevcut yapı, ölçüler, cephe koşulları ve yerel mevzuat dikkate alınarak belirlenmelidir.", "Fiyat nasıl belirlenir?|Ölçüler, sistem tipi, cam ve donanım seçenekleri ile montaj koşulları maliyeti etkiler." }),
        new("otomatik-kapi-ve-diger-sistemler", "Otomatik Kapı ve Diğer Sistemler",
            "Projenin kullanım senaryosuna göre otomatik kapı ve tamamlayıcı sistem seçenekleri.",
            "Otomatik kapı ve tamamlayıcı sistemlerde giriş yoğunluğu, açıklık ölçüsü, erişilebilirlik, güvenlik ve bakım ihtiyaçları değerlendirilmelidir. Bu kategoride yalnızca firmanızın fiilen sunduğu ürün ve hizmetleri yayınlayın.",
            "/images/otomatik-kapi.png", "Otomatik Kapı Sistemleri | Karakuş Alüminyum",
            "Otomatik kapı ve tamamlayıcı sistem seçenekleri hakkında bilgi alın. Projenize uygun çözüm için iletişime geçin.",
            new[] { "Kullanım yoğunluğuna göre seçim", "Giriş alanına uygun çözüm", "Bakım ve güvenlik gereksinimlerini değerlendirme" },
            new[] { "Otomatik kapı seçerken nelere dikkat edilir?|Geçiş yoğunluğu, açıklık, güvenlik sensörleri, erişilebilirlik ve bakım desteği değerlendirilmelidir.", "Mevcut kapıya otomasyon eklenebilir mi?|Uygunluk, mevcut kapı ve donanımın teknik özelliklerine göre keşif sırasında belirlenebilir." })
    };

    public static ServicePage? Find(string slug) =>
        All.FirstOrDefault(x => string.Equals(x.Slug, slug, StringComparison.OrdinalIgnoreCase));
}
