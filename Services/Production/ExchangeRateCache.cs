using System.Collections.Concurrent;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Drawing;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace KarakusAluminyum.Web.Services.Production;

// Güncel döviz kurları (her para biriminin en son tarihli kuru). Açılışta ve kur kaydedildiğinde yenilenir.
public sealed class ExchangeRateCache
{
    private readonly ConcurrentDictionary<string, decimal> _rates = new();

    public decimal? Get(string currency) => currency == "TRY" ? 1 : _rates.TryGetValue(currency, out var rate) ? rate : null;

    public async Task ReloadAsync(AppDbContext db)
    {
        var latest = await PriceLists.LatestRatesAsync(db);
        foreach (var key in _rates.Keys.Except(latest.Keys)) _rates.TryRemove(key, out _);
        foreach (var (currency, info) in latest) _rates[currency] = info.Rate;
    }
}

// Profil sistemi veritabanından okunurken fiyat para biriminin güncel kuru yazılır; böylece tüm fiyat hesapları TL döner.
// Tek paylaşılan örnektir (EF her yeni örnek için iç servis sağlayıcısını yeniden kurar); kurlar DbContext'in uygulama servislerinden okunur.
public sealed class ProfileSystemRateInterceptor : IMaterializationInterceptor
{
    public static readonly ProfileSystemRateInterceptor Instance = new();
    private ProfileSystemRateInterceptor() { }

    public object InitializedInstance(MaterializationInterceptionData materializationData, object entity)
    {
        if (entity is ProfileSystem system)
        {
            var services = materializationData.Context.GetService<Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptions>()
                .FindExtension<Microsoft.EntityFrameworkCore.Infrastructure.CoreOptionsExtension>()?.ApplicationServiceProvider;
            var rates = services?.GetService(typeof(ExchangeRateCache)) as ExchangeRateCache;
            system.RateToTry = system.PriceCurrency == "TRY" ? 1 : rates?.Get(system.PriceCurrency);
        }
        return entity;
    }
}
