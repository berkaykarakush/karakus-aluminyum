using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Models.Documents;
using KarakusAluminyum.Web.Services.Documents;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Data;

// Sözleşme ayrıntılı maddelere, teklif kurumsal teklif düzenine (TEKLİFTİR + ürün tablosu) geçti. Mevcut kurulumlarda
// yalnızca boş alanlar ve hiç değiştirilmemiş eski varsayılan metinler yenilenir; yöneticinin yazdığı metne dokunulmaz.
// Sürüm başına bir kez çalışır.
public static class DocumentTemplateUpgrade
{
    public const string MarkerKey = "DocumentTemplatesProforma";
    public const string Version = "2026-11-26";

    public static async Task ApplyAsync(AppDbContext db)
    {
        var marker = await db.SiteSettingRecords.FirstOrDefaultAsync(x => x.Key == MarkerKey);
        if (marker?.Value == Version) return;
        if (marker is null) db.SiteSettingRecords.Add(marker = new SiteSettingRecord { Key = MarkerKey, Description = "Belge şablonları kurumsal teklif ve ayrıntılı sözleşmeye yükseltildi" });
        marker.Value = Version;

        foreach (var template in await db.DocumentTemplates.ToListAsync())
        {
            if (template.VariablesJson is null or "" or "{}") template.VariablesJson = DocumentJson.Serialize(DocumentSeed.DefaultVariables());
            if (template.Kind == DocumentKinds.Quote)
            {
                if (template.Title == DocumentSeed.ProformaTitle) template.Title = DocumentSeed.QuoteTitle;
                if (template.Intro == DocumentSeed.FirstQuoteIntro || template.Intro == DocumentSeed.ProformaIntro) template.Intro = DocumentSeed.QuoteIntro;
                if (string.IsNullOrWhiteSpace(template.Closing) || template.Closing == DocumentSeed.FirstQuoteClosing) template.Closing = DocumentSeed.QuoteClosing;
                if (template.TermsJson is null or "" or "[]") template.TermsJson = DocumentJson.Serialize(DocumentSeed.QuoteTerms());
                template.AcceptanceText ??= DocumentSeed.Acceptance;
                template.SellerLegalName ??= template.SellerName;
            }
            else if (template.Kind == DocumentKinds.Contract)
            {
                var titles = DocumentJson.ParseArticles(template.ArticlesJson).Select(a => a.Title).ToArray();
                if (titles.SequenceEqual(DocumentSeed.FirstContractTitles)) template.ArticlesJson = DocumentJson.Serialize(DocumentSeed.ContractArticles());
            }
            template.UpdatedUtc = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
    }
}
