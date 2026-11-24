using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Accounting;
using KarakusAluminyum.Web.Models.Crm;
using KarakusAluminyum.Web.Models.Drawing;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Drawing;
using KarakusAluminyum.Web.Services.Notifications;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// README 7.11: keşif randevusu ve yerinde ölçü. Web talebinden açılabilir; ölçüler çizim listesine aktarılır, teklif oradan hazırlanır.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/kesif")]
public sealed class SurveyController : Controller
{
    private readonly AppDbContext _db;
    private readonly OrderService _orders;
    private readonly CustomerNotifier _notifier;
    private readonly IAuditService _audit;
    public SurveyController(AppDbContext db, OrderService orders, CustomerNotifier notifier, IAuditService audit) { _db = db; _orders = orders; _notifier = notifier; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index(SurveyStatus? durum, string? q)
    {
        var query = _db.SiteSurveys.AsNoTracking().Include(x => x.Customer).Include(x => x.Lines).AsQueryable();
        query = query.Where(x => x.Status == (durum ?? SurveyStatus.Planned));
        if (!string.IsNullOrWhiteSpace(q)) query = query.Where(x => EF.Functions.ILike(x.Number, $"%{q}%") || EF.Functions.ILike(x.Customer!.Name, $"%{q}%") || EF.Functions.ILike(x.Address ?? "", $"%{q}%"));
        ViewBag.Counts = await _db.SiteSurveys.GroupBy(x => x.Status).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count);
        (ViewBag.Status, ViewBag.Query) = (durum ?? SurveyStatus.Planned, q);
        var rows = await query.Take(500).ToListAsync();
        // Planlananlar tarihe göre (tarihsizler sonda), diğerleri yeniden eskiye.
        return View(durum is null or SurveyStatus.Planned
            ? rows.OrderBy(x => x.PlannedOn is null).ThenBy(x => x.PlannedOn).ThenBy(x => x.PlannedAt).ThenBy(x => x.Id).ToList()
            : rows.OrderByDescending(x => x.PlannedOn).ThenByDescending(x => x.Id).ToList());
    }

