using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using KarakusAluminyum.Web.Models.Production;

namespace KarakusAluminyum.Web.Services.Production;

// Yazıcıya giden tek etiketin içeriği: üst satır, büyük satır, iki açıklama satırı ve barkod.
public sealed record LabelContent(string Header, string Big, string Line1, string Line2, string Code);

// Etiketleri yazıcının kendi diline (ZPL / TSPL) çevirir. Ölçüler mm'den yazıcı noktasına (dot) çevrilir.
public static class PrinterLabels
{
    public static List<LabelContent> FromPozlar(ProductionOrder order, IEnumerable<PozLabel> labels)
        => labels.Select(p => new LabelContent($"{order.OrderNumber}  Poz {p.Position} - {p.Unit}/{p.Quantity}", p.Size,
            $"{p.Design.Name} - {order.Customer?.Name}", string.Join(" - ", p.Lines.Take(3)), p.Code)).ToList();

    public static List<LabelContent> FromParts(ProductionOrder order, IEnumerable<PartLabel> labels)
        => labels.Select(p => new LabelContent($"{p.Profile}  {order.OrderNumber}", $"{p.LengthMm.ToString("0.#", CultureInfo.InvariantCulture)} mm  {p.Ends}",
            p.Label, $"Poz {p.Position} ({p.Unit}/{p.Design.Quantity}) {p.Design.Name}", p.Code)).ToList();

    public static string Extension(LabelPrinter printer) => printer.Language == LabelLanguages.Tspl ? "prn" : "zpl";

    public static byte[] Render(LabelPrinter printer, IReadOnlyList<LabelContent> labels)
    {
        var text = printer.Language == LabelLanguages.Tspl ? Tspl(printer, labels) : Zpl(printer, labels);
        return printer.Language == LabelLanguages.Tspl || printer.AsciiOnly ? Encoding.ASCII.GetBytes(text) : new UTF8Encoding(false).GetBytes(text);
    }

    private sealed record Layout(int Width, int Height, int Margin, int Small, int Big, int BarcodeHeight, int Module, double DotsPerMm);

    private static Layout Measure(LabelPrinter printer, IReadOnlyList<LabelContent> labels)
    {
        var dpmm = printer.Dpi switch { 300 => 12.0, 600 => 24.0, 203 => 8.0, _ => printer.Dpi / 25.4 }; // yazıcı üreticilerinin nokta/mm değerleri
        int Dots(double mm) => (int)Math.Round(mm * dpmm);
        var width = Dots(printer.WidthMm);
        var height = Dots(printer.HeightMm);
        var margin = Dots(2);
        // 100 × 50 mm etikette küçük yazı 3 mm, büyük yazı 5 mm; küçük etiketlerde orantılı küçülür.
        var scale = Math.Clamp(Math.Min(printer.WidthMm / 100.0, printer.HeightMm / 50.0), 0.5, 2);
        var longest = labels.Count == 0 ? 20 : labels.Max(l => Code128.Modules(l.Code).Sum()) + 20;
        var module = Math.Clamp((width - 2 * margin) / longest, 1, 6);
        return new Layout(width, height, margin, Dots(3 * scale), Dots(5 * scale), Dots(printer.HeightMm * 0.3), module, dpmm);
    }

    // ZPL II: her etiket ^XA ... ^XZ. ^FB satırı etiket genişliğine sığdırır; ^BC Code 128 ve altında okunur metin.
    public static string Zpl(LabelPrinter printer, IReadOnlyList<LabelContent> labels)
    {
        var l = Measure(printer, labels);
        var text = new StringBuilder();
        var lineWidth = l.Width - 2 * l.Margin;
        foreach (var label in labels)
        {
            text.Append("^XA\n");
            if (!printer.AsciiOnly) text.Append("^CI28\n"); // UTF-8 alan verisi
            text.Append($"^PW{l.Width}\n^LL{l.Height}\n^LH0,0\n");
            if (printer.Darkness > 0) text.Append($"~SD{Math.Clamp(printer.Darkness, 1, 30):00}\n");
            var y = l.Margin;
            void Field(string value, int size)
            {
                text.Append($"^FO{l.Margin},{y}^A0N,{size},{size}^FB{lineWidth},1,0,L^FD{Clean(printer, value)}^FS\n");
                y += size + l.Margin / 2;
            }
            Field(label.Header, l.Small);
            Field(label.Big, l.Big);
            Field(label.Line1, l.Small);
            Field(label.Line2, l.Small);
            var barcodeY = Math.Max(y, l.Height - l.Margin - l.BarcodeHeight - l.Small);
            text.Append($"^FO{l.Margin},{barcodeY}^BY{l.Module},3,{l.BarcodeHeight}^BCN,{l.BarcodeHeight},Y,N,N^FD{Clean(printer, label.Code)}^FS\n");
            text.Append("^PQ1\n^XZ\n");
        }
        return text.ToString();
    }

