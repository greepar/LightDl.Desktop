param(
    [Parameter(Mandatory = $true)][string]$Project,
    [string]$RuntimeIdentifier
)
$ErrorActionPreference = 'Stop'
# Different NuGet CDN edges can briefly expose different package indexes.
# Retry an uncached restore; never downgrade a pinned dependency to make CI pass.
for ($attempt = 1; $attempt -le 3; $attempt++) {
    $restoreArguments = @('restore', $Project, '--no-http-cache')
    if ($RuntimeIdentifier) { $restoreArguments += @('-r', $RuntimeIdentifier) }
    & dotnet @restoreArguments
    if ($LASTEXITCODE -eq 0) { exit 0 }
    if ($attempt -lt 3) {
        Write-Warning "Restore failed (attempt $attempt/3); retrying without HTTP cache."
        Start-Sleep -Seconds (10 * $attempt)
    }
}
throw 'Dependency restore failed after three attempts.'
