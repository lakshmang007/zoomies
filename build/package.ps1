# Builds the installer: a self-contained release build of Zoomies, packed by Velopack into
#   artifacts\releases\Zoomies-win-Setup.exe       (what people download)
#   artifacts\releases\Zoomies-win-Portable.zip    (runs without installing)
#   plus the .nupkg and releases.win.json files the app's updater reads from GitHub Releases.
# Needs the .NET 10 SDK and the vpk tool:  dotnet tool install -g vpk --version 1.2.161
#   powershell -ExecutionPolicy Bypass -File build\package.ps1 -Version 0.1.0
param(
    [string]$Version = "0.1.0",
    [string]$Runtime = "win-x64"
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root "artifacts\publish\$Runtime"
$releases = Join-Path $root "artifacts\releases"

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }

dotnet publish "$root\src\Zoomies\Zoomies.csproj" -c Release -r $Runtime --self-contained true `
    -p:Version=$Version -p:PublishReadyToRun=true -o $publish
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed" }

vpk pack --packId Zoomies --packVersion $Version --packDir $publish --mainExe Zoomies.exe `
    --packTitle Zoomies --packAuthors "Lakshman G" --icon "$root\src\Zoomies\Assets\Zoomies.ico" `
    --shortcuts StartMenuRoot --runtime $Runtime --outputDir $releases
if ($LASTEXITCODE -ne 0) { throw "vpk pack failed" }

Get-ChildItem $releases | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
