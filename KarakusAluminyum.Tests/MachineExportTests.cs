using System.Net;
using System.Text;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace KarakusAluminyum.Tests;

// Kesim makinesi aktarımı: parçalar optimizasyon sırasıyla, ayarlanan sütun, ayraç ve kodlamayla yazılır.
public sealed class MachineExportTests
{
    private static (ProductionOrder Order, List<MachinePiece> Pieces) Sample(int quantity = 2)
    {
        var system = new ProfileSystem { Id = 3, Name = "Şeffaf seri", BarLengthMm = 6000, KerfMm = 4,
            MaterialsJson = MaterialTakeoff.SerializeRules(new[] { new MaterialRule { ProductId = 9, Basis = MaterialBases.ProfileBars, Key = "Kasa" } }) };
        var design = new WindowDesign { Id = 50, Name = "Salon", WidthMm = 1000, HeightMm = 1200, Quantity = quantity, LayoutJson = DrawingLists.Layout, ProfileSystem = system, ProfileSystemId = 3 };
        var order = new ProductionOrder { Id = 8, OrderNumber = "SP-2026-000008" };
        var (pieces, warnings) = MachineExport.Pieces(order, new[] { design }, new Dictionary<int, Product> { [9] = new() { Id = 9, Code = "K-70", Name = "Kasa" } });
        Assert.Empty(warnings);
        return (order, pieces);
    }

    [Fact]
    public void Pieces_FollowBarPlan_AndCarryPartBarcodes()
    {
        var (_, pieces) = Sample();
        var calc = Web.Services.Drawing.DrawingCalculation.Calculate(new WindowDesign { WidthMm = 1000, HeightMm = 1200, Quantity = 2, LayoutJson = DrawingLists.Layout }, new ProfileSystem { BarLengthMm = 6000, KerfMm = 4 })!;
        Assert.Equal(calc.Cuts.Sum(c => c.Count) * 2, pieces.Count);
        Assert.Equal(pieces.Select(p => p.Barcode).Distinct().Count(), pieces.Count); // her parça tek barkod
        Assert.All(pieces.Where(p => p.Profile == "Kasa"), p => Assert.Equal(("K-70", "45", "45"), (p.ProfileCode, p.AngleLeft, p.AngleRight)));
        Assert.Equal(pieces.Select(p => p.Bar).OrderBy(b => b), pieces.Select(p => p.Bar)); // boy sırasıyla
        foreach (var bar in pieces.GroupBy(p => p.Bar)) Assert.True(bar.Sum(p => p.LengthMm) + 4 * (bar.Count() - 1) <= 6000);
        Assert.Contains(pieces, p => p.Barcode == "S8-50-2-1");
    }

    [Fact]
    public void Build_UsesProfileColumnsDelimiterDecimalAndEncoding()
    {
        var (order, pieces) = Sample();
        var profile = new MachineExportProfile { Name = "Özgenç testere", Columns = "bar,profileCode,length,angleLeft,system", Delimiter = ";", DecimalSeparator = ",", LengthDecimals = 1, Encoding = MachineExportFields.Windows1254 };
        var file = MachineExport.Build(profile, order, pieces, new List<string>());
        var text = Encoding.GetEncoding(1254).GetString(file.Content);
        var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("Boy no;Profil kodu (stok kartı);Kesim ölçüsü (mm);Sol açı;Seri", lines[0]);
        var kasa = pieces.First(p => p.Profile == "Kasa");
        Assert.Contains($"{kasa.Bar};K-70;{kasa.LengthMm:0.0};45;Şeffaf seri".Replace(".", ","), lines);
        Assert.Contains((byte)0xDE, file.Content); // Ş, Windows-1254 kodlamasında 0xDE
        Assert.Equal("SP-2026-000008-ozgenc-testere.csv", file.FileName);
        Assert.Equal(pieces.Count + 1, lines.Length);

        profile = new MachineExportProfile { Name = "Eski", Columns = "profile,length,quantity", Delimiter = "tab", DecimalSeparator = ".", LengthDecimals = 2, HeaderRow = false, AsciiOnly = true, OneRowPerPiece = false, Encoding = MachineExportFields.Utf8Bom, Extension = "txt" };
        file = MachineExport.Build(profile, order, pieces, new List<string>());
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, file.Content.Take(3));
        text = Encoding.UTF8.GetString(file.Content.Skip(3).ToArray());
        Assert.DoesNotContain("ı", text);
        Assert.Contains($"Kasa\t{kasa.LengthMm:0.00}\t".Replace(",", "."), text);
        Assert.Equal(pieces.Count, text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries).Sum(l => int.Parse(l.Split('\t')[2])));
        Assert.True(file.Rows < pieces.Count);
    }

    [Fact]
    public void Angles_And_Escape()
    {
        Assert.Equal(("45", "90"), MachineExport.Angles("45°/90°"));
        Assert.Equal(("", ""), MachineExport.Angles("Şablon"));
        Assert.Equal("Cig sut", MachineExport.Ascii("Çiğ süt"));
    }
}

