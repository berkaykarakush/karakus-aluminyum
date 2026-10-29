using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
namespace KarakusAluminyum.Web.Services;
public sealed class SiteSettingsProvider
{
    private readonly AppDbContext _db;
    private readonly SiteSettings _defaults;
    public SiteSettingsProvider(AppDbContext db, IOptions<SiteSettings> options) { _db=db; _defaults=options.Value; }
    public async Task<SiteSettings> GetAsync(CancellationToken cancellationToken=default)
    {
        var result = new SiteSettings { SiteName=_defaults.SiteName, BaseUrl=_defaults.BaseUrl, PhoneDisplay=_defaults.PhoneDisplay, PhoneE164=_defaults.PhoneE164, Email=_defaults.Email, WhatsAppNumber=_defaults.WhatsAppNumber, Address=_defaults.Address, City=_defaults.City, PrimaryServiceArea=_defaults.PrimaryServiceArea, GoogleMapsUrl=_defaults.GoogleMapsUrl, GoogleBusinessProfileUrl=_defaults.GoogleBusinessProfileUrl, InstagramUrl=_defaults.InstagramUrl, FacebookUrl=_defaults.FacebookUrl, LinkedInUrl=_defaults.LinkedInUrl };
        var records = await _db.SiteSettingRecords.AsNoTracking().ToListAsync(cancellationToken);
        foreach (var record in records)
        {
            var property = typeof(SiteSettings).GetProperty(record.Key);
            if (property is not null && property.CanWrite && property.PropertyType == typeof(string)) property.SetValue(result, record.Value);
        }
        return result;
    }
}
