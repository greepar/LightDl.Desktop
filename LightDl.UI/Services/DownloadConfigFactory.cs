using System.Net;
using LightDl.UI.Models;

namespace LightDl.UI.Services;

public static class DownloadConfigFactory
{
    public static LightDownloadConfig Create(AppSettings settings,
        Action<LightDownloadProtocolSelection>? protocolSelectionHandler = null)
    {
        var config = new LightDownloadConfig
        {
            ChunkCount = settings.ChunkCount,
            MinChunkCount = settings.MinChunkCount,
            MaxChunkCount = settings.MaxChunkCount,
            EnableDynamicConcurrency = settings.EnableDynamicConcurrency,
            SegmentSize = settings.SegmentSizeMb * 1024L * 1024L,
            Timeout = TimeSpan.FromSeconds(settings.TimeoutSeconds),
            MaxRetry = settings.MaxRetry,
            EnableResume = settings.EnableResume,
            FileConflictPolicy = (LightDownloadFileConflictPolicy)settings.FileConflictPolicy,
            IgnoreSslErrors = settings.IgnoreSslErrors,
            EnableHttp3 = settings.Http3Mode != 2,
            ForceHttp3 = settings.Http3Mode == 1,
            ProtocolSelectionHandler = protocolSelectionHandler,
            HttpMessageHandlerFactory = DownloadPlatform.HttpMessageHandlerFactory,
            UseProxy = settings.ProxyMode != 1,
            Proxy = settings.ProxyMode == 2 ? new WebProxy(settings.ProxyUrl) : null,
            SpeedLimitProvider = settings.SpeedLimitKib > 0
                ? () => settings.SpeedLimitKib * 1024d
                : null
        };

        if (!string.IsNullOrWhiteSpace(settings.UserAgent))
            config.UserAgent = settings.UserAgent;

        return config;
    }
}
