using System.Text.Json;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Data;

// Profesyonel başlangıç metinleri. Yalnızca eski varsayılan metinler yenilenir; yöneticinin yazdıkları korunur.
public static class ContentSeed
{
    public const string VersionKey = "ContentRefreshVersion";
    private const string Version = "2";

    private sealed record ServiceCopy(string Slug, string Short, string Intro, string[] Benefits, string SeoTitle, string SeoDescription, (string Q, string A)[] Faqs);

    private static readonly ServiceCopy[] Services =
    {
        new("aluminyum-dograma", "Konut ve ticari yapılarda pencere, kapı ve geniş açıklıklar için dayanıklı, ince profilli alüminyum doğrama çözümleri sunuyoruz.",
            "Alüminyum doğrama; ince profil görünümü, yüksek taşıyıcılığı ve uzun ömürlü yüzey seçenekleriyle modern mimarinin en çok tercih edilen sistemlerinden biridir. Geniş cam yüzeylere imkân verdiği için hem gün ışığını hem de manzarayı öne çıkarır.\n\nIsı yalıtımlı ve yalıtımsız profil seçeneklerini kullanım amacına ve yapının bulunduğu koşullara göre değerlendiriyoruz. Açılım tipi, cam kombinasyonu, renk ve donanım seçimlerini projenizin ihtiyaçlarına göre birlikte netleştiriyoruz.",
            new[] { "Isı yalıtımlı ve yalıtımsız profil seçenekleri", "Geniş açıklıklara uygun ince profil görünümü", "Elektrostatik toz boya ve anodize yüzey alternatifleri", "Açılır, vasistas, sürme ve kapı uygulamaları", "Yerinde ölçüye göre projelendirme" },
            "Alüminyum Doğrama İzmir | Karakuş Alüminyum", "İzmir'de konut ve ticari yapılar için ısı yalıtımlı alüminyum pencere, kapı ve doğrama sistemleri. Projeniz için ölçü ve teklif alın.",
            new[] { ("Isı yalıtımlı alüminyum doğrama ne zaman tercih edilmeli?", "Isıtılan veya soğutulan yaşam ve çalışma alanlarında enerji kaybını ve yoğuşma riskini azalttığı için genellikle ısı yalıtımlı sistem öneriyoruz. Yalıtımsız profilleri ise iç mekân bölmeleri gibi yalıtım beklentisi olmayan alanlarda değerlendiriyoruz."), ("Hangi renk seçenekleri uygulanabilir?", "Elektrostatik toz boya ile RAL kartelasındaki renkleri, anodize kaplama ile de metalik yüzeyleri uygulayabiliyoruz. Seçim yaparken mimari dili ve yapının çevre koşullarını birlikte ele alıyoruz."), ("Teklif için hangi bilgiler gerekir?", "Proje konumu, yaklaşık ölçüler, adetler, açılım tercihleri ve varsa mimari çizimler ilk değerlendirme için yeterlidir. Kesin ölçüleri uygulama öncesinde yerinde alıyoruz.") }),
        new("pvc-dograma", "Isı ve ses yalıtımı öncelikli konut projeleri için kolay bakımlı, ekonomik PVC kapı ve pencere çözümleri sunuyoruz.",
            "PVC doğrama, çok odacıklı profil yapısı sayesinde ısı ve ses yalıtımında güçlü bir seçenektir. Düzenli boya gerektirmemesi ve kolay temizlenmesi nedeniyle özellikle konut projelerinde yaygın olarak tercih edilir.\n\nProfil serisini, cam kombinasyonunu, rengi ve açılım tipini; açıklığın ölçüsüne, cephenin durumuna ve yalıtım beklentinize göre belirliyoruz. Büyük ölçülerde kanat ağırlığını ve profil dayanımını ayrıca değerlendiriyoruz.",
            new[] { "Çok odacıklı profillerle ısı ve ses yalıtımı", "Boya gerektirmeyen, kolay temizlenen yüzey", "Beyaz ve laminasyonlu renk seçenekleri", "Açılır, açılır-devrilir ve sürme uygulamalar", "Isıcam kombinasyonlarıyla uyumlu kullanım" },
            "PVC Doğrama İzmir | Karakuş Alüminyum", "İzmir'de ısı ve ses yalıtımlı PVC kapı ve pencere çözümleri. Profil, cam ve açılım seçeneklerini projenize göre birlikte belirleyelim.",
            new[] { ("PVC mi alüminyum mu tercih etmeliyim?", "Kararı; açıklık ölçüsüne, mimari görünüme, yalıtım beklentinize ve bütçenize göre birlikte veriyoruz. Bilgi Merkezi'ndeki karşılaştırma yazımız seçenekleri değerlendirmenize yardımcı olur."), ("Eski pencerelerim yenisiyle nasıl değiştirilir?", "Mevcut kasanın durumunu, duvar bağlantılarını ve ölçüleri yerinde inceliyoruz. Söküm, montaj ve yalıtım detaylarını bu incelemeye göre planlıyoruz."), ("Renkli PVC seçenekleri var mı?", "Beyazın yanı sıra laminasyon kaplamalı renk ve desen seçenekleri uygulayabiliyoruz. Güncel seçenekleri teklif aşamasında paylaşıyoruz.") }),
        new("otomatik-giyotin-camlama-sistemleri", "Teras, restoran ve geniş açıklıklar için motorlu, dikey hareketli giyotin cam sistemleri uyguluyoruz.",
            "Otomatik giyotin cam sistemleri, cam panelleri motor yardımıyla dikey yönde hareket ettirerek açık ve kapalı alan kullanımı arasında hızlı geçiş sağlar. Kapalı konumda rüzgâr ve yağmura karşı koruma sunarken açık konumda manzarayı ve hava akışını engellemez.\n\nÖzellikle teras, kafe, restoran ve villa uygulamalarında tercih edilir. Açıklık ölçüsünü, taşıyıcı yapıyı, elektrik altyapısını ve güvenlik donanımlarını uygulama öncesinde birlikte değerlendiriyoruz.",
            new[] { "Uzaktan kumanda ile motorlu kullanım", "Tek hamlede açılıp kapanan geniş cam yüzey", "Temperli veya lamine cam seçenekleri", "Engel algılama gibi güvenlik donanımı seçenekleri", "Teras ve işletmelerde dört mevsim kullanım" },
            "Otomatik Giyotin Cam Sistemleri İzmir | Karakuş Alüminyum", "Teras, restoran ve villalar için motorlu giyotin cam sistemleri. İzmir'de keşif ve projelendirme için Karakuş Alüminyum ile görüşün.",
            new[] { ("Giyotin cam hangi alanlarda kullanılır?", "Teras, balkon, kış bahçesi, restoran ve kafe gibi açık alanların kontrollü biçimde kapatılmak istendiği yerlerde kullanılır."), ("Elektrik kesintisinde sistem nasıl çalışır?", "Bu durum seçilen motor ve donanıma göre değişir. Kesinti senaryosunu ve varsa yedek güç veya manuel kullanım seçeneklerini projelendirme sırasında netleştiriyoruz."), ("Kurulum için ön hazırlık gerekir mi?", "Taşıyıcı yapıyı, zemini ve elektrik altyapısını yerinde inceliyoruz. Gerekli hazırlıkları teklif öncesinde tarafınıza bildiriyoruz.") }),
        new("manuel-giyotin-ruzgar-kirici-sistemleri", "Teras ve açık oturma alanlarında rüzgârı kesen, elle kolayca kullanılan dikey hareketli cam sistemleri uyguluyoruz.",
            "Manuel giyotin rüzgâr kırıcı sistemler, motor ve elektrik altyapısı gerektirmeden dikey hareketli cam panellerle açık alanları korur. Rüzgârı keserken görüş açısını korur ve mekânın kullanım süresini uzatır.\n\nBalkon, teras ve işletme önü gibi alanlarda tercih edilir. Panel yüksekliğini, cam kalınlığını ve zemin bağlantısını; alanın ölçülerine ve maruz kaldığı rüzgâr koşullarına göre belirliyoruz.",
            new[] { "Motor ve elektrik gerektirmeyen kullanım", "Manzarayı kapatmayan cam yüzey", "İhtiyaca göre yükseklik ayarı", "Teras ve işletme önlerine uygun çözüm", "Temperli cam seçenekleri" },
            "Manuel Giyotin ve Rüzgâr Kırıcı Sistemler | Karakuş Alüminyum", "Teras ve açık oturma alanları için manuel giyotin cam ve rüzgâr kırıcı sistemler. İzmir'de ölçü ve teklif için bize ulaşın.",
            new[] { ("Manuel sistem ile otomatik sistem arasındaki fark nedir?", "Manuel sistemde paneller elle hareket ettirilir ve elektrik altyapısı gerekmez. Otomatik sistemde ise hareket motorla sağlanır, daha büyük açıklıklarda kullanım kolaylığı sunar."), ("Rüzgârlı bölgelerde kullanılabilir mi?", "Evet, ancak cam kalınlığı, panel ölçüsü ve bağlantı detayları bölgenin rüzgâr koşullarına göre seçilmelidir. Bu nedenle uygulama öncesinde yerinde değerlendirme yapıyoruz."), ("Balkonlarda uygulanabilir mi?", "Balkonun korkuluğu, taşıyıcı yapısı ve yönetim planı koşullarına bağlı olarak uygulanabilir.") }),
        new("katlanir-cam-sistemleri", "Balkon, teras ve işletmelerde açıklığı tamamen serbest bırakan katlanır cam sistemleri uyguluyoruz.",
            "Katlanır cam sistemlerinde paneller birbirine paralel biçimde katlanarak açıklığın bir veya iki yanında toplanır. Böylece balkon ya da teras, açıldığında neredeyse tamamen serbest kalır; kapatıldığında ise rüzgâr, yağmur ve tozdan korunur.\n\nCam balkon uygulamalarının en yaygın çözümlerinden biridir. Isıcamlı veya tek camlı seçenekleri, eşik tipini ve panel sayısını; kullanım amacına ve açıklığın ölçüsüne göre belirliyoruz.",
            new[] { "Tamamen açılabilen geniş yüzey", "Tek cam ve ısıcam seçenekleri", "Düşük eşikli uygulama alternatifleri", "Balkon, teras ve işletmelere uygun kullanım", "Temperli güvenlik camı" },
            "Katlanır Cam ve Cam Balkon Sistemleri İzmir | Karakuş Alüminyum", "Balkon ve teraslar için katlanır cam sistemleri. Isıcamlı ve tek camlı seçeneklerle İzmir'de cam balkon için teklif alın.",
            new[] { ("Cam balkon için yönetim izni gerekir mi?", "Dış görünümü etkileyen uygulamalar için apartman veya site yönetim planı ile kat malikleri kararlarının kontrol edilmesi gerekir. Gerektiğinde hukuki görüş almanızı öneririz."), ("Tek cam mı ısıcam mı seçmeliyim?", "Alan yalnızca rüzgâr ve yağmurdan korunacaksa tek cam yeterli olabilir. Yaşam alanının parçası gibi kullanılacaksa ısıcam daha uygun bir seçenektir."), ("Katlanır ile sürme sistem arasında nasıl seçim yapılır?", "Tam açıklık isteniyorsa katlanır, ray üzerinde pratik kullanım ve panellerin dışa taşmaması öncelikliyse sürme sistemi öneriyoruz.") }),
        new("yana-kayar-surme-cam-sistemleri", "Geniş açıklıklarda iç ve dış mekânı birleştiren, ray üzerinde yatay hareket eden sürme cam sistemleri uyguluyoruz.",
            "Yana kayar sürme sistemler, cam panellerin ray üzerinde yatay hareket etmesiyle çalışır. Paneller açılırken mekâna taşmadığı için alan kaybı oluşturmaz; bu özelliğiyle salon-teras geçişleri, kış bahçeleri ve geniş cepheler için uygundur.\n\nPanel sayısı ve ray adedi, ihtiyaç duyulan açıklık genişliğini belirler. Isı yalıtımlı profili, eşik detayını ve kilit donanımını yapının kullanım amacına göre birlikte seçiyoruz.",
            new[] { "Açılırken alan kaplamayan kullanım", "Geniş cam yüzey ve bol gün ışığı", "İki veya daha fazla raylı seçenekler", "Isı yalıtımlı profil alternatifleri", "İç ve dış mekân arasında akıcı geçiş" },
            "Sürme Cam Sistemleri İzmir | Karakuş Alüminyum", "Geniş açıklıklar, salon-teras geçişleri ve kış bahçeleri için yana kayar sürme cam sistemleri. İzmir'de projelendirme ve teklif.",
            new[] { ("Sürme sistem ne kadar açıklık sağlar?", "Açıklık; panel ve ray sayısına bağlıdır. Örneğin iki raylı iki panelli sistemde paneller üst üste geldiği için genişliğin yaklaşık yarısı açılır."), ("Eşik yüksekliği düşürülebilir mi?", "Sistem ve yalıtım beklentisine bağlı olarak düşük eşikli çözümleri değerlendirebiliyoruz. Su tahliyesi ve zemin detayı bu kararda belirleyicidir."), ("Sürme sistemlerde ısı yalıtımı nasıl sağlanır?", "Isı bariyerli profilleri, ısıcamı ve uygun fırça/conta detaylarını birlikte seçerek sağlıyoruz.") }),
        new("otomatik-tente", "Teras, balkon ve işletme önleri için motorlu kumaş tentelerle kontrollü gölgelendirme sağlıyoruz.",
            "Otomatik tenteler, motorlu mekanizmayla açılıp kapanarak dış mekânda ihtiyaç duyulan anda gölge sağlar. Teras, balkon, kafe ve restoran gibi alanların yaz aylarındaki kullanım konforunu artırır.\n\nAçılım ölçüsünü, kumaş tipini ve duvar bağlantısını alanın özelliklerine göre belirliyoruz. Rüzgâra açık cephelerde sistemin kullanım koşullarını ve varsa sensör seçeneklerini ayrıca değerlendiriyoruz.",
            new[] { "Uzaktan kumanda ile motorlu kullanım", "Teras ve işletmeler için geniş gölge alanı", "Farklı kumaş ve renk seçenekleri", "Kullanılmadığında toplanabilen yapı", "Cepheye uygun bağlantı detayları" },
            "Otomatik Tente Sistemleri İzmir | Karakuş Alüminyum", "Teras, balkon, kafe ve restoranlar için motorlu otomatik tente sistemleri. İzmir'de ölçü ve teklif için bize ulaşın.",
            new[] { ("Tente rüzgârda açık bırakılabilir mi?", "Kumaş tenteler rüzgârlı havalarda toplanmalıdır. Rüzgâra açık cephelerde sensör seçeneklerini de değerlendiriyoruz."), ("Kumaş seçimi neye göre yapılır?", "Güneş alma durumunu, renk tercihinizi ve kullanım yoğunluğunu birlikte değerlendiriyoruz."), ("Her cepheye montaj yapılabilir mi?", "Montaj; duvarın taşıyıcılığına ve açılım ölçüsüne bağlıdır. Bu nedenle yerinde inceleme yapıyoruz.") }),
        new("fotoselli-doner-kapi", "Mağaza, ofis, otel ve sağlık yapılarının girişleri için fotoselli ve döner kapı sistemleri sunuyoruz.",
            "Fotoselli ve döner kapılar, yoğun geçiş olan girişlerde konforlu kullanım sağlar ve iç mekân ile dış ortam arasındaki hava geçişini azaltmaya yardımcı olur. Sensörler sayesinde temas gerektirmeden çalışır.\n\nKapı tipini geçiş yoğunluğuna, giriş genişliğine ve yapının kullanım amacına göre seçiyoruz. Engelli erişimini, acil durum kullanımını ve güvenlik sensörlerini projelendirmede birlikte ele alıyoruz.",
            new[] { "Temassız ve konforlu geçiş", "Yoğun kullanımlı girişlere uygun yapı", "Tek ve çift kanatlı sürme seçenekleri", "Güvenlik sensörleri", "Bakım ve kullanım planlaması" },
            "Fotoselli ve Döner Kapı Sistemleri İzmir | Karakuş Alüminyum", "Mağaza, ofis, otel ve hastane girişleri için fotoselli ve döner kapı sistemleri. İzmir'de keşif ve teklif için bize ulaşın.",
            new[] { ("Fotoselli kapı mı döner kapı mı seçilmeli?", "Sürme fotoselli kapılar birçok giriş için pratik bir çözümdür. Döner kapılar ise daha geniş alan ister ve hava geçişinin sınırlanmasının önemli olduğu girişlerde değerlendirilir."), ("Elektrik kesintisinde ne olur?", "Bu davranış seçilen donanıma göre değişir. Yedek güç ve acil durum kullanımını projelendirme sırasında planlıyoruz."), ("Düzenli bakım gerekir mi?", "Evet. Otomatik kapıların güvenli çalışması için sensör ve mekanizmaların periyodik olarak kontrol edilmesi gerekir.") }),
        new("sarmal-kepenk-ve-panjur-sistemleri", "Mağaza, garaj ve pencere açıklıkları için güvenlik, karartma ve yalıtım sağlayan sarmal kepenk ve panjur sistemleri sunuyoruz.",
            "Sarmal kepenk ve panjur sistemleri, kapı ve pencere açıklıklarını kutu içine sarılan lameller yardımıyla kapatır. Panjurlar güneş kontrolü, karartma ve ek konfor sağlarken kepenkler özellikle işyeri ve garaj girişlerinde güvenlik amacıyla tercih edilir.\n\nKullanım motorlu veya manuel olabilir. Lamel tipini, kutu ölçüsünü ve yerleşimi; açıklığın boyutuna ve kullanım sıklığına göre belirliyoruz.",
            new[] { "Motorlu ve manuel kullanım seçenekleri", "Güneş kontrolü ve karartma", "İşyeri ve garaj girişlerinde güvenlik", "Doğrama ile uyumlu renk seçenekleri", "Açıklığa uygun lamel ve kutu seçimi" },
            "Sarmal Kepenk ve Panjur Sistemleri İzmir | Karakuş Alüminyum", "Mağaza, garaj ve pencereler için motorlu veya manuel sarmal kepenk ve panjur sistemleri. İzmir'de ölçü ve teklif alın.",
            new[] { ("Panjur ile kepenk arasındaki fark nedir?", "Panjur daha çok pencerelerde güneş kontrolü ve konfor için, kepenk ise işyeri ve garaj girişlerinde güvenlik için kullanılır."), ("Mevcut pencereye panjur eklenebilir mi?", "Çoğu durumda dış cepheye veya pervaz içine uygulanabilir. Kutu yerleşimini yerinde inceleyerek belirliyoruz."), ("Motorlu sisteme sonradan geçilebilir mi?", "Sistemin yapısına ve ölçüsüne bağlıdır. Uygun olduğunda motorlu kullanıma dönüştürülebilir.") }),
        new("ofis-bolme-sistemleri", "Açık ofisleri işlevsel alanlara ayıran, ferah görünümlü cam ve panel bölme sistemleri uyguluyoruz.",
            "Ofis bölme sistemleri, mekânı kalıcı inşaat işine gerek kalmadan toplantı odaları, yönetici ofisleri ve çalışma alanlarına ayırır. Cam yüzeyler gün ışığının mekâna yayılmasını sağlarken ferah bir görünüm oluşturur.\n\nCam ve dolu panel oranını, kapı tipini ve ses beklentisini kullanım senaryosuna göre belirliyoruz. Tavan, zemin ve elektrik detaylarını uygulama öncesinde birlikte değerlendiriyoruz.",
            new[] { "Gün ışığını koruyan cam yüzeyler", "Cam ve dolu panel kombinasyonları", "Kurumsal kimliğe uygun görünüm", "Toplantı odası ve yönetici ofisi çözümleri", "Mekân ihtiyacına göre planlama" },
            "Ofis Bölme Sistemleri İzmir | Karakuş Alüminyum", "Toplantı odası, yönetici ofisi ve açık ofisler için cam ve panel bölme sistemleri. İzmir'de ölçü ve teklif için bize ulaşın.",
            new[] { ("Cam bölmeler ses yalıtımı sağlar mı?", "Ses performansı; cam tipi, kalınlık ve birleşim detaylarına bağlıdır. Beklentinizi projelendirme aşamasında dikkate alıyoruz."), ("Yasal bir izin gerekir mi?", "Yapının kullanım durumuna göre bina yönetimi veya yangın kuralları gibi konuların kontrol edilmesi gerekebilir."), ("Var olan ofiste uygulanabilir mi?", "Evet. Tavan, zemin ve mevcut tesisat durumunu inceleyerek çalışma düzeninize uygun planlıyoruz.") }),
        new("endustriyel-ve-garaj-kapi-sistemleri", "Fabrika, depo ve konutlar için seksiyonel, sarmal ve hızlı açılan endüstriyel ve garaj kapıları sunuyoruz.",
            "Endüstriyel kapılar, sevkiyat ve üretim alanlarında güvenli ve kontrollü geçiş sağlar. Garaj kapıları ise konut ve site otoparklarında konforlu kullanım sunar. Seksiyonel, sarmal ve hızlı açılan tipleri kullanım yoğunluğuna ve açıklık ölçüsüne göre seçiyoruz.\n\nAçıklığın geometrisini, tavan yüksekliğini, otomasyon ve güvenlik donanımlarını uygulama öncesinde değerlendiriyoruz.",
            new[] { "Seksiyonel ve sarmal kapı seçenekleri", "Motorlu ve uzaktan kumandalı kullanım", "Yoğun kullanıma uygun çözümler", "Güvenlik donanımı seçenekleri", "Açıklığa göre projelendirme" },
            "Endüstriyel ve Garaj Kapıları İzmir | Karakuş Alüminyum", "Fabrika, depo, site ve konutlar için seksiyonel ve sarmal endüstriyel kapı ile garaj kapısı sistemleri. İzmir'de teklif alın.",
            new[] { ("Seksiyonel kapı için ne kadar tavan payı gerekir?", "Gereken pay kapının ölçüsüne ve ray tipine bağlıdır. Yerinde ölçüde lento ve yan boşlukları kontrol ediyoruz."), ("Mevcut garaj kapısı otomatiğe çevrilebilir mi?", "Kapının yapısı ve durumu uygunsa motor eklenebilir. Bu değerlendirmeyi yerinde yapıyoruz."), ("Kapıda hangi güvenlik önlemleri olmalı?", "Engel algılama, acil durum kullanımı ve düzenli bakım temel konulardır. Kapsamı kapının tipine göre belirliyoruz.") }),
        new("rampa-ve-koruk-sistemleri", "Yükleme alanlarında araç ile bina arasında güvenli ve verimli geçiş sağlayan rampa ve körük sistemleri sunuyoruz.",
            "Yükleme rampaları, araç kasası ile depo zemini arasındaki kot farkını gidererek forklift ve transpalet geçişini kolaylaştırır. Körükler ise yükleme sırasında araç ile bina arasını kapatarak iç ortamı dış etkilerden korumaya yardımcı olur.\n\nAraç tiplerini, yük kapasitesini, kullanım sıklığını ve yükleme alanının yerleşimini değerlendirerek uygun sistemi belirliyoruz.",
            new[] { "Araç ile depo arasında kot farkı çözümü", "Forklift ve transpalet geçişine uygunluk", "Dış etkilere karşı körük ile koruma", "Yükleme kapılarıyla bütünleşik planlama", "Kapasiteye göre seçim" },
            "Yükleme Rampası ve Körük Sistemleri İzmir | Karakuş Alüminyum", "Depo ve fabrikalar için yükleme rampası ve körük sistemleri. Araç tipi ve kapasiteye uygun çözüm için İzmir'de bizimle görüşün.",
            new[] { ("Rampa seçiminde hangi bilgiler gerekir?", "Yanaşan araç tipleri, kasa yükseklikleri, yük kapasitesi ve günlük kullanım sıklığı temel bilgilerdir."), ("Körük her rampada gerekli midir?", "Zorunlu değildir, ancak iç ortam sıcaklığının veya temizliğin önemli olduğu tesislerde faydalıdır."), ("Mevcut tesise sonradan uygulanabilir mi?", "Zemin ve yerleşim durumuna bağlıdır. Gerekli hazırlıkları yerinde inceleyerek belirliyoruz.") }),
        new("bahce-kapisi-ve-bariyer-sistemleri", "Site, işletme ve villa girişlerinde araç ve yaya geçişini kontrol eden bahçe kapısı ve bariyer sistemleri sunuyoruz.",
            "Bahçe kapıları ve bariyerler, giriş çıkışların kontrollü ve düzenli olmasını sağlar. Yana kayar ve kanatlı kapılar site ve villa girişlerinde, bariyerler ise otopark ve işletme girişlerinde yaygın olarak kullanılır.\n\nGeçiş yoğunluğunu, giriş genişliğini, zemin durumunu ve erişim kontrol yöntemini (kumanda, kart veya plaka tanıma gibi) birlikte değerlendiriyoruz.",
            new[] { "Yana kayar ve kanatlı kapı seçenekleri", "Otopark ve site girişleri için bariyer", "Uzaktan kumanda ve geçiş kontrolü entegrasyonu", "Güvenlik fotoselleri", "Giriş düzenine göre planlama" },
            "Bahçe Kapısı ve Bariyer Sistemleri İzmir | Karakuş Alüminyum", "Site, işletme ve villa girişleri için otomatik bahçe kapısı ve bariyer sistemleri. İzmir'de keşif ve teklif için bize ulaşın.",
            new[] { ("Yana kayar mı kanatlı kapı mı seçilmeli?", "Yana kayar kapı duvar boyunca boş alan ister, kanatlı kapı ise açılım alanı gerektirir. Girişin yerleşimi bu seçimde belirleyicidir."), ("Geçiş kontrol sistemine bağlanabilir mi?", "Çoğu sistem kart, kumanda veya benzeri geçiş kontrol yöntemleriyle birlikte kullanılabilir."), ("Elektrik kesintisinde kapı nasıl açılır?", "Motorun tipine göre manuel açma düzeneği veya yedek güç seçeneği planlıyoruz.") }),
    };

