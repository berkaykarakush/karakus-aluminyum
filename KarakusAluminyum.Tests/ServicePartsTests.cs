using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Öneri 7.1: servis parçaları stoktan düşer, ücretli servis satış faturası taslağına dönüşür (stok iki kez düşmez).
[Collection("postgres")]
public sealed class ServicePartsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Parts_LeaveStock_AndPaidServiceBecomesInvoice()
    {
        await using var app = postgres.CreateApp("service_parts");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var (gasket, requestId) = await app.WithDbAsync(async db =>
        {
            var gasket = new Product { Code = "CT-1", Name = "Kanat contası", Unit = "Metre", SalePrice = 150, VatRate = 20 };
            db.Products.Add(gasket);
            await db.SaveChangesAsync();
            db.StockMovements.Add(new StockMovement { ProductId = gasket.Id, Date = new DateOnly(2026, 1, 1), Kind = StockMovementKind.Opening, Quantity = 10 });
            var request = new ServiceRequest { Number = "SRV-T-1", CustomerId = customer, ReceivedOn = new DateOnly(2026, 11, 1), Complaint = "Su alıyor", Warranty = ServiceWarranty.Paid };
            db.ServiceRequests.Add(request);
            await db.SaveChangesAsync();
            return (gasket.Id, request.Id);
        });
        Task<decimal> Stock() => app.WithDbAsync(db => db.StockMovements.Where(m => m.ProductId == gasket).SumAsync(m => m.Quantity));

        Assert.Contains("data-price=\"150\"", await client.GetStringAsync($"/yonetim/servis/ziyaret/ekle/{requestId}"));
        await client.PostFormAsync($"/yonetim/servis/ziyaret/ekle/{requestId}", token, ("Date", "2026-11-05"), ("WorkDone", "Conta değişti"), ("Result", "Resolved"), ("LaborCharge", "200"),
            ("Parts[0].ProductId", gasket.ToString()), ("Parts[0].Description", ""), ("Parts[0].Quantity", "3"), ("Parts[0].UnitPrice", "150"),
            ("Parts[1].Description", "Silikon"), ("Parts[1].Quantity", "1"), ("Parts[1].Unit", "Tüp"), ("Parts[1].UnitPrice", "0"));
        Assert.Equal(7, await Stock());
        var visit = await app.WithDbAsync(db => db.ServiceVisits.AsNoTracking().Include(v => v.Parts).SingleAsync(v => v.ServiceRequestId == requestId));
        Assert.Equal(("Kanat contası", "Metre"), (visit.Parts.Single(p => p.ProductId == gasket).Description, visit.Parts.Single(p => p.ProductId == gasket).Unit)); // boş açıklama karttan

        await client.PostFormAsync($"/yonetim/servis/fatura/{visit.Id}", token);
        await client.PostFormAsync($"/yonetim/servis/fatura/{visit.Id}", token); // ikinci fatura açılmaz
        var invoice = await app.WithDbAsync(db => db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.CustomerId == customer));
        Assert.Equal((InvoiceKind.Sale, InvoiceStatus.Draft), (invoice.Kind, invoice.Status));
        Assert.Equal(2, invoice.Lines.Count); // ücretsiz silikon basılmaz
        Assert.All(invoice.Lines, l => Assert.Null(l.ProductId));
        Assert.Equal(650m, invoice.Net); // 3 × 150 + 200
        Assert.Equal(invoice.Id, await app.WithDbAsync(db => db.ServiceVisits.Where(v => v.Id == visit.Id).Select(v => v.InvoiceId).SingleAsync()));
        Assert.Contains($"/yonetim/muhasebe/faturalar/detay/{invoice.Id}", await client.GetStringAsync($"/yonetim/servis/detay/{requestId}"));

        // Fatura onaylanınca stok ikinci kez düşmez (satırlar stok kartına bağlı değil)
        await client.PostFormAsync($"/yonetim/muhasebe/faturalar/onayla/{invoice.Id}", token);
        Assert.Equal(InvoiceStatus.Approved, await app.WithDbAsync(db => db.Invoices.Where(i => i.Id == invoice.Id).Select(i => i.Status).SingleAsync()));
        Assert.Equal(7, await Stock());

        // Faturalı form silinemez; fatura iptal edilince form silinir ve parça stoğa döner
        await client.PostFormAsync($"/yonetim/servis/ziyaret/sil/{visit.Id}", token);
        Assert.True(await app.WithDbAsync(db => db.ServiceVisits.AnyAsync(v => v.Id == visit.Id)));
        await app.WithDbAsync(db => db.Invoices.Where(i => i.Id == invoice.Id).ExecuteUpdateAsync(x => x.SetProperty(i => i.Status, InvoiceStatus.Cancelled)));
        await client.PostFormAsync($"/yonetim/servis/ziyaret/sil/{visit.Id}", token);
        Assert.False(await app.WithDbAsync(db => db.ServiceVisits.AnyAsync(v => v.Id == visit.Id)));
        Assert.Equal(10, await Stock());
        Assert.Equal(3, await app.WithDbAsync(db => db.StockMovements.CountAsync(m => m.ProductId == gasket))); // açılış, kullanım, geri alma
    }
}
