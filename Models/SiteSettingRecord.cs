using System.ComponentModel.DataAnnotations;
namespace KarakusAluminyum.Web.Models;
public sealed class SiteSettingRecord
{
    public int Id { get; set; }
    [Required, StringLength(100)] public string Key { get; set; } = "";
    [StringLength(2000)] public string Value { get; set; } = "";
    [StringLength(250)] public string Description { get; set; } = "";
}
