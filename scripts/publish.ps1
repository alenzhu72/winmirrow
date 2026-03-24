param(
  [string]$Configuration = "Release",
  [string]$Runtime = "win-x64",
  [bool]$SelfContained = $false
)

$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\WinMirrorClicker\WinMirrorClicker.csproj"
$distRoot = Join-Path $PSScriptRoot "..\dist"
$outDir = Join-Path $distRoot "WinMirrorClicker_$Configuration"

if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

dotnet publish $project -c $Configuration -r $Runtime --self-contained:$SelfContained -o $outDir

$configSrc = Join-Path $PSScriptRoot "..\WinMirrorClicker\config.txt"
if (Test-Path $configSrc) { Copy-Item $configSrc (Join-Path $outDir "config.txt") -Force }

$zipPath = Join-Path $distRoot "WinMirrorClicker_$Configuration.zip"
if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -Force

Write-Host "Published to: $outDir"
Write-Host "Zip package: $zipPath"
