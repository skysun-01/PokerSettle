<#
.SYNOPSIS
  Runs every test (unit, FsCheck properties, migrations, performance, ViewModel integration).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\test.ps1
#>
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
dotnet test (Join-Path $root "tests\PokerSettle.Tests\PokerSettle.Tests.csproj") -nologo
exit $LASTEXITCODE
