# WAVE75 receiver packet and identity tests; no HID device is opened.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $root 'src\Hardware\Wave75DeviceHelper.cs'
$bytes = [System.IO.File]::ReadAllBytes($helper)
if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) { throw 'Wave75DeviceHelper.cs requires UTF-8 BOM' }
$csc = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (!(Test-Path $csc)) { $csc = 'C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe' }
$dll = Join-Path $env:TEMP ('FerrisPulse_Wave75_' + [guid]::NewGuid().ToString('N') + '.dll')
try {
    Push-Location $root
    & $csc /target:library /codepage:65001 /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:$dll /recurse:src\*.cs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Compilation failed' }
    $assemblyBytes = [System.IO.File]::ReadAllBytes($dll)
    $assembly = [Reflection.Assembly]::Load($assemblyBytes)
    $type = $assembly.GetType('RazerBatteryTray.Wave75DeviceHelper')
    $delegateName = 'Wave75TryParseReply_' + [guid]::NewGuid().ToString('N')
    $identityDelegateName = 'Wave75IsIdentity_' + [guid]::NewGuid().ToString('N')
    $delegateSource = 'public delegate bool ' + $delegateName + '(byte[] report, byte command, int offset, int length, out byte[] data); public delegate bool ' + $identityDelegateName + '(byte[] info);'
    $delegateTypes = Add-Type -TypeDefinition $delegateSource -PassThru
    $delegateType = $delegateTypes | Where-Object { $_.Name -eq $delegateName }
    $identityDelegateType = $delegateTypes | Where-Object { $_.Name -eq $identityDelegateName }
    $tryParseReply = $type.GetMethod('TryParseReply').CreateDelegate($delegateType)
    $isWave75Identity = $type.GetMethod('IsWave75Identity').CreateDelegate($identityDelegateType)
    $packet = $type.GetMethod('BuildRequest').Invoke($null, @([byte]0x30, 0, 24))
    if ($packet.Length -ne 33 -or $packet[0] -ne 0 -or $packet[1] -ne 0xAA -or $packet[2] -ne 0x30 -or $packet[5] -ne 24) { throw 'Request frame mismatch' }
    if ($null -ne $type.GetMethod('BuildRequest').Invoke($null, @([byte]0x30, 0, 25))) { throw 'Oversize chunk accepted' }
    $reply = New-Object byte[] 33
    $reply[1] = 0xAA; $reply[2] = 0x30; $reply[5] = 24; $reply[8] = 0x55; $reply[9] = 68; $reply[10] = 1
    [byte[]]$parsedData = $null
    if (!$tryParseReply.Invoke($reply, [byte]0x30, 0, 24, [ref]$parsedData) -or $parsedData[0] -ne 68 -or $parsedData[1] -ne 1) { throw 'Valid battery reply rejected' }
    $reply[8] = 0x0F
    [byte[]]$failureData = $null
    if ($tryParseReply.Invoke($reply, [byte]0x30, 0, 24, [ref]$failureData)) { throw 'Failure status accepted' }
    $identity = New-Object byte[] 56
    $identity[2] = 0xB0; $identity[3] = 0x36; $identity[4] = 0x09; $identity[5] = 0x30; $identity[8] = 2; $identity[11] = 103
    if (!$isWave75Identity.Invoke($identity)) { throw 'WAVE75 identity rejected' }
    $identity[11] = 0
    if ($isWave75Identity.Invoke($identity)) { throw 'Unknown downstream keyboard accepted' }
    Write-Host 'PASS: WAVE75 receiver frame and identity tests'
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $dll -ErrorAction SilentlyContinue
}
