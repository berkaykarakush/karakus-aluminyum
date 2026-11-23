using System.Globalization;
using System.Text;
using KarakusAluminyum.Web.Data;
using KarakusAluminyum.Web.Models.Production;
using KarakusAluminyum.Web.Services;
using KarakusAluminyum.Web.Services.Documents;
using KarakusAluminyum.Web.Services.Production;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Personel takvimi: kim hangi gün hangi işte; iş başına adam-gün metrikleri.
[Area("Admin"), Authorize(Roles = "Admin,Editor"), Route("yonetim/takvim")]
public sealed class WorkCalendarController : Controller
{
    private const int MaxRangeDays = 62;
    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    public WorkCalendarController(AppDbContext db, IAuditService audit) { _db = db; _audit = audit; }

    private static DateOnly Today => PrintDates.Today;

    [HttpGet("")]
    public async Task<IActionResult> Index(DateOnly? hafta)
    {
        var start = WorkStats.WeekStart(hafta ?? Today);
        var logs = await Logs().Where(x => x.Date >= start && x.Date < start.AddDays(7)).ToListAsync();
        var employeeIds = logs.Select(x => x.EmployeeId).ToHashSet();
        ViewBag.Employees = await _db.Employees.AsNoTracking().Where(x => x.IsActive || employeeIds.Contains(x.Id)).OrderBy(x => x.Name).ToListAsync();
        ViewBag.Start = start;
        ViewBag.Logs = logs;
        ViewBag.Overbooked = WorkStats.Overbooked(logs);
        ViewBag.Week = WorkStats.ByProject(logs, Today);
        return View();
    }

    [HttpGet("ekle")]
    public async Task<IActionResult> Create(int? personel, DateOnly? tarih, int? siparis, int? servis, DateOnly? hafta)
    {
        var input = new WorkLogInputVm { From = tarih ?? Today, ProductionOrderId = siparis, ServiceRequestId = servis, ReturnWeek = hafta?.ToString("yyyy-MM-dd") };
        if (personel is int id) input.EmployeeIds.Add(id);
        if (servis is not null) input.Activity = WorkActivities.Service;
        await LoadOptions();
        return View("Editor", input);
    }

