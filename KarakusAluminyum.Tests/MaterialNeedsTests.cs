using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Öneri 7.4: kritik stok ve açık siparişlerin metrajına göre sipariş önerisi ve tedarikçiye taslak alış faturası.
[Collection("postgres")]
public sealed class MaterialNeedsTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Suggestions_CountCriticalStockAndCommittedOrders()
    {
        await using var app = postgres.CreateApp("material_needs");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var ids = await app.WithDbAsync(async db =>
        {
            var low = new Product { Code = "A", Name = "Kritik altı", Unit = "Metre", CriticalStock = 5, PurchasePrice = 10 };
            var needed = new Product { Code = "B", Name = "Siparişte lazım", Unit = "Adet", PurchasePrice = 4 };
            var targeted = new Product { Code = "C", Name = "Hedefli", Unit = "Boy", CriticalStock = 2, TargetStock = 12, PurchasePrice = 100 };
            var fine = new Product { Code = "D", Name = "Yeterli", Unit = "Adet", CriticalStock = 5 };
            var supplier = new Customer { Name = "Profil tedarikçi", PartyType = PartyType.Supplier };
            db.AddRange(low, needed, targeted, fine, supplier);
            await db.SaveChangesAsync();
            foreach (var (p, q) in new[] { (low, 3m), (needed, 2m), (targeted, 1.5m), (fine, 20m) })
                db.StockMovements.Add(new StockMovement { ProductId = p.Id, Date = new DateOnly(2026, 1, 1), Kind = StockMovementKind.Opening, Quantity = q });
            var system = new ProfileSystem { Name = "İhtiyaç serisi", MaterialsJson = MaterialTakeoff.SerializeRules(new[] { new MaterialRule { ProductId = needed.Id, Basis = MaterialBases.Piece, Factor = 5 } }) };
            db.ProfileSystems.Add(system);
            await db.SaveChangesAsync();
            return (Low: low.Id, Needed: needed.Id, Targeted: targeted.Id, Fine: fine.Id, Supplier: supplier.Id, System: system.Id);
        });
        await DrawingLists.PostAsync(client, token, "İhtiyaç listesi", DrawingLists.Rows(DrawingLists.Row("Salon", 1000, 1000, quantity: 1, system: ids.System)));
        var listId = await DrawingLists.ListIdAsync(app, "İhtiyaç listesi");
        await app.WithDbAsync(async db =>
        {
            db.ProductionOrders.AddRange(
                new ProductionOrder { OrderNumber = "SP-NEED-1", CustomerId = customer, Title = "Onaylı", DrawingListId = listId, Status = OrderStatus.Approved, OrderDate = new DateOnly(2026, 11, 1) },
                new ProductionOrder { OrderNumber = "SP-NEED-2", CustomerId = customer, Title = "Planda", DrawingListId = listId, Status = OrderStatus.Plan, OrderDate = new DateOnly(2026, 11, 1) },
                new ProductionOrder { OrderNumber = "SP-NEED-3", CustomerId = customer, Title = "Düşülmüş", DrawingListId = listId, Status = OrderStatus.InProduction, StockDeducted = true, OrderDate = new DateOnly(2026, 11, 1) });
            return await db.SaveChangesAsync();
        });

        var rows = await app.WithServiceAsync<MaterialPlanning, List<ReorderRow>>(p => p.ReorderAsync());
        decimal S(int id) => rows.SingleOrDefault(r => r.Product.Id == id)?.Suggested ?? 0;
        Assert.Equal(7m, S(ids.Low));        // 3 → 2 × 5 = 10
        Assert.Equal(3m, S(ids.Needed));     // 2 − 5 (yalnızca onaylı sipariş) = −3 → 0'a tamamla
        Assert.Equal(11m, S(ids.Targeted));  // 1,5 → hedef 12 = 10,5 → boy olduğu için 11
        Assert.Equal(0m, S(ids.Fine));
        Assert.Equal(new[] { "SP-NEED-1" }, rows.Single(r => r.Product.Id == ids.Needed).Orders);

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/uretim/malzeme-ihtiyaci"));
        Assert.Contains("Siparişte lazım", page);
        Assert.Contains("SP-NEED-1", page);
        Assert.DoesNotContain("Yeterli", page);

        await client.PostFormAsync("/yonetim/uretim/malzeme-ihtiyaci", token, ("supplierId", ids.Supplier.ToString()), ("productIds", ids.Low.ToString()), ("productIds", ids.Needed.ToString()),
            ($"miktar_{ids.Low}", "8"), ($"miktar_{ids.Needed}", "3"), ($"miktar_{ids.Targeted}", "11")); // C işaretsiz: atlanır
        var invoice = await app.WithDbAsync(db => db.Invoices.AsNoTracking().Include(i => i.Lines).SingleAsync(i => i.CustomerId == ids.Supplier));
        Assert.Equal((InvoiceKind.Purchase, InvoiceStatus.Draft), (invoice.Kind, invoice.Status));
        Assert.Equal(new[] { (ids.Low, 8m), (ids.Needed, 3m) }, invoice.Lines.OrderBy(l => l.SortOrder).Select(l => (l.ProductId!.Value, l.Quantity)));
        Assert.Equal(92m, invoice.Net); // 8 × 10 + 3 × 4
    }
}
