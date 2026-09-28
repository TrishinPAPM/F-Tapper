#Requires -Version 5.0
param(
    [switch]$Firmware
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
Set-Location $root

function Patch-HexStamp {
    param([string]$HexPath)
    $marker = [byte[]](0x46, 0x54, 0x41, 0x50, 0xA5) # FTAP + 0xA5
    $lines = [System.IO.File]::ReadAllLines($HexPath)
    $map = @{}
    $min = [int]::MaxValue
    $max = [int]::MinValue
    foreach ($line in $lines) {
        if ($line.Length -lt 11 -or $line[0] -ne ':') { continue }
        $len = [Convert]::ToInt32($line.Substring(1, 2), 16)
        $addr = [Convert]::ToInt32($line.Substring(3, 4), 16)
        $type = [Convert]::ToInt32($line.Substring(7, 2), 16)
        if ($type -ne 0) { continue }
        for ($i = 0; $i -lt $len; $i++) {
            $a = $addr + $i
            $b = [Convert]::ToByte($line.Substring(9 + $i * 2, 2), 16)
            $map[$a] = $b
            if ($a -lt $min) { $min = $a }
            if ($a -gt $max) { $max = $a }
        }
    }
    $stampAt = -1
    for ($a = $min; $a -le $max - 7; $a++) {
        $ok = $true
        for ($k = 0; $k -lt 5; $k++) {
            if (-not $map.ContainsKey($a + $k) -or $map[$a + $k] -ne $marker[$k]) { $ok = $false; break }
        }
        if ($ok) { $stampAt = $a + 5; break }
    }
    if ($stampAt -lt 0) { throw "FTAP stamp marker not found in hex" }

    $crc = [uint32]0xFFFFFFFF
    for ($a = $min; $a -le $max; $a++) {
        $v = if ($a -ge $stampAt -and $a -le ($stampAt + 2)) { [byte]0 }
             elseif ($map.ContainsKey($a)) { $map[$a] }
             else { [byte]0xFF }
        $crc = $crc -bxor $v
        for ($b = 0; $b -lt 8; $b++) {
            if (($crc -band 1) -ne 0) { $crc = ($crc -shr 1) -bxor 0xEDB88320 }
            else { $crc = $crc -shr 1 }
        }
    }
    $crc = $crc -bxor 0xFFFFFFFF
    $s0 = [byte]($crc -band 0xFF)
    $s1 = [byte](($crc -shr 8) -band 0xFF)
    $s2 = [byte](($crc -shr 16) -band 0xFF)
    if ($s0 -eq 0 -and $s1 -eq 0 -and $s2 -eq 0) { $s0 = 1 }
    $map[$stampAt] = $s0
    $map[$stampAt + 1] = $s1
    $map[$stampAt + 2] = $s2

    $out = New-Object System.Collections.Generic.List[string]
    foreach ($line in $lines) {
        if ($line.Length -lt 11 -or $line[0] -ne ':') { $out.Add($line); continue }
        $len = [Convert]::ToInt32($line.Substring(1, 2), 16)
        $addr = [Convert]::ToInt32($line.Substring(3, 4), 16)
        $type = [Convert]::ToInt32($line.Substring(7, 2), 16)
        if ($type -ne 0) { $out.Add($line); continue }
        $touch = $false
        for ($i = 0; $i -lt $len; $i++) {
            $a = $addr + $i
            if ($a -ge $stampAt -and $a -le ($stampAt + 2)) { $touch = $true; break }
        }
        if (-not $touch) { $out.Add($line); continue }
        $sum = $len + (($addr -shr 8) -band 0xFF) + ($addr -band 0xFF) + $type
        $data = ''
        for ($i = 0; $i -lt $len; $i++) {
            $a = $addr + $i
            $b = $map[$a]
            $data += ('{0:X2}' -f $b)
            $sum += $b
        }
        $cs = (256 - ($sum -band 0xFF)) -band 0xFF
        $out.Add((':{0:X2}{1:X4}{2:X2}{3}{4:X2}' -f $len, $addr, $type, $data, $cs))
    }
    $utf8 = New-Object System.Text.UTF8Encoding $false
    [System.IO.File]::WriteAllLines($HexPath, $out, $utf8)
    Write-Host ("stamp {0:X2}{1:X2}{2:X2}" -f $s0, $s1, $s2)
}

if ($Firmware) {
    $cli = Join-Path $env:LOCALAPPDATA 'arduino-cli\arduino-cli.exe'
    if (-not (Test-Path $cli)) { throw "arduino-cli not found: $cli" }
    $build = Join-Path $env:TEMP 'ftapper-build'
    New-Item -ItemType Directory -Force -Path $build | Out-Null
    $fqbn = 'digistump:avr:digispark-tiny:clock=clock165'
    & $cli compile --fqbn $fqbn --libraries (Join-Path $root 'libraries') --output-dir $build $root
    if ($LASTEXITCODE -ne 0) { throw "arduino-cli compile failed" }
    $hex = Join-Path $build 'F-Tapper.ino.hex'
    if (-not (Test-Path $hex)) { throw "hex not produced" }
    Copy-Item -Force $hex (Join-Path $root 'flash\F-Tapper.ino.hex')
    Patch-HexStamp (Join-Path $root 'flash\F-Tapper.ino.hex')
    Write-Host 'firmware ok'
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "csc not found: $csc" }

$args = @(
    '/nologo', '/target:winexe',
    '/win32icon:F-Tapper.ico',
    '/win32manifest:F-Tapper.exe.manifest',
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll',
    '/out:F-Tapper.exe',
    '/resource:flash\micronucleus.exe,micronucleus.exe',
    '/resource:flash\libusb0.dll,libusb0.dll',
    '/resource:flash\libusbK.dll,libusbK.dll',
    '/resource:flash\F-Tapper.ino.hex,F-Tapper.ino.hex',
    '/resource:flash\drivers\Digispark_Bootloader.inf,Digispark_Bootloader.inf',
    '/resource:flash\drivers\Digispark_Bootloader.cat,Digispark_Bootloader.cat',
    '/resource:flash\drivers\amd64\libusb0.sys,drv_amd64_libusb0.sys',
    '/resource:flash\drivers\amd64\libusb0.dll,drv_amd64_libusb0.dll',
    '/resource:flash\drivers\amd64\libusbK.dll,drv_amd64_libusbK.dll',
    '/resource:flash\drivers\x86\libusb0.sys,drv_x86_libusb0.sys',
    '/resource:flash\drivers\x86\libusb0_x86.dll,drv_x86_libusb0.dll',
    '/resource:flash\drivers\x86\libusbK_x86.dll,drv_x86_libusbK.dll',
    'F-Tapper-app.cs'
)
& $csc @args
if ($LASTEXITCODE -ne 0) { throw "csc failed" }
Write-Host "built $(Join-Path $root 'F-Tapper.exe')"
