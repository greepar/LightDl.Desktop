using System.Net;
using System.Text.RegularExpressions;

namespace LightDl.UI.Services.Updates;

public static class ReleaseNotesFormatter
{
    public static string Format(string markdown)
    {
        var text = markdown.Replace("\r\n", "\n");
        text = Regex.Replace(text, @"<!-- lightdl-downloads:start -->[\s\S]*?<!-- lightdl-downloads:end -->", "");
        var lines = new List<string>();
        var inDownloads = false;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (Regex.IsMatch(line, @"^#{1,6}\s+(Downloads?\b|下载)", RegexOptions.IgnoreCase))
            {
                inDownloads = true;
                continue;
            }
            if (inDownloads)
            {
                if (!Regex.IsMatch(line, @"^#{1,6}\s+") && !line.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase)) continue;
                inDownloads = false;
            }
            if (line.StartsWith("**Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Full Changelog", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("<!--", StringComparison.Ordinal)) continue;
            line = Regex.Replace(line, @"!\[([^\]]*)\]\([^)]*\)", "");
            line = Regex.Replace(line, @"\[([^\]]+)\]\([^)]*\)", "$1");
            line = Regex.Replace(line, @"^#{1,6}\s+", "");
            line = Regex.Replace(line, @"^[-*+]\s+", "• ");
            line = line.Replace("**", "").Replace("__", "").Replace("`", "");
            lines.Add(WebUtility.HtmlDecode(line));
        }
        var result = Regex.Replace(string.Join('\n', lines), @"\n{3,}", "\n\n").Trim();
        return string.IsNullOrWhiteSpace(result)
            ? "此版本未提供具体更新说明，可在完整 Release 页面查看。"
            : result.Length > 16000 ? result[..16000] + "\n…更多内容请查看完整 Release。" : result;
    }
}
