using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace KarakusAluminyum.Web.Services;

// Panelde yazılan metin için küçük ve güvenli biçimleyici: başlık, liste, kalın yazı ve site içi bağlantı.
// Ham HTML desteklenmez; tüm metin önce kodlanır.
public static partial class SimpleMarkdown
{
    [GeneratedRegex(@"\*\*(.+?)\*\*")] private static partial Regex Bold();
    [GeneratedRegex(@"\[([^\]]+)\]\((/(?![/\\])[^\s()""'<>\\]*)\)")] private static partial Regex Link();

    private static string Inline(string text)
    {
        var encoded = WebUtility.HtmlEncode(text);
        encoded = Link().Replace(encoded, "<a href=\"$2\">$1</a>");
        return Bold().Replace(encoded, "<strong>$1</strong>");
    }

    public static string ToHtml(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        var html = new StringBuilder();
        var list = false;
        void CloseList()
        {
            if (list) html.Append("</ul>");
            list = false;
        }
        foreach (var block in text.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            CloseList();
            if (block.StartsWith("### ")) html.Append("<h3>").Append(Inline(block[4..])).Append("</h3>");
            else if (block.StartsWith("## ")) html.Append("<h2>").Append(Inline(block[3..])).Append("</h2>");
            else if (block.StartsWith("- "))
            {
                html.Append("<ul>");
                list = true;
                foreach (var line in block.Split('\n')) html.Append("<li>").Append(Inline(line.TrimStart('-', ' '))).Append("</li>");
            }
            else html.Append("<p>").Append(string.Join("<br />", block.Split('\n').Select(Inline))).Append("</p>");
        }
        CloseList();
        return html.ToString();
    }
}
