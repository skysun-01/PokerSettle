<#
.SYNOPSIS
  Builds a signed release APK for sideloading, then checks it: signature, 16 KB page alignment of
  native libraries, and that it does not request the INTERNET permission.
.DESCRIPTION
  The password is read from POKERSETTLE_KEYSTORE_PASSWORD if set, otherwise prompted. It is passed to
  the build through an environment variable, never on the command line.
  Output: artifacts\PokerSettle-<version>.apk
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\build-android-release.ps1
#>
param(
    [string]$KeystorePath = "$env:USERPROFILE\.pokersettle\poker-settle.keystore",
    [string]$Alias = "pokersettle"
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "common.ps1")
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "src\PokerSettle.App\PokerSettle.App.csproj"
$artifacts = Join-Path $root "artifacts"
$publishDir = Join-Path $artifacts "android-publish"

if (-not (Test-Path $KeystorePath)) {
    Write-Host "No keystore at $KeystorePath. Create one first: scripts\create-keystore.ps1" -ForegroundColor Yellow
    exit 1
}

# Password: environment variable, else keystore-password.txt next to the keystore, else ask.
$password = $env:POKERSETTLE_KEYSTORE_PASSWORD
$passwordFile = Join-Path (Split-Path -Parent $KeystorePath) "keystore-password.txt"
if (-not $password -and (Test-Path $passwordFile)) { $password = (Get-Content $passwordFile -Raw).Trim() }
if (-not $password) { $password = Read-PlainPassword "Keystore password" }

$env:PS_SIGNING_PASS = $password
try {
    if (Test-Path $publishDir) { Remove-Item $publishDir -Recurse -Force }
    dotnet publish $project -f net10.0-android -c Release -nologo `
        -p:AndroidPackageFormat=apk `
        -p:AndroidKeyStore=true `
        "-p:AndroidSigningKeyStore=$KeystorePath" `
        "-p:AndroidSigningKeyAlias=$Alias" `
        -p:AndroidSigningStorePass=env:PS_SIGNING_PASS `
        -p:AndroidSigningKeyPass=env:PS_SIGNING_PASS `
        -o $publishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
}
finally {
    Remove-Item Env:PS_SIGNING_PASS -ErrorAction SilentlyContinue
}

$signed = Get-ChildItem $publishDir -Filter "*-Signed.apk" | Select-Object -First 1
if (-not $signed) { throw "No signed APK was produced in $publishDir." }
$version = Get-AppVersion $root
$apk = Join-Path $artifacts "PokerSettle-$version.apk"
Copy-Item $signed.FullName $apk -Force

Write-Host ""
Write-Host "Checking $apk" -ForegroundColor Cyan

# 1. Signature (capture first: cutting the pipeline short would break the exit code)
$apksigner = Find-BuildTool "apksigner"
$certs = & $apksigner verify --print-certs $apk
if ($LASTEXITCODE -ne 0) { throw "APK signature verification failed." }
$certs | Select-Object -First 2 | ForEach-Object { "  $_" }

# 2. No INTERNET permission in the release build
$aapt2 = Find-BuildTool "aapt2"
$permissions = & $aapt2 dump permissions $apk
$permissions | ForEach-Object { "  $_" }
if ($permissions -match "android.permission.INTERNET") { throw "The release APK requests INTERNET; it must not." }

# 3. 16 KB page alignment of native libraries
& powershell -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot "check-16kb-alignment.ps1") $apk
if ($LASTEXITCODE -ne 0) { throw "Native libraries are not 16 KB aligned." }
$zipalign = Find-BuildTool "zipalign"
& $zipalign -c -P 16 4 $apk
if ($LASTEXITCODE -ne 0) { throw "zipalign -P 16 check failed." }

$size = [math]::Round((Get-Item $apk).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $apk ($size MB)" -ForegroundColor Green
Write-Host "Copy the APK to a phone to install it, or deploy to a connected device from Visual Studio."
