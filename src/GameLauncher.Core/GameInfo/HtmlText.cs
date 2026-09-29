using System.Net;
using System.Text.RegularExpressions;

namespace GameLauncher.Core.GameInfo;

/// <summary>Упрощённое превращение HTML описаний Steam в обычный текст.</summary>
public static partial class HtmlText
{
    public static string ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        var text = LineBreakTags().Replace(html, "\n");
        text = ListItemTag().Replace(text, "\n• ");
        text = AnyTag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);
        text = SpacesAndTabs().Replace(text, " ");
        text = SpaceAroundNewLine().Replace(text, "\n");
        text = ManyNewLines().Replace(text, "\n\n");
        return text.Trim();
    }

    [GeneratedRegex(@"<\s*(br|/p|/h\d|/div|/ul|/ol)\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreakTags();

    [GeneratedRegex(@"<\s*li[^>]*>", RegexOptions.IgnoreCase)]
    private static partial Regex ListItemTag();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex AnyTag();

    [GeneratedRegex(@"[ \t ]+")]
    private static partial Regex SpacesAndTabs();

    [GeneratedRegex(@" *\n *")]
    private static partial Regex SpaceAroundNewLine();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex ManyNewLines();
}
