using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace KarakusAluminyum.Web.Areas.Admin.Controllers;

// Yardım: panelin bölümlerini ve uçtan uca örnek bir işi (talep → keşif → teklif → sipariş → teslim → servis) anlatır.
[Area("Admin"), Authorize(Roles = "Admin,Editor,Accountant"), Route("yonetim/yardim")]
public sealed class HelpController : Controller
{
    [HttpGet("")]
    public IActionResult Index() => View();
}
