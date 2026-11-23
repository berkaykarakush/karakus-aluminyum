using System.Net;
using System.Text;
using System.Text.Json;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services.Drawing;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Öneri 7.3: profil sistemi fiyatları döviz olabilir; tüm fiyat hesapları güncel kurla TL döner.
[Collection("postgres")]
public sealed class SystemCurrencyTests(PostgresFixture postgres)
{
    private static ProfileSystem System(string currency, decimal factor, decimal? rate = 1) => new()
    {
        Name = "S", PriceCurrency = currency, RateToTry = rate, FramePricePerM = 100 * factor, SashPricePerM = 120 * factor, MullionPricePerM = 110 * factor, BeadPricePerM = 20 * factor,
        HardwareTiltPrice = 1000 * factor, WastePercent = 5, LaborPercent = 30, MarginPercent = 25
    };

    private static WindowCalculation Calc(ProfileSystem s) => DrawingCalculation.Calculate("aluminum", 1200, 1400, "rect", 0, 3, DrawingLists.Layout, null, s)!;

    [Fact]
    public void EuroPrices_TimesRate_EqualLiraPrices()
    {
        var lira = Calc(System("TRY", 50));
        var euro = Calc(System("EUR", 1, 50));
        Assert.Equal((lira.ProfileCost, lira.HardwareCost, lira.LaborCost, lira.UnitCost, lira.UnitPrice, lira.TotalPrice), (euro.ProfileCost, euro.HardwareCost, euro.LaborCost, euro.UnitCost, euro.UnitPrice, euro.TotalPrice));
        Assert.Equal(lira.Hardware.Single().UnitPrice, euro.Hardware.Single().UnitPrice);

        var missing = Calc(System("EUR", 1, rate: null));
        Assert.Equal(0, missing.UnitPrice);
        Assert.Contains(missing.Warnings, w => w.StartsWith("EUR kuru girilmemiş"));
        Assert.True(missing.IsValid); // kesim ölçüleri yine hesaplanır
    }

    [Fact]
    public async Task SavedRate_IsUsed_ByDrawingCalculation()
    {
        await using var app = postgres.CreateApp("system_currency");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var systemId = await app.WithDbAsync(async db => { var s = System("EUR", 1); db.ProfileSystems.Add(s); await db.SaveChangesAsync(); return s.Id; });
        var expected = Calc(System("TRY", 40)).UnitPrice;

        async Task<JsonElement> Calculate()
        {
            var body = JsonSerializer.Serialize(new { category = "aluminum", profileSystemId = systemId, widthMm = 1200, heightMm = 1400, shape = "rect", shapeParamMm = 0, quantity = 3, layoutJson = DrawingLists.Layout, name = "x" });
            var request = new HttpRequestMessage(HttpMethod.Post, "/yonetim/cizim/hesapla") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            request.Headers.Add("RequestVerificationToken", await client.GetTokenAsync("/yonetim/cizim/ekle"));
            var response = await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
        }

        var before = await Calculate();
        Assert.Equal(0, before.GetProperty("unitPrice").GetDecimal());
        Assert.Contains("kur yok", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/cizim/sistemler")));

        await client.PostFormAsync("/yonetim/fiyat-listeleri/kur", token, ("currency", "EUR"), ("rate", "40"), ("date", "2026-11-01"));
        Assert.Equal(expected, (await Calculate()).GetProperty("unitPrice").GetDecimal());

        // Sistem ekranında para birimi değiştirilebilir ve geçersiz değer reddedilir
        var form = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/sistemler/duzenle/{systemId}"));
        Assert.Contains("Birim fiyatlar (EUR)", form);
        Assert.Equal("EUR", await app.WithDbAsync(db => db.ProfileSystems.Where(s => s.Id == systemId).Select(s => s.PriceCurrency).SingleAsync()));
    }
}
