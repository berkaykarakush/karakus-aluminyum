using System.Net;
using System.Text.Json;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Services.Prospecting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace KarakusAluminyum.Tests;

public sealed class ProspectTextTests
{
    [Fact]
    public void ExtractContacts_PrefersSiteDomainEmail_AndNormalizesSocialLinks()
    {
        const string html = """
            <a href="mailto:ahmet@gmail.com">x</a> info&#64;yapi.com.tr <img src="logo@2x.png"> test@example.com
            <a href="https://www.instagram.com/p/ABC/">gönderi</a>
            <a href="https://instagram.com/yapifirma?igsh=1">ig</a>
            <a href="https://www.facebook.com/sharer/sharer.php?u=x">paylaş</a>
            <a href="https://tr-tr.facebook.com/YapiFirma/">fb</a>
            <a href="https://www.linkedin.com/company/yapi-firma/about/">in</a>
            <a href="https://www.tiktok.com/@yapifirma">tt</a>
            """;
        var found = ProspectText.ExtractContacts(html, "https://www.yapi.com.tr/");
        Assert.Equal(new[] { "info@yapi.com.tr", "ahmet@gmail.com" }, found.Emails);
        Assert.Equal("https://www.instagram.com/yapifirma/", found.Instagram);
        Assert.Equal("https://www.facebook.com/YapiFirma", found.Facebook);
        Assert.Equal("https://www.linkedin.com/company/yapi-firma", found.LinkedIn);
        Assert.Equal("https://www.tiktok.com/@yapifirma", found.TikTok);
    }

    // Sitede referans firmanın hesabı daha sık geçse de firmanın kendi hesabı seçilir; JSON-LD içindeki bağlantı da okunur.
    [Fact]
    public void ExtractContacts_PrefersOwnAccount_OverReferenceLinks()
    {
        const string html = """
            <a href="https://www.instagram.com/beevillas/">ref</a><a href="https://www.instagram.com/beevillas/">ref</a>
            <a href="https://www.instagram.com/karakusaluminyum">biz</a>
            <script type="application/ld+json">{"sameAs":["https:\/\/www.facebook.com\/karakusaluminyumizmir"]}</script>
            """;
        var found = ProspectText.ExtractContacts(html, "https://www.karakusaluminyum.com/");
        Assert.Equal("https://www.instagram.com/karakusaluminyum/", found.Instagram);
        Assert.Equal("https://www.facebook.com/karakusaluminyumizmir", found.Facebook);
        Assert.Equal("beevillas", ProspectText.ExtractContacts(html, "https://baska.com/").Instagram!.Split('/')[^2]); // benzerlik yoksa ilk geçen
        Assert.Equal("karakusaluminyum", ProspectText.Ascii("Karakuş-Alüminyum"));
    }

    [Fact]
    public void ContactPages_StayOnSameSite()
    {
        var pages = ProspectText.ContactPages("""<a href="/iletisim">İletişim</a><a href="https://baska.com/contact">x</a><a href="hakkimizda.html">h</a><a href="/urunler">u</a>""", new Uri("https://firma.com/"));
        Assert.Equal(new[] { "https://firma.com/iletisim", "https://firma.com/hakkimizda.html" }, pages.Select(p => p.AbsoluteUri));
    }

    [Theory]
    [InlineData("User-agent: *\nDisallow: /", "/", false)]
    [InlineData("User-agent: *\nDisallow: /admin", "/", true)]
    [InlineData("User-agent: Googlebot\nDisallow: /\n\nUser-agent: *\nDisallow:", "/", true)]
    [InlineData("User-agent: Googlebot\nUser-agent: *\nDisallow: /iletisim", "/iletisim", false)]
    [InlineData("", "/", true)]
    public void Robots_AreRespected(string robots, string path, bool allowed) => Assert.Equal(allowed, ProspectText.RobotsAllows(robots, path));

