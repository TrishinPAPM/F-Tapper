# Compile and flash F-Tapper to Digispark ATtiny85.
# USB power is cycled automatically; do not unplug the board.

$ErrorActionPreference = 'Stop'
$cli = Join-Path $env:LOCALAPPDATA 'arduino-cli\arduino-cli.exe'
$mn = Join-Path $env:LOCALAPPDATA 'Arduino15\packages\digistump\tools\micronucleus\2.6\micronucleus.exe'
$fqbn = 'digistump:avr:digispark-tiny:clock=clock165'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $env:TEMP 'ftapper-build'

if (-not (Test-Path $cli)) { Write-Error "arduino-cli not found: $cli" }
if (-not (Test-Path $mn)) { Write-Error "micronucleus not found: $mn" }

function Cycle-DigisparkUsb {
  $line = pnputil /enum-devices /connected | Select-String -Pattern 'USB\\VID_16[CD]0&PID_' | Select-Object -First 1
  $hub = 'USB\ROOT_HUB30\4&15B4B4FE&0&0'
  if ($line -and $line.Line -match '(USB\\VID_16[CD]0&PID_[0-9A-Fa-f]+\\[^\s]+)') {
    Write-Host "Found $($Matches[1]), power-cycling hub $hub"
  } else {
    Write-Host "Digispark not listed, still power-cycling hub $hub"
  }
  pnputil /disable-device "$hub"
  Start-Sleep -Seconds 2
  pnputil /enable-device "$hub"
  Start-Sleep -Seconds 1
  return $true
}

Set-Location $root
New-Item -ItemType Directory -Force -Path $build | Out-Null
& $cli compile --fqbn $fqbn --libraries "$root\libraries" --output-dir $build $root
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$hex = Join-Path $build 'F-Tapper.ino.hex'
if (-not (Test-Path $hex)) { Write-Error "hex missing: $hex" }

Write-Host ""
Write-Host "Waiting for bootloader, then cycling USB power..."
Write-Host ""

$outLog = Join-Path $env:TEMP 'micronucleus-out.log'
$errLog = Join-Path $env:TEMP 'micronucleus-err.log'
Remove-Item $outLog, $errLog -ErrorAction SilentlyContinue
$p = Start-Process -FilePath $mn -ArgumentList @('--no-ansi','--run','--timeout','90', $hex) -PassThru -WindowStyle Hidden -RedirectStandardOutput $outLog -RedirectStandardError $errLog

$ready = $false
for ($i = 0; $i -lt 20; $i++) {
  Start-Sleep -Milliseconds 250
  if (Test-Path $outLog) {
    $t = Get-Content $outLog -Raw -ErrorAction SilentlyContinue
    if ($t -match 'plug in|Please plug') { $ready = $true; break }
  }
  if ($p.HasExited) { break }
}
if (-not $ready) { Start-Sleep -Seconds 1 }

Cycle-DigisparkUsb | Out-Null

Wait-Process -Id $p.Id -Timeout 95 -ErrorAction SilentlyContinue
$stdout = if (Test-Path $outLog) { Get-Content $outLog -Raw } else { '' }
$stderr = if (Test-Path $errLog) { Get-Content $errLog -Raw } else { '' }
Write-Host $stdout
if ($stderr) { Write-Host $stderr }
if (-not $p.HasExited) { Stop-Process -Id $p.Id -Force; exit 1 }
exit $p.ExitCode
