# ATK A9 Ultimate: pure packet/catalog tests; never enumerate or access hardware.
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
param([string]$AssemblyPath = (Join-Path (Split-Path -Parent $PSScriptRoot) "FerrisPulse.exe"))
$ErrorActionPreference = "Stop"
$assembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($AssemblyPath))
$type = $assembly.GetType("RazerBatteryTray.UniversalGamingDeviceHelper", $true)
function Assert($Condition, [string]$Message) { if (!$Condition) { throw $Message } }
function Invoke-Static([string]$Name, [object[]]$Arguments) { $type.GetMethod($Name).Invoke($null, $Arguments) }
function Parse-Report([byte[]]$Report) {
    $arguments = [object[]]@($Report, -1, $false, 0)
    $valid = $type.GetMethod("TryParseAtkBatteryReport").Invoke($null, $arguments)
    return @{ Valid = $valid; Percent = $arguments[1]; Charging = $arguments[2]; Millivolts = $arguments[3] }
}
function Set-Checksum([byte[]]$Report) {
    $sum = 0
    for ($i = 0; $i -lt 16; $i++) { $sum += $Report[$i] }
    $Report[16] = [byte]((0x155 - ($sum -band 255)) -band 255)
}
$product = Invoke-Static "LookupProduct" @([int]0x373B, [int]0x11B6, "generic mouse")
Assert ($product.Brand -eq "ATK" -and $product.ModelName -eq "ATK A9 Ultimate") "Exact model mapping missing"
Assert (Invoke-Static "IsAtkA9Ultimate" @([int]0x373B, [int]0x11B6)) "Catalog id rejected"
Assert (Invoke-Static "IsAtkA9Ultimate" @([int]0x373B, [int]0x1118)) "Official runtime id rejected"
$officialProduct = Invoke-Static "LookupProduct" @([int]0x373B, [int]0x1118, "generic mouse")
Assert ($officialProduct.ModelName -eq "ATK A9 Ultimate") "Official runtime model mapping missing"
Assert (!(Invoke-Static "IsAtkA9Ultimate" @([int]0x373B, [int]0x101B))) "Shared 101B receiver must not identify a specific model"
Assert (!(Invoke-Static "IsAtkA9Ultimate" @([int]0x373B, [int]0x10C9))) "Shared receiver must not be assumed A9 Ultimate"
Assert (!(Invoke-Static "IsAtkA9Ultimate" @([int]0x3554, [int]0xF40E))) "Firmware alternate id lacks runtime protocol evidence"
Assert (Invoke-Static "IsAtkBatteryCollection" @([int]0xFF02, 2, 17, 17)) "Known collection rejected"
Assert (Invoke-Static "IsAtkBatteryCollection" @([int]0xFF04, 2, 17, 17)) "Official vendor collection rejected"
foreach ($case in @(@(0xFF04,1,17,17), @(1,2,17,17), @(0xFF02,1,17,17), @(0xFF02,2,16,17), @(0xFF02,2,17,64))) {
    Assert (!(Invoke-Static "IsAtkBatteryCollection" $case)) "Unverified collection/report length accepted"
}
[byte[]]$request = Invoke-Static "BuildAtkBatteryRequest" @()
Assert ($request.Length -eq 17 -and $request[0] -eq 8 -and $request[1] -eq 4 -and $request[16] -eq 0x49) "Battery request framing incorrect"
Assert (($request[2..15] | Measure-Object -Sum).Sum -eq 0) "Read query contains unexpected payload"
[byte[]]$report = @(8,4,0,0,0,4,73,1,0x10,0x04,0,0,0,0,0,0,0)
Set-Checksum $report
$result = Parse-Report $report
Assert ($result.Valid -and $result.Percent -eq 73 -and $result.Charging -and $result.Millivolts -eq 4100) "Battery fields decoded incorrectly"
foreach ($percent in @(0,100)) {
    $report[6] = $percent; $report[7] = 0; Set-Checksum $report
    $result = Parse-Report $report
    Assert ($result.Valid -and $result.Percent -eq $percent -and !$result.Charging) "Battery endpoint rejected"
}
$report[6] = 101; Set-Checksum $report
Assert (!(Parse-Report $report).Valid) "Invalid percentage accepted"
$report[6] = 50; Set-Checksum $report
$report[16] = $report[16] -bxor 1
Assert (!(Parse-Report $report).Valid) "Corrupt checksum accepted"
foreach ($field in @(0,1,5)) {
    [byte[]]$bad = $report.Clone(); $bad[$field] = 0; Set-Checksum $bad
    Assert (!(Parse-Report $bad).Valid) "Invalid report/header accepted"
}
foreach ($length in @(1,11)) {
    $report[5] = $length; Set-Checksum $report
    Assert (!(Parse-Report $report).Valid) "Invalid data length accepted"
}
Assert (!(Parse-Report ([byte[]]@(8,4))).Valid) "Truncated report accepted"
Assert (!(Parse-Report $null).Valid) "Null report accepted"
$report[5] = 2; Set-Checksum $report
$result = Parse-Report $report
Assert ($result.Valid -and $result.Millivolts -eq 0) "Two-byte battery reply rejected"
Write-Host "PASS: ATK A9 identity, collection, battery query, parsing, and malformed reports (no hardware access)."
