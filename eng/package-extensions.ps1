#requires -Version 7.4
param([string]$OutputDirectory = 'artifacts')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoDirectory = Split-Path $PSScriptRoot -Parent
$output = [IO.Directory]::CreateDirectory([IO.Path]::GetFullPath($OutputDirectory)).FullName
foreach ($browser in @('chromium', 'firefox')) {
    $source = Join-Path $repoDirectory "browser-extensions/automatic/$browser"
    $archive = Join-Path $output "lightdl-automatic-$browser.zip"
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    $zip = [IO.Compression.ZipFile]::Open($archive, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $source -Recurse -File -Force) {
            if ($file.Name -eq '.DS_Store') { continue }
            $relative = [IO.Path]::GetRelativePath($source, $file.FullName).Replace('\', '/')
            [void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $file.FullName, $relative)
        }
    }
    finally { $zip.Dispose() }
    $zip = [IO.Compression.ZipFile]::OpenRead($archive)
    try {
        $manifest = $zip.GetEntry('manifest.json')
        if (!$manifest) { throw "Archive has no root manifest: $archive" }
        $reader = [IO.StreamReader]::new($manifest.Open())
        try { [void]($reader.ReadToEnd() | ConvertFrom-Json) }
        finally { $reader.Dispose() }
    }
    finally { $zip.Dispose() }
    Write-Host "Created and verified $archive"
}
