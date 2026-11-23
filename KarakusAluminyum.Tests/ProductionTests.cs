using System.Net;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Barkodlu üretim: etiketler, istasyonlarda okutma ve sipariş durumunun kendiliğinden ilerlemesi.
[Collection("postgres")]
public sealed class ProductionTests(PostgresFixture postgres)
{
    [Fact]
    public void LabelCode_RoundTrips_AndRejectsOtherText()
    {
        Assert.Equal(new LabelCode(12, 345, 2, null), ProductionLabels.Parse(ProductionLabels.PozCode(12, 345, 2)));
        Assert.Equal(new LabelCode(12, 345, 2, 7), ProductionLabels.Parse(" s12-345-2-7 "));
        Assert.Null(ProductionLabels.Parse("SP-2026-000001"));
        Assert.Null(ProductionLabels.Parse(""));
    }

    [Fact]
    public async Task Labels_AndScans_DriveOrderThroughProduction()
    {
        await using var app = postgres.CreateApp("production_scans");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var systemId = await app.WithDbAsync(async db => { var s = new ProfileSystem { Name = "Etiket serisi" }; db.ProfileSystems.Add(s); await db.SaveChangesAsync(); return s.Id; });
        var list = "Etiket listesi";
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 2, system: systemId), DrawingLists.Row("Banyo", 600, 600, quantity: 1, system: 0)));
        var listId = await DrawingLists.ListIdAsync(app, list);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Etiket işi"), ("DrawingListId", listId.ToString()), ("OrderDate", DateTime.Today.ToString("yyyy-MM-dd")));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Etiket işi").Select(o => o.Id).SingleAsync());
        var designs = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.DrawingListId == listId).OrderBy(d => d.SortOrder).Select(d => d.Id).ToArrayAsync());

        var pozHtml = await client.GetStringAsync($"/yonetim/uretim/etiketler/{orderId}");
        Assert.Equal(3, pozHtml.Split("class=\"lb\"").Length - 1); // 2 + 1 adet
        Assert.Contains($"S{orderId}-{designs[0]}-2", pozHtml);
        Assert.Contains("<svg", pozHtml);
        var partHtml = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/uretim/etiketler/{orderId}?tur=parca&boyut=termal"));
        Assert.Contains($"S{orderId}-{designs[0]}-2-1", partHtml);
        Assert.DoesNotContain($"S{orderId}-{designs[1]}-1-1", partHtml); // sistemsiz pozun parçası yok
        Assert.Contains("Kasa", partHtml);

        async Task<string> Scan(string code, string station)
        {
            var response = await client.PostFormAsync("/yonetim/uretim/okut", token, ("code", code), ("station", station));
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            return WebUtility.HtmlDecode(await client.GetStringAsync(response.Headers.Location!.ToString()));
        }
        Task<OrderStatus> Status() => app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync());

        Assert.Contains("bir üretim etiketi değil", await Scan("ABC", ProductionStations.Cutting));
        Assert.Contains("etiket 5. adedi gösteriyor", await Scan(ProductionLabels.PozCode(orderId, designs[1], 5), ProductionStations.Cutting));

        // Plan aşamasındaki sipariş okutmayla üretime geçmez; onaylıysa geçer
        await Scan(ProductionLabels.PozCode(orderId, designs[0], 1), ProductionStations.Cutting);
        Assert.Equal(OrderStatus.Plan, await Status());
        await client.PostFormAsync($"/yonetim/siparisler/durum/{orderId}", token, ("status", OrderStatus.Approved.ToString()));
        Assert.Contains("Sipariş üretime alındı", await Scan(ProductionLabels.PartCode(orderId, designs[0], 2, 3), ProductionStations.Cutting)); // parça etiketi pozun adedini sayar
        Assert.Equal(OrderStatus.InProduction, await Status());
        Assert.Contains("zaten okutulmuş", await Scan(ProductionLabels.PozCode(orderId, designs[0], 2), ProductionStations.Cutting));

        await Scan(ProductionLabels.PozCode(orderId, designs[0], 1), ProductionStations.Packed);
        await Scan(ProductionLabels.PozCode(orderId, designs[0], 2), ProductionStations.Packed);
        Assert.Equal(OrderStatus.InProduction, await Status());
        Assert.Contains("sipariş depoya alındı", await Scan(ProductionLabels.PozCode(orderId, designs[1], 1), ProductionStations.Packed));
        Assert.Equal(OrderStatus.Warehouse, await Status());

        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{orderId}"));
        Assert.Contains("Üretim ilerlemesi", details);
        Assert.Contains("3 / 3 adet", details);
        Assert.Contains("2 / 3 adet", details);
        Assert.Equal(5, await app.WithDbAsync(db => db.ProductionScans.CountAsync(s => s.ProductionOrderId == orderId)));
    }
}
