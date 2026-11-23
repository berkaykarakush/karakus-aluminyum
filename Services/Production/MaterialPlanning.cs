using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record ReorderRow(Product Product, decimal Stock, decimal OpenNeed, decimal Projected, decimal Target, decimal Suggested, IReadOnlyList<string> Orders);
public sealed record PurchaseDraftResult(Invoice Invoice, IReadOnlyList<string> MissingRates);

// Malzeme planlama: sipariş metrajı, açık siparişlerin stok ihtiyacı, sipariş önerisi ve tedarikçiye taslak alış faturası.
public sealed class MaterialPlanning
{
    // Stoktan düşülmemiş, üretime girmiş veya onaylanmış siparişlerin malzemesi stoktan ayrılmış sayılır.
    public static readonly OrderStatus[] CommittedStatuses = { OrderStatus.Approved, OrderStatus.InProduction };

    private readonly AppDbContext _db;
    public MaterialPlanning(AppDbContext db) => _db = db;

    public async Task<TakeoffResult> TakeoffAsync(ProductionOrder order)
    {
        var designs = order.DrawingListId is int listId
            ? await _db.WindowDesigns.AsNoTracking().Include(x => x.ProfileSystem).Where(x => x.DrawingListId == listId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync()
            : new List<WindowDesign>();
        var productIds = designs.Where(x => x.ProfileSystem is not null).Select(x => x.ProfileSystem!).DistinctBy(x => x.Id)
            .SelectMany(x => MaterialTakeoff.ParseRules(x.MaterialsJson).Select(r => r.ProductId)).Distinct().ToList();
        var products = await _db.Products.AsNoTracking().Where(x => productIds.Contains(x.Id)).ToDictionaryAsync(x => x.Id);
        return MaterialTakeoff.Build(designs, products);
    }

    public async Task<Dictionary<int, decimal>> StockAsync(IEnumerable<int>? productIds = null)
    {
        var query = _db.StockMovements.AsNoTracking();
        if (productIds is not null) { var ids = productIds.ToList(); query = query.Where(x => ids.Contains(x.ProductId)); }
        return (await query.Select(x => new { x.ProductId, x.Quantity }).ToListAsync()).GroupBy(x => x.ProductId).ToDictionary(g => g.Key, g => g.Sum(x => x.Quantity));
    }

    // Kritik seviyenin altına düşen veya açık siparişlerin ihtiyacıyla düşecek stok kartları ve önerilen sipariş miktarı.
    // Hedef: kartta girilen hedef stok, yoksa kritik seviyenin iki katı; kritik seviye yoksa yalnızca eksik kadar.
    public async Task<List<ReorderRow>> ReorderAsync()
    {
        var products = await _db.Products.AsNoTracking().Where(x => x.Kind == ProductKind.Stock && x.IsActive).ToListAsync();
        var stock = await StockAsync();
        var need = new Dictionary<int, decimal>();
        var orders = new Dictionary<int, List<string>>();
        foreach (var order in await _db.ProductionOrders.AsNoTracking().Where(x => !x.StockDeducted && x.DrawingListId != null && CommittedStatuses.Contains(x.Status)).ToListAsync())
            foreach (var line in (await TakeoffAsync(order)).Lines)
            {
                need[line.Product.Id] = need.GetValueOrDefault(line.Product.Id) + line.Quantity;
                if (!orders.TryGetValue(line.Product.Id, out var list)) orders[line.Product.Id] = list = new();
                list.Add(order.OrderNumber);
            }
        return products.Select(p =>
            {
                var current = stock.GetValueOrDefault(p.Id);
                var open = need.GetValueOrDefault(p.Id);
                var projected = current - open;
                var target = p.TargetStock > 0 ? p.TargetStock : p.CriticalStock * 2;
                var below = p.CriticalStock > 0 ? projected <= p.CriticalStock : projected < 0;
                var suggested = below ? Math.Max(target, 0) - projected : 0;
                if (MaterialTakeoff.IsCountUnit(p.Unit)) suggested = Math.Ceiling(suggested);
                return new ReorderRow(p, current, open, projected, target, Math.Max(0, suggested), orders.GetValueOrDefault(p.Id, new List<string>()));
            })
            .Where(x => x.Suggested > 0).OrderBy(x => x.Product.Code ?? "").ThenBy(x => x.Product.Name).ToList();
    }

    // Tek yabancı para birimli listede fatura o para biriminde, aksi halde güncel kurla TL açılır. Onayda stok artar.
    public async Task<PurchaseDraftResult> PurchaseDraftAsync(int supplierId, IReadOnlyList<(Product Product, decimal Quantity)> lines, string note)
    {
        var today = Documents.PrintDates.Today;
        var rates = await PriceLists.LatestRatesAsync(_db);
        var currencies = lines.Select(x => x.Product.PriceCurrency).Distinct().ToList();
        var currency = currencies.Count == 1 && currencies[0] != "TRY" && rates.ContainsKey(currencies[0]) ? currencies[0] : "TRY";
        var missingRate = lines.Where(x => currency == "TRY" && PriceLists.ToTry(x.Product.PurchasePrice, x.Product.PriceCurrency, rates) is null).Select(x => x.Product.PriceCurrency).Distinct().ToList();
        var invoice = new Invoice
        {
            Kind = InvoiceKind.Purchase, Status = InvoiceStatus.Draft, CustomerId = supplierId, Date = today, DueDate = today.AddDays(30), Currency = currency,
            ExchangeRate = currency == "TRY" ? 1 : rates[currency].Rate, Notes = note
        };
        invoice.Lines.AddRange(lines.Select((x, i) => new InvoiceLine
        {
            SortOrder = i, ProductId = x.Product.Id, Description = string.IsNullOrWhiteSpace(x.Product.Code) ? x.Product.Name : $"{x.Product.Code} {x.Product.Name}",
            Quantity = x.Quantity, Unit = x.Product.Unit, VatRate = x.Product.VatRate,
            UnitPrice = currency == "TRY" ? decimal.Round(PriceLists.ToTry(x.Product.PurchasePrice, x.Product.PriceCurrency, rates) ?? 0, 4) : x.Product.PurchasePrice
        }));
        Accounting.InvoiceRules.Calculate(invoice);
        _db.Invoices.Add(invoice);
        return new PurchaseDraftResult(invoice, missingRate);
    }
}