    // Seçilen her personel ve tarih aralığındaki her gün için bir kayıt; aynısı varsa tekrar eklenmez.
    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(WorkLogInputVm input)
    {
        var to = input.To ?? input.From;
        if (input.EmployeeIds.Count == 0) ModelState.AddModelError(nameof(input.EmployeeIds), "En az bir personel seçin.");
        else if (await _db.Employees.CountAsync(x => input.EmployeeIds.Contains(x.Id)) != input.EmployeeIds.Distinct().Count()) ModelState.AddModelError(nameof(input.EmployeeIds), "Personel bulunamadı.");
        if (to < input.From) ModelState.AddModelError(nameof(input.To), "Bitiş tarihi başlangıçtan önce olamaz.");
        else if (to.DayNumber - input.From.DayNumber >= MaxRangeDays) ModelState.AddModelError(nameof(input.To), $"Tek seferde en fazla {MaxRangeDays} gün girilebilir.");
        if (input.ProductionOrderId is not null && input.ServiceRequestId is not null) ModelState.AddModelError(string.Empty, "Kayıt ya siparişe ya servis talebine bağlanır.");
        if (input.ProductionOrderId is int orderId && !await _db.ProductionOrders.AnyAsync(x => x.Id == orderId)) ModelState.AddModelError(nameof(input.ProductionOrderId), "Sipariş bulunamadı.");
        if (input.ServiceRequestId is int serviceId && !await _db.ServiceRequests.AnyAsync(x => x.Id == serviceId)) ModelState.AddModelError(nameof(input.ServiceRequestId), "Servis talebi bulunamadı.");
        if (!WorkActivities.All.Contains(input.Activity)) ModelState.AddModelError(nameof(input.Activity), "Geçersiz faaliyet.");
        if (input.Days is not (0.25m or 0.5m or 0.75m or 1m)) ModelState.AddModelError(nameof(input.Days), "Süre tam gün, yarım gün veya çeyrek gün olmalıdır.");
        var dates = Enumerable.Range(0, Math.Max(0, to.DayNumber - input.From.DayNumber + 1)).Select(input.From.AddDays)
            .Where(d => !input.SkipSundays || d.DayOfWeek != DayOfWeek.Sunday).ToList();
        if (ModelState.IsValid && dates.Count == 0) ModelState.AddModelError(nameof(input.From), "Seçilen aralıkta Pazar dışında gün yok.");
        if (!ModelState.IsValid) { await LoadOptions(); return View("Editor", input); }

        var ids = input.EmployeeIds.Distinct().ToList();
        var existing = (await _db.WorkLogs.AsNoTracking().Where(x => ids.Contains(x.EmployeeId) && x.Date >= input.From && x.Date <= to && x.ProductionOrderId == input.ProductionOrderId && x.ServiceRequestId == input.ServiceRequestId && x.Activity == input.Activity)
            .Select(x => new { x.EmployeeId, x.Date }).ToListAsync()).Select(x => (x.EmployeeId, x.Date)).ToHashSet();
        var added = 0;
        foreach (var employee in ids)
            foreach (var date in dates.Where(d => !existing.Contains((employee, d))))
            {
                _db.WorkLogs.Add(new WorkLog { EmployeeId = employee, Date = date, ProductionOrderId = input.ProductionOrderId, ServiceRequestId = input.ServiceRequestId, Activity = input.Activity, Days = input.Days, Note = input.Note?.Trim(), CreatedBy = User.Identity?.Name });
                added++;
            }
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("create", "WorkLog", "batch", $"{added} kayıt");
        TempData["Notice"] = $"{added} çalışma kaydı eklendi." + (added < ids.Count * dates.Count ? $" {ids.Count * dates.Count - added} kayıt zaten vardı." : "");
        return RedirectToAction(nameof(Index), new { hafta = WorkStats.WeekStart(input.From).ToString("yyyy-MM-dd") });
    }

