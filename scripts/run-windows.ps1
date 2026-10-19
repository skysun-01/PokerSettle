<#
.SYNOPSIS
  Builds Poker Settle for Windows and starts it.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\run-windows.ps1
  powershell -ExecutionPolicy Bypass -File scripts\run-windows.ps1 -Configuration Release
#>
param([ValidateSet("Debug", "Release")][string]$Configuration = "Debug")

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$tfm = "net10.0-windows10.0.19041.0"
$project = Join-Path $root "src\PokerSettle.App\PokerSettle.App.csproj"

$running = Get-Process PokerSettle -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Poker Settle is already running; close it first so the build can replace the files." -ForegroundColor Yellow
    exit 1
}

dotnet build $project -f $tfm -c $Configuration -nologo
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$exe = Get-ChildItem (Join-Path $root "src\PokerSettle.App\bin\$Configuration\$tfm") -Recurse -Filter PokerSettle.exe | Select-Object -First 1
if (-not $exe) { throw "PokerSettle.exe not found after the build." }
Write-Host "Starting $($exe.FullName)"
Start-Process $exe.FullName
