using KarakusAluminyum.Web.Models.Documents;

namespace KarakusAluminyum.Web.Services.Documents;

// Teklif ve sözleşme şablonlarının başlangıç içeriği. Depo herkese açık olduğundan
// kişisel iletişim, vekil ve vergi bilgileri burada tutulmaz; şablon ayarlarından girilir.
public static class DocumentSeed
{
    private const string Seller = "Karakuş Alüminyum";
    private const string Address = "Karacaoğlan Mah. 6252 Sk. No: 3 Bornova / İzmir";
    private const string Phone = "+90 553 835 48 72";

    public static DocumentTemplate QuoteTemplate() => new()
    {
        Kind = DocumentKinds.Quote,
        Title = "TEKLİFTİR",
        SellerName = Seller,
        SellerAddress = Address,
        Phone = Phone,
        FooterAddress = Address,
        Intro = "Yapımı devam eden {{proje}} inşaatlarınızın {{is_tanimi}} imalatı ile montaj işlerine ilişkin teklifimiz aşağıda bilgilerinize sunulmuştur:",
        Closing = "Belirtilen tüm işlerin imalatı ve montajı, aşağıda sunulan fiyatlar doğrultusunda firmamız tarafından gerçekleştirilecektir.",
        SignOff = "Saygılarımızla,"
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
        ArticlesJson = DocumentJson.Serialize(new List<ContractArticle>
        {
            new() { Title = "Taraflar", Body = "Bir tarafta **{{alici}}**, adresi {{alici_adres}} (bu sözleşmede “Alıcı” olarak anılacaktır) ile, diğer tarafta **{{satici}}**, adresi **{{satici_adres}}** (bu sözleşmede “Satıcı” olarak anılacaktır), aşağıda belirtilen şartlar çerçevesinde işbu sözleşmeyi karşılıklı olarak kabul etmişlerdir." },
            new() { Title = "Sözleşmenin Konusu", Body = "Bu sözleşmenin konusu; **{{konu}}**", Kind = ContractArticleKinds.Subject },
            new() { Title = "İşin Toplam Bedeli", Kind = ContractArticleKinds.Price },
            new() { Title = "Ödeme Şekli", Body = "Ödeme, aşağıdaki şekilde yapılacaktır:", Kind = ContractArticleKinds.Payment },
            new() { Title = "İşin Teslim Süresi", Body = "İşin teslim süresi, **şantiye iş programına göre** taraflar arasında karşılıklı mutabakatla belirlenecektir." },
            new() { Title = "Tebligat Adresi", Body = "Taraflar, işbu sözleşmenin 1. maddesinde belirtilen adreslerin **yasal tebligat adresleri** olduğunu kabul ederler. Adres değişiklikleri yazılı olarak bildirilmediği sürece, bu adreslere yapılacak tüm tebligatlar geçerli sayılacaktır." },
            new() { Title = "Yetkili Mahkeme ve İcra Daireleri", Body = "İşbu sözleşmeden doğabilecek her türlü ihtilafın çözümünde **İzmir Mahkemeleri ve İcra Daireleri** yetkilidir." }
        })
    };

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
