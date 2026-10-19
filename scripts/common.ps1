# Shared helpers for the build scripts (dot-source this file).

function Find-AndroidSdk {
    foreach ($candidate in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "${env:ProgramFiles(x86)}\Android\android-sdk", "$env:LOCALAPPDATA\Android\Sdk")) {
        if ($candidate -and (Test-Path (Join-Path $candidate "platform-tools"))) { return $candidate }
    }
    throw "Android SDK not found. Install it with Visual Studio (.NET MAUI workload) or Android Studio, or set ANDROID_HOME."
}

function Find-BuildTool([string]$name) {
    $sdk = Find-AndroidSdk
    $tool = Get-ChildItem (Join-Path $sdk "build-tools") -Directory | Sort-Object { [version]($_.Name -replace '[^0-9.]', '') } -Descending |
        ForEach-Object { Get-ChildItem $_.FullName -Filter "$name*" -File } | Where-Object { $_.Name -in @("$name.exe", "$name.bat") } | Select-Object -First 1
    if (-not $tool) { throw "$name not found in the Android SDK build-tools." }
    return $tool.FullName
}

function Find-Keytool {
    foreach ($candidate in @("$env:JAVA_HOME\bin\keytool.exe", "${env:ProgramFiles}\Android\openjdk\*\bin\keytool.exe", "${env:ProgramFiles}\Microsoft\jdk-*\bin\keytool.exe", "${env:ProgramFiles}\Android\Android Studio\jbr\bin\keytool.exe")) {
        $found = Get-Item $candidate -ErrorAction SilentlyContinue | Select-Object -First 1
        if ($found) { return $found.FullName }
    }
    $onPath = Get-Command keytool -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }
    throw "keytool not found. Install a JDK (17 or newer) and set JAVA_HOME."
}

function Read-PlainPassword([string]$prompt) {
    $secure = Read-Host $prompt -AsSecureString
    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try { return [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr) }
    finally { [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr) }
}

function Get-AppVersion([string]$root) {
    $csproj = [xml](Get-Content (Join-Path $root "src\PokerSettle.App\PokerSettle.App.csproj") -Raw)
    $version = ($csproj.Project.PropertyGroup | ForEach-Object { $_.ApplicationDisplayVersion } | Where-Object { $_ } | Select-Object -First 1)
    if (-not $version) { $version = "1.0.0" }
    return $version
}
