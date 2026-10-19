<#
.SYNOPSIS
  Checks that every 64-bit native library (.so) in an APK/AAR/folder has LOAD segments aligned
  to at least 16 KB, as required for Android devices that use 16 KB memory pages.
.EXAMPLE
  powershell -ExecutionPolicy Bypass -File scripts\check-16kb-alignment.ps1 artifacts\PokerSettle-1.0.0.apk
#>
param([Parameter(Mandatory = $true)][string]$Path)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression.FileSystem

function Test-Elf([byte[]]$bytes, [string]$name) {
    if ($bytes.Length -lt 64 -or $bytes[0] -ne 0x7F -or $bytes[1] -ne 0x45) { return $null }
    if ($bytes[4] -ne 2) { return [pscustomobject]@{ File = $name; Bits = 32; MinAlign = $null; Ok = $true } } # 32-bit ABIs are exempt
    $phoff = [BitConverter]::ToUInt64($bytes, 0x20)
    $phentsize = [BitConverter]::ToUInt16($bytes, 0x36)
    $phnum = [BitConverter]::ToUInt16($bytes, 0x38)
    $min = [UInt64]::MaxValue
    for ($i = 0; $i -lt $phnum; $i++) {
        $o = [int]($phoff + [UInt64]($i * $phentsize))
        $type = [BitConverter]::ToUInt32($bytes, $o)
        if ($type -ne 1) { continue } # PT_LOAD
        $align = [BitConverter]::ToUInt64($bytes, $o + 48)
        if ($align -lt $min) { $min = $align }
    }
    [pscustomobject]@{ File = $name; Bits = 64; MinAlign = $min; Ok = ($min -ge 16384) }
}

$results = @()
$item = Get-Item $Path
if ($item.PSIsContainer) {
    foreach ($so in Get-ChildItem $Path -Recurse -Filter *.so) { $results += Test-Elf ([IO.File]::ReadAllBytes($so.FullName)) $so.FullName }
} else {
    $zip = [IO.Compression.ZipFile]::OpenRead($item.FullName)
    try {
        foreach ($entry in $zip.Entries | Where-Object { $_.FullName -like '*.so' }) {
            $ms = New-Object IO.MemoryStream
            $s = $entry.Open(); $s.CopyTo($ms); $s.Dispose()
            $results += Test-Elf $ms.ToArray() $entry.FullName
        }
    } finally { $zip.Dispose() }
}

$results | Where-Object { $_ } | Format-Table File, Bits, @{ n = "MinAlign"; e = { if ($_.MinAlign) { "0x{0:X}" -f $_.MinAlign } else { "-" } } }, Ok -AutoSize | Out-String -Width 200
$bad = @($results | Where-Object { $_ -and -not $_.Ok })
if ($bad.Count -gt 0) { Write-Host "FAIL: $($bad.Count) 64-bit libraries are not 16 KB aligned." -ForegroundColor Red; exit 1 }
Write-Host "OK: all 64-bit native libraries are 16 KB aligned." -ForegroundColor Green
