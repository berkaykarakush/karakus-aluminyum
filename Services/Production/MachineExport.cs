using System.Globalization;
using System.Text;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services.Drawing;

namespace KarakusAluminyum.Web.Services.Production;

public sealed record MachinePiece(int Bar, decimal BarLengthMm, ProfileSystem System, string Profile, string? ProfileCode, string Part, decimal LengthMm,
    string AngleLeft, string AngleRight, int Position, WindowDesign Design, int Unit, string Barcode, int Quantity = 1);

public sealed record MachineFile(string FileName, byte[] Content, int Rows, IReadOnlyList<string> Warnings);

// Kesim makinesi aktarımı: siparişin parçaları optimizasyondaki boy sırasıyla, profile göre ayarlanan sütunlarla düz metin (CSV) olarak yazılır.
// Makineye özel ikili veya özel biçimler örnek dosya görülmeden üretilmez; bu dosya, makinenin "CSV/metin listesi içe aktar" özelliği içindir.
public static class MachineExport
{
    static MachineExport() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public static (List<MachinePiece> Pieces, List<string> Warnings) Pieces(ProductionOrder order, IReadOnlyList<WindowDesign> designs, IReadOnlyDictionary<int, Product> products)
    {
        var warnings = new List<string>();
        var groups = new Dictionary<(int SystemId, string Profile), List<(BarPiece Piece, MachinePiece Data)>>();
        foreach (var (design, position) in designs.Select((d, i) => (d, i + 1)))
        {
            if (design.ProfileSystem is null) { warnings.Add($"Poz {position} ({design.Name}): profil sistemi yok, aktarılmadı."); continue; }
            var calculation = DrawingCalculation.Calculate(design, design.ProfileSystem);
            if (calculation is null || !calculation.IsValid) { warnings.Add($"Poz {position} ({design.Name}): hesaplanamadı, aktarılmadı."); continue; }
            var rules = MaterialTakeoff.ParseRules(design.ProfileSystem.MaterialsJson);
            for (var unit = 1; unit <= design.Quantity; unit++)
            {
                var part = 0;
                foreach (var cut in calculation.Cuts)
                {
                    var code = rules.Where(r => r.Basis is MaterialBases.ProfileBars or MaterialBases.ProfileMetres && string.Equals(r.Key, cut.Profile, StringComparison.CurrentCultureIgnoreCase))
                        .Select(r => products.GetValueOrDefault(r.ProductId)?.Code).FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
                    var (left, right) = Angles(cut.Ends);
                    for (var n = 0; n < cut.Count; n++)
                    {
                        part++;
                        var key = (design.ProfileSystem.Id, cut.Profile);
                        if (!groups.TryGetValue(key, out var list)) groups[key] = list = new();
                        var data = new MachinePiece(0, design.ProfileSystem.BarLengthMm, design.ProfileSystem, cut.Profile, code, cut.Label, cut.LengthMm, left, right, position, design, unit,
                            ProductionLabels.PartCode(order.Id, design.Id, unit, part));
                        list.Add((new BarPiece(list.Count.ToString(CultureInfo.InvariantCulture), cut.LengthMm), data));
                    }
                }
            }
        }

        var pieces = new List<MachinePiece>();
        var bar = 0;
        foreach (var ((_, profile), list) in groups.OrderBy(g => g.Value[0].Data.System.Name).ThenBy(g => g.Key.Profile))
        {
            var system = list[0].Data.System;
            var plan = BarOptimizer.Optimize(profile, list.Select(x => x.Piece), system.BarLengthMm, system.KerfMm);
            foreach (var cutBar in plan.Bars)
            {
                bar++;
                foreach (var piece in cutBar.Pieces)
                    pieces.Add(list[int.Parse(piece.Label, CultureInfo.InvariantCulture)].Data with { Bar = bar });
            }
        }
        return (pieces, warnings);
    }

