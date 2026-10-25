#requires -Version 7.4
param(
    [Parameter(Mandatory)][string]$PublishDirectory,
    [Parameter(Mandatory)]
    [ValidateSet('win-x86', 'win-x64', 'win-arm64', 'linux-x64', 'linux-arm64', 'osx-x64', 'osx-arm64')]
    [string]$RuntimeIdentifier,
    [string]$OutputDirectory = '.',
    [string]$Version,
    [string]$AppImageTool,
    [string]$Python = 'python3'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
Set-StrictMode -Version Latest
$repoDirectory = Split-Path $PSScriptRoot -Parent
$publish = (Resolve-Path -LiteralPath $PublishDirectory).Path
$output = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)).FullName
$stem = "lightdl-desktop-$RuntimeIdentifier"

if ($RuntimeIdentifier.StartsWith('win-')) {
    if (!(Test-Path -LiteralPath (Join-Path $publish 'LightDl.Desktop.exe') -PathType Leaf)) {
        throw 'Published Windows executable is missing.'
    }
    Compress-Archive -Path (Join-Path $publish '*') -DestinationPath (Join-Path $output "$stem.zip") -Force
    return
}

if ($RuntimeIdentifier.StartsWith('osx-')) {
    if (!$IsMacOS) { throw 'Package and sign macOS applications on macOS.' }
    $temporary = Join-Path ([IO.Path]::GetTempPath()) ("lightdl-macos-package-" + [guid]::NewGuid().ToString('N'))
    try {
        $arguments = @((Join-Path $PSScriptRoot 'macos/package.py'), $publish, '--rid', $RuntimeIdentifier, '--output', $temporary)
        if ($Version) { $arguments += @('--version', $Version) }
        & $Python @arguments
        & $Python (Join-Path $PSScriptRoot 'macos/verify.py') $temporary --rid $RuntimeIdentifier
        Copy-Item -LiteralPath (Join-Path $temporary "$stem.dmg") -Destination $output -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Recurse -Force }
    }
    return
}

if (!$IsLinux) { throw 'Package AppImages on Linux.' }
$architecture = if ($RuntimeIdentifier -eq 'linux-arm64') { 'aarch64' } else { 'x86_64' }
$temporary = [IO.Directory]::CreateTempSubdirectory('lightdl-appimage-').FullName
$oldArchitecture = $env:ARCH
$oldExtractAndRun = $env:APPIMAGE_EXTRACT_AND_RUN
try {
    if (!$AppImageTool) {
        $AppImageTool = Join-Path $temporary 'appimagetool.AppImage'
        Invoke-WebRequest -Uri "https://github.com/AppImage/appimagetool/releases/download/1.9.1/appimagetool-$architecture.AppImage" `
            -OutFile $AppImageTool -MaximumRetryCount 3 -RetryIntervalSec 2
    }
    $AppImageTool = (Resolve-Path -LiteralPath $AppImageTool).Path
    $executableMode = [IO.UnixFileMode]493 # 0755
    [IO.File]::SetUnixFileMode($AppImageTool, $executableMode)
    $appDirectory = Join-Path $temporary 'LightDl.AppDir'
    $binDirectory = [IO.Directory]::CreateDirectory((Join-Path $appDirectory 'usr/bin')).FullName
    $executable = Join-Path $binDirectory 'LightDl.Desktop'
    Copy-Item -LiteralPath (Join-Path $publish 'LightDl.Desktop') -Destination $executable
    [IO.File]::SetUnixFileMode($executable, $executableMode)
    Copy-Item -LiteralPath (Join-Path $repoDirectory 'LightDl.UI/Assets/lightdl-light.png') -Destination (Join-Path $appDirectory 'lightdl.png')
    [void][IO.File]::CreateSymbolicLink((Join-Path $appDirectory '.DirIcon'), 'lightdl.png')
    $entry = [IO.File]::ReadAllText((Join-Path $repoDirectory 'packaging/linux/io.lightdl.LightDl.desktop')).
        Replace('Exec=/opt/lightdl/LightDl.Desktop', 'Exec=LightDl.Desktop').
        Replace('Icon=/opt/lightdl/lightdl.png', 'Icon=lightdl')
    $desktopFile = Join-Path $appDirectory 'io.lightdl.LightDl.desktop'
    [IO.File]::WriteAllText($desktopFile, $entry.Replace("`r`n", "`n"))
    # This is a runtime POSIX launcher, so users do not need PowerShell to launch the AppImage.
    $launcher = @'
#!/bin/sh
set -eu
app_dir="$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)"
exec "$app_dir/usr/bin/LightDl.Desktop" "$@"
'@
    $appRun = Join-Path $appDirectory 'AppRun'
    [IO.File]::WriteAllText($appRun, $launcher.Replace("`r`n", "`n") + "`n")
    [IO.File]::SetUnixFileMode($appRun, $executableMode)
    & desktop-file-validate $desktopFile
    $env:ARCH = $architecture
    $env:APPIMAGE_EXTRACT_AND_RUN = '1'
    $package = Join-Path $output "$stem.AppImage"
    & $AppImageTool $appDirectory $package
    [IO.File]::SetUnixFileMode($package, $executableMode)
    & $Python (Join-Path $PSScriptRoot 'linux/verify-appimage.py') $package --rid $RuntimeIdentifier
}
finally {
    $env:ARCH = $oldArchitecture
    $env:APPIMAGE_EXTRACT_AND_RUN = $oldExtractAndRun
    Remove-Item -LiteralPath $temporary -Recurse -Force
}
