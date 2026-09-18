param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts/OwHelper-installer-source'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if (Test-Path $Output) {
        Remove-Item -Recurse -Force $Output
    }
    dotnet publish src/OwHelper.Desktop/OwHelper.Desktop.csproj -c $Configuration -r $Runtime --self-contained true -o $Output
    Write-Host "Published installer source to $Output (desktop self-contained only)"
} finally {
    Pop-Location
}
