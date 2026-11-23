using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record OrderOption(int Id, int CustomerId, string Text);

// Servis talepleri: numara, garanti önerisi, ziyaret sonucuna göre durum ve siparişin geçmişine kayıt.
public sealed class ServiceDesk
{
    private readonly AppDbContext _db;
    private readonly OrderService _orders;
    public ServiceDesk(AppDbContext db, OrderService orders) { _db = db; _orders = orders; }

    public Task<string> NextRequestNumberAsync(DateOnly date) => _orders.NextNumberAsync("SRV", date.Year);
    public Task<string> NextVisitNumberAsync(DateOnly date) => _orders.NextNumberAsync("SF", date.Year);

    // Garanti süresi (yıl) siparişin sözleşmesindeki değişkenden, yoksa sözleşme şablonundan gelir (varsayılan 2).
    public async Task<int> WarrantyYearsAsync(ProductionOrder? order)
    {
        static int? Parse(string? value) => int.TryParse(value?.Trim(), out var years) && years is > 0 and < 100 ? years : null;
        if (order?.ContractId is int contractId && await _db.Contracts.Where(x => x.Id == contractId).Select(x => x.DocumentJson).FirstOrDefaultAsync() is string json
            && Parse(DocumentJson.ParseContent(json).Variables.GetValueOrDefault("garanti_suresi")) is int fromContract) return fromContract;
        var template = await DocumentViews.CurrentTemplateAsync(_db, DocumentKinds.Contract);
        return Parse(DocumentJson.ParseVariables(template.VariablesJson).GetValueOrDefault("garanti_suresi")) ?? 2;
    }

    // Teslimden itibaren garanti süresi içinde bildirilen talep garanti kapsamındadır; teslim tarihi yoksa belirlenmez.
    public static ServiceWarranty SuggestWarranty(DateOnly? deliveredOn, int years, DateOnly receivedOn)
        => deliveredOn is not DateOnly delivered ? ServiceWarranty.Unknown
            : receivedOn <= delivered.AddYears(years) ? ServiceWarranty.InWarranty : ServiceWarranty.Paid;

    public static DateOnly? WarrantyEnds(DateOnly? deliveredOn, int years) => deliveredOn?.AddYears(years);

    public static List<int> ParsePozIds(string? value)
        => (value ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var id) ? id : 0).Where(x => x > 0).Distinct().ToList();

    public async Task<List<WindowDesign>> OrderPozlarAsync(int? orderId)
    {
        if (orderId is null) return new();
        var listId = await _db.ProductionOrders.Where(x => x.Id == orderId).Select(x => x.DrawingListId).FirstOrDefaultAsync();
        return listId is int id
            ? await _db.WindowDesigns.AsNoTracking().Where(x => x.DrawingListId == id).OrderBy(x => x.SortOrder).ThenBy(x => x.Id).ToListAsync()
            : new();
    }

    // Ziyaret sonucu talebin durumunu belirler: sorun giderildiyse tamamlanır, aksi halde yeni ziyaret için açık kalır.
    public static void ApplyVisit(ServiceRequest request, ServiceVisit visit)
    {
        if (visit.Result == ServiceResult.Resolved)
        {
            request.Status = ServiceStatus.Completed;
            request.CompletedOn = visit.Date;
            request.PlannedOn = null;
        }
        else
        {
            request.Status = ServiceStatus.Open;
            request.CompletedOn = null;
            request.PlannedOn = null; // yeni ziyaret tarihi ayrıca planlanır
        }
        request.UpdatedUtc = DateTime.UtcNow;
    }

    public async Task RecordOnOrderAsync(int? orderId, string text, string? user)
    {
        if (orderId is not int id) return;
        var order = await _db.ProductionOrders.FirstOrDefaultAsync(x => x.Id == id);
        if (order is not null) OrderService.Record(order, text, user);
    }
}
