using System.Globalization;
using System.Text;

namespace KarakusAluminyum.Web.Services.Production;

// Code 128 (B kümesi) barkod: el tipi okuyucuların hepsinin okuduğu doğrusal barkod. Yalnızca ASCII 32-126.
// Desenler ISO/IEC 15417 tablosundandır; testlerde bağımsız bir okuyucuyla (ZXing) çözülerek doğrulanır.
public static class Code128
{
    private const int StartB = 104;
    private const int Stop = 106;
    private const int QuietModules = 10;

    // Her değer için çubuk/boşluk genişlikleri (modül), çubukla başlar.
    private static readonly string[] Patterns =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312", "132212", "221213",
        "221312", "231212", "112232", "122132", "122231", "113222", "123122", "123221", "223211", "221132",
        "221231", "213212", "223112", "312131", "311222", "321122", "321221", "312212", "322112", "322211",
        "212123", "212321", "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121", "313121", "211331",
        "231131", "213113", "213311", "213131", "311123", "311321", "331121", "312113", "312311", "332111",
        "314111", "221411", "431111", "111224", "111422", "121124", "121421", "141122", "141221", "112214",
        "112412", "122114", "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112", "421211", "212141",
        "214121", "412121", "111143", "111341", "131141", "114113", "114311", "411113", "411311", "113141",
        "114131", "311141", "411131", "211412", "211214", "211232", "2331112"
    };

    public static IReadOnlyList<string> PatternTable => Patterns;

    public static bool IsEncodable(string? text) => !string.IsNullOrEmpty(text) && text.All(c => c is >= ' ' and <= '~');

    // Sessiz bölge hariç modül genişlikleri dizisi (çubuk, boşluk, çubuk, ...).
    public static List<int> Modules(string text)
    {
        if (!IsEncodable(text)) throw new ArgumentException("Code 128 yalnızca ASCII 32-126 karakterleri kodlar.", nameof(text));
        var values = new List<int> { StartB };
        values.AddRange(text.Select(c => c - 32));
        var checksum = StartB;
        for (var i = 1; i < values.Count; i++) checksum += values[i] * i;
        values.Add(checksum % 103);
        values.Add(Stop);
        return values.SelectMany(v => Patterns[v].Select(c => c - '0')).ToList();
    }

    // Ölçeklenebilir SVG; yazdırmada keskin kalması için shape-rendering crispEdges.
    public static string Svg(string text, double moduleWidth = 1, double height = 40, bool showText = true)
    {
        var modules = Modules(text);
        var total = modules.Sum() + 2 * QuietModules;
        var textHeight = showText ? 10 : 0;
        string N(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);
        var svg = new StringBuilder();
        svg.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" class=\"barcode\" viewBox=\"0 0 {N(total * moduleWidth)} {N(height + textHeight)}\" preserveAspectRatio=\"none\" shape-rendering=\"crispEdges\" role=\"img\" aria-label=\"Barkod {System.Net.WebUtility.HtmlEncode(text)}\">");
        svg.Append($"<rect width=\"100%\" height=\"100%\" fill=\"#fff\"/><path fill=\"#000\" d=\"");
        var x = QuietModules;
        for (var i = 0; i < modules.Count; i++)
        {
            if (i % 2 == 0) svg.Append($"M{N(x * moduleWidth)} 0h{N(modules[i] * moduleWidth)}v{N(height)}h-{N(modules[i] * moduleWidth)}z");
            x += modules[i];
        }
        svg.Append("\"/>");
        if (showText)
            svg.Append($"<text x=\"50%\" y=\"{N(height + textHeight - 1)}\" text-anchor=\"middle\" font-family=\"monospace\" font-size=\"9\">{System.Net.WebUtility.HtmlEncode(text)}</text>");
        svg.Append("</svg>");
        return svg.ToString();
    }
}
