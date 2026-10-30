using System.Globalization;

namespace KarakusAluminyum.Web.Services;

// Ölçüler veritabanında ve hesaplarda her zaman milimetredir; birim yalnızca gösterimi etkiler.
// Tercih çerezde tutulur, böylece sunucu ve tarayıcı aynı birimi kullanır. Varsayılan cm'dir.
public sealed class LengthUnits
{
    public const string CookieName = "length_unit";
    public const string Default = "cm";
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    public LengthUnits(IHttpContextAccessor accessor) => Code = Normalize(accessor.HttpContext?.Request.Cookies[CookieName]);

    public string Code { get; }

    public static string Normalize(string? code) => code is "mm" or "cm" or "m" ? code : Default;

    public string Format(decimal mm) => Format(mm, Code);

    public string WithLabel(decimal mm) => $"{Format(mm)} {Code}";

    public string Size(decimal widthMm, decimal heightMm) => $"{Format(widthMm)} × {Format(heightMm)}";

    public static string Format(decimal mm, string code) => code switch
    {
        "mm" => mm.ToString("0.#", Turkish),
        "m" => (mm / 1000m).ToString("0.###", Turkish),
        _ => (mm / 10m).ToString("0.##", Turkish)
    };
}
