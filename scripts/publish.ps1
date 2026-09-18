param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',
    [string]$Output = 'artifacts/OwHelper-win-x64',
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $sc = $SelfContained ? 'true' : 'false'
    dotnet publish src/OwHelper/OwHelper.csproj -c $Configuration -r $Runtime --self-contained $sc -o $Output
    dotnet publish src/OwHelper.Desktop/OwHelper.Desktop.csproj -c $Configuration -r $Runtime --self-contained $sc -o $Output
    dotnet publish src/BgKeyProbe/BgKeyProbe.csproj -c $Configuration -r $Runtime --self-contained $sc -o $Output
    if ($SelfContained) {
        Write-Host "Published to $Output (self-contained, no .NET install needed)"
    } else {
        Write-Host "Published to $Output (framework-dependent, requires .NET 8 Desktop Runtime)"
    }
} finally {
    Pop-Location
}

