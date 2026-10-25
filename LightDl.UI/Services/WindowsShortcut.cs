using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace LightDl.UI.Services;

/// <summary>Native Unicode Shell links, also usable in NativeAOT without runtime COM marshalling.</summary>
[SupportedOSPlatform("windows")]
public static unsafe partial class WindowsShortcut
{
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly Guid ShellLinkW = new("000214F9-0000-0000-C000-000000000046");
    private static readonly Guid PersistFile = new("0000010B-0000-0000-C000-000000000046");

    public static void Create(string shortcut, string executable)
    {
        executable = Path.GetFullPath(executable);
        shortcut = Path.GetFullPath(shortcut);
        Directory.CreateDirectory(Path.GetDirectoryName(shortcut)!);
        using var shell = new ShellLink();
        SetString(shell.Pointer, 20, executable); // IShellLinkW::SetPath
        SetString(shell.Pointer, 9, Path.GetDirectoryName(executable)!); // SetWorkingDirectory
        SetString(shell.Pointer, 7, "LightDl Download Manager"); // SetDescription
        fixed (char* icon = executable)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Vtable(shell.Pointer)[17])(shell.Pointer, icon, 0));
        var persist = Query(shell.Pointer, PersistFile);
        try
        {
            fixed (char* path = shortcut)
                Check(((delegate* unmanaged[Stdcall]<nint, char*, int, int>)Vtable(persist)[6])(persist, path, 1)); // IPersistFile::Save
        }
        finally { Release(persist); }
    }

    public static string ReadTarget(string shortcut)
    {
        using var shell = new ShellLink();
        var persist = Query(shell.Pointer, PersistFile);
        try
        {
            fixed (char* path = Path.GetFullPath(shortcut))
                Check(((delegate* unmanaged[Stdcall]<nint, char*, uint, int>)Vtable(persist)[5])(persist, path, 0)); // IPersistFile::Load
        }
        finally { Release(persist); }
        var buffer = new char[32768];
        fixed (char* path = buffer)
        {
            Check(((delegate* unmanaged[Stdcall]<nint, char*, int, nint, uint, int>)Vtable(shell.Pointer)[3])(
                shell.Pointer, path, buffer.Length, 0, 4)); // IShellLinkW::GetPath, SLGP_RAWPATH
            return new string(path);
        }
    }

    private static void SetString(nint pointer, int slot, string value)
    {
        fixed (char* text = value)
            Check(((delegate* unmanaged[Stdcall]<nint, char*, int>)Vtable(pointer)[slot])(pointer, text));
    }
    private static nint* Vtable(nint pointer) => *(nint**)pointer;
    private static nint Query(nint pointer, Guid iid)
    {
        nint result = 0;
        Check(((delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)Vtable(pointer)[0])(pointer, &iid, &result));
        return result;
    }
    private static void Release(nint pointer) => ((delegate* unmanaged[Stdcall]<nint, uint>)Vtable(pointer)[2])(pointer);
    private static void Check(int result)
    {
        if (result < 0) throw new IOException($"Windows Shell shortcut failed (HRESULT 0x{result:X8}).");
    }
    private sealed class ShellLink : IDisposable
    {
        public nint Pointer { get; }
        private readonly bool _uninitialize;
        public ShellLink()
        {
            var result = CoInitializeEx(0, 2);
            // The calling thread can already belong to an MTA. ShellLink supports both apartments.
            if (result != unchecked((int)0x80010106)) Check(result);
            _uninitialize = result >= 0;
            try
            {
                Check(CoCreateInstance(in ShellLinkClass, 0, 1, in ShellLinkW, out var pointer));
                Pointer = pointer;
            }
            catch { if (_uninitialize) CoUninitialize(); throw; }
        }
        public void Dispose()
        {
            Release(Pointer);
            if (_uninitialize) CoUninitialize();
        }
    }
    [LibraryImport("ole32.dll")]
    private static partial int CoInitializeEx(nint reserved, uint flags);
    [LibraryImport("ole32.dll")]
    private static partial void CoUninitialize();
    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid clsid, nint outer, uint context, in Guid iid, out nint pointer);
}
