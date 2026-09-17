param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts/OwHelper-win-x64'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    dotnet publish src/OwHelper/OwHelper.csproj -c $Configuration -r $Runtime --self-contained false -o $Output
    dotnet publish src/OwHelper.Tray/OwHelper.Tray.csproj -c $Configuration -r $Runtime --self-contained false -o $Output
    dotnet publish src/BgKeyProbe/BgKeyProbe.csproj -c $Configuration -r $Runtime --self-contained false -o $Output
    Write-Host "Published to $Output (framework-dependent, requires .NET 8 Desktop Runtime)"
} finally {
    Pop-Location
}
