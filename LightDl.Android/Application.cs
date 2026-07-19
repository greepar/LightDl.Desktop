using Android.App;
using Android.Runtime;
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Android;
using LightDl.UI;
using LightDl.UI.Services;
using Semi.Avalonia;
#if LIGHTDL_ANDROID_PLATFORM_HTTP
using Xamarin.Android.Net;
#endif

namespace LightDl.Android
{
    [Application]
    public class Application : AvaloniaAndroidApplication<App>
    {
        protected Application(nint javaReference, JniHandleOwnership transfer) : base(javaReference, transfer)
        {
        }

        protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
        {
            DownloadPlatform.DefaultDownloadDirectoryFactory = static () =>
                global::Android.OS.Environment.GetExternalStoragePublicDirectory(
                    global::Android.OS.Environment.DirectoryDownloads)?.AbsolutePath
                ?? global::Android.App.Application.Context.FilesDir?.AbsolutePath
                ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            DownloadPlatform.EnsureDownloadDirectoryAccessAsync = MainActivity.EnsureDownloadDirectoryAccessAsync;
            App.MobileThemeFactory = static () => new SemiTheme
            {
                Locale = CultureInfo.GetCultureInfo("zh-CN")
            };
#if LIGHTDL_ANDROID_PLATFORM_HTTP
            DownloadPlatform.HttpMessageHandlerFactory = static () => new AndroidMessageHandler
            {
                AllowAutoRedirect = true,
                AutomaticDecompression = System.Net.DecompressionMethods.All,
                MaxConnectionsPerServer = 64,
                ConnectTimeout = TimeSpan.FromSeconds(30),
                ReadTimeout = TimeSpan.FromSeconds(30)
            };
#endif
            return base.CustomizeAppBuilder(builder)
                .WithInterFont();
        }
    }
}
