using System.ComponentModel.DataAnnotations;
using KarakusAluminyum.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
namespace KarakusAluminyum.Web.Controllers;
public class AccountController:Controller
{
 private readonly SignInManager<ApplicationUser> _signIn;
 public AccountController(SignInManager<ApplicationUser> signIn)=>_signIn=signIn;
 [HttpGet("yonetim-giris")] public IActionResult Login(string? returnUrl=null){ViewBag.ReturnUrl=returnUrl;return View();}
 [HttpPost("yonetim-giris"),ValidateAntiForgeryToken] public async Task<IActionResult> Login(LoginInput model,string? returnUrl=null){ViewBag.ReturnUrl=returnUrl;if(!ModelState.IsValid)return View(model);var result=await _signIn.PasswordSignInAsync(model.Email,model.Password,model.RememberMe,lockoutOnFailure:true);if(result.Succeeded){if(!string.IsNullOrEmpty(returnUrl)&&Url.IsLocalUrl(returnUrl))return Redirect(returnUrl);return Redirect("/yonetim");}ModelState.AddModelError(string.Empty,"Giriş başarısız. Bilgileri kontrol edin veya bir süre sonra tekrar deneyin.");return View(model);}
 [HttpPost("yonetim-cikis"),ValidateAntiForgeryToken] public async Task<IActionResult> Logout(){await _signIn.SignOutAsync();return RedirectToAction(nameof(Login));}
 [HttpGet("erisim-engellendi")] public IActionResult AccessDenied()=>View();
}
public sealed class LoginInput{[Required,EmailAddress]public string Email{get;set;}="";[Required,DataType(DataType.Password)]public string Password{get;set;}="";public bool RememberMe{get;set;}}
