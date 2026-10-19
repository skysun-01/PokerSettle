<#
.SYNOPSIS
  Installs (or updates) the release APK on a phone connected by USB with USB debugging on.
.DESCRIPTION
  Updating keeps the app's data only when the APK is signed with the same keystore as the installed app.
  If Android says the signatures don't match, export a backup in the app first: the only way forward is
  to uninstall, which deletes the app's data.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\install-android.ps1
#>
param([string]$Apk)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
$root = Split-Path -Parent $PSScriptRoot
if (-not $Apk) {
    $Apk = Get-ChildItem (Join-Path $root "artifacts") -Filter "PokerSettle-*.apk" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $Apk -or -not (Test-Path $Apk)) { throw "No APK found. Build one with scripts\build-android-release.ps1." }

$adb = Join-Path (Find-AndroidSdk) "platform-tools\adb.exe"
$devices = & $adb devices | Select-Object -Skip 1 | Where-Object { $_ -match "\tdevice$" }
if (-not $devices) { throw "No phone found. Connect it by USB, enable USB debugging, and accept the prompt on the phone." }

Write-Host "Installing $Apk"
& $adb install -r $Apk
if ($LASTEXITCODE -ne 0) {
    Write-Host "Install failed. If it mentions signatures (INSTALL_FAILED_UPDATE_INCOMPATIBLE), the installed app was signed with a different key." -ForegroundColor Yellow
    Write-Host "Export a backup in the app first, then uninstall it and install again." -ForegroundColor Yellow
    exit 1
}
Write-Host "Installed." -ForegroundColor Green
