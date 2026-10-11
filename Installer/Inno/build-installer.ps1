# Builds the self-contained Windows build and wraps it in an installer.
# Needs the .NET SDK and Inno Setup 6 (https://jrsoftware.org/isdl.php). Run from anywhere:
#   powershell -ExecutionPolicy Bypass -File Installer\Inno\build-installer.ps1
param(
    [string]$Version = "0.4.40",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..\..")
$publish = Join-Path $root "artifacts\publish"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

Write-Host "Publishing the game..."
dotnet publish (Join-Path $root "TheAirline\TheAirline.csproj") -c $Configuration -r win-x64 --self-contained true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

if (-not (Test-Path (Join-Path $publish "data\Languages.xml"))) { throw "The game data was not copied to the publish folder" }

$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    $cmd = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($cmd) { $iscc = $cmd.Source }
}
if (-not $iscc) { throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php" }

Write-Host "Building the installer..."
& $iscc "/DAppVersion=$Version" "/DPublishDir=$publish" (Join-Path $PSScriptRoot "TheAirline.iss")
if ($LASTEXITCODE -ne 0) { throw "Inno Setup failed" }

Write-Host "Done: $(Join-Path $root "artifacts")"