    [Theory]
    [InlineData("127.0.0.1", false)]
    [InlineData("10.1.2.3", false)]
    [InlineData("172.20.0.1", false)]
    [InlineData("192.168.1.10", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("::1", false)]
    [InlineData("fd00::1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("0.0.0.0", false)]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700::1111", true)]
    public void OnlyPublicAddresses_AreFetched(string ip, bool allowed) => Assert.Equal(allowed, ProspectText.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("0232 123 45 67", "2321234567", null)]
    [InlineData("+90 (532) 111 22 33", "5321112233", "905321112233")]
    [InlineData("123", null, null)]
    public void Phones_AreComparedByLastTenDigits(string phone, string? key, string? whatsapp)
    {
        Assert.Equal(key, ProspectText.PhoneKey(phone));
        Assert.Equal(whatsapp, ProspectText.WhatsAppNumber(phone));
    }

    [Fact]
    public void Websites_AreNormalized()
    {
        Assert.Equal("https://firma.com/", ProspectText.NormalizeWebsite("firma.com"));
        Assert.Equal("http://www.firma.com/tr", ProspectText.NormalizeWebsite("http://www.firma.com/tr?utm=x#a"));
        Assert.Null(ProspectText.NormalizeWebsite("javascript:alert(1)"));
        Assert.Null(ProspectText.NormalizeWebsite("localhost"));
        Assert.Equal("firma.com", ProspectText.HostKey("https://www.firma.com/iletisim"));
    }

    [Fact]
    public void Csv_BlocksFormulas_AndQuotesSeparators()
    {
        Assert.Equal("'=HYPERLINK(\"x\")", ProspectText.CsvCell("=HYPERLINK(\"x\")").Trim('"').Replace("\"\"", "\""));
        Assert.Equal("'+905321112233", ProspectText.CsvCell("+905321112233"));
        Assert.Equal("\"a;b\"", ProspectText.CsvCell("a;b"));
        Assert.Equal("Yapı A.Ş.", ProspectText.CsvCell("Yapı A.Ş."));
    }

    [Fact]
    public void Keywords_AreTrimmedAndDeduplicated()
        => Assert.Equal(new[] { "inşaat firması", "Müteahhit" }, ProspectText.KeywordsFromLines(" inşaat firması \r\n\nMüteahhit\nmüteahhit\n"));

    [Fact]
    public void PlacesResponse_IsParsed()
    {
        const string json = """
            { "places": [
              { "id": "abc", "displayName": { "text": " Ege Yapı " }, "formattedAddress": "Bornova/İzmir", "nationalPhoneNumber": "0232 111 22 33",
                "websiteUri": "https://egeyapi.com/", "googleMapsUri": "https://maps.google.com/?cid=1", "rating": 4.6, "userRatingCount": 12,
                "addressComponents": [ { "longText": "Bornova", "types": ["administrative_area_level_2", "political"] }, { "longText": "İzmir", "types": ["administrative_area_level_1"] } ] },
              { "id": "kapali", "displayName": { "text": "Eski Firma" }, "businessStatus": "CLOSED_PERMANENTLY" },
              { "id": "adsiz" } ],
              "nextPageToken": "t2" }
            """;
        var page = GooglePlacesClient.Parse(json);
        Assert.Equal("t2", page.NextPageToken);
        Assert.Equal(2, page.Places.Count);
        var first = page.Places[0];
        Assert.Equal(("Ege Yapı", "Bornova", "İzmir", "0232 111 22 33", 4.6, 12), (first.Name, first.District, first.City, first.Phone, first.Rating!.Value, first.RatingCount!.Value));
        Assert.True(page.Places[1].Closed);
    }
}

[Collection("postgres")]
public sealed class ProspectTests(PostgresFixture postgres)
{
    private sealed class FakePlaces(Func<string, string?, PlacePage> respond) : IPlacesClient
    {
        public List<string> Queries { get; } = new();
        public Task<PlacePage> SearchAsync(string query, string? pageToken, CancellationToken cancellationToken) { Queries.Add(query + (pageToken is null ? "" : "#" + pageToken)); return Task.FromResult(respond(query, pageToken)); }
    }

    private sealed class FakeWebsite(SiteContacts contacts) : IWebsiteReader
    {
        public int Calls { get; private set; }
        public Task<SiteContacts> ReadAsync(string website, CancellationToken cancellationToken) { Calls++; return Task.FromResult(contacts); }
    }

    private static PlaceResult Place(string id, string name, string? phone = null, string? website = null, string city = "İzmir", bool closed = false)
        => new(id, name, $"{name} adresi", null, city, phone, website, "https://maps.google.com/?cid=" + id, 4.5, 10, closed);

    private static ProspectScanner Scanner(AppDbContext db, IPlacesClient places, IWebsiteReader? site = null, int limit = 900, int pages = 1, string? key = "test")
        => new(db, places, site ?? new FakeWebsite(new SiteContacts(Array.Empty<string>(), null, null, null, null)),
            Options.Create(new ProspectingOptions { GooglePlacesApiKey = key, MonthlyRequestLimit = limit, MaxPagesPerQuery = pages }), NullLogger<ProspectScanner>.Instance);

