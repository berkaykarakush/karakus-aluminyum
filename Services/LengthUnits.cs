using System.Globalization;

namespace KarakusAluminyum.Web.Services;

// Tüm ölçüler milimetredir: veritabanında, hesaplarda, ekranda ve basılan belgelerde (Ercom gibi). Birim seçimi yoktur.
public sealed class LengthUnits
{
    public string Code => "mm";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public string Format(decimal mm) => mm.ToString("0.#", Turkish);

    public string WithLabel(decimal mm) => $"{Format(mm)} mm";

    public string Size(decimal widthMm, decimal heightMm) => $"{Format(widthMm)} × {Format(heightMm)}";
}
