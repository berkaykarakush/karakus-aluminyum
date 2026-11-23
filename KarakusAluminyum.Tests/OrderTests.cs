using System.Net;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace KarakusAluminyum.Tests;

// Üretim siparişleri (Ercom sipariş formu): tekliften/sözleşmeden sipariş, durum akışı, işaretler, cam, montaj, irsaliye, plan.
[Collection("postgres")]
public sealed class OrderTests(PostgresFixture postgres)
{
    private static string Tag(string prefix = "Sipariş") => prefix + " " + Guid.NewGuid().ToString("N")[..8];

    // Kabul edilmiş, iki pozlu (3 + 2 adet) çizim listesine bağlı teklif.
    private static async Task<(int QuoteId, int ListId, int[] Designs)> AcceptedQuoteAsync(AppFactory app, HttpClient client, string token, QuoteStatus status = QuoteStatus.Accepted)
    {
        var list = Tag("Liste");
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row("Salon", 1500, 1400, quantity: 3), DrawingLists.Row("Mutfak", 1000, 1200, quantity: 2)));
        var listId = await DrawingLists.ListIdAsync(app, list);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var quoteId = await app.WithDbAsync(async db =>
        {
            var quote = new Quote { QuoteNumber = "T-" + Guid.NewGuid().ToString("N")[..10], Title = Tag("İş"), CustomerId = customer, Status = status, DrawingListId = listId, TaxRate = 20 };
            db.Quotes.Add(quote);
            await db.SaveChangesAsync();
            return quote.Id;
        });
        var designs = await app.WithDbAsync(db => db.WindowDesigns.Where(d => d.DrawingListId == listId).OrderBy(d => d.SortOrder).Select(d => d.Id).ToArrayAsync());
        return (quoteId, listId, designs);
    }

    private static Task<ProductionOrder> OrderAsync(AppFactory app, int quoteId) => app.WithDbAsync(db => db.ProductionOrders.AsNoTracking().Include(o => o.Events).Include(o => o.DeliveryNotes).ThenInclude(n => n.Lines).SingleAsync(o => o.QuoteId == quoteId));

    [Fact]
    public async Task AcceptedQuote_BecomesOneApprovedOrder_LinkedToItsDrawings()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (quoteId, listId, _) = await AcceptedQuoteAsync(app, client, token);

        Assert.Contains("Sipariş oluştur", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{quoteId}")));
        var response = await client.PostFormAsync($"/yonetim/siparisler/tekliften/{quoteId}", token);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var order = await OrderAsync(app, quoteId);
        Assert.Matches($"^SP-{DateTime.Today.Year}-\\d{{6}}$", order.OrderNumber);
        Assert.Equal((OrderStatus.Approved, listId), (order.Status, order.DrawingListId!.Value));
        Assert.Equal("Test Mah. 1", order.InstallAddress);
        Assert.Contains("tekliften oluşturuldu", Assert.Single(order.Events).Text);

        await client.PostFormAsync($"/yonetim/siparisler/tekliften/{quoteId}", token); // ikinci kez: aynı sipariş
        Assert.Equal(1, await app.WithDbAsync(db => db.ProductionOrders.CountAsync(o => o.QuoteId == quoteId)));
        Assert.Contains("Siparişi aç", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/crm/teklifler/detay/{quoteId}")));

        var details = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/detay/{order.Id}"));
        Assert.Contains("2 poz · 5 adet", details);
        Assert.Contains("Salon", details);
        Assert.Contains(order.OrderNumber, WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/cizim/detay/{listId}")));
    }

    [Fact]
    public async Task DraftQuote_CannotBecomeOrder()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (quoteId, _, _) = await AcceptedQuoteAsync(app, client, token, QuoteStatus.Draft);
        await client.PostFormAsync($"/yonetim/siparisler/tekliften/{quoteId}", token);
        Assert.False(await app.WithDbAsync(db => db.ProductionOrders.AnyAsync(o => o.QuoteId == quoteId)));
    }

    [Fact]
    public async Task StatusFlow_WritesDatesAndHistory_AndClosedOrdersStayClosed()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (quoteId, _, _) = await AcceptedQuoteAsync(app, client, token);
        await client.PostFormAsync($"/yonetim/siparisler/tekliften/{quoteId}", token);
        var id = (await OrderAsync(app, quoteId)).Id;

        foreach (var status in new[] { OrderStatus.InProduction, OrderStatus.Warehouse, OrderStatus.Shipped, OrderStatus.Delivered })
            await client.PostFormAsync($"/yonetim/siparisler/durum/{id}", token, ("status", status.ToString()));
        var order = await OrderAsync(app, quoteId);
        Assert.Equal(OrderStatus.Delivered, order.Status);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), order.ShippedOn);
        Assert.Equal(DateOnly.FromDateTime(DateTime.Today), order.DeliveredOn);
        Assert.Equal(5, order.Events.Count);
        Assert.Contains(order.Events, e => e.Text == "Durum: Depo / Sevkiyat → Sevk edildi." && e.User == AppFactory.AdminEmail);

        await client.PostFormAsync($"/yonetim/siparisler/durum/{id}", token, ("status", OrderStatus.Plan.ToString()));
        Assert.Equal(OrderStatus.Delivered, (await OrderAsync(app, quoteId)).Status); // teslim edilen plana dönmez

        await client.PostFormAsync($"/yonetim/siparisler/durum/{id}", token, ("status", OrderStatus.Shipped.ToString()));
        order = await OrderAsync(app, quoteId);
        Assert.Equal((OrderStatus.Shipped, (DateOnly?)null), (order.Status, order.DeliveredOn)); // teslim geri alınabilir

        await client.PostFormAsync($"/yonetim/siparisler/durum/{id}", token, ("status", OrderStatus.Cancelled.ToString()), ("note", "Müşteri vazgeçti"));
        await client.PostFormAsync($"/yonetim/siparisler/durum/{id}", token, ("status", OrderStatus.Approved.ToString()));
        order = await OrderAsync(app, quoteId);
        Assert.Equal(OrderStatus.Cancelled, order.Status);
        Assert.Contains(order.Events, e => e.Text.EndsWith("Müşteri vazgeçti"));
    }

    [Fact]
    public async Task FlagsGlassAndInstallation_AreSaved_WithHistory()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (quoteId, _, _) = await AcceptedQuoteAsync(app, client, token);
        await client.PostFormAsync($"/yonetim/siparisler/tekliften/{quoteId}", token);
        var id = (await OrderAsync(app, quoteId)).Id;
        var (supplier, customerOnly) = await app.WithDbAsync(async db =>
        {
            var s = new Customer { Name = Tag("Camcı"), PartyType = PartyType.Supplier };
            var c = new Customer { Name = Tag("Müşteri"), PartyType = PartyType.Customer };
            db.Customers.AddRange(s, c);
            await db.SaveChangesAsync();
            return (s.Id, c.Id);
        });

        await client.PostFormAsync($"/yonetim/siparisler/isaretler/{id}", token, ("stockDeducted", "true"), ("reserved", "true")); // stok işareti elle konmaz, metrajdan düşülür
        await client.PostFormAsync($"/yonetim/siparisler/cam/{id}", token, ("glassSupplierId", customerOnly.ToString()), ("glassOrderedOn", "2026-11-20"));
        Assert.Null((await OrderAsync(app, quoteId)).GlassSupplierId); // camcı tedarikçi cari olmalı
        await client.PostFormAsync($"/yonetim/siparisler/cam/{id}", token, ("glassSupplierId", supplier.ToString()), ("glassOrderedOn", "2026-11-20"), ("glassExpectedOn", "2026-11-27"));

        await client.PostFormAsync("/yonetim/siparisler/montajcilar/ekle", token, ("Name", "Test ekibi"), ("TeamSize", "3"), ("IsActive", "true"));
        var installer = await app.WithDbAsync(db => db.Installers.Where(i => i.Name == "Test ekibi").Select(i => i.Id).FirstAsync());
        await client.PostFormAsync($"/yonetim/siparisler/montaj/{id}", token, ("installerId", installer.ToString()), ("installationDate", DateTime.Today.AddDays(3).ToString("yyyy-MM-dd")));

        var order = await OrderAsync(app, quoteId);
        Assert.True(!order.StockDeducted && order.Reserved && !order.SentToCutting);
        Assert.Equal((supplier, new DateOnly(2026, 11, 27)), (order.GlassSupplierId!.Value, order.GlassExpectedOn!.Value));
        Assert.Equal(installer, order.InstallerId);
        Assert.Contains(order.Events, e => e.Text == "Rezerve: evet.");
        Assert.Contains(order.Events, e => e.Text.StartsWith("Montaj: ekip Test ekibi"));

        var plan = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/siparisler/planlama"));
        Assert.Contains("Montaj takvimi", plan);
        Assert.Contains(order.OrderNumber, plan);
        Assert.Contains("Beklenen camlar", plan);
    }

    [Fact]
    public async Task DeliveryNotes_TrackRemaining_RejectOverDelivery_AndShipWhenComplete()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (quoteId, _, designs) = await AcceptedQuoteAsync(app, client, token);
        await client.PostFormAsync($"/yonetim/siparisler/tekliften/{quoteId}", token);
        var id = (await OrderAsync(app, quoteId)).Id;

        var form = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/irsaliye/ekle/{id}"));
        Assert.Contains("name=\"Lines[0].Quantity\" value=\"3\"", form); // kalan adet hazır gelir

        // Kısmi sevk: Salon'dan 2 adet
        var partial = await client.PostFormAsync($"/yonetim/siparisler/irsaliye/ekle/{id}", token, ("Date", "2026-11-20"), ("Vehicle", "35 ABC 123"),
            ("Lines[0].WindowDesignId", designs[0].ToString()), ("Lines[0].Quantity", "2"), ("Lines[1].WindowDesignId", designs[1].ToString()), ("Lines[1].Quantity", "0"));
        Assert.Equal(HttpStatusCode.Redirect, partial.StatusCode);
        var order = await OrderAsync(app, quoteId);
        var note = Assert.Single(order.DeliveryNotes);
        Assert.Matches("^IRS-2026-\\d{6}$", note.Number);
        Assert.Equal(2m, Assert.Single(note.Lines).Quantity);
        Assert.Equal(OrderStatus.Approved, order.Status);

        // Fazla sevk reddedilir: Salon'dan kalan 1
        var over = await client.PostFormAsync($"/yonetim/siparisler/irsaliye/ekle/{id}", token, ("Date", "2026-11-21"), ("Lines[0].WindowDesignId", designs[0].ToString()), ("Lines[0].Quantity", "2"));
        Assert.Equal(HttpStatusCode.OK, over.StatusCode);
        Assert.Contains("en fazla 1 adet", WebUtility.HtmlDecode(await over.Content.ReadAsStringAsync()));

        // Kalanların tamamı + ek satır: sipariş "Sevk edildi"
        await client.PostFormAsync($"/yonetim/siparisler/irsaliye/ekle/{id}", token, ("Date", "2026-11-21"),
            ("Lines[0].WindowDesignId", designs[0].ToString()), ("Lines[0].Quantity", "1"), ("Lines[1].WindowDesignId", designs[1].ToString()), ("Lines[1].Quantity", "2"),
            ("Lines[2].Description", "Sineklik"), ("Lines[2].Quantity", "5"), ("Lines[2].Unit", "Adet"));
        order = await OrderAsync(app, quoteId);
        Assert.Equal(2, order.DeliveryNotes.Count);
        Assert.Equal(OrderStatus.Shipped, order.Status);
        Assert.Contains(order.Events, e => e.Text.Contains("Tüm pozlar sevk edildi."));

        var printed = order.DeliveryNotes.OrderBy(n => n.Id).Last();
        var html = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/irsaliye/{printed.Id}"));
        Assert.Contains("SEVK BELGESİ", html);
        Assert.Contains("Sineklik", html);
        Assert.Contains("resmi irsaliye", html);

        // İrsaliyesi olan sipariş iptal edilemez
        await client.PostFormAsync($"/yonetim/siparisler/durum/{id}", token, ("status", OrderStatus.Cancelled.ToString()));
        Assert.Equal(OrderStatus.Shipped, (await OrderAsync(app, quoteId)).Status);
    }

    [Fact]
    public async Task ContractOrder_UsesQuoteOrderIfExists_AndPrintsWorkOrderAndHandover()
    {
        var app = postgres.Shared;
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var (quoteId, _, _) = await AcceptedQuoteAsync(app, client, token);
        await client.PostFormAsync($"/yonetim/sozlesmeler/tekliften/{quoteId}", token);
        var contractId = await app.WithDbAsync(db => db.Contracts.Where(c => c.QuoteId == quoteId).Select(c => c.Id).SingleAsync());

        await client.PostFormAsync($"/yonetim/siparisler/sozlesmeden/{contractId}", token);
        var order = await OrderAsync(app, quoteId);
        Assert.Equal((contractId, OrderStatus.AwaitingApproval), (order.ContractId!.Value, order.Status)); // imzasız sözleşme onay bekler
        await client.PostFormAsync($"/yonetim/siparisler/tekliften/{quoteId}", token);
        Assert.Equal(1, await app.WithDbAsync(db => db.ProductionOrders.CountAsync(o => o.QuoteId == quoteId)));

        var work = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/is-emri/{order.Id}"));
        Assert.Contains("İŞ EMRİ", work);
        Assert.Contains("Poz : 1 / Ad : 3 - Salon", work);
        Assert.DoesNotContain("TL", work); // fiyat yok

        var handover = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/siparisler/teslim-tutanagi/{order.Id}"));
        Assert.Contains("TESLİM TUTANAĞI", handover);
        Assert.Contains("<strong>7 gün</strong>", handover); // sözleşmenin muayene süresi
        Assert.Contains("Toplam 2 poz, 5 adet", handover);
    }

    [Fact]
    public async Task ManualOrder_ListPlanningAndCapacity()
    {
        await using var app = postgres.CreateApp("orders_planning");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        var list = Tag("Liste");
        await DrawingLists.PostAsync(client, token, list, DrawingLists.Rows(DrawingLists.Row("Kapı", 1000, 2000, quantity: 5)));
        var listId = await DrawingLists.ListIdAsync(app, list);
        var past = DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd");

        var bad = await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Geç"), ("OrderDate", DateTime.Today.ToString("yyyy-MM-dd")), ("PlannedShipDate", past));
        Assert.Equal(HttpStatusCode.OK, bad.StatusCode); // sevk tarihi sipariş tarihinden önce olamaz

        var created = await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Elle sipariş"), ("DrawingListId", listId.ToString()),
            ("OrderDate", DateTime.Today.AddDays(-10).ToString("yyyy-MM-dd")), ("PlannedShipDate", past));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var order = await app.WithDbAsync(db => db.ProductionOrders.AsNoTracking().SingleAsync(o => o.Title == "Elle sipariş"));
        Assert.Equal(OrderStatus.Plan, order.Status);

        var index = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/siparisler"));
        Assert.Contains("(gecikti)", index);
        Assert.Contains("10,00", index); // 5 × 1,0 × 2,0 m²
        Assert.Contains(order.OrderNumber, WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/siparisler?geciken=true")));
        Assert.Contains("Siparişler", WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim")));

        Assert.Equal(HttpStatusCode.Redirect, (await client.PostFormAsync("/yonetim/siparisler/planlama/kapasite", token, ("capacity", "8"))).StatusCode);
        var plan = WebUtility.HtmlDecode(await client.GetStringAsync("/yonetim/siparisler/planlama"));
        Assert.Contains("Sevk tarihi geçen siparişler (1)", plan);
        Assert.Contains("value=\"8\"", plan);

        // Plan aşamasındaki sipariş silinebilir
        await client.PostFormAsync($"/yonetim/siparisler/sil/{order.Id}", token);
        Assert.False(await app.WithDbAsync(db => db.ProductionOrders.AnyAsync(o => o.Id == order.Id)));
    }

    [Fact]
    public async Task Accountant_CannotOpenOrders()
    {
        var app = postgres.Shared;
        var email = $"{Guid.NewGuid():N}@test.local";
        await using (var scope = app.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<KarakusAluminyum.Web.Models.ApplicationUser>>();
            var user = new KarakusAluminyum.Web.Models.ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
            Assert.True((await users.CreateAsync(user, AppFactory.Password)).Succeeded);
            Assert.True((await users.AddToRoleAsync(user, "Accountant")).Succeeded);
        }
        var client = app.NewClient();
        await client.LoginAsync(email);
        var response = await client.GetAsync("/yonetim/siparisler");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
