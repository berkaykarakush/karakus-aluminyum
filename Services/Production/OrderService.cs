using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Production;

public sealed class OrderException(string message) : Exception(message);

// Siparişin iş yükü: poz sayısı, adet ve alan. Üretim planı ve listeler bunu kullanır.
public sealed record OrderLoad(int Positions, int Units, decimal AreaM2)
{
    public static readonly OrderLoad Empty = new(0, 0, 0);

    public static OrderLoad Of(IEnumerable<WindowDesign> items)
    {
        var list = items.ToList();
        return new OrderLoad(list.Count, list.Sum(x => x.Quantity), Math.Round(list.Sum(x => x.WidthMm * (decimal)x.HeightMm * x.Quantity) / 1_000_000m, 2));
    }
}

// Pozun sevk durumu: siparişteki adet, irsaliyelerle sevk edilen ve kalan.
public sealed record DeliveryProgress(WindowDesign Design, decimal Ordered, decimal Delivered)
{
    public decimal Remaining => Math.Max(0, Ordered - Delivered);
}

public sealed class OrderService
{
    private readonly AppDbContext _db;
    public OrderService(AppDbContext db) => _db = db;

    // Yıla göre sıralı numara (SP-2026-000001, IRS-2026-000001). Satır kilidiyle artar; aynı numara iki kez verilmez.
    public async Task<string> NextNumberAsync(string prefix, int year)
    {
        var key = $"{prefix}-{year}";
        var next = (await _db.Database.SqlQueryRaw<int>(
            "INSERT INTO \"AccountingSequences\" (\"Key\", \"Next\") VALUES ({0}, 1) ON CONFLICT (\"Key\") DO UPDATE SET \"Next\" = \"AccountingSequences\".\"Next\" + 1 RETURNING \"Next\" AS \"Value\"", key).ToListAsync()).Single();
        return $"{key}-{next:000000}";
    }

