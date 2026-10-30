namespace KarakusAluminyum.Web.Areas.Admin.Models;

public sealed record KpiVm(string Label, string Value, decimal Current, decimal? Previous, bool ComparePoints = false)
{
    // Önceki dönem sıfırsa yüzde anlamsızdır.
    public string? Change
    {
        get
        {
            if (Previous is null) return null;
            if (ComparePoints) return Current == Previous ? "Önceki dönemle aynı" : $"{(Current > Previous ? "▲" : "▼")} {Math.Abs(Current - Previous.Value):0.#} puan";
            if (Previous == 0) return Current == 0 ? "Önceki dönemle aynı" : "Önceki dönemde kayıt yok";
            if (Current == Previous) return "Önceki dönemle aynı";
            var percent = Math.Round((Current - Previous.Value) / Previous.Value * 100m, 0, MidpointRounding.AwayFromZero);
            return $"{(percent >= 0 ? "▲" : "▼")} %{Math.Abs(percent)} önceki döneme göre";
        }
    }
}
