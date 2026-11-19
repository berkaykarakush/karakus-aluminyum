using KarakusAluminyum.Web.Models.Documents;

namespace KarakusAluminyum.Web.Services.Documents;

// Teklif ve sözleşme şablonlarının başlangıç içeriği. Depo herkese açık olduğundan
// kişisel iletişim, vekil ve vergi bilgileri burada tutulmaz; şablon ayarlarından girilir.
public static class DocumentSeed
{
    private const string Seller = "Karakuş Alüminyum";
    private const string Address = "Karacaoğlan Mah. 6252 Sk. No: 3 Bornova / İzmir";
    private const string Phone = "+90 553 835 48 72";

    // Proforma (ERP'lerdeki satış teklifi düzeni): kalem tablosu, iskonto, KDV, şartlar, banka ve onay kutusu.
    public const string QuoteTitle = "PROFORMA FATURA";
    public const string QuoteIntro = "Talebiniz doğrultusunda hazırlanan proforma faturamız aşağıda bilgilerinize sunulmuştur.";
    public const string Acceptance = "Yukarıdaki fiyat ve şartları kabul ederek siparişi onaylıyorum.";

    // Yeni belgeye gelen değişken değerleri. Şablon ayarlarından ve her belgede değiştirilebilir.
    public static Dictionary<string, string> DefaultVariables() => new()
    {
        ["teslim_suresi"] = "30", ["garanti_suresi"] = "2", ["avans_orani"] = "50",
        ["muayene_suresi"] = "7", ["gecikme_binde"] = "1", ["gecikme_ust_sinir"] = "10"
    };

    public static List<BlockClause> QuoteTerms() => new()
    {
        new() { Title = "Fiyatlar", Text = "Fiyatlar, ekteki poz listesinde belirtilen tip, ölçü ve adetler için geçerlidir. Yerinde alınacak kesin ölçülerle oluşan miktar farkları aynı birim fiyatlar üzerinden hesaplanır." },
        new() { Title = "Ödeme", Text = "Bedelin %{{avans_orani}}'si sipariş onayında peşin, kalanı montaj bitiminde ödenir." },
        new() { Title = "Teslim süresi", Text = "Sipariş onayı ve kesin ölçü alımından itibaren {{teslim_suresi}} iş günüdür." },
        new() { Title = "Nakliye ve montaj", Text = "İmalat, nakliye ve montaj fiyata dahildir. Teslim yeri işin yapılacağı şantiyedir." },
        new() { Title = "Fiyata dahil olmayanlar", Text = "Sıva, boya, mermer ve denizlik, iskele, elektrik tesisatı ile eski doğramaların sökümü aksi yazılmadıkça fiyata dahil değildir." },
        new() { Title = "Garanti", Text = "İmalat ve montaj işçiliğine teslimden itibaren {{garanti_suresi}} yıl garanti verilir. Profil, cam, motor ve aksesuarlar üretici garantisi kapsamındadır." }
    };

    public static DocumentTemplate QuoteTemplate() => new()
    {
        Kind = DocumentKinds.Quote,
        Title = QuoteTitle,
        SellerName = Seller,
        SellerLegalName = Seller,
        SellerAddress = Address,
        Phone = Phone,
        FooterAddress = Address,
        Intro = QuoteIntro,
        SignOff = "Saygılarımızla,",
        AcceptanceText = Acceptance,
        TermsJson = DocumentJson.Serialize(QuoteTerms()),
        VariablesJson = DocumentJson.Serialize(DefaultVariables())
    };

    public static DocumentTemplate ContractTemplate() => new()
    {
        Kind = DocumentKinds.Contract,
        Title = "SÖZLEŞMEDİR",
        SellerName = Seller,
        SellerLegalName = Seller,
        SignatoryTitle = "Vekil",
        SellerAddress = Address,
        Phone = Phone,
        FooterAddress = Address,
        BuyerLabel = "Alıcı",
        SellerLabel = "Satıcı",
        Closing = "Yukarıda belirtilen tüm iş kalemlerinin **imalat ve montajı**, aşağıda belirtilen bedel üzerinden taraflarca mutabakata varılarak gerçekleştirilecektir.",
        SignOff = "Bu sözleşme **{{madde_sayisi}} ({{madde_sayisi_yazi}}) maddeden ve {{sayfa_sayisi}} ({{sayfa_sayisi_yazi}}) sayfadan** ibaret olup, **{{tarih}}** tarihinde **iki nüsha** olarak düzenlenmiş ve taraflarca karşılıklı olarak imzalanmıştır.",
        VariablesJson = DocumentJson.Serialize(DefaultVariables()),
        ArticlesJson = DocumentJson.Serialize(ContractArticles())
    };

