using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Android;
using LightDl.UI.Services;

namespace LightDl.Android;

[Activity(
    Label = "LightDl",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
[IntentFilter(
    [Intent.ActionView],
    Categories = [Intent.CategoryDefault, Intent.CategoryBrowsable],
    DataScheme = "lightdl",
    DataHost = "add")]
public class MainActivity : AvaloniaMainActivity
{
    private const int StoragePermissionRequestCode = 1001;
    private static MainActivity? _current;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        _current = this;
        base.OnCreate(savedInstanceState);
        ActivationBroker.Submit(Intent?.DataString);
    }

    protected override void OnDestroy()
    {
        if (ReferenceEquals(_current, this))
            _current = null;

        base.OnDestroy();
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        Intent = intent;
        ActivationBroker.Submit(intent?.DataString);
    }

    public static Task<bool> EnsureDownloadDirectoryAccessAsync()
    {
        var activity = _current;
        if (activity is null)
            return Task.FromResult(false);

        if (OperatingSystem.IsAndroidVersionAtLeast(30))
        {
            if (global::Android.OS.Environment.IsExternalStorageManager)
                return Task.FromResult(true);

            var intent = new Intent(Settings.ActionManageAppAllFilesAccessPermission,
                global::Android.Net.Uri.Parse($"package:{activity.PackageName}"));
            activity.StartActivity(intent);
            return Task.FromResult(false);
        }

        if (activity.CheckSelfPermission(global::Android.Manifest.Permission.WriteExternalStorage) == Permission.Granted)
            return Task.FromResult(true);

        activity.RequestPermissions([global::Android.Manifest.Permission.WriteExternalStorage], StoragePermissionRequestCode);
        return Task.FromResult(false);
    }
}
