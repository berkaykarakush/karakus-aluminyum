using KarakusAluminyum.Web.Services.Production;
using Xunit;
using ZXing.Common;
using ZXing.OneD;

namespace KarakusAluminyum.Tests;

// Code 128 kodlayıcı bağımsız bir okuyucuyla (ZXing) doğrulanır: desen tablosundaki tek hata okunmaz barkod demektir.
public sealed class BarcodeTests
{
    private static string Decode(string text, int scale = 3)
    {
        var modules = Code128.Modules(text);
        var width = (modules.Sum() + 20) * scale;
        var row = new BitArray(width);
        var x = 10 * scale;
        for (var i = 0; i < modules.Count; i++)
        {
            if (i % 2 == 0) for (var p = 0; p < modules[i] * scale; p++) row[x + p] = true;
            x += modules[i] * scale;
        }
        return new Code128Reader().decodeRow(0, row, null).Text;
    }

    [Theory]
    [InlineData("S12-345-1")]
    [InlineData("S1-1-1-1")]
    [InlineData("SP-2026-0001")]
    [InlineData(" !\"#$%&'()*+,-./0123456789:;<=>?@")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`")]
    [InlineData("abcdefghijklmnopqrstuvwxyz{|}~")]
    public void Encoded_IsReadBackByIndependentReader(string text) => Assert.Equal(text, Decode(text));

    [Fact]
    public void PatternTable_IsWellFormed()
    {
        Assert.Equal(107, Code128.PatternTable.Count);
        Assert.All(Code128.PatternTable.Take(106), p => Assert.Equal(11, p.Sum(c => c - '0')));
        Assert.Equal(13, Code128.PatternTable[106].Sum(c => c - '0'));
        Assert.Equal(107, Code128.PatternTable.Distinct().Count());
        // Her ASCII değeri okuyucudan geri dönmeli (checksum dahil tüm desenler sırayla denenir)
        for (var c = 32; c <= 126; c++) Assert.Equal("A" + (char)c + "Z", Decode("A" + (char)c + "Z"));
    }

    [Fact]
    public void NonAscii_IsRejected()
    {
        Assert.False(Code128.IsEncodable("Çıta"));
        Assert.Throws<ArgumentException>(() => Code128.Modules("Çıta"));
        Assert.Contains("<path", Code128.Svg("S1-2-3"));
    }
}
