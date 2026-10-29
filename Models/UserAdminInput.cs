using System.ComponentModel.DataAnnotations;

namespace KarakusAluminyum.Web.Models;

public sealed class UserAdminInput
{
    [Required, EmailAddress, StringLength(256)] public string Email { get; set; } = "";
    [Required, StringLength(100, MinimumLength = 12)] public string Password { get; set; } = "";
    [Required] public string Role { get; set; } = "Editor";
}
