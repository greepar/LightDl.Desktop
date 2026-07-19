param(
    [Parameter(Mandatory = $true)]
    [string]$ExecutablePath
)

$resolvedPath = (Resolve-Path -LiteralPath $ExecutablePath).Path
$protocolKey = "HKCU:\Software\Classes\lightdl"

New-Item -Path $protocolKey -Force | Out-Null
Set-Item -Path $protocolKey -Value "URL:LightDl Protocol"
New-ItemProperty -Path $protocolKey -Name "URL Protocol" -Value "" -PropertyType String -Force | Out-Null

$commandKey = Join-Path $protocolKey "shell\open\command"
New-Item -Path $commandKey -Force | Out-Null
Set-Item -Path $commandKey -Value ('"{0}" "%1"' -f $resolvedPath)
