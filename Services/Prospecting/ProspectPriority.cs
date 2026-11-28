using KarakusAluminyum.Web.Models.Crm;

namespace KarakusAluminyum.Web.Services.Prospecting;

public sealed record ProspectScore(int Total, IReadOnlyList<string> Reasons);

// Günlük arama listesi: hangi firma önce aranmalı ve görüşmeden sonra ne zaman yeniden aranmalı.
public static class ProspectPriority
{
    public static readonly ProspectStage[] Closed = { ProspectStage.Won, ProspectStage.Lost, ProspectStage.Unqualified };

    // Arama listesine girer: açık, iletişim kurulabilir, takip günü gelmiş ya da hiç aranmamış ve telefonu olan firma.
    public static bool IsCallable(Prospect p, DateOnly today)
        => !p.DoNotContact && !Closed.Contains(p.Stage) && p.Phone is not null
           && (p.NextFollowUp is DateOnly due ? due <= today : p.Stage == ProspectStage.New);

    // 0-100 arası öncelik. Sıcak (görüşülmüş, teklif verilmiş) ve takibi geciken firma önce gelir; aynı durumdakiler arasında
    // Google'da çok yorumlu (büyük, aktif) ve iletişim bilgisi tam olan öne çıkar. sectorWinRate: sektörün kazanma oranı (0-1).
    public static ProspectScore Score(Prospect p, DateOnly today, double? sectorWinRate = null)
    {
        var reasons = new List<string>();
        var total = 0;
        void Add(int points, string reason) { if (points <= 0) return; total += points; reasons.Add(reason); }

        Add(p.Stage switch { ProspectStage.Quoted => 30, ProspectStage.Meeting => 25, ProspectStage.Contacted => 12, _ => 0 }, "Aşama: " + ProspectLabels.Stage(p.Stage));
        if (p.NextFollowUp is DateOnly due && due <= today)
        {
            var late = today.DayNumber - due.DayNumber;
            Add(late >= 7 ? 25 : late >= 1 ? 20 : 15, late == 0 ? "Takip günü bugün" : $"Takip {late} gün gecikti");
        }
        var reviews = p.RatingCount ?? 0;
        Add(reviews >= 100 ? 15 : reviews >= 30 ? 10 : reviews >= 5 ? 5 : 0, $"Google'da {reviews} yorum");
        if (p.Rating is >= 4.5 && reviews >= 5) Add(3, "Yüksek Google puanı");
        Add(p.Email is not null ? 5 : 0, "E-postası var");
        Add(p.Website is not null ? 5 : 0, "Web sitesi var");
        Add(p.ContactPerson is not null ? 5 : 0, "Yetkili adı biliniyor");
        if (sectorWinRate is double rate && rate > 0) Add((int)Math.Round(Math.Min(rate, 0.5) * 24), $"Sektörde kazanma oranı %{rate * 100:0}");
        return new ProspectScore(Math.Min(total, 100), reasons);
    }

    // Görüşmeden sonraki takip günü: ulaşılamayınca 2 gün, ilk temasta 3, görüşmede 5, teklifte 7 gün sonra. Sonuçlanan firmada takip yok.
    public static DateOnly? SuggestFollowUp(ProspectStage stage, ProspectActivityKind kind, DateOnly today)
    {
        if (Closed.Contains(stage)) return null;
        if (kind == ProspectActivityKind.NoAnswer) return today.AddDays(2);
        return stage switch
        {
            ProspectStage.New => today.AddDays(3),
            ProspectStage.Contacted => today.AddDays(3),
            ProspectStage.Meeting => today.AddDays(5),
            _ => today.AddDays(7)
        };
    }

    // Görüşme türüne göre aşamanın ilerlemesi: yeni firmayla ilk temas "iletişime geçildi", toplantı/ziyaret "görüşme yapıldı" yapar.
    // Not ve ulaşılamadı aşamayı değiştirmez; geri gitmez.
    public static ProspectStage AdvanceStage(ProspectStage stage, ProspectActivityKind kind)
    {
        if (kind is ProspectActivityKind.Note or ProspectActivityKind.NoAnswer || Closed.Contains(stage)) return stage;
        var next = kind is ProspectActivityKind.Meeting or ProspectActivityKind.Visit ? ProspectStage.Meeting : ProspectStage.Contacted;
        return next > stage ? next : stage;
    }

    public const string DefaultMessage = "Merhaba{{yetkili_hitap}}, Karakuş Alüminyum'dan yazıyoruz. {{ilce}} bölgesindeki projeleriniz için alüminyum doğrama, PVC pencere, cam balkon ve korkuluk çözümlerimizi tanıtmak isteriz. Uygun olduğunuz bir zamanda kısa bir görüşme yapabilir miyiz?\n\n{{imza}}";

    // Hazır mesaj değişkenleri: {{firma}}, {{yetkili}}, {{yetkili_hitap}} (yetkili varsa başında boşlukla adı), {{ilce}},
    // {{gonderen}} (gönderen kullanıcının adı), {{imza}} ("Ad Soyad, Karakuş Alüminyum" ya da yalnızca firma adı).
    public static string Message(string? template, Prospect p, string? sender)
    {
        var text = string.IsNullOrWhiteSpace(template) ? DefaultMessage : template;
        var values = new Dictionary<string, string>
        {
            ["firma"] = p.Name,
            ["yetkili"] = p.ContactPerson ?? "",
            ["yetkili_hitap"] = string.IsNullOrWhiteSpace(p.ContactPerson) ? "" : " " + p.ContactPerson.Trim(),
            ["ilce"] = string.IsNullOrWhiteSpace(p.District) ? "İzmir" : p.District.Trim(),
            ["gonderen"] = string.IsNullOrWhiteSpace(sender) ? "Karakuş Alüminyum" : sender.Trim(),
            ["imza"] = string.IsNullOrWhiteSpace(sender) ? "Karakuş Alüminyum" : sender.Trim() + ", Karakuş Alüminyum"
        };
        foreach (var (key, value) in values) text = text.Replace("{{" + key + "}}", value);
        return System.Text.RegularExpressions.Regex.Replace(text.Replace("\r\n", "\n"), "[ \t]{2,}", " ").Trim();
    }
}
