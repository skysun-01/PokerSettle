<#
.SYNOPSIS
  Publishes a self-contained Windows build: a folder with PokerSettle.exe that runs on any Windows 10/11
  PC without installing .NET or the Windows App SDK.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\publish-windows.ps1
  # then run artifacts\windows\PokerSettle.exe (copy the whole folder anywhere you like)
#>
param([ValidateSet("win-x64", "win-arm64")][string]$Runtime = "win-x64")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\PokerSettle.App\PokerSettle.App.csproj"
$output = Join-Path $root "artifacts\windows"

if (Test-Path $output) { Remove-Item $output -Recurse -Force }
dotnet publish $project -f net10.0-windows10.0.19041.0 -c Release -r $Runtime --self-contained true -nologo `
    -p:WindowsPackageType=None `
    -p:WindowsAppSDKSelfContained=true `
    -p:PublishReadyToRun=true `
    -o $output
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Join-Path $output "PokerSettle.exe"
if (-not (Test-Path $exe)) { throw "PokerSettle.exe was not produced." }
$size = [math]::Round(((Get-ChildItem $output -Recurse | Measure-Object Length -Sum).Sum) / 1MB, 0)
Write-Host ""
Write-Host "Done: $exe (folder is $size MB)" -ForegroundColor Green
Write-Host "Data is stored in %LOCALAPPDATA%\PokerSettle\PokerSettle\Data no matter where the folder lives."
