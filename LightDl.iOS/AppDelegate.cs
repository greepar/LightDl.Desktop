using Foundation;
using UIKit;
using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.iOS;
using Avalonia.Media;
using LightDl.UI;
using LightDl.UI.Services;
using Semi.Avalonia;

namespace LightDl.iOS;

// The UIApplicationDelegate for the application. This class is responsible for launching the
// User Interface of the application, as well as listening (and optionally responding) to
// application events from iOS.
[Register("AppDelegate")]
#pragma warning disable CA1711 // Identifiers should not have incorrect suffix
public partial class AppDelegate : AvaloniaAppDelegate<App>
#pragma warning restore CA1711 // Identifiers should not have incorrect suffix
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        DownloadPlatform.DefaultDownloadDirectoryFactory = static () =>
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        App.MobileThemeFactory = static () => new SemiTheme
        {
            Locale = CultureInfo.GetCultureInfo("zh-CN")
        };
        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
