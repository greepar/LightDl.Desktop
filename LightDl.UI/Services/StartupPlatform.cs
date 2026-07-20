namespace LightDl.UI.Services;

public static class StartupPlatform
{
    public static Func<bool, Task>? ConfigureAutoStartAsync { get; set; }
}
