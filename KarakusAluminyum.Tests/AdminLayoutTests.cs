using Xunit;

namespace KarakusAluminyum.Tests;

// Yönetim menüsü: aynı öneki paylaşan bağlantılardan yalnızca en özel olanı seçili görünür.
[Collection("postgres")]
public sealed class AdminLayoutTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData("/yonetim/siparisler", "Siparişler")]
    [InlineData("/yonetim/siparisler/planlama", "Üretim planı")]
    [InlineData("/yonetim/siparisler/montajcilar", "Montajcılar")]
    [InlineData("/yonetim/cizim/sistemler", "Profil sistemleri")]
    [InlineData("/yonetim/cizim", "Çizim listeleri")]
    public async Task ExactlyOneMenuLink_IsCurrent(string url, string text)
    {
        var (client, _) = await QuoteDocumentTests.AdminAsync(postgres.Shared);
        var html = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync(url));
        var current = html.Split("aria-current=\"page\"").Skip(1).ToList();
        var only = Assert.Single(current);
        Assert.StartsWith($"><span>{text}</span>", only);
    }
}
