using KarakusAluminyum.Web.Models;
using KarakusAluminyum.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

[Area("Admin"), Authorize(Roles = "Admin"), Route("yonetim/kullanicilar")]
public sealed class UsersController : Controller
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<IdentityRole> _roles;
    private readonly IAuditService _audit;
    public UsersController(UserManager<ApplicationUser> users, RoleManager<IdentityRole> roles, IAuditService audit)
    { _users = users; _roles = roles; _audit = audit; }

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var users = await _users.Users.OrderBy(x => x.Email).ToListAsync();
        var rows = new List<UserListRow>();
        foreach (var user in users)
            rows.Add(new UserListRow(user.Id, user.Email ?? user.UserName ?? "", await _users.GetRolesAsync(user), user.LockoutEnd > DateTimeOffset.UtcNow));
        return View(rows);
    }

    [HttpGet("ekle")]
    public IActionResult Create() => View(new UserAdminInput());

    [HttpPost("ekle"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserAdminInput input)
    {
        if (input.Role is not ("Admin" or "Editor" or "Accountant")) ModelState.AddModelError(nameof(input.Role), "Geçersiz rol.");
        if (!await _roles.RoleExistsAsync(input.Role)) ModelState.AddModelError(nameof(input.Role), "Rol bulunamadı.");
        if (!ModelState.IsValid) return View(input);
        var user = new ApplicationUser { UserName = input.Email.Trim(), Email = input.Email.Trim(), EmailConfirmed = false };
        var result = await _users.CreateAsync(user, input.Password);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(input);
        }
        var roleResult = await _users.AddToRoleAsync(user, input.Role);
        if (!roleResult.Succeeded)
        {
            await _users.DeleteAsync(user);
            foreach (var error in roleResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return View(input);
        }
        await _audit.RecordAsync("create", "User", user.Id, $"role={input.Role}; email={user.Email}");
        TempData["Notice"] = "Kullanıcı oluşturuldu. İlk giriş için e-posta doğrulama akışı ayrıca yapılandırılmalıdır.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("kilitle/{id}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleLock(string id)
    {
        var user = await _users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (user.Id == _users.GetUserId(User)) { TempData["Notice"] = "Kendi hesabınızı bu ekrandan kilitleyemezsiniz."; return RedirectToAction(nameof(Index)); }
        var locked = user.LockoutEnd > DateTimeOffset.UtcNow;
        user.LockoutEnabled = true;
        user.LockoutEnd = locked ? null : DateTimeOffset.UtcNow.AddYears(10);
        var result = await _users.UpdateAsync(user);
        if (!result.Succeeded) TempData["Notice"] = string.Join("; ", result.Errors.Select(x => x.Description));
        else
        {
            await _audit.RecordAsync(locked ? "unlock" : "lock", "User", user.Id, user.Email);
            TempData["Notice"] = locked ? "Kullanıcı kilidi kaldırıldı." : "Kullanıcı kilitlendi.";
        }
        return RedirectToAction(nameof(Index));
    }
}

public sealed record UserListRow(string Id, string Email, IList<string> Roles, bool IsLocked);
