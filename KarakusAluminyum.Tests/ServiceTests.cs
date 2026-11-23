using System.Net;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// README madde 4: bitmiş işlerde servis talebi, servis formu (ziyaret) ve yazdırılabilir formlar.
[Collection("postgres")]
public sealed class ServiceTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("2025-12-01", "2027-12-01", ServiceWarranty.InWarranty)]
    [InlineData("2025-12-01", "2027-12-02", ServiceWarranty.Paid)]
    [InlineData(null, "2026-01-01", ServiceWarranty.Unknown)]
    public void Warranty_IsSuggestedFromDeliveryAndYears(string? delivered, string received, ServiceWarranty expected)
        => Assert.Equal(expected, ServiceDesk.SuggestWarranty(delivered is null ? null : DateOnly.Parse(delivered), 2, DateOnly.Parse(received)));

    [Fact]
    public async Task Request_Visits_StatusFlow_AndPrintouts()
    {
        await using var app = postgres.CreateApp("service_requests");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var other = await QuoteDocumentTests.NewCustomerAsync(app);
        await DrawingLists.PostAsync(client, token, "Servis listesi", DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 2), DrawingLists.Row("Banyo", 600, 600, quantity: 1)));
        var listId = await DrawingLists.ListIdAsync(app, "Servis listesi");
        var designs = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.DrawingListId == listId).OrderBy(d => d.SortOrder).Select(d => d.Id).ToArrayAsync());
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Bitmiş iş"), ("DrawingListId", listId.ToString()), ("OrderDate", "2025-11-01"), ("InstallAddress", "Bornova İzmir"));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Bitmiş iş").Select(o => o.Id).SingleAsync());
        await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).ExecuteUpdateAsync(x => x.SetProperty(o => o.Status, OrderStatus.Delivered).SetProperty(o => o.DeliveredOn, PrintDatesToday().AddYears(-1))));

        // Siparişten açılan talep formu: adres ve garanti önerisi siparişten gelir
        var form = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/servis/ekle?siparis={orderId}"));
        Assert.Contains("value=\"Bornova İzmir\"", form);
        Assert.Contains($"selected=\"selected\" value=\"{ServiceWarranty.InWarranty}\"", form.Replace("value=\"InWarranty\" selected=\"selected\"", "selected=\"selected\" value=\"InWarranty\""));
        Assert.Contains("Poz 2: Banyo", form);

        (string, string)[] Request(int customerId, params (string, string)[] extra) => new[] { ("CustomerId", customerId.ToString()), ("ProductionOrderId", orderId.ToString()), ("ReceivedOn", PrintDatesToday().ToString("yyyy-MM-dd")),
            ("Channel", "Telefon"), ("Category", "Conta / sızdırmazlık / su alma"), ("Complaint", "Salon penceresi yağmurda su alıyor."), ("Warranty", "InWarranty"), ("ContactName", "Ayşe Hanım"), ("Address", "Bornova İzmir") }.Concat(extra).ToArray();
        var wrong = await client.PostFormAsync("/yonetim/servis/ekle", token, Request(other));
        Assert.Contains("bu müşteriye ait değil", WebUtility.HtmlDecode(await wrong.Content.ReadAsStringAsync()));
        var created = await client.PostFormAsync("/yonetim/servis/ekle", token, Request(customer, ("PozIds", designs[0].ToString()), ("PozIds", "999999")));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var request = await app.WithDbAsync(db => db.ServiceRequests.AsNoTracking().SingleAsync(r => r.CustomerId == customer));
        Assert.StartsWith("SRV-", request.Number);
        Assert.Equal((designs[0].ToString(), ServiceStatus.Open), (request.PozIds, request.Status)); // başka listenin pozu atılır
        Assert.Contains(await app.WithDbAsync(db => db.OrderEvents.Where(e => e.ProductionOrderId == orderId).Select(e => e.Text).ToListAsync()), t => t.StartsWith($"Servis talebi açıldı: {request.Number}"));

        // Ziyaret tarihi verilince planlanır
        var planned = PrintDatesToday().AddDays(2).ToString("yyyy-MM-dd");
        await client.PostFormAsync($"/yonetim/servis/duzenle/{request.Id}", token, Request(customer, ("PozIds", designs[0].ToString()), ("PlannedOn", planned)));
        Assert.Equal(ServiceStatus.Scheduled, await Status(app, request.Id));

        // Parça beklenen ziyaret talebi açık bırakır; sorun giderilince tamamlanır
        await client.PostFormAsync($"/yonetim/servis/ziyaret/ekle/{request.Id}", token, ("Date", planned), ("WorkDone", "Conta kontrol edildi, parça sipariş edildi."), ("Result", "NeedsPart"));
        Assert.Equal(ServiceStatus.Open, await Status(app, request.Id));
        await client.PostFormAsync($"/yonetim/servis/ziyaret/ekle/{request.Id}", token, ("Date", planned), ("ArrivedAt", "10:00"), ("LeftAt", "11:30"), ("Technicians", "Ali, Veli"),
            ("Findings", "Kanat contası yıpranmış."), ("WorkDone", "Kanat contası değiştirildi, ayar yapıldı."), ("Result", "Resolved"), ("LaborCharge", "200"),
            ("Parts[0].Description", "Kanat contası"), ("Parts[0].Quantity", "2"), ("Parts[0].Unit", "Metre"), ("Parts[0].UnitPrice", "150"), ("Parts[1].Description", ""));
        var done = await app.WithDbAsync(db => db.ServiceRequests.AsNoTracking().Include(r => r.Visits).ThenInclude(v => v.Parts).SingleAsync(r => r.Id == request.Id));
        Assert.Equal((ServiceStatus.Completed, DateOnly.Parse(planned)), (done.Status, done.CompletedOn!.Value));
        var visit = done.Visits.Single(v => v.Result == ServiceResult.Resolved);
        Assert.StartsWith("SF-", visit.Number);
        Assert.Single(visit.Parts); // boş parça satırı atlanır

        var bad = await client.PostFormAsync($"/yonetim/servis/ziyaret/ekle/{request.Id}", token, ("Date", planned), ("ArrivedAt", "12:00"), ("LeftAt", "11:00"), ("WorkDone", "x"));
        Assert.Contains("Çıkış saati varış saatinden önce olamaz", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));

        // Çıktılar
        var requestPrint = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/servis/yazdir/{request.Id}"));
        foreach (var text in new[] { "SERVİS TALEP FORMU", request.Number, "Salon penceresi yağmurda su alıyor.", "Bornova İzmir", "Ayşe Hanım", "Salon", "Garanti kapsamında", "name=\"tarih\"" })
            Assert.Contains(text, requestPrint);
        Assert.DoesNotContain("Banyo", requestPrint); // seçilmeyen poz basılmaz
        var visitPrint = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/servis/form/{visit.Id}"));
        foreach (var text in new[] { "SERVİS FORMU", visit.Number, "Kanat contası değiştirildi", "10:00 / 11:30", "Ali, Veli", "500,00 ₺", "100,00 ₺", "600,00 ₺", "Sorun giderildi", "Ziyaret tarihi (" })
            Assert.Contains(text, visitPrint);
        Assert.Contains("SERVİS FORMU", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/servis/bos-form")));

        // Liste, sipariş paneli ve menü sayacı
        Assert.Contains(request.Number, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/servis?tumu=true")));
        Assert.DoesNotContain(request.Number, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/servis"))); // tamamlanan açık listede yok
        Assert.Contains(request.Number, WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}")));

        // Son ziyaret silinince durum kalan ziyarete göre döner; ziyaretli talep silinemez
        await client.PostFormAsync($"/yonetim/servis/ziyaret/sil/{visit.Id}", token);
        Assert.Equal(ServiceStatus.Open, await Status(app, request.Id));
        await client.PostFormAsync($"/yonetim/servis/sil/{request.Id}", token);
        Assert.True(await app.WithDbAsync(db => db.ServiceRequests.AnyAsync(r => r.Id == request.Id)));
        await client.PostFormAsync($"/yonetim/servis/durum/{request.Id}", token, ("status", "Cancelled"));
        Assert.Equal(ServiceStatus.Cancelled, await Status(app, request.Id));
        var refused = await client.PostFormAsync($"/yonetim/servis/ziyaret/ekle/{request.Id}", token, ("Date", planned), ("WorkDone", "x"));
        Assert.Contains("İptal edilen talebe servis formu eklenemez", WebUtility.HtmlDecode(await refused.Content.ReadAsStringAsync()));
    }

    private static DateOnly PrintDatesToday() => KarakusAluminyum.Web.Services.Documents.PrintDates.Today;
    private static Task<ServiceStatus> Status(AppFactory app, int id) => app.WithDbAsync(db => db.ServiceRequests.Where(r => r.Id == id).Select(r => r.Status).SingleAsync());
}