    public static string Location(Customer? customer)
    {
        if (customer is null) return "";
        var place = string.Join(" / ", new[] { customer.District, customer.City }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return string.Join(", ", new[] { customer.Address, place }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    // Kabul edilen tekliften sipariş: müşteri, başlık, çizim listesi ve montaj adresi gelir. Bir teklifin tek siparişi olur.
    public async Task<ProductionOrder> FromQuoteAsync(int quoteId, string? user)
    {
        var existing = await _db.ProductionOrders.FirstOrDefaultAsync(x => x.QuoteId == quoteId);
        if (existing is not null) return existing;
        var quote = await _db.Quotes.Include(x => x.Customer).FirstOrDefaultAsync(x => x.Id == quoteId) ?? throw new OrderException("Teklif bulunamadı.");
        if (quote.Status is not (QuoteStatus.Accepted or QuoteStatus.Converted)) throw new OrderException("Sipariş yalnızca kabul edilen tekliften oluşturulabilir.");
        var contractId = await _db.Contracts.Where(x => x.QuoteId == quoteId && x.Status != ContractStatus.Cancelled).Select(x => (int?)x.Id).FirstOrDefaultAsync();
        return await CreateAsync(new ProductionOrder
        {
            CustomerId = quote.CustomerId, QuoteId = quote.Id, ContractId = contractId, DrawingListId = quote.DrawingListId, Title = quote.Title,
            InstallAddress = Trim(Location(quote.Customer), 500), ContactPhone = quote.Customer?.Phone, Status = OrderStatus.Approved
        }, $"{quote.QuoteNumber} numaralı tekliften oluşturuldu.", user);
    }

    // Sözleşmeden sipariş: sözleşmenin teklifi varsa onun çizim listesi kullanılır.
    public async Task<ProductionOrder> FromContractAsync(int contractId, string? user)
    {
        var existing = await _db.ProductionOrders.FirstOrDefaultAsync(x => x.ContractId == contractId);
        if (existing is not null) return existing;
        var contract = await _db.Contracts.Include(x => x.Customer).Include(x => x.Quote).FirstOrDefaultAsync(x => x.Id == contractId) ?? throw new OrderException("Sözleşme bulunamadı.");
        if (contract.Status == ContractStatus.Cancelled) throw new OrderException("İptal edilen sözleşmeden sipariş oluşturulamaz.");
        if (contract.QuoteId is int quoteId && await _db.ProductionOrders.FirstOrDefaultAsync(x => x.QuoteId == quoteId) is { } fromQuote)
        {
            fromQuote.ContractId ??= contract.Id;
            await _db.SaveChangesAsync();
            return fromQuote;
        }
        return await CreateAsync(new ProductionOrder
        {
            CustomerId = contract.CustomerId, QuoteId = contract.QuoteId, ContractId = contract.Id, DrawingListId = contract.Quote?.DrawingListId, Title = contract.Title,
            InstallAddress = Trim(contract.BuyerAddress ?? Location(contract.Customer), 500), ContactPhone = contract.Customer?.Phone,
            Status = contract.Status == ContractStatus.Signed ? OrderStatus.Approved : OrderStatus.AwaitingApproval
        }, $"{contract.ContractNumber} numaralı sözleşmeden oluşturuldu.", user);
    }

    public async Task<ProductionOrder> CreateAsync(ProductionOrder order, string text, string? user)
    {
        order.OrderDate = order.OrderDate == default ? DateOnly.FromDateTime(DateTime.Today) : order.OrderDate;
        order.OrderNumber = await NextNumberAsync("SP", order.OrderDate.Year);
        order.Events.Add(new OrderEvent { Status = order.Status, Text = $"{text} Durum: {OrderStatuses.Label(order.Status)}.", User = user });
        _db.ProductionOrders.Add(order);
        await _db.SaveChangesAsync();
        return order;
    }

    // Durum değişikliği. İptal ve teslim edilen sipariş kapanmıştır; sevk ve teslim tarihleri kendiliğinden yazılır.
    public void ChangeStatus(ProductionOrder order, OrderStatus next, string? note, string? user)
    {
        if (!Enum.IsDefined(next)) throw new OrderException("Geçersiz durum.");
        if (order.Status == next) throw new OrderException("Sipariş zaten bu durumda.");
        if (order.Status == OrderStatus.Cancelled) throw new OrderException("İptal edilen sipariş yeniden açılamaz.");
        if (order.Status == OrderStatus.Delivered && next != OrderStatus.Shipped) throw new OrderException("Teslim edilen sipariş yalnızca \"Sevk edildi\" durumuna geri alınabilir.");
        if (next == OrderStatus.Cancelled && order.DeliveryNotes.Count > 0) throw new OrderException("İrsaliyesi kesilmiş sipariş iptal edilemez; önce irsaliyeleri silin.");

        var today = DateOnly.FromDateTime(DateTime.Today);
        if (next is OrderStatus.Shipped or OrderStatus.Delivered) order.ShippedOn ??= today;
        if (next == OrderStatus.Delivered) order.DeliveredOn ??= today;
        if (next != OrderStatus.Delivered) order.DeliveredOn = null;
        var text = $"Durum: {OrderStatuses.Label(order.Status)} → {OrderStatuses.Label(next)}." + (string.IsNullOrWhiteSpace(note) ? "" : " " + note.Trim());
        order.Status = next;
        Record(order, Trim(text, 500), user, next);
    }

    public static void Record(ProductionOrder order, string text, string? user, OrderStatus? status = null)
    {
        order.Events.Add(new OrderEvent { ProductionOrderId = order.Id, Status = status, Text = Trim(text, 500), User = user });
        order.UpdatedUtc = DateTime.UtcNow;
    }

    public async Task<Dictionary<int, OrderLoad>> LoadsAsync(IEnumerable<int?> listIds)
    {
        var ids = listIds.OfType<int>().Distinct().ToList();
        if (ids.Count == 0) return new();
        var items = await _db.WindowDesigns.AsNoTracking().Where(x => ids.Contains(x.DrawingListId)).ToListAsync();
        return items.GroupBy(x => x.DrawingListId).ToDictionary(g => g.Key, g => OrderLoad.Of(g));
    }

    // Pozların sevk durumu: irsaliye satırlarında pozla bağlantılı adetler toplanır.
    public async Task<List<DeliveryProgress>> ProgressAsync(ProductionOrder order)
    {
        if (order.DrawingListId is not int listId) return new();
        var designs = await _db.WindowDesigns.AsNoTracking().Where(x => x.DrawingListId == listId).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync();
        var delivered = await _db.DeliveryNoteLines.AsNoTracking().Where(x => x.DeliveryNote!.ProductionOrderId == order.Id && x.WindowDesignId != null)
            .GroupBy(x => x.WindowDesignId!.Value).Select(g => new { g.Key, Total = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Key, x => x.Total);
        return designs.Select(d => new DeliveryProgress(d, d.Quantity, delivered.GetValueOrDefault(d.Id))).ToList();
    }

    public static bool IsLate(ProductionOrder order, DateOnly today) =>
        order.PlannedShipDate is DateOnly planned && planned < today && order.Status is not (OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled);

    private static string Trim(string text, int max) => text.Length > max ? text[..max] : text;
}