    public static MachineFile Build(MachineExportProfile profile, ProductionOrder order, IReadOnlyList<MachinePiece> pieces, IReadOnlyList<string> warnings)
    {
        var rows = profile.OneRowPerPiece ? pieces.ToList()
            : pieces.GroupBy(p => (p.Bar, p.Profile, p.Part, p.LengthMm, p.AngleLeft, p.AngleRight, p.Position, p.Unit))
                .Select(g => g.First() with { Quantity = g.Count() }).ToList();
        var columns = Columns(profile.Columns);
        var delimiter = profile.Delimiter == "tab" ? "\t" : profile.Delimiter;
        var text = new StringBuilder();
        if (profile.HeaderRow) text.Append(string.Join(delimiter, columns.Select(c => Escape(Clean(profile, MachineExportFields.Label(c)), delimiter)))).Append("\r\n");
        foreach (var (row, i) in rows.Select((r, i) => (r, i + 1)))
            text.Append(string.Join(delimiter, columns.Select(c => Escape(Clean(profile, Value(profile, order, row, i, c)), delimiter)))).Append("\r\n");

        var encoding = profile.Encoding switch
        {
            MachineExportFields.Windows1254 => Encoding.GetEncoding(1254),
            MachineExportFields.Utf8Bom => new UTF8Encoding(true),
            _ => new UTF8Encoding(false)
        };
        var body = encoding.GetBytes(text.ToString());
        var content = profile.Encoding == MachineExportFields.Utf8Bom ? encoding.GetPreamble().Concat(body).ToArray() : body;
        var extension = string.IsNullOrWhiteSpace(profile.Extension) ? "csv" : profile.Extension.Trim().TrimStart('.');
        return new MachineFile($"{order.OrderNumber}-{Slug(profile.Name)}.{extension}", content, rows.Count, warnings);
    }

    public static List<string> Columns(string? columns)
        => (columns ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(c => MachineExportFields.All.Any(f => f.Key == c)).ToList();

    private static string Value(MachineExportProfile profile, ProductionOrder order, MachinePiece p, int row, string column)
    {
        string Length(decimal v) => v.ToString("F" + profile.LengthDecimals, CultureInfo.InvariantCulture).Replace(".", profile.DecimalSeparator == "," ? "," : ".");
        return column switch
        {
            "row" => row.ToString(CultureInfo.InvariantCulture),
            "order" => order.OrderNumber,
            "customer" => order.Customer?.Name ?? "",
            "bar" => p.Bar.ToString(CultureInfo.InvariantCulture),
            "barLength" => Length(p.BarLengthMm),
            "profileCode" => p.ProfileCode ?? "",
            "profile" => p.Profile,
            "part" => p.Part,
            "length" => Length(p.LengthMm),
            "angleLeft" => p.AngleLeft,
            "angleRight" => p.AngleRight,
            "quantity" => p.Quantity.ToString(CultureInfo.InvariantCulture),
            "pos" => p.Position.ToString(CultureInfo.InvariantCulture),
            "posName" => p.Design.Name,
            "unit" => $"{p.Unit}/{p.Design.Quantity}",
            "color" => ProductColors.NameOf(p.Design.ColorHex) ?? p.Design.ColorHex,
            "barcode" => p.Barcode,
            "system" => p.System.Name,
            _ => ""
        };
    }

    // "45°/45°" → (45, 45); bükme ve şablon parçalarında açı boş kalır.
    public static (string Left, string Right) Angles(string ends)
    {
        var parts = ends.Replace("°", "").Split('/');
        return parts.Length == 2 && parts.All(x => decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) ? (parts[0].Trim(), parts[1].Trim()) : ("", "");
    }

    private static string Escape(string value, string delimiter)
        => value.Contains(delimiter) || value.Contains('"') || value.Contains('\n') || value.Contains('\r') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    public static string Clean(MachineExportProfile profile, string value) => profile.AsciiOnly ? Ascii(value) : value;

    public static string Ascii(string value)
    {
        var map = new Dictionary<char, string> { ['ç'] = "c", ['Ç'] = "C", ['ğ'] = "g", ['Ğ'] = "G", ['ı'] = "i", ['İ'] = "I", ['ö'] = "o", ['Ö'] = "O", ['ş'] = "s", ['Ş'] = "S", ['ü'] = "u", ['Ü'] = "U", ['×'] = "x", ['°'] = "" };
        var text = new StringBuilder();
        foreach (var c in value) text.Append(map.TryGetValue(c, out var r) ? r : c <= '~' ? c.ToString() : "?");
        return text.ToString();
    }

    private static string Slug(string name)
    {
        var ascii = Ascii(name).ToLowerInvariant();
        var slug = new string(ascii.Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        while (slug.Contains("--")) slug = slug.Replace("--", "-");
        return slug.Length == 0 ? "kesim" : slug;
    }
}
