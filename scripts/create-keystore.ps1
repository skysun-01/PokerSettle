<#
.SYNOPSIS
  Creates the Android signing keystore. Run this ONCE, then keep the file and its password safe forever.
.DESCRIPTION
  Every release APK must be signed with the same key, or Android refuses to install it as an update
  (you would have to uninstall, which deletes the app's data). The keystore is created outside the
  repository by default and is never committed (*.keystore and *.jks are in .gitignore).
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\create-keystore.ps1
#>
param(
    [string]$KeystorePath = "$env:USERPROFILE\.pokersettle\poker-settle.keystore",
    [string]$Alias = "pokersettle"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")

if (Test-Path $KeystorePath) {
    Write-Host "A keystore already exists at $KeystorePath. Not overwriting it: replacing it would stop updates from installing." -ForegroundColor Yellow
    exit 1
}

$keytool = Find-Keytool
$password = Read-PlainPassword "Choose a keystore password (at least 6 characters)"
$confirm = Read-PlainPassword "Type it again"
if ($password -ne $confirm) { throw "The passwords don't match." }
if ($password.Length -lt 6) { throw "Use at least 6 characters." }

New-Item -ItemType Directory -Force (Split-Path -Parent $KeystorePath) | Out-Null
$env:PS_KEYSTORE_PASS = $password
try {
    & $keytool -genkeypair -v -keystore $KeystorePath -storetype PKCS12 -alias $Alias -keyalg RSA -keysize 2048 -validity 10000 `
        -storepass:env PS_KEYSTORE_PASS -keypass:env PS_KEYSTORE_PASS -dname "CN=Poker Settle, OU=Personal, O=Poker Settle, C=IN"
    if ($LASTEXITCODE -ne 0) { throw "keytool failed." }
}
finally {
    Remove-Item Env:PS_KEYSTORE_PASS -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "Keystore created: $KeystorePath (alias '$Alias')." -ForegroundColor Green
Write-Host "Back up this file AND the password somewhere safe (password manager, a USB drive)."
Write-Host "Lose either and you can't publish updates over the installed app without uninstalling it (which deletes its data)."
