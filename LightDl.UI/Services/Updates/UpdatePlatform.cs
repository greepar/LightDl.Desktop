namespace LightDl.UI.Services.Updates;

public static class UpdatePlatform
{
    public static DesktopUpdateService? Service { get; set; }
    public static Action? StartupCompleted { get; set; }
}