    // Kişiye özel ölçüyle imal edilip monte edilen doğrama işi eser sözleşmesidir (TBK m. 470 vd.).
    // Maddeler; ayıp ihbarı (TBK 474, 477), cezai şart (TBK 179-182) ve tüketici hakları (6502) gözetilerek yazılmıştır.
    // Hukuki danışmanlık yerine geçmez; firma kendi avukatıyla gözden geçirmelidir.
    public static List<ContractArticle> ContractArticles() => new()
    {
        new() { Title = "Taraflar", Body = "Bir tarafta **{{alici}}**, adresi {{alici_adres}} (bu sözleşmede “Alıcı” olarak anılacaktır) ile, diğer tarafta **{{satici}}**, adresi **{{satici_adres}}** (bu sözleşmede “Satıcı” olarak anılacaktır), aşağıda belirtilen şartlar çerçevesinde işbu sözleşmeyi karşılıklı olarak kabul etmişlerdir." },
        new() { Title = "Sözleşmenin Konusu", Body = "Bu sözleşmenin konusu; **{{konu}}**", Kind = ContractArticleKinds.Subject },
        new() { Title = "Sözleşmenin Ekleri", Body = "İşbu sözleşmenin ekleri; **Ek-1** iş kalemleri ve birim fiyatlar tablosu ile **Ek-2** poz listesi ve çizimlerdir. Ekler sözleşmenin ayrılmaz parçasıdır. Sözleşme ile ekleri arasında çelişki olması hâlinde sözleşme hükümleri esas alınır." },
        new() { Title = "İş Kalemleri ve Birim Fiyatlar", Body = "İşin kalemleri, miktarları ve birim fiyatları aşağıdaki tabloda gösterilmiştir:", Kind = ContractArticleKinds.Items },
        new() { Title = "İşin Toplam Bedeli", Kind = ContractArticleKinds.Price },
        new() { Title = "Ödeme Şekli", Body = "Ödeme, aşağıdaki şekilde yapılacaktır:", Kind = ContractArticleKinds.Payment },
        new() { Title = "Ölçü, Onay ve Değişiklikler", Body = "Kesin ölçüler Satıcı tarafından yerinde alınır. Alıcı; poz listesindeki tip, açılım yönü, renk ve ölçüleri imalattan önce onaylar. Onaydan sonra Alıcının istediği değişiklikler ve ilave işler yazılı olarak kararlaştırılır ve sözleşmedeki birim fiyatlar üzerinden ayrıca bedellendirilir. Kesin ölçülere göre oluşan miktar farkları da aynı birim fiyatlarla hesaplanır." },
        new() { Title = "İşin Teslim Süresi", Body = "İşin teslim süresi; kesin ölçülerin alınıp poz listesinin onaylanmasından ve peşinatın ödenmesinden itibaren **{{teslim_suresi}} iş günüdür**. Alıcıdan kaynaklanan gecikmeler (montaj alanının hazır olmaması, ödemelerin veya onayların gecikmesi) ile mücbir sebep süreleri teslim süresine eklenir." },
        new() { Title = "Alıcının Yükümlülükleri", Body = "Alıcı; montaj yapılacak boşlukları (kaba sıva, şap ve denizlik seviyeleri tamamlanmış olarak) zamanında hazırlamayı, montaj için gerekli elektriği sağlamayı, gerekiyorsa iskeleyi kurdurmayı, şantiyeye bırakılan malzemenin korunması için kapalı ve güvenli bir alan göstermeyi ve ödemeleri ödeme planına uygun olarak yapmayı kabul eder." },
        new() { Title = "Satıcının Yükümlülükleri", Body = "Satıcı; işi sözleşme ve eklerinde belirtilen marka, seri, renk ve özelliklerde, ilgili standartlara ve tekniğine uygun olarak imal ve monte etmeyi, montaj sırasında iş sağlığı ve güvenliği mevzuatına uymayı ve kendi montaj atıklarını toplamayı kabul eder. Satıcının çalıştırdığı personelin sigorta ve iş güvenliği yükümlülükleri Satıcıya aittir." },
        new() { Title = "Gecikme ve Cezai Şart", Body = "Satıcının kusuruyla teslim süresinin aşılması hâlinde geciken her gün için sözleşme bedelinin **binde {{gecikme_binde}}** oranında gecikme cezası uygulanır; gecikme cezasının toplamı sözleşme bedelinin **%{{gecikme_ust_sinir}}**'unu geçemez. Alıcının ödemeleri zamanında yapmaması hâlinde Satıcı, ödeme yapılıncaya kadar işi durdurabilir; bu süre teslim süresine eklenir ve geciken tutara yasal temerrüt faizi uygulanır." },
        new() { Title = "Teslim ve Kabul", Body = "Montajın tamamlanmasıyla iş, taraflarca imzalanacak teslim tutanağı ile teslim edilir. Alıcı, teslimden itibaren **{{muayene_suresi}} gün** içinde işi gözden geçirerek açık ayıpları yazılı olarak bildirir; bu süre içinde bildirilmeyen açık ayıplar bakımından iş kabul edilmiş sayılır (TBK m. 474 ve 477). Gizli ayıplar ortaya çıktıklarında gecikmeksizin bildirilir. Satıcıdan kaynaklanan ayıplar Satıcı tarafından makul sürede giderilir." },
        new() { Title = "Garanti", Body = "Satıcı, imalat ve montaj işçiliğinden kaynaklanan ayıplara karşı işi teslim tarihinden itibaren **{{garanti_suresi}} yıl** garanti eder. Profil, cam, motor ve aksesuarlar ayrıca üreticilerinin garanti koşullarına tabidir. Kullanım hatası, yetkisiz müdahale, bina hareketleri, doğal afetler ve olağan aşınma garanti kapsamı dışındadır. Alıcının tüketici olması hâlinde 6502 sayılı Tüketicinin Korunması Hakkında Kanun'dan doğan hakları saklıdır." },
        new() { Title = "Özel Şartlar", Body = "Aşağıdaki özel şartlar da sözleşmenin parçasıdır:", Kind = ContractArticleKinds.Terms },
        new() { Title = "Mücbir Sebepler", Body = "Doğal afet, salgın, savaş, genel grev ve resmi makam kararları gibi tarafların kontrolü dışındaki olaylar mücbir sebep sayılır. Mücbir sebep süresince tarafların yükümlülükleri askıya alınır ve süreler bu süre kadar uzar. Mücbir sebep 60 günü aşarsa taraflardan biri sözleşmeyi, o tarihe kadar yapılan işlerin bedeli ödenmek kaydıyla feshedebilir." },
        new() { Title = "Sözleşmenin Feshi", Body = "Taraflardan biri yükümlülüklerine aykırı davranır ve yazılı ihtara rağmen 15 gün içinde aykırılığı gidermezse diğer taraf sözleşmeyi feshedebilir. Fesih hâlinde o tarihe kadar imal veya monte edilen işlerin bedeli ile kişiye özel ölçüde üretildiği için başka bir işte kullanılamayacak malzemelerin bedeli Alıcı tarafından ödenir." },
        new() { Title = "Kişisel Verilerin Korunması", Body = "Taraflar, işbu sözleşme kapsamında edindikleri kişisel verileri yalnızca sözleşmenin ifası amacıyla ve 6698 sayılı Kişisel Verilerin Korunması Kanunu'na uygun olarak işler." },
        new() { Title = "Tebligat Adresi", Body = "Taraflar, işbu sözleşmenin 1. maddesinde belirtilen adreslerin **yasal tebligat adresleri** olduğunu kabul ederler. Adres değişiklikleri yazılı olarak bildirilmediği sürece, bu adreslere yapılacak tüm tebligatlar geçerli sayılacaktır." },
        new() { Title = "Damga Vergisi ve Masraflar", Body = "İşbu sözleşmeden doğan damga vergisi taraflarca yarı yarıya ödenir." },
        new() { Title = "Yetkili Mahkeme ve İcra Daireleri", Body = "İşbu sözleşmeden doğabilecek her türlü ihtilafın çözümünde **İzmir Mahkemeleri ve İcra Daireleri** yetkilidir." }
    };

