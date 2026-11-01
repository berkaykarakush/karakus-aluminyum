using System.Net;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace KarakusAluminyum.Web.Services;

// img-sizes verilen görselde küçük kopya varsa srcset ekler; tarayıcı ekrana uygun dosyayı indirir.
// Kartlarda telefon için 66vw yazılır: 3x ekranda 800 piksellik kopya seçilsin, göze görünmeyen ayrıntı için büyük dosya inmesin.
[HtmlTargetElement("img", Attributes = "img-sizes")]
public sealed class ResponsiveImageTagHelper(IWebHostEnvironment environment) : TagHelper
{
    public string ImgSizes { get; set; } = "";

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        var path = Source(context);
        if (path is null || !path.StartsWith('/') || !path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)) return;
        var medium = ImageOptimizer.MediumPath(path);
        if (!environment.WebRootFileProvider.GetFileInfo(medium).Exists) return;
        output.Attributes.SetAttribute("srcset", $"{medium} {ImageOptimizer.MediumWidth}w, {path} {ImageOptimizer.MaxEdge}w");
        output.Attributes.SetAttribute("sizes", ImgSizes);
    }

    private static string? Source(TagHelperContext context)
    {
        if (!context.AllAttributes.TryGetAttribute("src", out var attribute)) return null;
        if (attribute.Value is not IHtmlContent content) return attribute.Value?.ToString();
        using var writer = new StringWriter();
        content.WriteTo(writer, HtmlEncoder.Default);
        return WebUtility.HtmlDecode(writer.ToString());
    }
}