    private static readonly string[] OldBenefits = { "Projeye göre değerlendirme", "Kullanım ihtiyacına uygun sistem seçimi", "Uygulama öncesi ölçü ve detayların değerlendirilmesi" };
    private static readonly (string Q, string A)[] OldFaqs =
    {
        ("Sistem seçimi nasıl yapılır?", "Ölçüler, kullanım amacı, uygulama koşulları ve beklentiler değerlendirilerek uygun seçenekler belirlenir."),
        ("Teklif almak için hangi bilgiler gerekir?", "Proje konumu, yaklaşık ölçüler, varsa çizimler ve istenen sistem bilgileri ilk değerlendirmeyi kolaylaştırır.")
    };

    private static string OldIntro(string description) => description + " Uygun sistem seçimi; alanın ölçüleri, kullanım amacı ve uygulama koşulları değerlendirilerek yapılmalıdır. Projeniz için detay ve teklif almak üzere bizimle iletişime geçebilirsiniz.";
    private static string OldSeoDescription(string name) => "İzmir'de " + name.ToLowerInvariant() + " çözümleri. Projeniz hakkında bilgi ve teklif almak için Karakuş Alüminyum ile iletişime geçin.";

    // Eski varsayılan ayarlar: yalnızca bu değerlerdeyse yenilenir.
    private static readonly Dictionary<string, string> OldSettings = new()
    {
        ["HeroText"] = "Alüminyum doğrama, sürme sistemler, cephe ve tamamlayıcı mimari çözümler. Projenizin ihtiyaçlarını birlikte değerlendirelim.",
        ["ApproachTitle"] = "Detaylara önem veren, projeye odaklı yaklaşım.",
        ["ApproachText"] = "Her proje farklı ölçülerle ve farklı beklentilerle başlar. Açıklıktan donanıma, camdan montaj detayına kadar sistemi projenizin ihtiyaçlarına göre birlikte değerlendiriyoruz.",
        ["CtaText"] = "İhtiyacınızı anlatın, ekibimizle iletişime geçin.",
        ["AboutIntro"] = "İzmir’de mimari alüminyum sistemleri alanında, projeye özel çözümlerle çalışıyoruz.",
        ["AboutText"] = "Her proje farklı ölçülerle ve farklı beklentilerle başlar. Sistem seçiminden uygulama detaylarına kadar süreci ihtiyaçlarınıza göre birlikte değerlendiriyoruz.",
        ["FooterText"] = "Mimari projeler için estetik, işlevsel ve uzun ömürlü alüminyum çözümleri.",
        ["SiteDescription"] = "İzmir'de alüminyum doğrama, sürme sistemler, kapı ve pencere, giydirme cephe, cam balkon ve otomatik kapı çözümleri sunan mimari alüminyum firması."
    };