    // İlk sürümün sözleşme madde başlıkları: şablonu hiç değiştirilmemiş kurulumlar yeni maddelere yükseltilir.
    public static readonly string[] FirstContractTitles = { "Taraflar", "Sözleşmenin Konusu", "İşin Toplam Bedeli", "Ödeme Şekli", "İşin Teslim Süresi", "Tebligat Adresi", "Yetkili Mahkeme ve İcra Daireleri" };
    public const string FirstQuoteIntro = "Yapımı devam eden {{proje}} inşaatlarınızın {{is_tanimi}} imalatı ile montaj işlerine ilişkin teklifimiz aşağıda bilgilerinize sunulmuştur:";
    public const string FirstQuoteClosing = "Belirtilen tüm işlerin imalatı ve montajı, aşağıda sunulan fiyatlar doğrultusunda firmamız tarafından gerçekleştirilecektir.";

    public static IEnumerable<TemplateClause> Clauses()
    {
        var order = 0;
        TemplateClause Clause(string name, string quote, string contract) => new() { Name = name, QuoteText = quote, ContractText = contract, SortOrder = order++ };

        yield return Clause("PVC Doğramalar",
            "Doğramalarınız {{marka}} marka {{renk}} renkte ekteki tip ve adetlerdeki gibi üretilecektir.",
            "{{adet}} adet PVC doğramalar **{{marka}} marka {{renk}} renkte** olacak, camları **{{cam}}** özellikte olup, ekte belirtilen tip ve adetlere göre imalat ve montaj yapılacaktır.");
        yield return Clause("Alüminyum Doğramalar",
            "Alüminyum doğramalarınız {{marka}} marka {{renk}} renkte ekteki tip ve adetlerdeki gibi üretilecektir.",
            "Alüminyum doğramalar **{{marka}} marka {{renk}} renkte** olacak, camları **{{cam}}** özellikte olup, ekte belirtilen tip ve adetlere göre imalat ve montaj yapılacaktır.");
        yield return Clause("Camlar",
            "Camlar, {{cam}} olacaktır.",
            "Camlar **{{cam}}** özellikte olacaktır.");
        yield return Clause("Banyo ve WC Pencereleri",
            "Banyo ve tuvalet camları alüminyum menfez olacaktır.",
            "Banyo ve WC pencereleri menfez olacaktır.");
        yield return Clause("Dükkan Doğramaları",
            "Dükkan doğramaları {{dukkan_serisi}} serisi olacak ve ekte belirtilen tip ve adetlere göre imalat ve montaj yapılacaktır. Camlar {{dukkan_cami}} olacaktır.",
            "{{dukkan_adedi}} dükkanın doğramaları **{{dukkan_serisi}} serisi alüminyum** profillerden yapılacak, camları **{{dukkan_cami}}** olacaktır.");
        yield return Clause("Balkon Korkulukları",
            "Balkon korkulukları {{korkuluk_profili}} alüminyum kare profilden yapılacak olup {{korkuluk_cami}} kullanılacaktır.",
            "**{{korkuluk_profili}} mm ebatlarında alüminyum kare profilden {{renk}} renkte** yapılacak olup, korkuluk camları **{{korkuluk_cami}}** olacaktır.");
        yield return Clause("Merdiven Korkulukları",
            "Merdiven korkulukları alüminyum boru profilden camsız olarak yapılacaktır.",
            "**Alüminyum boru profilden**, **camsız emniyet çubuklu** olarak imal edilecektir.");
        yield return Clause("Panjur Kutuları",
            "Panjur kutuları {{kutu_profili}} demir profilden yapılacaktır.",
            "**{{kutu_profili}} demir profilden olacak, iç ve dış yüzü boardex** olarak imal edilecektir.");
        yield return Clause("Motorlu Panjurlar",
            "Panjurlar, {{renk}} renkte {{lamel}} lamel, {{motor}} motorlu detay panjur olacaktır.",
            "**{{renk}} renkte {{motor}} motor {{lamel}} lamelden** detay panjur olarak imal edilecektir.");
    }
}