    // Her test yalnızca kendi sektörü ve iki ilçesiyle çalışır.
    private static async Task<int> PrepareAsync(AppDbContext db, params string[] keywords)
    {
        await db.ProspectScans.ExecuteDeleteAsync();
        await db.Prospects.ExecuteDeleteAsync();
        await db.ProspectDistricts.ExecuteUpdateAsync(s => s.SetProperty(x => x.IsActive, x => x.Name == "Bornova" || x.Name == "Buca"));
        var sector = new ProspectSector { Name = "Test " + Guid.NewGuid().ToString("N")[..6], KeywordsJson = JsonSerializer.Serialize(keywords) };
        db.ProspectSectors.Add(sector);
        await db.SaveChangesAsync();
        return sector.Id;
    }

    [Fact]
    public async Task Seed_AddsIzmirDistrictsAndConstructionSectors()
    {
        var (districts, sectors) = await postgres.Shared.WithDbAsync(async db => (await db.ProspectDistricts.CountAsync(), await db.ProspectSectors.Select(x => x.Name).ToListAsync()));
        Assert.Equal(30, districts);
        Assert.Contains("Zemin etüdü ve jeoteknik", sectors);
        Assert.Contains("Elektrik mühendisliği ve taahhüt", sectors);
    }

    [Fact]
    public async Task Scan_AddsAndDeduplicates_SkipsClosedAndOutOfCity_AndKeepsManualEdits()
    {
        await using var app = postgres.CreateApp("prospect_scan");
        await app.WithDbAsync(async db =>
        {
            var sectorId = await PrepareAsync(db, "inşaat firması");
            db.Prospects.Add(new Prospect { Name = "Elle Eklenen", Phone = "+90 232 999 88 77", Notes = "önemli", Stage = ProspectStage.Contacted });
            db.Prospects.Add(new Prospect { Name = "Daha önce bulunan", GooglePlaceId = "p1", Phone = "0232 000 00 00", Stage = ProspectStage.Meeting });
            await db.SaveChangesAsync();

            var places = new FakePlaces((query, _) => new PlacePage(new[]
            {
                Place("p1", "Ege Yapı", "0232 111 11 11", "egeyapi.com"),
                Place("p2", "Ege Yapı Şube", "0232 999 88 77"), // telefonla elle eklenen kayda eşleşir
                Place("p3", "Yeni İnşaat", website: "https://yeni.com.tr"),
                Place("p4", "Manisa Firması", city: "Manisa"),
                Place("p5", "Kapanmış", closed: true)
            }, null));
            var scan = await Scanner(db, places).EnqueueAsync(sectorId, "test");
            await Scanner(db, places).RunNextAsync(CancellationToken.None);

            Assert.Equal(new[] { "inşaat firması Bornova İzmir", "inşaat firması Buca İzmir" }, places.Queries);
            var done = await db.ProspectScans.AsNoTracking().FirstAsync(x => x.Id == scan.Id);
            Assert.Equal((ProspectScanStatus.Done, 2, 1), (done.Status, done.RequestCount, done.NewCount));

            var all = await db.Prospects.AsNoTracking().ToListAsync();
            Assert.Equal(new[] { "Daha önce bulunan", "Elle Eklenen", "Yeni İnşaat" }, all.Select(x => x.Name).OrderBy(x => x));
            var earlier = all.Single(x => x.GooglePlaceId == "p1");
            Assert.Equal(("0232 000 00 00", ProspectStage.Meeting, "https://egeyapi.com/"), (earlier.Phone, earlier.Stage, earlier.Website)); // boş alan doldu, düzeltilen korunur
            var manual = all.Single(x => x.Name == "Elle Eklenen");
            Assert.Equal(("p2", "önemli", ProspectStage.Contacted), (manual.GooglePlaceId, manual.Notes, manual.Stage));
            var added = all.Single(x => x.Name == "Yeni İnşaat");
            Assert.Equal((ProspectSources.Google, sectorId, "Bornova"), (added.Source, added.SectorId, added.District));
            Assert.NotNull((await db.ProspectSectors.FindAsync(sectorId))!.LastScanUtc);
            return 0;
        });
    }

