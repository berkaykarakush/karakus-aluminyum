using System.Net;
using System.Net.Sockets;
using System.Text;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Barkod yazıcısı çıktısı: ZPL (Zebra) ve TSPL (TSC/Xprinter); ağdaki yazıcıya 9100 portundan ham gönderim.
public sealed class PrinterLabelTests
{
    private static readonly LabelContent[] Labels =
    {
        new("SP-2026-000001  Poz 1 - 1/2", "1200 × 1400 mm", "Salon - Şükrü Çağ", "PVC 70 - 1 Açılır", "S1-10-1"),
        new("SP-2026-000001  Poz 1 - 2/2", "1200 × 1400 mm", "Salon ^XZ~JR", "PVC 70", "S1-10-2")
    };

    [Fact]
    public void Zpl_OneFormatPerLabel_SizedInDots()
    {
        var printer = new LabelPrinter { Language = LabelLanguages.Zpl, Dpi = 203, WidthMm = 100, HeightMm = 50, AsciiOnly = true, Darkness = 20 };
        var zpl = PrinterLabels.Zpl(printer, Labels);
        Assert.Equal(2, zpl.Split("^XA").Length - 1);
        Assert.Equal(2, zpl.Split("^XZ").Length - 1); // alan verisindeki ^XZ temizlendi, etiketi erken bitirmez
        Assert.Contains("^PW800", zpl);  // 100 mm × 8 nokta
        Assert.Contains("^LL400", zpl);
        Assert.Contains("~SD20", zpl);
        Assert.Contains("^BCN,120,Y,N,N^FDS1-10-2^FS", zpl);
        Assert.Contains("Sukru Cag", zpl);
        Assert.Contains("1200 x 1400 mm", zpl);
        Assert.DoesNotContain("^CI28", zpl);
        Assert.DoesNotContain("~JR", zpl);

        printer.AsciiOnly = false;
        printer.Dpi = 300;
        zpl = PrinterLabels.Zpl(printer, Labels);
        Assert.Contains("^CI28", zpl);
        Assert.Contains("Şükrü Çağ", Encoding.UTF8.GetString(PrinterLabels.Render(printer, Labels)));
        Assert.Contains("^PW1200", zpl); // 300 dpi = 12 nokta/mm
    }

    [Fact]
    public void Tspl_SetsSizeOnce_AndPrintsEachLabel()
    {
        var printer = new LabelPrinter { Language = LabelLanguages.Tspl, Dpi = 203, WidthMm = 70, HeightMm = 37, GapMm = 3, AsciiOnly = false };
        var tspl = Encoding.ASCII.GetString(PrinterLabels.Render(printer, Labels));
        Assert.StartsWith("SIZE 70 mm,37 mm\r\nGAP 3 mm,0 mm\r\n", tspl);
        Assert.Equal(2, tspl.Split("PRINT 1,1").Length - 1);
        Assert.Equal(2, tspl.Split("CLS\r\n").Length - 1);
        Assert.Contains("\"128\",", tspl);
        Assert.Contains("\"S1-10-1\"", tspl);
        Assert.Contains("Sukru Cag", tspl); // TSPL fontu ASCII
        Assert.DoesNotContain("?", tspl);
        foreach (var line in tspl.Split("\r\n").Where(l => l.StartsWith("TEXT")))
            Assert.True(line.Split('"')[3].Length <= (70 * 8 - 32) / 12); // etikete sığacak kadar kısaltılır
    }

    [Theory]
    [InlineData("192.168.1.50", true)]
    [InlineData("10.0.0.9", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("172.32.1.1", false)]
    [InlineData("8.8.8.8", false)]
    [InlineData("printer.example.com", false)]
    [InlineData("", false)]
    public void OnlyLocalNetworkAddresses_AreAllowed(string host, bool allowed) => Assert.Equal(allowed, PrinterLabels.IsLocalAddress(host));

    [Fact]
    public async Task SendAsync_WritesRawBytesToPrinterPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var received = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            using var memory = new MemoryStream();
            await client.GetStream().CopyToAsync(memory);
            return memory.ToArray();
        });
        var printer = new LabelPrinter { Language = LabelLanguages.Zpl, Host = "127.0.0.1", Port = port };
        var data = PrinterLabels.Render(printer, Labels);
        await PrinterLabels.SendAsync(printer, data);
        Assert.Equal(data, await received.WaitAsync(TimeSpan.FromSeconds(5)));
        listener.Stop();
        await Assert.ThrowsAsync<InvalidOperationException>(() => PrinterLabels.SendAsync(new LabelPrinter { Host = "8.8.8.8" }, data));
    }
}

[Collection("postgres")]
public sealed class PrinterPageTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Printer_IsSaved_AndOrderLabelsDownloadAsZpl()
    {
        await using var app = postgres.CreateApp("label_printers");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var bad = await client.PostFormAsync("/yonetim/uretim/yazicilar/ekle", token, ("Name", "Dış"), ("Language", "zpl"), ("Dpi", "203"), ("WidthMm", "100"), ("HeightMm", "50"), ("GapMm", "2"), ("Port", "9100"), ("Host", "8.8.8.8"));
        Assert.Contains("yerel ağ IP", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
        foreach (var name in new[] { "Eski Zebra", "Zebra ZD421" })
            await client.PostFormAsync("/yonetim/uretim/yazicilar/ekle", token, ("Name", name), ("Language", "zpl"), ("Dpi", "203"), ("WidthMm", "100"), ("HeightMm", "50"), ("GapMm", "2"), ("Port", "9100"),
                ("AsciiOnly", "true"), ("IsDefault", "true"), ("IsActive", "true"));
        var printers = await app.WithDbAsync(db => db.LabelPrinters.AsNoTracking().OrderBy(p => p.Id).ToListAsync());
        Assert.Equal(new[] { false, true }, printers.Select(p => p.IsDefault)); // tek varsayılan

        await DrawingLists.PostAsync(client, token, "Yazıcı listesi", DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 2)));
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Yazıcı işi"), ("DrawingListId", (await DrawingLists.ListIdAsync(app, "Yazıcı listesi")).ToString()), ("OrderDate", DateTime.Today.ToString("yyyy-MM-dd")));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Yazıcı işi").Select(o => o.Id).SingleAsync());

        var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/uretim/etiketler/{orderId}"));
        Assert.Contains("Zebra ZD421 (ZPL", page);
        Assert.True(page.IndexOf("Zebra ZD421", StringComparison.Ordinal) < page.IndexOf("Eski Zebra", StringComparison.Ordinal)); // varsayılan önce
        var file = await client.GetAsync($"/yonetim/uretim/etiketler/{orderId}/dosya?tur=poz&yazici={printers[1].Id}");
        var zpl = await file.Content.ReadAsStringAsync();
        Assert.Equal(2, zpl.Split("^XA").Length - 1);
        Assert.Contains("poz-etiketleri.zpl", file.Content.Headers.ContentDisposition!.ToString());

        var sent = await client.PostFormAsync($"/yonetim/uretim/etiketler/{orderId}/yazdir", token, ("yazici", printers[1].Id.ToString()), ("tur", "poz"));
        Assert.Contains("ağ adresi yok", WebUtility.HtmlDecode(await client.GetStringAsync(sent.Headers.Location!.ToString())));
    }
}