    // TSPL: SIZE/GAP bir kez, her etiket CLS ... PRINT 1. Yazıcı fontları ASCII'dir; satırlar karakter sayısına göre kısaltılır.
    public static string Tspl(LabelPrinter printer, IReadOnlyList<LabelContent> labels)
    {
        var l = Measure(printer, labels);
        var dpiScale = printer.Dpi / 203.0;
        // TSPL dahili fontları (203 dpi'da nokta): "2" 12 × 20, "3" 16 × 24, "4" 24 × 32, "5" 32 × 48
        (string Font, int W, int H) Font(int target)
        {
            var fonts = new[] { ("2", 12, 20), ("3", 16, 24), ("4", 24, 32), ("5", 32, 48) }.Select(f => (f.Item1, (int)(f.Item2 * dpiScale), (int)(f.Item3 * dpiScale))).ToList();
            return fonts.LastOrDefault(f => f.Item3 <= target, fonts[0]);
        }
        var small = Font(l.Small);
        var big = Font(l.Big);
        var inv = CultureInfo.InvariantCulture;
        var text = new StringBuilder();
        text.Append($"SIZE {printer.WidthMm} mm,{printer.HeightMm} mm\r\n");
        text.Append($"GAP {printer.GapMm.ToString("0.##", inv)} mm,0 mm\r\n");
        text.Append("DIRECTION 1\r\nREFERENCE 0,0\r\n");
        if (printer.Darkness > 0) text.Append($"DENSITY {Math.Clamp(printer.Darkness, 0, 15)}\r\n");
        foreach (var label in labels)
        {
            text.Append("CLS\r\n");
            var y = l.Margin;
            void Field(string value, (string Font, int W, int H) font)
            {
                var max = Math.Max(1, (l.Width - 2 * l.Margin) / font.W);
                var clean = Clean(printer, value, ascii: true).Replace("\"", "'");
                if (clean.Length > max) clean = clean[..max];
                text.Append($"TEXT {l.Margin},{y},\"{font.Font}\",0,1,1,\"{clean}\"\r\n");
                y += font.H + l.Margin / 2;
            }
            Field(label.Header, small);
            Field(label.Big, big);
            Field(label.Line1, small);
            Field(label.Line2, small);
            var barcodeY = Math.Max(y, l.Height - l.Margin - l.BarcodeHeight - small.H);
            text.Append($"BARCODE {l.Margin},{barcodeY},\"128\",{l.BarcodeHeight},1,0,{l.Module},{l.Module},\"{Clean(printer, label.Code, ascii: true)}\"\r\n");
            text.Append("PRINT 1,1\r\n");
        }
        return text.ToString();
    }

    // ZPL'de ^ ve ~ komut başlatır; alan verisinden çıkarılır. Yazıcı Türkçe font taşımıyorsa harfler sadeleştirilir.
    private static string Clean(LabelPrinter printer, string value, bool ascii = false)
    {
        var text = value.Replace('^', ' ').Replace('~', ' ').Replace('×', 'x').Replace("\r", " ").Replace("\n", " ");
        return printer.AsciiOnly || ascii ? MachineExport.Ascii(text) : text;
    }

    // Yazıcıya doğrudan gönderim yalnızca yerel ağ adreslerine yapılır (sunucunun dışarıya istek atmak için kullanılmasını önler).
    public static bool IsLocalAddress(string? host)
    {
        if (!IPAddress.TryParse(host?.Trim(), out var ip) || ip.AddressFamily != AddressFamily.InterNetwork) return false;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] is >= 16 and <= 31) || (b[0] == 192 && b[1] == 168) || b[0] == 127;
    }

    public static async Task SendAsync(LabelPrinter printer, byte[] data, CancellationToken cancellation = default)
    {
        if (!IsLocalAddress(printer.Host)) throw new InvalidOperationException("Yazıcı adresi yerel ağ IP'si olmalıdır (ör. 192.168.1.50).");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Parse(printer.Host!.Trim()), printer.Port, timeout.Token);
        await using var stream = client.GetStream();
        await stream.WriteAsync(data, timeout.Token);
        await stream.FlushAsync(timeout.Token);
    }
}