    [Fact]
    public async Task Scan_StopsAtMonthlyLimit_AndFollowsPages()
    {
        await using var app = postgres.CreateApp("prospect_limit");
        await app.WithDbAsync(async db =>
        {
            var sectorId = await PrepareAsync(db, "mimarlık ofisi", "iç mimarlık");
            var counter = 0;
            var places = new FakePlaces((_, token) => new PlacePage(new[] { Place("x" + counter++, "Firma " + counter) }, token is null ? "next" : null));
            await Scanner(db, places, limit: 3, pages: 2).EnqueueAsync(sectorId, null);
            await Scanner(db, places, limit: 3, pages: 2).RunNextAsync(CancellationToken.None);
            Assert.Equal(new[] { "mimarlık ofisi Bornova İzmir", "mimarlık ofisi Bornova İzmir#next", "mimarlık ofisi Buca İzmir" }, places.Queries);
            var scan = await db.ProspectScans.AsNoTracking().SingleAsync();
            Assert.Equal(3, scan.RequestCount);
            Assert.Contains("Aylık istek sınırına", scan.Message);
            Assert.Equal(3, await Scanner(db, places).MonthlyUsageAsync());

            return 0;
        });
    }

    [Fact]
    public async Task FailedScan_IsRecorded_AndWithoutKeyNothingIsRequested()
    {
        await using var app = postgres.CreateApp("prospect_fail");
        await app.WithDbAsync(async db =>
        {
            var sectorId = await PrepareAsync(db, "zemin etüdü");
            var places = new FakePlaces((_, _) => throw new PlacesApiException("Google Places API 403: API key not valid."));
            await Scanner(db, places).EnqueueAsync(sectorId, null);
            await Scanner(db, places).RunNextAsync(CancellationToken.None);
            var failed = await db.ProspectScans.AsNoTracking().SingleAsync();
            Assert.Equal(ProspectScanStatus.Failed, failed.Status);
            Assert.Contains("API key not valid", failed.Message);

            var unused = new FakePlaces((_, _) => new PlacePage(Array.Empty<PlaceResult>(), null));
            await Scanner(db, unused, key: null).EnqueueAsync(sectorId, null);
            await Scanner(db, unused, key: null).RunNextAsync(CancellationToken.None);
            Assert.Empty(unused.Queries);
            return 0;
        });
    }

    [Fact]
    public async Task Enrichment_FillsOnlyEmptyFields_AndSkipsDoNotContact()
    {
        await using var app = postgres.CreateApp("prospect_enrich");
        await app.WithDbAsync(async db =>
        {
            await PrepareAsync(db, "x");
            db.Prospects.AddRange(
                new Prospect { Name = "A", Website = "https://a.com/", Email = "satis@a.com" },
                new Prospect { Name = "B", Website = "https://b.com/", DoNotContact = true },
                new Prospect { Name = "C" });
            await db.SaveChangesAsync();
            var site = new FakeWebsite(new SiteContacts(new[] { "info@a.com" }, "https://www.instagram.com/a/", null, "https://www.linkedin.com/company/a", null));
            Assert.Equal(1, await Scanner(db, new FakePlaces((_, _) => throw new InvalidOperationException()), site).EnrichPendingAsync(10, CancellationToken.None));
            var a = await db.Prospects.AsNoTracking().SingleAsync(x => x.Name == "A");
            Assert.Equal(("satis@a.com", "https://www.instagram.com/a/", "https://www.linkedin.com/company/a"), (a.Email, a.Instagram, a.LinkedIn));
            Assert.NotNull(a.EnrichedUtc);
            Assert.Equal(1, site.Calls);
            return 0;
        });
    }

    private static async Task<(HttpClient Client, string Token)> AdminAsync(AppFactory app)
    {
        var client = app.NewClient();
        await client.LoginAsync(AppFactory.AdminEmail);
        return (client, await client.GetTokenAsync("/yonetim/crm/firmalar/ekle"));
    }