    public const string HomeFaqQuestion = "Alüminyum doğrama nasıl seçilir?";
    public const string HomeFaqAnswer = "Açıklık ölçüsünü, kullanım amacını, mimari tasarımı, yalıtım beklentisini ve donanım seçeneklerini birlikte değerlendiriyoruz. Bilgi Merkezi yazılarımız seçenekleri karşılaştırmanıza yardımcı olur.";

    private sealed record Article(string Slug, string Title, string Summary, string Image, string SeoDescription, string Content);

    private static readonly Article[] Articles =
    {
        new("aluminyum-mu-pvc-mi-dograma-secimi", "Alüminyum mu PVC mi? Doğrama Seçerken Dikkate Alınması Gerekenler", "Alüminyum ve PVC doğrama arasındaki farkları; yalıtım, dayanım, görünüm ve kullanım alanı açısından karşılaştırıyoruz.", "/images/aluminyum-dograma.png", "Alüminyum ve PVC doğrama farkları: yalıtım, dayanım, görünüm ve bakım açısından hangi sistemin projenize uygun olduğunu öğrenin.",
"""
Yeni bir yapıda veya tadilatta doğrama seçimi, uzun yıllar birlikte yaşanacak bir karardır. En sık sorulan soru da bellidir: alüminyum mu, PVC mi? Doğru cevap projeye göre değişir. Bu yazıda iki malzemeyi seçimi etkileyen başlıklar üzerinden karşılaştırıyoruz.

## Dayanım ve açıklık ölçüsü

Alüminyum, ağırlığına göre yüksek dayanımlı bir malzemedir. Bu nedenle ince profil kesitleriyle geniş camları taşıyabilir. Büyük sürme sistemler, yüksek kapılar ve geniş cephe açıklıkları için öne çıkar.

PVC profillerde dayanım, profil içine yerleştirilen çelik takviyeyle desteklenir. Standart konut pencerelerinde gayet yeterlidir. Ancak çok büyük ölçülerde kanat ağırlığı ve profil genişliği sınırlayıcı olabilir.

## Isı ve ses yalıtımı

PVC, doğası gereği ısıyı az ileten bir malzemedir ve çok odacıklı yapısıyla iyi bir yalıtım sağlar. Alüminyum ise ısıyı iyi ilettiği için konut uygulamalarında **ısı bariyerli** profiller kullanılmalıdır. Bu profillerde iç ve dış alüminyum kesitler, ısı geçişini azaltan poliamid çubuklarla birbirinden ayrılır.

Unutulmaması gereken nokta şudur: pencerenin yüzeyinin büyük bölümü camdır. Bu yüzden yalıtım performansı yalnızca profile değil, seçilen ısıcama ve montaj kalitesine de bağlıdır.

## Görünüm ve renk

Alüminyumun ince kesitleri daha geniş cam yüzeyi ve modern bir görünüm sunar. Elektrostatik toz boya ile geniş bir renk yelpazesi, anodize kaplama ile metalik yüzeyler elde edilebilir.

PVC'de beyaz en yaygın seçenektir. Laminasyon kaplamalarla renkli ve ahşap desenli yüzeyler de uygulanabilir.

## Bakım ve kullanım ömrü

Her iki malzeme de boya gerektirmez ve doğru kullanımda uzun ömürlüdür. Rutin bakım; yüzeylerin temizlenmesi, contaların kontrolü ve donanımın ayarlanmasından oluşur. Kıyıya yakın bölgelerde alüminyum için doğru yüzey işlemi, PVC için de güneş ışığına dayanıklı kaliteli profil seçimi önem kazanır.

## Hangisi nerede daha uygun?

- **Alüminyum:** geniş açıklıklar, büyük sürme sistemler, ticari yapılar ve ince profil görünümü istenen mimari projeler.
- **PVC:** yalıtımın ve bütçenin öncelikli olduğu konutlar, standart ölçülü pencereler.
- **İkisi birlikte:** aynı yapıda geniş açıklıklarda alüminyum, diğer pencerelerde PVC kullanmak da mümkündür; bu durumda renk uyumu önceden planlanmalıdır.

## Maliyeti doğru karşılaştırmak

Birim fiyat tek başına yanıltıcı olabilir. İki teklifi karşılaştırırken şu kalemlerin aynı kapsamda olup olmadığına bakmanızı öneriyoruz:

- Profil serisi ve varsa ısı bariyeri genişliği,
- Cam kombinasyonu (Low-E, argon, temperli),
- Donanım markası ve açılım tipleri,
- Söküm, montaj, dış ve iç bitiş işleri,
- Nakliye ve iskele gibi şantiye giderleri.

Yalnızca malzeme fiyatına değil, kullanım süresi boyunca enerji tüketimine ve bakım ihtiyacına da bakmanın daha sağlıklı bir karar sağlayacağını düşünüyoruz.

## Tadilatta dikkat edilecekler

Mevcut pencere değişiminde söküm sırasında duvar kenarlarının zarar görmesi, iç sıva ve dış mantolama detayları göz önünde bulundurulmalıdır. Yeni kasanın mevcut açıklığa oturması için ölçü, söküm yapılacak duvar durumuna göre alınmalıdır. Mantolama yapılacak bir binada pencerenin konumunu ve dış denizlik detayını yalıtım işiyle birlikte planlamanızı öneriyoruz; aksi durumda birleşim noktaları ısı ve su açısından zayıf kalabilir.

Yeni yapılarda ise doğrama seçiminin mümkün olduğunca erken aşamada yapılmasını öneriyoruz. Açıklık ölçüleri, lento kotları ve panjur kutusu gibi detaylar inşaat sürecine doğrudan etki eder.

## Sonuç

Bize göre tek bir "en iyi" malzeme yoktur. Açıklık ölçüleri, mimari dil, yalıtım beklentisi ve bütçe birlikte değerlendirildiğinde doğru seçim netleşir. [Alüminyum doğrama](/urunler/aluminyum-dograma) ve [PVC doğrama](/urunler/pvc-dograma) sayfalarımızdan seçenekleri inceleyebilir, projeniz için [bizimle iletişime geçebilirsiniz](/iletisim).
"""),
        new("isi-yalitimli-aluminyum-dograma-nedir", "Isı Yalıtımlı Alüminyum Doğrama Nedir, Nasıl Çalışır?", "Isı bariyeri, poliamid çubuk ve Uf değeri gibi kavramları sade bir dille açıklıyor, doğru seçim için kontrol listesi sunuyoruz.", "/images/kapi-ve-pencere-sistemleri.png", "Isı yalıtımlı alüminyum doğramanın çalışma prensibi, ısı bariyeri, Uf ve Uw değerleri ve yoğuşmayı azaltmak için dikkat edilmesi gerekenler.",
"""
Alüminyum dayanıklı ve şekil verilebilir bir malzemedir, ancak ısıyı iyi iletir. Yalıtımsız bir profilde dış ortamın sıcaklığı içeriye kolayca taşınır; kışın iç yüzeyin soğuması yoğuşmaya ve enerji kaybına yol açabilir. Isı yalıtımlı sistemler bu sorunu çözmek için geliştirilmiştir. Bu yazıda çalışma prensibini ve seçimde dikkat ettiğimiz noktaları sade bir dille anlatıyoruz.

## Isı bariyeri nedir?

Isı yalıtımlı profillerde iç ve dış alüminyum kesitler, **poliamid** çubuklarla birbirine bağlanır. Poliamid, alüminyuma göre ısıyı çok daha az iletir. Böylece iç ve dış yüzey arasında bir ısı köprüsü oluşması büyük ölçüde engellenir. Çubuk genişliği ve profilin iç tasarımı arttıkça yalıtım performansı genellikle yükselir.

## Değerleri nasıl okumalı?

Teklif ve teknik belgelerde şu kavramlarla karşılaşırsınız. Tümünde değer ne kadar **düşükse** yalıtım o kadar iyidir (birim: W/m²K).

- **Uf:** yalnızca çerçevenin (profilin) ısı geçirgenliği.
- **Ug:** yalnızca camın ısı geçirgenliği.
- **Uw:** profil, cam ve kenar detayları birlikte hesaplanan pencerenin toplam değeri.

Karşılaştırma yaparken asıl Uw değerine bakmanızı öneriyoruz. Çünkü iyi bir profil, zayıf bir camla birlikte beklenen sonucu vermez. Türkiye'de binalardaki ısı yalıtımı kuralları **TS 825** standardıyla düzenlenir; pencere ve dış kapılar için ürün standardı ise **TS EN 14351-1**'dir.

## Performansı belirleyen diğer unsurlar

- **Cam:** ısıcamda boşluk kalınlığı, gaz dolgusu ve düşük salımlı (Low-E) kaplama sonucu doğrudan etkiler.
- **Fitil ve contalar:** hava sızdırmazlığını sağlar, kalitesi ve doğru montajı önemlidir.
- **Montaj:** kasa ile duvar arasındaki boşluğun doğru şekilde yalıtılmaması, iyi bir sistemin performansını düşürür.

## Yoğuşma tamamen önlenir mi?

Yoğuşma yalnızca pencereyle ilgili değildir. İç mekândaki nem oranı, havalandırma alışkanlığı ve duvar yalıtımı da etkilidir. Isı yalıtımlı sistem iç yüzey sıcaklığını yükselterek riski azaltır. Yine de düzenli havalandırmayı ihmal etmemenizi öneriyoruz.

## Isı bariyerli sürme sistemlerde durum

Geniş sürme sistemlerde kanatlar hareket ettiği için sızdırmazlık, açılır pencerelere göre daha zordur. Fırça ve conta detayları, ray sayısı ve eşik tasarımı toplam performansı belirler. Yüksek yalıtım beklentisi olan yapılarda kaldır-sür (lift-and-slide) tipi sistemler değerlendirilebilir; kanat kapatıldığında contalara oturarak sızdırmazlığı artırır.

## Montaj detayı neden kritik?

Kasa ile duvar arasındaki birleşim, yalıtımın en sık zayıfladığı noktadır. Boşluğun yalıtım malzemesiyle doldurulmasını, dış tarafta su geçirmez ve iç tarafta buhar geçişini sınırlayan birleşim bantları kullanılmasını öneriyoruz. Denizlik eğimi ve damlalık detayı, suyun cepheye zarar vermeden uzaklaşmasını sağlar.

## Renk seçimi yalıtımı etkiler mi?

Renk, ısı bariyerinin çalışma prensibini değiştirmez. Ancak koyu renk yüzeyler güneşte daha fazla ısınır. Bu nedenle güneş alan büyük cephelerde profilin ısıl genleşmesi ve iç ortam konforu birlikte düşünülmelidir. Doğru sistem seçildiğinde koyu renkler de güvenle kullanılabilir.

## Seçim için kısa kontrol listesi

- Teklifte profil serisi ve ısı bariyeri bilgisi belirtilmiş mi?
- Uw değeri ve hesaplandığı pencere ölçüsü paylaşılıyor mu?
- Cam kombinasyonu açıkça yazılı mı?
- Montaj ve dış yalıtım detayları kapsamda mı?

Projeniz için uygun sistemi birlikte değerlendirmek isterseniz [alüminyum doğrama](/urunler/aluminyum-dograma) sayfamızı inceleyebilir veya [teklif talep edebilirsiniz](/iletisim).
"""),
        new("isicam-low-e-cam-secimi", "Isıcam ve Low-E Cam: Pencerede Doğru Cam Nasıl Seçilir?", "Çift cam, Low-E kaplama, argon gazı, temperli ve lamine cam farklarını kullanım alanlarıyla birlikte anlatıyoruz.", "/images/kapi-ve-pencere-sistemleri.png", "Isıcam, Low-E kaplama, argon gazı, temperli ve lamine cam farkları. Pencere ve geniş cam yüzeyler için doğru cam seçimi rehberi.",
"""
Pencerenin yüzeyinin büyük bölümünü cam oluşturur. Bu nedenle ısı kaybı, güneş kontrolü ve güvenlik söz konusu olduğunda cam seçimini en az profil kadar önemsiyoruz.

## Isıcam nedir?

Isıcam, iki veya daha fazla cam levhanın aralarında boşluk bırakılarak fabrikada birleştirilmesiyle üretilen yalıtım camıdır. "4+16+4" ifadesi; 4 mm cam, 16 mm boşluk ve 4 mm cam anlamına gelir. Isıcam ürünleri için temel standart **TS EN 1279**'dur.

## Low-E kaplama ne işe yarar?

Düşük salımlı (Low-E) kaplama, camın yüzeyine uygulanan ve gözle neredeyse fark edilmeyen ince bir tabakadır. Isının geçişini azaltır. Yaklaşık değerlerle kıyaslarsak standart çift camda Ug 2,7 W/m²K civarındayken, Low-E kaplamalı ve argon dolgulu ısıcamda 1,1 W/m²K seviyelerine inebilir. Kesin değer ürüne ve boşluk kalınlığına göre değişir; teklif sırasında bu değeri sormanızı öneriyoruz.

## Güneş kontrol camı

Güneş kontrol kaplamaları, özellikle batı ve güney cephelerde içeri giren güneş ısısını azaltır. İzmir gibi sıcak yaz dönemi uzun olan bölgelerde, soğutma yükü açısından değerlendirilmesini önerdiğimiz bir seçenektir. Görüş ve gün ışığı geçirgenliği ile birlikte ele alınmalıdır.

## Temperli ve lamine cam farkı

- **Temperli cam:** ısıl işlemle dayanımı artırılmış camdır. Kırıldığında keskin parçalar yerine küçük parçalara ayrılır (TS EN 12150).
- **Lamine cam:** iki cam arasına ara katman yerleştirilerek üretilir. Kırıldığında parçalar ara katmana tutunur, düşme riskini ve güvenlik zafiyetini azaltır (TS EN 14449).

Kapılar, yere kadar inen camlar, korkuluklar ve üst yüzeyler gibi çarpma veya düşme riski olan yerlerde güvenlik camı kullanımını öncelikle değerlendiriyoruz. Hangi camın gerektiğini uygulamanın konumuna ve projeye göre belirliyoruz.

## Ses yalıtımı için

Yoğun trafiğe bakan cephelerde farklı kalınlıkta camlardan oluşan ısıcam veya akustik lamine cam daha iyi sonuç verir. Eşit kalınlıktaki camlara kıyasla belirli ses frekanslarında performansı artırır.

## Kenar çıtası ve yoğuşma

Isıcamın kenarında iki camı ayıran bir çıta bulunur. Alüminyum çıta ısıyı daha çok iletirken, "sıcak kenar" olarak bilinen düşük iletkenlikli çıtalar kenar bölgesindeki ısı kaybını azaltır. Bu detay, özellikle soğuk havalarda cam kenarındaki yoğuşma riskini düşürmeye yardımcı olur.

## Üç camlı ısıcam gerekli mi?

Üç camlı çözümler daha düşük Ug değerine ulaşabilir, ancak ağırlığı ve kalınlığı artırır. Profil serisinin ve menteşelerin bu yükü taşıyabilmesi gerekir. Ege iklimindeki pek çok konut için iyi seçilmiş iki camlı Low-E ısıcam yeterli olabilir; soğuk bölgelerde veya çok yüksek enerji hedeflerinde üç cam değerlendirilebilir.

## Sık yapılan hatalar

- Tüm cephelerde aynı camı kullanmak, yön farkını göz ardı etmek,
- Yalnızca Ug değerine bakıp gün ışığı geçirgenliğini unutmak,
- Geniş camlarda güvenlik camı ihtiyacını sonradan düşünmek,
- Cam ağırlığını profil ve donanım seçiminde hesaba katmamak.

## Nasıl karar verilmeli?

- Cephenin yönünü ve aldığı güneşi belirleyin.
- Çarpma veya düşme riski olan camları işaretleyin.
- Trafik gibi gürültü kaynaklarını değerlendirin.
- Teklifte cam kombinasyonunun açıkça yazıldığını kontrol edin.

Doğru cam, her cephe için aynı olmak zorunda değildir. Projenize uygun kombinasyonu birlikte belirlemek için [bize ulaşabilirsiniz](/iletisim).
"""),
        new("cam-balkon-katlanir-mi-surme-mi", "Cam Balkon Sistemleri: Katlanır mı, Sürme mi?", "Katlanır ve sürme cam balkon sistemlerini; kullanım, açıklık ve yalıtım açısından karşılaştırıyor, uygulama öncesi kontrol edilmesi gerekenleri sıralıyoruz.", "/images/katlanir-cam-sistemleri.png", "Katlanır ve sürme cam balkon karşılaştırması. Isıcamlı ve tek camlı seçenekler, yönetim planı ve uygulama öncesi kontrol listesi.",
"""
Cam balkon, balkonu yılın daha uzun bölümünde kullanılabilir hale getirir. Rüzgâr, yağmur ve tozu keser, üstelik manzarayı kapatmaz. Karar verirken ilk soru genellikle şudur: katlanır mı, sürme mi?

## Katlanır sistem

Paneller birbirine paralel biçimde katlanarak bir veya iki yanda toplanır. Açıldığında balkon neredeyse tamamen serbest kalır. Bu sistem açık hava hissini korumak isteyenler için uygundur. Toplanan panellerin duracağı alan ve açılım yönü önceden planlanmalıdır.

## Sürme sistem

Paneller rayların üzerinde yatay hareket eder ve açılırken mekâna taşmaz. Dar balkonlarda kullanım kolaylığı sağlar. Ancak paneller üst üste geldiği için katlanır sistemdeki kadar geniş bir açıklık elde edilmez.

## Tek cam mı, ısıcam mı?

- **Tek cam:** rüzgâr ve yağmurdan korunma amacı için yeterli olabilir, daha hafiftir. Yalıtım değeri sınırlıdır.
- **Isıcam:** balkonun yaşam alanına yakın kullanılacağı durumlarda daha uygundur. Ağırlığı daha fazla olduğu için sistem ve taşıyıcı yapı buna göre seçilmelidir.

Kışın ısıtılan bir mekân gibi kullanılacaksa profil, cam ve eşik birlikte değerlendirilmeli; yalnızca camı değiştirmek beklenen konforu tek başına sağlamaz.

## Uygulama öncesi kontrol edilecekler

- **Yönetim planı ve kat malikleri kararı:** Dış görünümü etkileyen uygulamalarda apartman veya site yönetim planı ile Kat Mülkiyeti Kanunu hükümleri göz önünde bulundurulmalıdır. Şüphe durumunda hukuki görüş almanızı öneririz.
- **Taşıyıcı yapı:** Balkonun ve korkuluğun ek yükü taşıyabileceği kontrol edilmelidir.
- **Su tahliyesi:** Eşik ve drenaj detayı doğru çözülmezse yağmur suyu içeriye sızabilir.
- **Güvenlik:** Cam tipi temperli olmalı, yüksek katlarda düşme riski ayrıca değerlendirilmelidir.
- **Temizlik:** Camların dış yüzeyine güvenli erişim sağlayan bir sistem tercih edilmelidir.

## Cam balkon alanı nasıl kullanılacak?

Karar vermeden önce kullanım senaryosunu netleştirmenizi öneriyoruz. Balkon, yalnızca çamaşır ve depolama alanı olarak mı kullanılacak, yoksa kışın oturma alanına mı dönüşecek? Birinci durumda tek camlı ekonomik sistemler yeterli olabilir. İkinci durumda ısı yalıtımı, su tahliyesi ve havalandırma çok daha önemlidir.

Kapalı balkonda hava sirkülasyonu sağlanmazsa nem birikebilir. Bu nedenle camların bir kısmını kolayca aralayabilmek veya havalandırma pozisyonu sunan sistemleri tercih etmek faydalıdır. Mobilya ve bitki seçiminde de güneş ve yaz sıcaklığı dikkate alınmalıdır; güneye bakan bir cam balkon yazın hızla ısınabilir, gölgelendirme için perde veya tente düşünülmelidir.

## Teklif alırken sorulacaklar

- Cam türü ve kalınlığı nedir?
- Profil ve ray sistemi hangi seridendir?
- Fitiller ve tekerlekler için yedek parça temini nasıldır?
- Söküm, montaj ve mevcut korkuluğa ilişkin işler teklife dahil mi?

## Bakım

Rayların ve tekerleklerin düzenli temizlenmesi sistemin ömrünü uzatır. Fitillerin durumunu kontrol etmenizi, sürtünme hissettiğinizde ayar yaptırmanızı öneriyoruz.

## Hangi sistem size uygun?

Tam açıklık önceliğinizse katlanır, pratik kullanım ve dar alan önceliğinizse sürme sistem öne çıkar. [Katlanır cam sistemleri](/urunler/katlanir-cam-sistemleri) ve [sürme cam sistemleri](/urunler/yana-kayar-surme-cam-sistemleri) sayfalarımızdan ayrıntılara bakabilir, balkonunuz için [keşif talep edebilirsiniz](/iletisim).
"""),
        new("giyotin-cam-sistemleri-rehberi", "Giyotin Cam Sistemleri Rehberi: Otomatik ve Manuel Seçenekler", "Giyotin cam sistemlerinin çalışma prensibini, otomatik ve manuel farklarını ve güvenlik açısından dikkat edilmesi gerekenleri anlatıyoruz.", "/images/otomatik-giyotin-camlama-sistemleri.png", "Giyotin cam sistemi nedir, otomatik ve manuel farkları nelerdir? Teras ve işletmeler için güvenlik ve uygulama rehberi.",
"""
Giyotin cam sistemleri, cam panelleri dikey yönde hareket ettirerek terasları ve açık alanları kontrollü biçimde kapatır. Son yıllarda villalarda, kafe ve restoranlarda sıkça tercih edilir. Çünkü aynı alan hem açık hem kapalı olarak kullanılabilir.

## Nasıl çalışır?

Paneller, yan taşıyıcılar içindeki mekanizmayla yukarı veya aşağı hareket eder. Açık konumda paneller üst üste toplanır ve görüş açısı büyük ölçüde serbest kalır. Kapalı konumda ise rüzgâr ve yağmura karşı koruma sağlanır.

## Otomatik mi, manuel mi?

- **Otomatik sistem:** motorla çalışır, kumandayla tek hamlede açılıp kapanır. Büyük ve ağır panellerde kullanım kolaylığı sağlar. Elektrik altyapısı ve bakım planı gerektirir.
- **Manuel sistem:** elle kullanılır, motor ve elektrik gerektirmez. Daha küçük açıklıklar ve rüzgâr kırıcı uygulamalar için pratik bir çözümdür.

## Güvenlik neden önemli?

Yukarıda duran ağır cam paneller söz konusu olduğu için güvenliği seçimin merkezine koyuyoruz. Değerlendirdiğimiz başlıklar şunlardır:

- Cam tipinin (temperli veya lamine) uygulamaya uygun seçilmesi,
- Otomatik sistemlerde engel algılama donanımı ve acil durum kullanımı,
- Taşıyıcı yapının ve bağlantıların yükü güvenle karşılaması,
- Düzenli bakım ve kontrol.

Kullanıcıların, özellikle çocukların bulunduğu mekânlarda hareketli parçaların yakınında dikkat etmesi gerekir. Üreticinin kullanım talimatlarına uyulmalıdır.

## Uygulama öncesi değerlendirme

- **Açıklık ölçüsü:** Panel genişliği ve yüksekliği, sistem sınırları içinde olmalıdır.
- **Taşıyıcı yapı:** Zemin ve üst bağlantı noktalarını yerinde inceliyoruz.
- **Rüzgâr yükü:** Yüksek katları ve açık cepheleri ayrıca değerlendiriyoruz.
- **Su tahliyesi:** Zemin eğimini ve drenaj çözümünü planlıyoruz.
- **Elektrik:** Otomatik sistemde motor beslemesinin önceden hazırlanması gerekir; ihtiyacı keşifte paylaşıyoruz.

## Bakım ve kullanım alışkanlıkları

Paneller düzenli hareket ettirildiğinde mekanizma daha sağlıklı çalışır. Yan kanallardaki toz ve yaprak birikintileri temizlenmeli, cam yüzeyler aşındırıcı olmayan ürünlerle silinmelidir. Otomatik sistemlerde motor, kayış veya zincir ile sensörler üreticinin önerdiği aralıklarla kontrol edilmelidir. Olağandışı ses, takılma veya yavaşlama fark edildiğinde sistem kullanılmamalı ve servis çağırılmalıdır.

Şiddetli rüzgâr beklenen günlerde üreticinin önerdiği kullanım koşullarına uyulmalıdır. Bazı sistemler kapalı konumda rüzgâra daha dayanıklıdır; bu bilgi kullanım kılavuzunda ve teklif aşamasında netleştirilmelidir.

## İşletmeler için ek notlar

Restoran ve kafelerde sistem, yoğun kullanım koşullarına göre seçilmelidir. Mutfak havalandırması, ısıtıcı yerleşimi ve kaçış yolları açılıp kapanan alanı etkileyebilir. Kaldırım veya ortak alan kullanımı söz konusuysa ilgili belediye ve yönetim izinlerini önceden araştırmanızı öneriyoruz.

## Giyotin mi, katlanır mı?

Giyotin sistemde paneller zeminde yer kaplamaz ve manzara açık kalır. Katlanır sistemde ise paneller yana toplanır, geçişi tamamen serbest bırakır. Sık giriş çıkış yapılan yerlerde katlanır, korkuluk üstü görüş ve hızlı kullanım istenen yerlerde giyotin öne çıkabilir.

Ayrıntılar için [otomatik giyotin](/urunler/otomatik-giyotin-camlama-sistemleri) ve [manuel giyotin](/urunler/manuel-giyotin-ruzgar-kirici-sistemleri) sayfalarımızı inceleyebilir, alanınız için [keşif talep edebilirsiniz](/iletisim).
"""),
        new("izmir-iklimi-dograma-secimi", "İzmir ve Kıyı Bölgelerinde Doğrama Seçimi: Nem, Tuz, Güneş", "Sıcak yazlar, kıyı nemi, tuzlu hava ve rüzgâr gibi koşullar doğrama seçimini nasıl etkiler? Yüzey, cam ve donanım önerilerimizi paylaşıyoruz.", "/images/yana-kayar-surme-cam-sistemleri.png", "İzmir ve kıyı bölgelerinde alüminyum doğrama seçimi. Tuzlu hava, nem, güneş ve rüzgâra karşı yüzey, cam ve donanım tercihleri.",
"""
Doğrama seçimi yalnızca ölçüye ve bütçeye bağlı değildir; yapının bulunduğu iklim de sonucu doğrudan etkiler. İzmir'in uzun ve sıcak yazları, kıyıya yakın bölgelerdeki nem ve tuzlu hava, bazı ilçelerde ise güçlü rüzgârlar dikkate alınması gereken koşullardır.

## Tuzlu hava ve yüzey koruması

Alüminyum doğal bir oksit tabakasıyla korunur, ancak kıyıya yakın yerlerde yüzey işleminin kalitesi daha da önem kazanır. Değerlendirdiğimiz başlıklar:

- **Elektrostatik toz boya:** Kıyı koşulları için ön işlemi güçlendirilmiş uygulamalar tercih edilmelidir. Qualicoat "Seaside" sınıfı bu konuda bilinen kalite referanslarından biridir.
- **Anodize kaplama:** Sertifikalı uygulamalarda (ör. Qualanod) kaplama kalınlığı, kullanım ortamına göre seçilir.
- **Donanım ve bağlantılar:** Paslanmaz çelik vidalar ve kıyı koşullarına uygun menteşe ve kilitler uzun ömür için önemlidir.

Kaliteli bir yüzey bile temizlik ister. Tuz birikintilerinin düzenli olarak yıkanması, özellikle yağmur almayan yüzeylerde kritiktir.

## Güneş ve yaz konforu

Batı ve güney cephelerde büyük cam yüzeyler soğutma yükünü artırır. Güneş kontrol kaplamalı ısıcam, dış gölgelendirme ve panjur birlikte değerlendirildiğinde daha iyi sonuç alınır. Kışın güneş kazancını da düşünerek cephe bazında karar vermek gerekir.

PVC profillerde ise koyu renklerin yüzey sıcaklığını artırabileceği göz önünde bulundurulmalı, profilin güneşe dayanımı ve renk garantisi kontrol edilmelidir.

## Nem ve yoğuşma

Kıyıya yakın yapılarda iç mekân nemi daha yüksek olabilir. Isı bariyerli profiller ve düşük salımlı ısıcam, iç yüzey sıcaklığını yükselterek yoğuşma riskini azaltır. Düzenli havalandırma ise vazgeçilmezdir. Mikro havalandırma özellikli donanımlar da bu konuda yardımcı olabilir.

## Rüzgâr ve yağmur

Yüksek katlarda ve açık cephelerde rüzgâr yükü ile su sızdırmazlığı öne çıkar. **TS EN 14351-1** kapsamında beyan edilen hava geçirgenliği, su sızdırmazlığı ve rüzgâr dayanımı sınıfları karşılaştırma için kullanılabilir. Geniş sürme sistemlerde ray drenajı ayrıca kontrol edilmelidir.

## Cephe yönüne göre karar

Tüm cephelere aynı çözümü uygulamak yerine yön bazlı düşünmeyi öneriyoruz:

- **Batı ve güney:** Öğleden sonra güneşi güçlüdür. Güneş kontrol camı, panjur veya tente değerlendirilmelidir.
- **Kuzey:** Doğrudan güneş az olduğu için gün ışığını kesmeyen camlar ve iyi ısı yalıtımı öne çıkar.
- **Denize bakan cepheler:** Yüzey işlemi, donanım dayanımı ve rüzgâr performansı önceliklidir. Tuzlu su serpintisi alan alanlar düzenli yıkanmalıdır.

## Yaz ve kış dengesi

İzmir'de kışlar görece ılımlı olsa da yağışlı ve nemlidir, yazlar ise uzun ve sıcaktır. Bu yüzden yalnızca kış ısı kaybına odaklanmayı eksik bir yaklaşım olarak görüyoruz. Düşük U değeri ile uygun güneş kontrolü birlikte ele alınmalı, dış gölgelendirme imkânları değerlendirilmelidir. Gece serinliğinden yararlanmak için kolay havalandırılabilen açılım tipleri de konforu artırır.

## Bakım önerileri

- Yüzeyleri aşındırıcı olmayan temizleyiciyle düzenli yıkayın.
- Tahliye deliklerini açık tutun.
- Yılda en az bir kez fitil ve donanımı kontrol edin, hareketli parçaları üreticinin önerdiği şekilde yağlayın.

Bulunduğunuz konuma ve cepheye uygun çözümü belirlemek için [bizimle iletişime geçebilirsiniz](/iletisim).
"""),
        new("otomatik-kapi-secimi-rehberi", "Otomatik Kapı Seçimi: Fotoselli, Döner, Endüstriyel ve Bahçe Kapıları", "Bina girişinden depoya kadar farklı otomatik kapı tiplerini, kullanım alanlarını ve güvenlik gereksinimlerini özetliyoruz.", "/images/fotoselli-doner-kapi.png", "Fotoselli, döner, seksiyonel ve bahçe kapısı karşılaştırması. Otomatik kapı seçiminde kullanım alanı, güvenlik ve bakım rehberi.",
"""
Otomatik kapılar yalnızca konfor sağlamaz; yapının güvenliğini, enerji kullanımını ve iş akışını da etkiler. Doğru seçim için önce kapının nerede ve ne sıklıkla kullanılacağını netleştiriyoruz.

## Yaya girişleri: fotoselli ve döner kapılar

- **Fotoselli sürme kapılar:** Mağaza, ofis ve sağlık yapılarında yaygındır. Sensörler sayesinde temassız geçiş sağlar, erişilebilirlik açısından avantajlıdır.
- **Döner kapılar:** İç ve dış ortam arasındaki hava geçişini sınırlar, temsil niteliği olan girişlerde tercih edilir. Daha geniş alan gerektirir ve çoğu zaman yanında ayrıca bir kaçış veya erişilebilir geçiş planlanır.

Yaya otomatik kapılarında güvenlik gereksinimleri için **TS EN 16005** standardı referans alınır.

## Araç ve yük geçişleri

- **Seksiyonel kapılar:** Tavana doğru açıldığı için önünde boşluk gerektirmez. Garaj ve depo girişlerinde yaygındır.
- **Sarmal kepenkler:** Kutu içine sarılarak açılır, tavan alanının kısıtlı olduğu yerlerde uygundur.
- **Hızlı açılan kapılar:** Geçişin yoğun olduğu ve iç ortam koşullarının korunmak istendiği tesislerde değerlendirilir.

Endüstriyel ve garaj kapılarında **TS EN 13241** ürün standardı, güvenli kullanım gereksinimleri için ise **TS EN 12453** esas alınır.

## Site ve villa girişleri

Yana kayar bahçe kapıları duvar boyunca boş alan ister. Kanatlı kapılar ise açılım alanı gerektirir. Otopark girişlerinde bariyerler hızlı geçiş sağlar, kart veya plaka tanıma gibi geçiş kontrol yöntemleriyle birlikte kullanılabilir.

## Seçimde sorduğumuz sorular

- Günde kaç geçiş olacak?
- Geçişi kim veya ne yapacak: yaya, araç, forklift?
- Engelli erişimi gereksinimi var mı?
- Elektrik kesintisinde kapı nasıl açılacak?
- Yapının yangın ve kaçış planı kapıyı etkiliyor mu?

## Yangın ve kaçış senaryosu

Bina girişindeki otomatik kapılar çoğu zaman acil durum planının parçasıdır. Elektrik kesildiğinde veya yangın alarmı geldiğinde kapının nasıl davranacağı önceden belirlenmelidir. Bazı yapılarda kaçış yolu üzerinde döner kapı tek başına yeterli sayılmaz; yanında ayrıca bir kapı gerekebilir. Bu konunun proje müellifiyle ve ilgili mevzuat çerçevesinde değerlendirilmesini öneriyoruz.

## Erişilebilirlik

Tekerlekli sandalye kullananlar, çocuklu aileler ve yük taşıyan ziyaretçiler için geçiş genişliği, açılma süresi ve sensör alanı önemlidir. Hassas kullanıcıların bulunduğu sağlık ve bakım yapılarında kapı hızları ve güvenlik sensörleri buna uygun ayarlanmalıdır.

## Enerji ve iklim etkisi

Sık açılan girişlerde iç ortam havası kolayca dışarı kaçar. Sensör menzilinin doğru ayarlanması, gereksiz açılmaları azaltır. Rüzgâr alan girişlerde hava perdesi veya çift kapılı giriş holü gibi önlemler de konforu artırır.

## Bakım ihmal edilmemeli

Otomatik kapılar hareketli parçalar ve güvenlik sensörleri içerir. Düzenli bakım hem arızaları azaltır hem de güvenli kullanımın koşuludur. Fotosel, kuvvet sınırlama ve acil durum işlevlerini periyodik olarak kontrol ettirmenizi öneriyoruz.

Ayrıntılar için [fotoselli kapı](/urunler/fotoselli-doner-kapi), [endüstriyel ve garaj kapıları](/urunler/endustriyel-ve-garaj-kapi-sistemleri) ile [bahçe kapısı ve bariyer](/urunler/bahce-kapisi-ve-bariyer-sistemleri) sayfalarımızı inceleyebilirsiniz.
"""),
        new("dograma-bakimi-rehberi", "Pencere ve Kapı Bakımı: Doğramanın Ömrünü Uzatan Basit Adımlar", "Alüminyum ve PVC doğramaların temizliği, fitil ve donanım kontrolü ile sık görülen sorunların çözümüne dair pratik önerilerimizi derledik.", "/images/kapi-ve-pencere-sistemleri.png", "Alüminyum ve PVC pencere bakımı: temizlik, fitil ve menteşe kontrolü, sık görülen sorunlar ve ne zaman uzman çağırılması gerektiği.",
"""
Kaliteli bir doğrama uzun yıllar sorunsuz kullanılabilir, ancak bu büyük ölçüde düzenli bakıma bağlıdır. Çoğu bakım adımı basittir ve yılda birkaç kez yapılması yeterlidir. Bu yazıda önerdiğimiz temel adımları bir araya getirdik.

## Yüzey temizliği

- Yumuşak bezle, ılık su ve nötr temizleyici kullanın.
- Tiner, aseton, çamaşır suyu ve aşındırıcı süngerlerden kaçının. Bu ürünler boya veya laminasyon yüzeyine zarar verebilir.
- Kıyıya yakın yapılarda tuz birikintilerini daha sık temizleyin.
- Şantiye sonrası kalan harç ve boya kalıntılarını beklemeden, yüzeye zarar vermeden alın.

## Tahliye kanalları

Pencerenin alt profilindeki küçük tahliye delikleri, içeri giren suyu dışarı atar. Tıkandıklarında su geri dönebilir. Rayları ve tahliye kanallarını süpürge veya yumuşak fırçayla temizleyin, delikleri açık tutun.

## Fitil ve contalar

Hava ve su sızdırmazlığını sağlayan fitiller zamanla sertleşebilir. Yılda bir kez yumuşak bir bezle temizleyin ve üreticinin önerdiği silikon bazlı bakım ürünüyle koruyun. Çatlamış veya ezilmiş fitiller değiştirilmelidir.

## Menteşe ve kilit donanımı

Hareketli parçaları yılda en az bir kez üreticinin önerdiği yağla bakımdan geçirin. Kanat zor kapanıyor, kolu sıkışıyor veya alt köşesi kasaya sürtüyorsa ayar gerekebilir. Bu sorunlar genellikle basit bir ayarla çözülür; zorlayarak kullanmak ise donanıma zarar verir.

## Sürme sistemler

Rayları toz ve küçük taşlardan arındırın, tekerleklerin rahat döndüğünü kontrol edin. Sürtünme artarsa yağlamadan önce ray temizliğini yapın; tozlu rayda yağ, kirin birikmesine yol açar.

## Sık görülen sorunlar

- **Camın iç yüzeyinde buğu:** Genellikle iç mekân nemi kaynaklıdır, havalandırma yardımcı olur.
- **Isıcamın iki camı arasında buğu:** Isıcamın sızdırmazlığı bozulmuş olabilir, camın değişmesi gerekir.
- **Rüzgârda ıslık sesi veya hava akımı:** Fitil veya kanat ayarı kontrol edilmelidir.
- **Kenarlardan su girişi:** Tahliye kanalları ve dış silikon birleşimleri incelenmelidir.

## Mevsimlere göre kısa takvim

- **İlkbahar:** Kış boyunca biriken kirleri temizleyin, tahliye deliklerini ve dış silikonları kontrol edin.
- **Yaz başı:** Sürme rayları ve sineklikleri temizleyin, panjur ve tente hareketlerini kontrol edin.
- **Sonbahar:** Yağmur öncesi tahliye kanallarını açın, fitilleri ve dış birleşimleri gözden geçirin.
- **Kış:** Yoğuşma görürseniz havalandırma alışkanlığını gözden geçirin, cam kenarlarındaki nemi silin.

## Ömrü kısaltan kullanım hataları

- Kanadın üzerine yük asmak veya açık kanada yaslanmak,
- Kolu tam çevirmeden kanadı zorlamak,
- Rüzgârlı havada kanadı kilitlemeden açık bırakmak,
- Yüzeye yapışkan bant veya sert kazıyıcı kullanmak,
- Doğramaya vida çakarak perde veya klima montajı yapmak; bu işlem profil ve yalıtımı bozabilir.

Periyodik kontrol küçük bir ayarla çözülecek sorunların büyümesini önler. Özellikle yüksek binalarda ve büyük camlarda kontrollerin güvenlik açısından yetkin ekiplerce yapılmasını öneriyoruz.

## Ne zaman uzman çağırmalı?

Kırık cam, kanadın düşmesi, kilit arızası ve otomatik sistemlerdeki sorunlar için kendi başınıza müdahale etmeyin. Güvenlik riski taşıyan durumlarda kullanımı durdurun ve yetkili servise başvurun.

Doğramalarınızla ilgili destek veya yenileme için [bize ulaşabilirsiniz](/iletisim).
"""),
    };

