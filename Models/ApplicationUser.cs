using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
namespace KarakusAluminyum.Web.Models;

// Ad soyad ve unvan belgelerde kullanılır: keşifte ölçüyü alan, teklif ve sözleşmede imza yetkilisi olarak otomatik yazılır.
public sealed class ApplicationUser : IdentityUser
{
    [StringLength(120)] public string? FullName { get; set; }
    [StringLength(60)] public string? JobTitle { get; set; }
}
