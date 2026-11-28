using System.Globalization;
using System.Text.RegularExpressions;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Production;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Services;

public enum NeighborLevel { District = 0, Neighbourhood = 1, Street = 2, Building = 3 }

public sealed record AddressKey(string? Building, string? Street, string? Neighbourhood, string? District);
public sealed record NeighborMatch(int OrderId, string OrderNumber, string CustomerName, string? Address, DateOnly? DeliveredOn, NeighborLevel Level)
{
    public string LevelText => Level switch
    {
        NeighborLevel.Building => "Aynı site / apartman",
        NeighborLevel.Street => "Aynı sokak / cadde",
        NeighborLevel.Neighbourhood => "Aynı mahalle",
        _ => "Aynı ilçe"
    };
}

// Komşu işler: teslim edilmiş siparişlerden aynı site/apartman, sokak veya mahallede olanlar. Harita servisi kullanılmaz;
// adresteki "Sitesi", "Apt.", "Sok.", "Cad.", "Mah." gibi kelimelerin önündeki adlar karşılaştırılır.
// Satışta referans olarak ("aynı sitede X dairesinin pencerelerini biz yaptık") ve yeni talebi öne almak için kullanılır.
public static partial class NeighborWork
{
    private static readonly CultureInfo Tr = new("tr-TR");
    private static readonly string[] BuildingWords = { "sitesi", "site", "sit", "apartmanı", "apartmani", "apt", "konutları", "konutlari", "evleri", "rezidans", "residence", "residance", "blokları", "bloklari", "plaza" };
    private static readonly string[] StreetWords = { "sokak", "sokağı", "sokagi", "sok", "sk", "cadde", "caddesi", "cad", "cd", "bulvarı", "bulvari", "blv", "bulv" };
    private static readonly string[] NeighbourhoodWords = { "mahallesi", "mahalle", "mah", "mh" };
    private static readonly HashSet<string> AllMarkers = BuildingWords.Concat(StreetWords).Concat(NeighbourhoodWords).ToHashSet();
    private static readonly string[] Districts = Prospecting.ProspectSeed.Districts.Select(d => d.Name).ToArray();

    public static AddressKey Parse(string? address, string? district = null)
    {
        var words = Words(address);
        var dist = Clean(district);
        if (dist is null && words.Select(w => Districts.FirstOrDefault(d => Fold(d) == w)).FirstOrDefault(d => d is not null) is { } found) dist = Fold(found);
        // Sokaklar çoğunlukla numaralıdır ("1203/5 Sok."); site ve mahalle adındaki numara, blok harfi gibi parçalar atlanır.
        return new AddressKey(Before(words, BuildingWords, 2, digits: false), Before(words, StreetWords, 1, digits: true), Before(words, NeighbourhoodWords, 1, digits: false), dist);
    }

    public static NeighborLevel? Compare(AddressKey a, AddressKey b)
    {
        // İki adres de ilçe bildiriyorsa ilçe aynı olmalı (aynı adlı site başka ilçede olabilir).
        if (a.District is not null && b.District is not null && a.District != b.District) return null;
        if (a.Building is not null && a.Building == b.Building) return NeighborLevel.Building;
        if (a.Street is not null && a.Street == b.Street && (a.Neighbourhood is null || b.Neighbourhood is null || a.Neighbourhood == b.Neighbourhood)) return NeighborLevel.Street;
        if (a.Neighbourhood is not null && a.Neighbourhood == b.Neighbourhood) return NeighborLevel.Neighbourhood;
        return null;
    }

    // Teslim edilmiş siparişler arasında bu adrese en yakın olanlar (en fazla max; önce aynı bina, sonra yeni teslim).
    public static async Task<List<NeighborMatch>> FindAsync(AppDbContext db, string? address, string? district, int? excludeCustomerId = null, int max = 5)
    {
        var key = Parse(address, district);
        if (key.Building is null && key.Street is null && key.Neighbourhood is null) return new();
        var orders = await db.ProductionOrders.AsNoTracking().Where(x => x.Status == OrderStatus.Delivered && (excludeCustomerId == null || x.CustomerId != excludeCustomerId))
            .OrderByDescending(x => x.Id).Take(2000)
            .Select(x => new { x.Id, x.OrderNumber, Customer = x.Customer!.Name, Address = x.InstallAddress ?? x.Customer.Address, x.Customer.District, x.PlannedShipDate })
            .ToListAsync();
        return orders.Select(o => (o, level: Compare(key, Parse(o.Address, o.District))))
            .Where(x => x.level is not null)
            .Select(x => new NeighborMatch(x.o.Id, x.o.OrderNumber, x.o.Customer, x.o.Address, x.o.PlannedShipDate, x.level!.Value))
            .OrderByDescending(x => x.Level).ThenByDescending(x => x.OrderId).Take(max).ToList();
    }

    private static string? Before(IReadOnlyList<string> words, string[] markers, int take, bool digits)
    {
        for (var i = 1; i < words.Count; i++)
        {
            if (!markers.Contains(words[i])) continue;
            var start = Math.Max(0, i - take);
            // Adın içine önceki adres parçasının kelimesi girmesin: "Gül Sok. Lale Sitesi" → "lale".
            var part = words.Skip(start).Take(i - start).ToList();
            var cut = part.FindLastIndex(w => AllMarkers.Contains(w));
            var name = part.Skip(cut + 1).Where(w => !IsNoise(w, digits)).ToList();
            if (name.Count > 0) return string.Join(" ", name);
        }
        return null;
    }

    // Kapı numarası, blok harfi ve "no" gibi parçalar ad sayılmaz.
    private static bool IsNoise(string w, bool digits) => (w.Length <= 1 && !(digits && char.IsDigit(w[0]))) || (!digits && w.Any(char.IsDigit)) || w is "no" or "blok" or "kat" or "daire" or "d" or "k";

    private static List<string> Words(string? text)
        => string.IsNullOrWhiteSpace(text) ? new() : WordPattern().Matches(Fold(text)).Select(m => m.Value).ToList();

    private static string Fold(string text) => text.ToLower(Tr).Replace('â', 'a').Replace('î', 'i').Replace('û', 'u');

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : Fold(value.Trim());

    [GeneratedRegex(@"[\p{L}\p{N}/]+")]
    private static partial Regex WordPattern();
}