    [HttpPost("sil/{id:int}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id)
    {
        var log = await _db.WorkLogs.FindAsync(id);
        if (log is null) return NotFound();
        _db.WorkLogs.Remove(log);
        await _db.SaveChangesAsync();
        await _audit.RecordAsync("delete", "WorkLog", id.ToString(), $"{log.EmployeeId} {log.Date:yyyy-MM-dd}");
        TempData["Notice"] = "Çalışma kaydı silindi.";
        return RedirectToAction(nameof(Index), new { hafta = WorkStats.WeekStart(log.Date).ToString("yyyy-MM-dd") });
    }

    // Metrikler: tarih aralığında iş ve personel bazında adam-gün. Varsayılan bu ay.
    [HttpGet("rapor")]
    public async Task<IActionResult> Report(DateOnly? bas, DateOnly? bit, int? siparis, bool csv = false)
    {
        var from = bas ?? new DateOnly(Today.Year, Today.Month, 1);
        var to = bit ?? from.AddMonths(1).AddDays(-1);
        if (to < from) (from, to) = (to, from);
        var query = Logs().Where(x => x.Date >= from && x.Date <= to);
        if (siparis is int orderId) query = query.Where(x => x.ProductionOrderId == orderId);
        var logs = await query.ToListAsync();
        var projects = WorkStats.ByProject(logs, Today);
        var employees = WorkStats.ByEmployee(logs, Today);
        // İşçilik maliyeti maaş bilgisinden gelir; yalnızca yönetici ve muhasebeci görür.
        var showCost = User.IsInRole("Admin") || User.IsInRole("Accountant");
        if (showCost) (projects, employees) = (WorkStats.WithCost(projects, logs, Today), WorkStats.WithCost(employees, logs, Today));
        if (csv) return Csv(projects, employees, from, to, showCost);
        ViewBag.ShowCost = showCost;
        (ViewBag.From, ViewBag.To, ViewBag.Order) = (from, to, siparis);
        ViewBag.Employees = employees;
        ViewBag.Overbooked = WorkStats.Overbooked(logs).Count;
        return View(projects);
    }

    private FileContentResult Csv(List<WorkProjectRow> projects, List<WorkEmployeeRow> employees, DateOnly from, DateOnly to, bool showCost)
    {
        var culture = TurkishNumbers.Culture;
        string N(decimal v) => v.ToString("0.##", culture);
        string Cell(string v) => v.Contains(';') || v.Contains('"') ? "\"" + v.Replace("\"", "\"\"") + "\"" : v;
        var text = new StringBuilder();
        var cost = showCost ? ";İşçilik maliyeti (TL)" : "";
        text.Append("İş;Adam-gün (gerçekleşen);Adam-gün (planlanan);Kişi;Çalışılan gün;İlk gün;Son gün" + cost + "\r\n");
        foreach (var p in projects)
            text.Append(string.Join(';', Cell(p.Label), N(p.ManDays), N(p.PlannedManDays), p.People, p.WorkDays, p.First?.ToString("dd.MM.yyyy") ?? "", p.Last?.ToString("dd.MM.yyyy") ?? "") + (showCost ? ";" + p.LaborCost.ToString("0.00", culture) : "")).Append("\r\n");
        text.Append("\r\nPersonel;Adam-gün (gerçekleşen);Adam-gün (planlanan);İş sayısı;Çalışılan gün" + cost + "\r\n");
        foreach (var e in employees)
            text.Append(string.Join(';', Cell(e.Name), N(e.ManDays), N(e.PlannedManDays), e.Projects, e.WorkDays) + (showCost ? ";" + e.LaborCost.ToString("0.00", culture) : "")).Append("\r\n");
        var bytes = new UTF8Encoding(true).GetPreamble().Concat(Encoding.UTF8.GetBytes(text.ToString())).ToArray(); // Excel Türkçe karakterleri doğru açsın
        return File(bytes, "text/csv", $"adam-gun-{from:yyyy-MM-dd}-{to:yyyy-MM-dd}.csv");
    }

    private IQueryable<WorkLog> Logs() => _db.WorkLogs.AsNoTracking().Include(x => x.Employee).Include(x => x.ProductionOrder).Include(x => x.ServiceRequest);

    private async Task LoadOptions()
    {
        ViewBag.Employees = await _db.Employees.AsNoTracking().Where(x => x.IsActive).OrderBy(x => x.Name).ToListAsync();
        // Açık siparişler önce; teslim edilenler montaj sonrası işler ve servis için listede kalır.
        var orders = await _db.ProductionOrders.AsNoTracking().Where(x => x.Status != OrderStatus.Cancelled).OrderBy(x => x.Status == OrderStatus.Delivered).ThenByDescending(x => x.Id).Take(400)
            .Select(x => new { x.Id, x.CustomerId, x.OrderNumber, Customer = x.Customer!.Name, x.Title }).ToListAsync();
        ViewBag.Orders = orders.Select(x => new OrderOption(x.Id, x.CustomerId, $"{x.OrderNumber} · {x.Customer} · {x.Title}")).ToList();
        var services = await _db.ServiceRequests.AsNoTracking().Where(x => x.Status == ServiceStatus.Open || x.Status == ServiceStatus.Scheduled).OrderByDescending(x => x.Id).Take(200)
            .Select(x => new { x.Id, x.CustomerId, x.Number, Customer = x.Customer!.Name, x.Category }).ToListAsync();
        ViewBag.Services = services.Select(x => new OrderOption(x.Id, x.CustomerId, $"{x.Number} · {x.Customer} · {x.Category}")).ToList();
    }
}