    public static async Task ApplyAsync(AppDbContext db)
    {
        var marker = await db.SiteSettingRecords.FirstOrDefaultAsync(x => x.Key == VersionKey);
        if (marker?.Value == Version) return;

        var services = await db.Services.ToListAsync();
        foreach (var copy in Services)
        {
            var row = services.FirstOrDefault(x => x.Slug == copy.Slug);
            if (row is null) continue;
            var oldShort = SeedData.OriginalDescription(copy.Slug);
            if (row.ShortDescription == oldShort || ContentFingerprints.IsVersion1Default(row.ShortDescription)) row.ShortDescription = copy.Short;
            if (row.Intro == OldIntro(oldShort ?? "") || ContentFingerprints.IsVersion1Default(row.Intro)) row.Intro = copy.Intro;
            if (row.SeoTitle == row.Name + " | Karakuş Alüminyum") row.SeoTitle = copy.SeoTitle;
            if (row.SeoDescription == OldSeoDescription(row.Name)) row.SeoDescription = copy.SeoDescription;
            if (row.BenefitsJson == JsonSerializer.Serialize(OldBenefits)) row.BenefitsJson = JsonSerializer.Serialize(copy.Benefits);

            var faqs = await db.FaqItems.Where(x => x.ServiceId == row.Id).ToListAsync();
            var generic = faqs.Where(x => OldFaqs.Any(o => o.Q == x.Question && o.A == x.Answer)).ToList();
            db.FaqItems.RemoveRange(generic);
            var order = faqs.Except(generic).Select(x => (int?)x.SortOrder).Max() + 1 ?? 0;
            foreach (var (question, answer) in copy.Faqs)
            {
                var faq = faqs.FirstOrDefault(x => x.Question == question);
                if (faq is null) db.FaqItems.Add(new FaqItem { ServiceId = row.Id, Question = question, Answer = answer, SortOrder = order++ });
                else if (ContentFingerprints.IsVersion1Default(faq.Answer)) faq.Answer = answer;
            }
            row.UpdatedUtc = DateTime.UtcNow;
        }

        foreach (var project in await db.Projects.Where(x => x.Summary.Contains("firma tarafından doğrulandıktan sonra")).ToListAsync())
        {
            project.Summary = $"{project.Location} bölgesindeki {project.Title} projesinde uygulama gerçekleştirdik.";
            project.UpdatedUtc = DateTime.UtcNow;
        }

        var settings = await db.SiteSettingRecords.ToListAsync();
        foreach (var (key, old) in OldSettings)
        {
            var setting = settings.FirstOrDefault(x => x.Key == key);
            if (setting?.Value == old) setting.Value = KarakusAluminyum.Web.Services.SiteSettingCatalog.Find(key)!.Default;
        }
        foreach (var setting in settings.Where(x => ContentFingerprints.IsVersion1Default(x.Value)))
            setting.Value = KarakusAluminyum.Web.Services.SiteSettingCatalog.Find(setting.Key)?.Default ?? setting.Value;

        var homeFaq = await db.FaqItems.FirstOrDefaultAsync(x => x.ServiceId == null && x.Question == HomeFaqQuestion);
        if (ContentFingerprints.IsVersion1Default(homeFaq?.Answer)) homeFaq!.Answer = HomeFaqAnswer;

        // Yayınlanmamış yer tutucu yazı kaldırılır, yeni yazılar bir kez eklenir.
        db.BlogPosts.RemoveRange(db.BlogPosts.Where(x => x.Slug == "aluminyum-ve-mimari-sistemler" && !x.IsPublished && x.Content.StartsWith("Bu yazı taslak olarak")));
        var posts = await db.BlogPosts.ToListAsync();
        var slugs = posts.Select(x => x.Slug).ToList();
        var now = DateTime.UtcNow;
        foreach (var article in Articles)
        {
            var post = posts.FirstOrDefault(x => x.Slug == article.Slug);
            if (post is null) continue;
            var summary = ContentFingerprints.IsVersion1Default(post.Summary);
            var content = ContentFingerprints.IsVersion1Default(post.Content);
            if (summary) post.Summary = article.Summary;
            if (content) post.Content = article.Content;
            if (summary || content) post.UpdatedUtc = now;
        }
        for (var i = 0; i < Articles.Length; i++)
        {
            var article = Articles[i];
            if (slugs.Contains(article.Slug)) continue;
            db.BlogPosts.Add(new BlogPost { Slug = article.Slug, Title = article.Title, Summary = article.Summary, Image = article.Image, SeoTitle = article.Title + " | Karakuş Alüminyum", SeoDescription = article.SeoDescription, Content = article.Content.Trim(), IsPublished = true, PublishedUtc = now.AddMinutes(-i), UpdatedUtc = now });
        }

        if (marker is null) db.SiteSettingRecords.Add(new SiteSettingRecord { Key = VersionKey, Value = Version, Description = "İçerik yenilemesi uygulandı" });
        else marker.Value = Version;
    }
}
