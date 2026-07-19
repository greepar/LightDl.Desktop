namespace LightDl.UI.Services;

public static class ProtocolActivation
{
    public static bool TryGetDownloadUrl(string value, out string downloadUrl)
    {
        downloadUrl = string.Empty;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var activation) ||
            !activation.Scheme.Equals("lightdl", StringComparison.OrdinalIgnoreCase) ||
            !activation.Host.Equals("add", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (var part in activation.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length != 2 || !pair[0].Equals("url", StringComparison.OrdinalIgnoreCase))
                continue;

            var candidate = Uri.UnescapeDataString(pair[1].Replace('+', ' '));
            if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
                (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
            {
                downloadUrl = uri.AbsoluteUri;
                return true;
            }
        }

        return false;
    }
}