[Collection("postgres")]
public sealed class MachineExportPageTests(PostgresFixture postgres)
{
    [Fact]
    public async Task MachineProfile_IsSaved_AndOrderFileDownloads()
    {
        await using var app = postgres.CreateApp("machine_export");
        var (client, token) = await QuoteDocumentTests.AdminAsync(app);
        var bad = await client.PostFormAsync("/yonetim/uretim/makineler/ekle", token, ("Name", "Boş"), ("Delimiter", ";"), ("DecimalSeparator", ","), ("Encoding", "utf-8"), ("LengthDecimals", "1"), ("col_length", ""));
        Assert.Contains("En az bir sütun seçin", WebUtility.HtmlDecode(await bad.Content.ReadAsStringAsync()));
        var ok = await client.PostFormAsync("/yonetim/uretim/makineler/ekle", token, ("Name", "Elumatec"), ("Delimiter", ","), ("DecimalSeparator", "."), ("Encoding", "utf-8"), ("LengthDecimals", "1"),
            ("HeaderRow", "true"), ("OneRowPerPiece", "true"), ("IsActive", "true"), ("col_length", "2"), ("col_profile", "1"), ("col_barcode", "3"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var machine = await app.WithDbAsync(db => db.MachineExportProfiles.SingleAsync(m => m.Name == "Elumatec"));
        Assert.Equal("profile,length,barcode", machine.Columns);

        var systemId = await app.WithDbAsync(async db => { var s = new ProfileSystem { Name = "Aktarım serisi" }; db.ProfileSystems.Add(s); await db.SaveChangesAsync(); return s.Id; });
        await DrawingLists.PostAsync(client, token, "Aktarım listesi", DrawingLists.Rows(DrawingLists.Row("Salon", 1200, 1400, quantity: 1, system: systemId)));
        var customer = await QuoteDocumentTests.NewCustomerAsync(app);
        await client.PostFormAsync("/yonetim/siparisler/ekle", token, ("CustomerId", customer.ToString()), ("Title", "Aktarım işi"), ("DrawingListId", (await DrawingLists.ListIdAsync(app, "Aktarım listesi")).ToString()), ("OrderDate", DateTime.Today.ToString("yyyy-MM-dd")));
        var orderId = await app.WithDbAsync(db => db.ProductionOrders.Where(o => o.Title == "Aktarım işi").Select(o => o.Id).SingleAsync());

        Assert.Contains("Dosyayı indir", WebUtility.HtmlDecode(await client.GetStringAsync($"/yonetim/uretim/kesim-aktarimi/{orderId}")));
        var file = await client.GetAsync($"/yonetim/uretim/kesim-dosyasi/{orderId}?makine={machine.Id}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        var text = await file.Content.ReadAsStringAsync();
        Assert.StartsWith("Profil,Kesim ölçüsü (mm),Barkod (parça etiketi)\r\n", text);
        Assert.Contains($",S{orderId}-", text);
        Assert.Contains("elumatec.csv", file.Content.Headers.ContentDisposition!.ToString());
    }
}
