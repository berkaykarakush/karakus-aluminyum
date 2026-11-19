using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Documents;

// Teklif ve sözleşme ekleri: çizim listesindeki gibi Ercom tarzı poz listesi.
public sealed record AttachmentList(IReadOnlyList<PozView> Items)
{
    public int UnitCount => Items.Sum(i => i.Design.Quantity);
    public decimal TotalPerimeterM => Items.Sum(i => i.PerimeterM * i.Design.Quantity);
    public decimal TotalAreaM2 => Items.Sum(i => i.AreaM2 * i.Design.Quantity);
}

public sealed record QuoteBlockView(DocumentBlock Block, IReadOnlyList<QuoteItem> Items, decimal Subtotal);

public sealed record QuotePrintVm(Quote Quote, string CustomerName, DocumentTemplate Template, DocumentContent Content, IReadOnlyList<QuoteBlockView> Blocks, AttachmentList Attachments, PresentationVm? Presentation = null)
{
    public PriceSheet Prices { get; } = PriceSheet.FromQuote(Quote);

    public Dictionary<string, string> System => new()
    {
        ["musteri"] = CustomerName,
        ["tarih"] = TurkishNumbers.Date(Quote.DocumentDate),
        ["gecerlilik"] = Quote.ValidUntilUtc is DateTime valid ? TurkishNumbers.Date(valid) : "",
        ["belge_no"] = Quote.QuoteNumber,
        ["toplam"] = TurkishNumbers.Money(Quote.GrandTotal),
        ["toplam_yazi"] = TurkishNumbers.MoneyWords(Quote.GrandTotal)
    };
}

public sealed record ContractPrintVm(Contract Contract, DocumentTemplate Template, DocumentContent Content, IReadOnlyList<ContractArticle> Articles, AttachmentList Attachments)
{
    public PriceSheet Prices { get; } = PriceSheet.FromContent(Content, Contract.VatMode, Contract.TaxRate, Contract.DiscountRate);

    public Dictionary<string, string> System
    {
        get
        {
            var seller = string.IsNullOrWhiteSpace(Template.SellerLegalName) ? Template.SellerName : Template.SellerLegalName;
            return new()
            {
                ["alici"] = Contract.BuyerName,
                ["alici_adres"] = Contract.BuyerAddress ?? "",
                ["satici"] = seller ?? "",
                ["satici_adres"] = Template.SellerAddress ?? "",
                ["konu"] = (Content.Subject ?? "").Replace("**", ""), // şablon konuyu zaten kalın basar
                ["tarih"] = TurkishNumbers.Date(Contract.DocumentDate),
                ["toplam"] = TurkishNumbers.Money(Contract.TotalAmount),
                ["toplam_yazi"] = TurkishNumbers.MoneyWords(Contract.TotalAmount),
                ["madde_sayisi"] = Articles.Count.ToString(),
                ["madde_sayisi_yazi"] = TurkishNumbers.CountWords(Articles.Count),
                ["sayfa_sayisi"] = Contract.PageCount.ToString(),
                ["sayfa_sayisi_yazi"] = TurkishNumbers.CountWords(Contract.PageCount)
            };
        }
    }

    public string PriceLine
    {
        get
        {
            var money = TurkishNumbers.MoneyWithWords(Contract.TotalAmount);
            // Kalemli sözleşmede bedel kalemlerden hesaplanan genel toplamdır: KDV eklenmiş ya da fiyata dahildir.
            if (Prices.HasLines && Contract.VatMode != VatMode.None) return "KDV Dahil " + money;
            return Contract.VatMode switch { VatMode.Included => "KDV Dahil " + money, VatMode.Added => money + " + KDV", _ => money };
        }
    }
}

public static class DocumentViews
{
    // Kalemi olmayan sözleşmede fiyat tablosu, şartı olmayanda özel şartlar maddesi basılmaz; madde numaraları ve sayısı buna göre oluşur.
    public static List<ContractArticle> VisibleArticles(IEnumerable<ContractArticle> articles, DocumentContent content) => articles.Where(a => a.Kind switch
    {
        ContractArticleKinds.Items => content.Blocks.Any(b => b.Items.Any(i => !string.IsNullOrWhiteSpace(i.Description))),
        ContractArticleKinds.Terms => content.Terms.Any(t => !string.IsNullOrWhiteSpace(t.Text)),
        _ => true
    }).ToList();

    public static async Task<DocumentTemplate> CurrentTemplateAsync(AppDbContext db, string kind)
        => await db.DocumentTemplates.AsNoTracking().FirstAsync(x => x.Kind == kind);

    // Belge kendi şablon kopyasıyla basılır; eski kayıtlarda kopya yoksa güncel şablon kullanılır.
    public static async Task<DocumentTemplate> TemplateForAsync(AppDbContext db, string kind, string? snapshotJson)
        => DocumentJson.ParseTemplate(snapshotJson) ?? await CurrentTemplateAsync(db, kind);

    public static IReadOnlyList<QuoteBlockView> BuildBlocks(Quote quote, DocumentContent content)
    {
        static string Key(string? name) => name?.Trim() ?? "";
        var items = quote.Items.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
        var used = new HashSet<QuoteItem>();
        var views = new List<QuoteBlockView>();
        foreach (var block in content.Blocks)
        {
            var own = items.Where(i => Key(i.BlockName) == Key(block.Name) && used.Add(i)).ToList();
            views.Add(new QuoteBlockView(block, own, own.Sum(i => i.LineTotal)));
        }

        // Bloğu olmayan kalemler (ör. eski teklifler veya çizimden eklenenler) ayrı gösterilir.
        var rest = items.Where(i => !used.Contains(i)).ToList();
        if (rest.Count > 0 || views.Count == 0)
            views.Add(new QuoteBlockView(new DocumentBlock(), rest, rest.Sum(i => i.LineTotal)));
        return views;
    }

    public static async Task<AttachmentList> LoadAttachmentsAsync(AppDbContext db, IReadOnlyCollection<int> ids)
    {
        if (ids.Count == 0) return new AttachmentList(Array.Empty<PozView>());
        var rows = await db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem).Include(x => x.Brand).Where(x => ids.Contains(x.Id)).ToListAsync();
        var ordered = ids.Select(id => rows.FirstOrDefault(r => r.Id == id)).OfType<WindowDesign>();
        return new AttachmentList(PozSheet.Build(ordered));
    }
}
