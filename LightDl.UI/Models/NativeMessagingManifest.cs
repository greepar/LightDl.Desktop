namespace LightDl.UI.Models;

public sealed class NativeMessagingManifest
{
    public string Name { get; set; } = "com.lightdl.browser";

    public string Description { get; set; } = "LightDl browser integration host";

    public string Path { get; set; } = string.Empty;

    public string Type { get; set; } = "stdio";

    public string[]? AllowedOrigins { get; set; }

    public string[]? AllowedExtensions { get; set; }
}