    [Fact]
    public async Task Panel_CreatesTracksConvertsAndExports()
    {
        await using var app = postgres.CreateApp("prospect_panel");
        var (client, token) = await AdminAsync(app);

        var invalid = await client.PostFormAsync("/yonetim/crm/firmalar/ekle", token, ("Name", "X"), ("Stage", "New"), ("Instagram", "javascript:alert(1)"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);

        var created = await client.PostFormAsync("/yonetim/crm/firmalar/ekle", token, ("Name", "=Kötü Yapı"), ("Stage", "New"), ("Phone", "0532 111 22 33"), ("Website", "kotuyapi.com"), ("District", "Bornova"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var id = int.Parse(created.Headers.Location!.OriginalString.Split('/').Last());
        var row = await app.WithDbAsync(db => db.Prospects.AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Equal(("https://kotuyapi.com/", ProspectSources.Manual), (row.Website, row.Source));

        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3));
        await client.PostFormAsync($"/yonetim/crm/firmalar/{id}/gorusme", token, ("kind", "Call"), ("text", "Proje müdürüyle görüşüldü"), ("nextFollowUp", today.ToString("yyyy-MM-dd")));
        row = await app.WithDbAsync(db => db.Prospects.Include(x => x.Activities).AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.Equal((ProspectStage.Contacted, today, 1), (row.Stage, row.NextFollowUp, row.Activities.Count));
        Assert.Equal(AppFactory.AdminEmail, row.Activities.Single().CreatedBy);

        var list = await client.GetStringAsync("/yonetim/crm/firmalar?show=takip");
        Assert.Contains("=Kötü Yapı", WebUtility.HtmlDecode(list));
        Assert.Contains("wa.me/905321112233", list);

        var csv = await client.GetByteArrayAsync("/yonetim/crm/firmalar/disa-aktar?show=takip");
        var text = System.Text.Encoding.UTF8.GetString(csv);
        Assert.StartsWith("﻿Firma;", text);
        Assert.Contains("'=Kötü Yapı", text);

        await client.PostFormAsync($"/yonetim/crm/firmalar/{id}/musteriye-donustur", token);
        row = await app.WithDbAsync(db => db.Prospects.Include(x => x.Customer).AsNoTracking().FirstAsync(x => x.Id == id));
        Assert.NotNull(row.Customer);
        Assert.Equal(("=Kötü Yapı", "0532 111 22 33", "Bornova"), (row.Customer!.Name, row.Customer.Phone, row.Customer.District));
        Assert.Equal(ProspectStage.Quoted, row.Stage);
        await client.PostFormAsync($"/yonetim/crm/firmalar/{id}/musteriye-donustur", token);
        Assert.Equal(1, await app.WithDbAsync(db => db.Customers.CountAsync(x => x.Name == "=Kötü Yapı")));
    }

    [Fact]
    public async Task Panel_ManagesSectorsAndDistricts_AndRequiresKeyToScan()
    {
        await using var app = postgres.CreateApp("prospect_settings");
        var (client, token) = await AdminAsync(app);

        var page = await client.GetStringAsync("/yonetim/crm/firmalar/tarama");
        Assert.Contains("Google Places API anahtarı tanımlı değil", page);

        var start = await client.PostFormAsync("/yonetim/crm/firmalar/tarama/baslat", token);
        Assert.Equal(HttpStatusCode.Redirect, start.StatusCode);
        Assert.Equal(0, await app.WithDbAsync(db => db.ProspectScans.CountAsync()));

        var empty = await client.PostFormAsync("/yonetim/crm/firmalar/sektor/ekle", token, ("Name", "Peyzaj"), ("keywords", " \n "));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var ok = await client.PostFormAsync("/yonetim/crm/firmalar/sektor/ekle", token, ("Name", "Peyzaj"), ("keywords", "peyzaj firması\npeyzaj mimarlığı"), ("IsActive", "true"));
        Assert.Equal(HttpStatusCode.Redirect, ok.StatusCode);
        var sector = await app.WithDbAsync(db => db.ProspectSectors.AsNoTracking().SingleAsync(x => x.Name == "Peyzaj"));
        Assert.Equal(new[] { "peyzaj firması", "peyzaj mimarlığı" }, ProspectText.ParseKeywords(sector.KeywordsJson));

        var bornova = await app.WithDbAsync(db => db.ProspectDistricts.SingleAsync(x => x.Name == "Bornova"));
        await client.PostFormAsync("/yonetim/crm/firmalar/tarama/ilceler", token, ("active", bornova.Id.ToString()));
        Assert.Equal(new[] { "Bornova" }, await app.WithDbAsync(db => db.ProspectDistricts.Where(x => x.IsActive).Select(x => x.Name).ToListAsync()));
    }

    [Fact]
    public async Task Panel_RequiresLogin()
    {
        var anonymous = postgres.Shared.NewClient();
        var response = await anonymous.GetAsync("/yonetim/crm/firmalar");
        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }
}
