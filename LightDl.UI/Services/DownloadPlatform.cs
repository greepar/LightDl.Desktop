using System.Net.Http;

namespace LightDl.UI.Services;

public static class DownloadPlatform
{
    public static Func<HttpMessageHandler>? HttpMessageHandlerFactory { get; set; }

    public static Func<string>? DefaultDownloadDirectoryFactory { get; set; }

    public static Func<Task<bool>>? EnsureDownloadDirectoryAccessAsync { get; set; }
}
