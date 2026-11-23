using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Drawing;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/cizim/sistemler")]
public sealed class DrawingSystemsController : Controller
{
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public DrawingSystemsController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await _db.ProfileSystems.AsNoTracking().OrderBy(x => x.Category).ThenBy(x => x.Name).ToListAsync());

    [HttpGet("ekle")]
    public async Task<IActionResult> Create(string? tur)
    {
        var category = ProductCategories.All.Contains(tur) ? tur! : ProductCategories.Aluminum;
        ViewBag.Settings = new ProductSettings();
        return await EditorView(new ProfileSystem { Category = category }, new List<MaterialRule>());
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ProfileSystem input, ProductSettings settings, List<MaterialRule>? rules)
    {
        input.Id = 0;
        if (!await Prepare(input, settings, rules)) { ViewBag.Settings = settings; return await EditorView(input, rules); }
        _db.ProfileSystems.Add(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "ProfileSystem", input.Id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var row = await _db.ProfileSystems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (row is null) return NotFound();
        ViewBag.Settings = WindowDrawings.ParseSettings(row.SettingsJson);
        return await EditorView(row, MaterialTakeoff.ParseRules(row.MaterialsJson));
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ProfileSystem input, ProductSettings settings, List<MaterialRule>? rules)
    {
        input.Id = id;
        var current = await _db.ProfileSystems.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (current is null) return NotFound();
        input.Category = current.Category; // kullanılan çizimlerle tutarlı kalsın diye tür değişmez
        if (!await Prepare(input, settings, rules)) { ViewBag.Settings = settings; return await EditorView(input, rules); }
        _db.ProfileSystems.Update(input);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "ProfileSystem", id.ToString(), input.Name);
        return RedirectToAction(nameof(Index));
    }

    private async Task<bool> Prepare(ProfileSystem input, ProductSettings settings, List<MaterialRule>? rules)
    {
        if (!ProductCategories.All.Contains(input.Category)) ModelState.AddModelError(nameof(input.Category), "Geçersiz ürün türü.");
        var cleaned = await CleanRules(rules);
        if (!ModelState.IsValid) return false;
        input.Material = ProductCategories.Label(input.Category);
        input.SettingsJson = WindowDrawings.Serialize(settings);
        input.MaterialsJson = MaterialTakeoff.SerializeRules(cleaned);
        return true;
    }

    // Boş satırlar atlanır; stok kartı, hesap esası, profil/donanım adı ve katsayı doğrulanır.
    private async Task<List<MaterialRule>> CleanRules(List<MaterialRule>? rules)
    {
        var rows = (rules ?? new()).Where(x => x.ProductId > 0).ToList();
        var ids = rows.Select(x => x.ProductId).Distinct().ToList();
        var known = await _db.Products.Where(x => ids.Contains(x.Id)).Select(x => x.Id).ToListAsync();
        foreach (var (rule, i) in rows.Select((r, i) => (r, i + 1)))
        {
            rule.Key = string.IsNullOrWhiteSpace(rule.Key) ? null : rule.Key.Trim();
            if (!known.Contains(rule.ProductId)) ModelState.AddModelError("rules", $"Malzeme satırı {i}: stok kartı bulunamadı.");
            if (!MaterialBases.All.Contains(rule.Basis)) ModelState.AddModelError("rules", $"Malzeme satırı {i}: hesap esası geçersiz.");
            else if (MaterialBases.NeedsKey(rule.Basis) && rule.Key is null) ModelState.AddModelError("rules", $"Malzeme satırı {i}: profil veya donanım adını yazın.");
            else if (!MaterialBases.NeedsKey(rule.Basis)) rule.Key = null;
            if (rule.Key?.Length > 80) ModelState.AddModelError("rules", $"Malzeme satırı {i}: ad en fazla 80 karakter olabilir.");
            if (rule.Factor is <= 0 or > 100000) ModelState.AddModelError("rules", $"Malzeme satırı {i}: katsayı 0'dan büyük olmalıdır.");
        }
        return rows;
    }

    private async Task<IActionResult> EditorView(ProfileSystem system, List<MaterialRule>? rules)
    {
        ViewBag.Rules = rules ?? new List<MaterialRule>();
        ViewBag.Products = await _db.Products.AsNoTracking().Where(x => x.Kind == ProductKind.Stock && x.IsActive).OrderBy(x => x.Code).ThenBy(x => x.Name).ToListAsync();
        return View("EditSystem", system);
    }

    // Örnek seri: sistemin profil ve donanımları için stok kartları ve tahmini metraj kuralları oluşturur.
    // Var olan kurallara dokunmaz; aynı adlı stok kartı varsa onu kullanır. Değerler kullanıcı tarafından düzeltilmelidir.
    [HttpPost("ornek-malzeme/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SampleMaterials(int id)
    {
        var system = await _db.ProfileSystems.FirstOrDefaultAsync(x => x.Id == id);
        if (system is null) return NotFound();
        var rules = MaterialTakeoff.ParseRules(system.MaterialsJson);
        var added = 0;
        foreach (var sample in SampleCatalog.For(system))
        {
            // Profil ve donanımda aynı ad zaten bir kartla eşleşiyorsa (kullanıcının kendi kartı) örnek eklenmez.
            if (MaterialBases.NeedsKey(sample.Basis) && rules.Any(r => r.Basis == sample.Basis && string.Equals(r.Key, sample.Key, StringComparison.CurrentCultureIgnoreCase))) continue;
            var name = $"{system.Name} - {sample.Name}";
            if (name.Length > 200) name = name[..200];
            var product = await _db.Products.FirstOrDefaultAsync(x => x.Name == name);
            if (product is not null && rules.Any(r => r.ProductId == product.Id)) continue;
            if (product is null)
            {
                product = new Product { Name = name, Unit = sample.Unit, Kind = ProductKind.Stock, PurchasePrice = sample.Price, VatRate = 20 };
                _db.Products.Add(product);
                await _db.SaveChangesAsync();
            }
            rules.Add(new MaterialRule { ProductId = product.Id, Basis = sample.Basis, Key = sample.Key, Factor = sample.Factor });
            added++;
        }
        system.MaterialsJson = MaterialTakeoff.SerializeRules(rules);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "ProfileSystem", id.ToString(), $"{system.Name}: {added} örnek malzeme kuralı");
        TempData["Notice"] = added > 0 ? $"{added} örnek malzeme kuralı eklendi. Katsayıları ve stok kartlarını kendi serinize göre düzeltin." : "Eklenecek örnek kural yok; tüm profil ve donanımlar zaten tanımlı.";
        return RedirectToAction(nameof(Edit), new { id });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var row = await _db.ProfileSystems.FindAsync(id);
        if (row is null) return NotFound();
        if (await _db.WindowDesigns.AnyAsync(x => x.ProfileSystemId == id))
        {
            TempData["Notice"] = "Bu sistemi kullanan çizimler var; silmek yerine pasif yapın.";
            return RedirectToAction(nameof(Index));
        }
        _db.ProfileSystems.Remove(row);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "ProfileSystem", id.ToString(), row.Name);
        return RedirectToAction(nameof(Index));
    }
}