    // mesaj: web sitesinden gelen talep; müşteri kartı yoksa formdaki bilgilerle açılır.
    [HttpGet("ekle")]
    public async Task<IActionResult> Create(int? musteri, int? mesaj)
    {
        var input = new SurveyInputVm { CustomerId = musteri };
        if (musteri is int customerId && await _db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == customerId) is { } customer)
            (input.Address, input.ContactName, input.ContactPhone) = (OrderService.Location(customer), customer.ContactPerson ?? customer.Name, customer.Phone);
        if (mesaj is int messageId && await _db.ContactMessages.AsNoTracking().FirstOrDefaultAsync(x => x.Id == messageId) is { } message)
        {
            input.ContactMessageId = message.Id;
            input.ContactName = message.Name;
            input.Address = message.ProjectLocation;
            input.Notes = $"Web talebi ({message.Service ?? "hizmet belirtilmedi"}): {message.Message}".Trim();
            if (input.Notes.Length > 2000) input.Notes = input.Notes[..2000];
            var phone = NotificationComposer.PhoneDigits(message.ContactInfo);
            var email = NotificationComposer.IsEmail(message.ContactInfo) ? message.ContactInfo.Trim() : null;
            input.ContactPhone = phone is null ? null : message.ContactInfo.Trim();
            // Aynı telefon veya e-postalı müşteri varsa o seçilir, yoksa yeni bireysel müşteri önerilir.
            input.CustomerId ??= await MatchCustomerAsync(phone, email);
            if (input.CustomerId is null) (input.NewCustomerName, input.NewCustomerPhone, input.NewCustomerEmail) = (message.Name, input.ContactPhone, email);
        }
        await LoadOptions();
        return View("Editor", input);
    }

    private async Task<int?> MatchCustomerAsync(string? phoneDigits, string? email)
    {
        if (email is not null && await _db.Customers.Where(x => x.Email != null && x.Email.ToLower() == email.ToLower()).Select(x => (int?)x.Id).FirstOrDefaultAsync() is int byEmail) return byEmail;
        if (phoneDigits is null) return null;
        var phones = await _db.Customers.AsNoTracking().Where(x => x.Phone != null).Select(x => new { x.Id, x.Phone }).ToListAsync();
        return phones.FirstOrDefault(x => NotificationComposer.PhoneDigits(x.Phone) == phoneDigits)?.Id;
    }

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(SurveyInputVm input)
    {
        input.Id = 0;
        await Validate(input);
        if (!ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }
        if (input.CustomerId is null)
        {
            var customer = new Customer
            {
                Name = input.NewCustomerName!.Trim(), Kind = CustomerKind.Individual, Status = CustomerStatus.Lead, PartyType = PartyType.Customer,
                Phone = input.NewCustomerPhone?.Trim(), Email = input.NewCustomerEmail?.Trim(), Address = input.Address?.Trim(), ContactPerson = input.ContactName?.Trim(),
                Source = input.ContactMessageId is null ? null : "Web sitesi"
            };
            input.ContactName ??= customer.Name;
            input.ContactPhone ??= customer.Phone;
            _db.Customers.Add(customer);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("create", "Customer", customer.Id.ToString(), customer.Name);
            input.CustomerId = customer.Id;
        }
        var survey = new SiteSurvey { Number = await _orders.NextNumberAsync("KSF", PrintDates.Today.Year), ContactMessageId = input.ContactMessageId };
        Apply(survey, input);
        _db.SiteSurveys.Add(survey);
        await _notifier.SurveyChangedAsync(survey, SurveySnapshot.None);
        if (input.ContactMessageId is int messageId) await _db.ContactMessages.Where(x => x.Id == messageId).ExecuteUpdateAsync(s => s.SetProperty(x => x.IsRead, true));
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "SiteSurvey", survey.Id.ToString(), survey.Number);
        TempData["Notice"] = $"Keşif {survey.Number} oluşturuldu.";
        return RedirectToAction(nameof(Details), new { id = survey.Id });
    }

    [HttpGet("duzenle/{id:int}")]
    public async Task<IActionResult> Edit(int id)
    {
        var s = await _db.SiteSurveys.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (s is null) return NotFound();
        await LoadOptions();
        return View("Editor", new SurveyInputVm
        {
            Id = id, CustomerId = s.CustomerId, ContactMessageId = s.ContactMessageId, Address = s.Address, ContactName = s.ContactName, ContactPhone = s.ContactPhone,
            PlannedOn = s.PlannedOn, PlannedAt = s.PlannedAt, Surveyor = s.Surveyor, Notes = s.Notes
        });
    }

    [HttpPost("duzenle/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, SurveyInputVm input)
    {
        var survey = await _db.SiteSurveys.FirstOrDefaultAsync(x => x.Id == id);
        if (survey is null) return NotFound();
        input.Id = id;
        await Validate(input);
        if (input.CustomerId is null) ModelState.AddModelError(nameof(input.CustomerId), "Müşteri seçin.");
        if (!ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }
        var before = SurveySnapshot.Of(survey);
        Apply(survey, input);
        await _notifier.SurveyChangedAsync(survey, before);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("update", "SiteSurvey", id.ToString(), survey.Number);
        TempData["Notice"] = "Keşif güncellendi.";
        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpGet("detay/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var survey = await LoadAsync(id);
        if (survey is null) return NotFound();
        ViewBag.Notices = await _db.NotificationJobs.AsNoTracking().Where(x => x.SiteSurveyId == id).OrderByDescending(x => x.Id).Take(50).ToListAsync();
        return View(survey);
    }

    // Sahada (telefonda) girilen ölçüler. Boş satırlar atlanır; ölçüler aktarılırken ayrıca denetlenir.
    [HttpPost("olculer/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveLines(int id, List<SurveyLineInput> lines)
    {
        var survey = await _db.SiteSurveys.Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (survey is null) return NotFound();
        if (survey.DrawingListId is not null) { TempData["Notice"] = "Ölçüler çizim listesine aktarıldı; değişiklikleri çizim listesinde yapın."; return RedirectToAction(nameof(Details), new { id }); }
        var kept = new List<SurveyLine>();
        var errors = new List<string>();
        foreach (var (line, i) in lines.Select((l, i) => (l, i + 1)))
        {
            if (line.WidthMm == 0 && line.HeightMm == 0 && string.IsNullOrWhiteSpace(line.Location) && string.IsNullOrWhiteSpace(line.Note)) continue;
            if (!ProductCategories.All.Contains(line.Category)) line.Category = ProductCategories.Aluminum;
            if (!SurveyLabels.Openings.Any(o => o.Key == line.Opening)) line.Opening = "fixed";
            if (line.WidthMm is < 1 or > 100000 || line.HeightMm is < 1 or > 20000) errors.Add($"Satır {i}: genişlik ve yükseklik mm olarak yazılmalıdır.");
            if (line.Quantity < 1) line.Quantity = 1;
            kept.Add(new SurveyLine
            {
                SortOrder = kept.Count, Location = (line.Location ?? "").Trim() is { Length: > 0 } loc ? loc[..Math.Min(loc.Length, 120)] : $"Poz {kept.Count + 1}",
                Category = line.Category, Opening = ProductCategories.IsWindow(line.Category) ? line.Opening : "fixed",
                WidthMm = line.WidthMm, HeightMm = line.HeightMm, Quantity = Math.Min(line.Quantity, 10000), Note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim()[..Math.Min(line.Note.Trim().Length, 300)]
            });
        }
        if (errors.Count > 0) { TempData["Notice"] = string.Join(" ", errors); return RedirectToAction(nameof(Details), new { id }); }
        _db.SurveyLines.RemoveRange(survey.Lines);
        survey.Lines = kept;
        survey.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        TempData["Notice"] = $"{kept.Count} ölçü kaydedildi.";
        return Redirect($"/yonetim/kesif/detay/{id}#olculer");
    }

    [HttpPost("durum/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> SetStatus(int id, SurveyStatus status)
    {
        var survey = await _db.SiteSurveys.FirstOrDefaultAsync(x => x.Id == id);
        if (survey is null) return NotFound();
        if (Enum.IsDefined(status) && status != survey.Status)
        {
            var before = SurveySnapshot.Of(survey);
            (survey.Status, survey.UpdatedUtc) = (status, DateTime.UtcNow);
            await _notifier.SurveyChangedAsync(survey, before);
            await _db.SaveChangesAsync();
            await _audit.RecordAsync("status", "SiteSurvey", id.ToString(), $"{survey.Number}: {status}");
            TempData["Notice"] = $"Keşif: {SurveyLabels.Label(status)}.";
        }
        return RedirectToAction(nameof(Details), new { id });
    }

    // Ölçüleri yeni çizim listesine aktarır: her satır bir poz olur, açılım başlangıç çizimi olarak kurulur.
    [HttpPost("aktar/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(int id)
    {
        var survey = await _db.SiteSurveys.Include(x => x.Customer).Include(x => x.Lines).FirstOrDefaultAsync(x => x.Id == id);
        if (survey is null) return NotFound();
        if (survey.DrawingListId is int existing) return Redirect($"/yonetim/cizim/detay/{existing}");
        if (survey.Lines.Count == 0) { TempData["Notice"] = "Önce ölçüleri girin."; return RedirectToAction(nameof(Details), new { id }); }
        var (list, errors) = BuildDrawingList(survey);
        if (errors.Count > 0) { TempData["Notice"] = "Aktarılamadı: " + string.Join(" ", errors); return Redirect($"/yonetim/kesif/detay/{id}#olculer"); }
        _db.DrawingLists.Add(list);
        survey.DrawingList = list;
        var before = SurveySnapshot.Of(survey);
        (survey.Status, survey.UpdatedUtc) = (SurveyStatus.Done, DateTime.UtcNow);
        await _notifier.SurveyChangedAsync(survey, before);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("transfer", "SiteSurvey", id.ToString(), $"{survey.Number} → çizim listesi {list.Id}");
        TempData["Notice"] = $"{list.Items.Count} poz çizim listesine aktarıldı. Açılımları ve sistemleri buradan ayrıntılandırıp teklif hazırlayabilirsiniz.";
        return Redirect($"/yonetim/cizim/detay/{list.Id}");
    }

    public static (DrawingList List, List<string> Errors) BuildDrawingList(SiteSurvey survey)
    {
        var name = $"{survey.Number} {survey.Customer?.Name}".Trim();
        var list = new DrawingList { Name = name.Length > 160 ? name[..160] : name, CustomerId = survey.CustomerId, Notes = survey.Notes is null ? null : survey.Notes[..Math.Min(survey.Notes.Length, 1000)] };
        var errors = new List<string>();
        foreach (var (line, i) in survey.Lines.OrderBy(x => x.SortOrder).Select((l, i) => (l, i + 1)))
        {
            var window = ProductCategories.IsWindow(line.Category);
            var layout = window ? WindowLayout.Serialize(Layout(line.Opening, line.WidthMm)) : "{}";
            var parameters = window ? "{}" : WindowDrawings.Serialize(new ProductParams());
            foreach (var error in WindowDrawings.Validate(line.Category, line.WidthMm, line.HeightMm, WindowShapes.Rect, 0, line.Quantity, layout, parameters))
                errors.Add($"{line.Location}: {error}");
            list.Items.Add(new WindowDesign
            {
                SortOrder = i - 1, Name = line.Location, CustomerId = survey.CustomerId, Category = line.Category, WidthMm = line.WidthMm, HeightMm = line.HeightMm,
                Quantity = line.Quantity, LayoutJson = layout, ParamsJson = parameters, Notes = line.Note
            });
        }
        return (list, errors);
    }

    public static LayoutNode Layout(string opening, int width) => opening switch
    {
        "tilt" => new LayoutNode { Id = "root", Opening = WindowOpenings.Tilt },
        "turn" => new LayoutNode { Id = "root", Opening = WindowOpenings.TiltRight },
        "door" => new LayoutNode { Id = "root", Opening = WindowOpenings.DoorLeft },
        "slide" => new LayoutNode
        {
            Id = "root", Split = "v", Sliding = true, Sizes = new() { width / 2, width - width / 2 },
            Children = new() { new LayoutNode { Id = "a", Opening = WindowOpenings.SlideRight }, new LayoutNode { Id = "b", Opening = WindowOpenings.SlideLeft } }
        },
        _ => new LayoutNode { Id = "root", Opening = WindowOpenings.Fixed }
    };

    [HttpGet("yazdir/{id:int}")]
    public async Task<IActionResult> Print(int id)
    {
        var survey = await LoadAsync(id);
        return survey is null ? NotFound() : View(survey);
    }

    // Sahaya götürülecek boş keşif formu.
    [HttpGet("bos-form")]
    public IActionResult Blank() => View("Print", new SiteSurvey { Number = "" });

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var survey = await _db.SiteSurveys.FirstOrDefaultAsync(x => x.Id == id);
        if (survey is null) return NotFound();
        if (survey.DrawingListId is not null) { TempData["Notice"] = "Çizim listesine aktarılan keşif silinemez; iptal edin."; return RedirectToAction(nameof(Details), new { id }); }
        _db.SiteSurveys.Remove(survey);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "SiteSurvey", id.ToString(), survey.Number);
        TempData["Notice"] = "Keşif silindi.";
        return RedirectToAction(nameof(Index));
    }

    private Task<SiteSurvey?> LoadAsync(int id) => _db.SiteSurveys.AsNoTracking().Include(x => x.Customer).Include(x => x.DrawingList).Include(x => x.Lines.OrderBy(l => l.SortOrder)).FirstOrDefaultAsync(x => x.Id == id);

    private async Task Validate(SurveyInputVm input)
    {
        if (input.CustomerId is int customerId)
        {
            if (!await _db.Customers.AnyAsync(x => x.Id == customerId)) ModelState.AddModelError(nameof(input.CustomerId), "Geçerli bir müşteri seçin.");
        }
        else if (string.IsNullOrWhiteSpace(input.NewCustomerName)) ModelState.AddModelError(nameof(input.NewCustomerName), "Müşteri seçin veya yeni müşterinin adını yazın.");
        else if (string.IsNullOrWhiteSpace(input.NewCustomerPhone) && string.IsNullOrWhiteSpace(input.NewCustomerEmail)) ModelState.AddModelError(nameof(input.NewCustomerPhone), "Yeni müşteri için telefon veya e-posta yazın.");
        if (input.PlannedAt is not null && input.PlannedOn is null) ModelState.AddModelError(nameof(input.PlannedOn), "Saat için randevu günü de seçin.");
        if (input.ContactMessageId is int messageId && !await _db.ContactMessages.AnyAsync(x => x.Id == messageId)) input.ContactMessageId = null;
    }

    private static void Apply(SiteSurvey survey, SurveyInputVm input)
    {
        survey.CustomerId = input.CustomerId!.Value;
        survey.Address = input.Address?.Trim();
        survey.ContactName = input.ContactName?.Trim();
        survey.ContactPhone = input.ContactPhone?.Trim();
        survey.PlannedOn = input.PlannedOn;
        survey.PlannedAt = input.PlannedOn is null ? null : input.PlannedAt;
        survey.Surveyor = input.Surveyor?.Trim();
        survey.Notes = input.Notes?.Trim();
        survey.UpdatedUtc = DateTime.UtcNow;
    }

    private async Task LoadOptions()
        => ViewBag.Customers = new SelectList(await _db.Customers.AsNoTracking().Where(x => x.PartyType != PartyType.Supplier).OrderBy(x => x.Name).Select(x => new { x.Id, x.Name }).ToListAsync(), "Id", "Name");
}
