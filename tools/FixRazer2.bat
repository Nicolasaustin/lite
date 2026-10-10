@echo off
setlocal
set "FIXRAZER_SELF=%~f0"
title Razer Input Fix 2
if /i "%~1"=="elevated" goto :run
fltmc >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath $env:FIXRAZER_SELF -ArgumentList 'elevated' -Verb RunAs"
  exit /b
)
:run
echo ============================================================
echo   Razer input fix 2
echo   Shows what Windows sees, clears what is left of the Razer
echo   drivers, and sets the mouse and keyboard up again on
echo   Windows' own drivers.
echo.
echo   This can take 3-5 minutes. Do NOT close this window,
echo   even when nothing seems to happen for a while.
echo   Keep BOTH Razer devices plugged in.
echo ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "$t = [IO.File]::ReadAllText($env:FIXRAZER_SELF); $i = $t.IndexOf('#' + 'PS1BEGIN'); & ([ScriptBlock]::Create($t.Substring($i)))"
echo.
pause
exit /b

#PS1BEGIN
$ErrorActionPreference = 'Continue'
$ProgressPreference = 'SilentlyContinue'
function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Head($t) { Write-Host ''; Write-Host $t -ForegroundColor Cyan }

$log = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FixRazer2-log.txt'
try { Start-Transcript -Path $log | Out-Null } catch {}
Say ('Log file: ' + $log) DarkGray
Say ('Windows ' + [Environment]::OSVersion.Version + '   PowerShell ' + $PSVersionTable.PSVersion) DarkGray
$needReboot = $false

# Device Manager's problem codes, in words.
$codes = @{ 0 = 'OK'; 1 = 'not configured'; 3 = 'driver corrupt'; 10 = 'cannot start'; 14 = 'needs restart'; 18 = 'reinstall driver';
    19 = 'registry damaged'; 22 = 'disabled'; 24 = 'not present'; 28 = 'no driver'; 31 = 'cannot load driver'; 32 = 'driver service disabled';
    37 = 'driver failed to start'; 38 = 'old driver still loaded'; 39 = 'driver missing or corrupt'; 43 = 'device reported failure';
    45 = 'not connected'; 47 = 'needs restart'; 48 = 'driver blocked'; 52 = 'driver not signed' }

# One device property as text ('' when it is not set or cannot be read).
function Prop($id, $key) {
    try { [string]((Get-PnpDeviceProperty -InstanceId $id -KeyName $key -ErrorAction Stop).Data -join ', ') } catch { '' }
}

# Why a device is in the state it is: its driver, who made it, and the filters sitting on it.
function Detail($d) {
    $id = $d.InstanceId
    $st = Prop $id 'DEVPKEY_Device_ProblemStatus'
    try { if ($st -ne '') { $st = '0x{0:X8}' -f [uint32]([int64]$st -band 0xFFFFFFFF) } } catch {}
    Say ('        inf=' + (Prop $id 'DEVPKEY_Device_DriverInfPath') + '  provider=' + (Prop $id 'DEVPKEY_Device_DriverProvider') +
        '  upper=' + (Prop $id 'DEVPKEY_Device_UpperFilters') + '  lower=' + (Prop $id 'DEVPKEY_Device_LowerFilters') +
        $(if ($st -ne '') { '  status=' + $st } else { '' })) DarkGray
}

# Every device entry that is Razer's: the Naga and BlackWidow by USB id (1532), anything named Razer, and
# anything bound to a Razer driver. Includes entries for devices that are not plugged in right now.
function RazerDevices {
    @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
            $_.InstanceId -match 'VID_1532' -or $_.FriendlyName -match 'Razer' -or ([string]$_.Manufacturer) -match 'Razer' -or ([string]$_.Service) -match '^rz'
        })
}

# A USB device that could not even identify itself (Windows then shows it as VID_0000 / "Unknown USB Device").
function UsbFailures {
    @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object {
            $_.InstanceId -match '^USB\\VID_0000' -or $_.FriendlyName -match 'Unknown USB Device' -or [int]$_.ConfigManagerErrorCode -eq 43
        })
}

function ShowDevices($title, $detailAll) {
    Head $title
    Say '  (reading the device list, this can take a moment)' DarkGray
    $devs = RazerDevices
    if ($devs.Count -eq 0) { Say '  Windows sees no Razer device at all. Are they plugged in?' Yellow }
    foreach ($d in ($devs | Sort-Object InstanceId)) {
        $code = [int]$d.ConfigManagerErrorCode
        $what = if ($codes.ContainsKey($code)) { $codes[$code] } else { 'code ' + $code }
        if (-not $d.Present) { $what = 'not plugged in'; $color = 'DarkGray' }
        elseif ($code -ne 0) { $color = 'Red' }
        elseif (([string]$d.Service) -match '^rz') { $what = 'OK, on a Razer driver'; $color = 'Yellow' }
        else { $color = 'Green' }
        Say ('  [' + $what + ']  ' + $d.FriendlyName + '   driver=' + $d.Service + '   ' + $d.InstanceId) $color
        if ($d.Present -and ($code -ne 0 -or $detailAll)) { Detail $d }
    }
    foreach ($u in (UsbFailures)) {
        Say ('  [USB failure, code ' + $u.ConfigManagerErrorCode + ']  ' + $u.FriendlyName + '   ' + $u.InstanceId) Red
        Say '        A device on this USB port could not identify itself. That is the device, its cable or the port - not a driver. Try another port.' Red
    }
}

ShowDevices 'BEFORE - what Windows sees right now:' $true

Head 'Razer driver entries in the services list (information only):'
$any = $false
foreach ($k in (Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services' -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^rz' })) {
    $v = Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue
    $img = [string]$v.ImagePath
    $file = $img
    if ($file -match '^"([^"]+)"') { $file = $matches[1] } elseif ($file -match '^(\S+\.(sys|exe))(\s|$)') { $file = $matches[1] }
    $file = $file -replace '^\\\?\?\\', '' -replace '^\\SystemRoot\\', ($env:SystemRoot + '\') -replace '^System32\\', ($env:SystemRoot + '\System32\')
    $exists = if ($file -and (Test-Path -LiteralPath $file)) { 'file present' } else { 'FILE MISSING' }
    Say ('  ' + $k.PSChildName + '  type=' + $v.Type + '  start=' + $v.Start + '  ' + $img + '  ' + $exists)
    $any = $true
}
if (-not $any) { Say '  none.' }

# ---- 1. Razer software off, so nothing puts the drivers back while this runs -----------------------------
Head 'Closing Razer software and turning its services and auto-start off...'
foreach ($p in 'RazerAppEngine', 'RzEngineMon', 'Razer Synapse 3', 'Razer Synapse', 'RzSynapse', 'Razer Central', 'LiteSynapse') {
    Get-Process -Name $p -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill(); Say ('  closed ' + $_.ProcessName) } catch {} }
}
$svcs = @(Get-Service -ErrorAction SilentlyContinue | Where-Object {
        ($_.Name -match '^(Rz|Razer)' -or $_.DisplayName -match 'Razer') -and $_.Name -notmatch 'Audiosrv|AudioEndpointBuilder'
    })
foreach ($s in $svcs) {
    $null = & sc.exe config $s.Name start= disabled 2>&1
    try { Stop-Service -Name $s.Name -Force -NoWait -ErrorAction SilentlyContinue } catch {}
    Say ('  service off: ' + $s.Name + '  (' + $s.DisplayName + ')')
}
Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match 'Razer' -or $_.TaskPath -match 'Razer' } | ForEach-Object {
    Disable-ScheduledTask -InputObject $_ -ErrorAction SilentlyContinue | Out-Null
    Say ('  task off: ' + $_.TaskPath + $_.TaskName)
}
# Sign-in auto-start entries of Razer's programs (a Razer folder in the path, or Razer's program names). The
# user's own folder is taken off the path first, so an account name with "razer" in it cannot match.
$skip = @('PSPath', 'PSParentPath', 'PSChildName', 'PSDrive', 'PSProvider')
$prof = [regex]::Escape([string]$env:USERPROFILE)
foreach ($rk in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run') {
    $props = Get-ItemProperty -LiteralPath $rk -ErrorAction SilentlyContinue
    if ($null -eq $props) { continue }
    foreach ($pp in $props.PSObject.Properties) {
        if ($skip -contains $pp.Name) { continue }
        $val = [string]$pp.Value
        if ($prof -ne '') { $val = $val -replace $prof, '' }
        if ($pp.Name -match 'Razer' -or $val -match '\\Razer[^\\]*\\|RazerAppEngine|Razer Synapse|RzSynapse|Razer Central') {
            Remove-ItemProperty -LiteralPath $rk -Name $pp.Name -ErrorAction SilentlyContinue
            Say ('  auto-start off: ' + $pp.Name + '  (' + $pp.Value + ')')
        }
    }
}
$inst = @()
foreach ($uk in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*') {
    $inst += @(Get-ItemProperty -Path $uk -ErrorAction SilentlyContinue | Where-Object { $_.DisplayName -match 'Razer' } | ForEach-Object { $_.DisplayName })
}
$inst = @($inst | Select-Object -Unique)
if ($inst.Count -gt 0) { Say ('  Razer software still installed: ' + ($inst -join ', ')) Yellow }

# ---- 2. Windows must not download Razer's driver again when it sets the devices up --------------------------
Head 'Telling Windows not to download device drivers from Windows Update on its own...'
$ds = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\DriverSearching'
$old = (Get-ItemProperty -LiteralPath $ds -Name 'SearchOrderConfig' -ErrorAction SilentlyContinue).SearchOrderConfig
Say ('  was: ' + $(if ($null -eq $old) { '(not set = Yes, download)' } else { $old })) DarkGray
if (-not (Test-Path -LiteralPath $ds)) { New-Item -Path $ds -Force -ErrorAction SilentlyContinue | Out-Null }
Set-ItemProperty -LiteralPath $ds -Name 'SearchOrderConfig' -Value 0 -Type DWord -ErrorAction SilentlyContinue
Say '  now: 0 (No). This is the "Device Installation Settings" switch: Settings > System > About > Advanced system settings > Hardware.' DarkGray

# ---- 3. Razer driver packages ------------------------------------------------------------------------------
# The Razer packages in the driver store (provider Razer, file name rz*). Ok=false when the store could not be read.
function RazerPackages {
    try {
        $list = @(Get-WindowsDriver -Online -ErrorAction Stop)
        $found = @($list | Where-Object { ([string]$_.ProviderName) -match 'Razer' -and ([IO.Path]::GetFileName([string]$_.OriginalFileName)) -match '^rz' } |
                ForEach-Object { @{ Driver = [string]$_.Driver; Name = [IO.Path]::GetFileName([string]$_.OriginalFileName).ToLower() } })
        return @{ Ok = $true; Packages = $found }
    } catch { Say ('  (Get-WindowsDriver failed: ' + $_.Exception.Message.Trim() + ' - reading pnputil instead)') DarkGray }
    $text = @(& pnputil.exe /enum-drivers 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0) { return @{ Ok = $false; Packages = @() } }
    $found = @(); $cur = @{}
    foreach ($line in ($text + @(''))) {
        if ($line -match '^\s*$') {
            if ($cur.Driver -and ([string]$cur.Provider) -match 'Razer' -and ([string]$cur.Name) -match '^rz') { $found += @{ Driver = $cur.Driver; Name = $cur.Name } }
            $cur = @{}; continue
        }
        if ($line -match '^Published Name\s*:\s*(\S+)') { $cur.Driver = $matches[1] }
        elseif ($line -match '^Original Name\s*:\s*(\S+)') { $cur.Name = $matches[1].ToLower() }
        elseif ($line -match '^Provider Name\s*:\s*(.+)$') { $cur.Provider = $matches[1].Trim() }
    }
    return @{ Ok = $true; Packages = $found }
}
Head 'Checking the driver store for Razer driver packages (this can take a minute)...'
$store = RazerPackages
if (-not $store.Ok) { Say '  the driver store could not be read; the device entries are still cleared below.' Red }
elseif ($store.Packages.Count -eq 0) { Say '  none installed.' Green }
else {
    Say ('  ' + $store.Packages.Count + ' Razer package(s) found. Removing them...') Yellow
    foreach ($p in $store.Packages) {
        if ($p.Name -match 'mshidkmdf') { continue }
        $out = @(& pnputil.exe /delete-driver $p.Driver /uninstall /force 2>&1 | ForEach-Object { "$_" })
        if ($LASTEXITCODE -eq 0) { Say ('  removed ' + $p.Driver + ' (' + $p.Name + ')') }
        elseif ($LASTEXITCODE -eq 3010) { Say ('  removed ' + $p.Driver + ' (' + $p.Name + ') - finishes at restart') Yellow; $needReboot = $true }
        else { Say ('  FAILED (exit ' + $LASTEXITCODE + ') ' + $p.Driver + ' (' + $p.Name + '): ' + (($out | Where-Object { $_ -match '\S' -and $_ -notmatch 'PnP Utility' }) -join ' | ')) Red }
    }
    $again = RazerPackages
    if ($again.Ok -and $again.Packages.Count -gt 0) {
        Say ('  still in the driver store: ' + (($again.Packages | ForEach-Object { $_.Driver + ' (' + $_.Name + ')' }) -join ', ')) Red
        $needReboot = $true
    }
}

# ---- 4. Leftover filter names ------------------------------------------------------------------------------
# A device class whose UpperFilters/LowerFilters name a driver that is no longer there cannot start its devices.
# Only names starting with "rz" (Razer's) are taken out; Windows' own (mouclass, kbdclass, ...) stay. Each change
# is read back, so "removed" is only printed when it really is.
Head 'Clearing leftover Razer filter entries...'
function StripClass($keyPath, $valueName) {
    $p = Get-ItemProperty -LiteralPath $keyPath -Name $valueName -ErrorAction SilentlyContinue
    if ($null -eq $p) { return $false }
    $vals = @($p.$valueName)
    $drop = @($vals | Where-Object { $_ -match '^rz' })
    if ($drop.Count -eq 0) { return $false }
    $keep = @($vals | Where-Object { $_ -and ($_ -notmatch '^rz') })
    $cls = [string](Get-ItemProperty -LiteralPath $keyPath -Name 'Class' -ErrorAction SilentlyContinue).Class
    $label = ($keyPath -replace '^.*CurrentControlSet\\', '') + ' (' + $cls + ') ' + $valueName
    try {
        if ($keep.Count -eq 0) { Remove-ItemProperty -LiteralPath $keyPath -Name $valueName -ErrorAction Stop }
        else { Set-ItemProperty -LiteralPath $keyPath -Name $valueName -Value ([string[]]$keep) -Type MultiString -ErrorAction Stop }
    } catch { Say ('  ' + $label + ': could not change (' + $_.Exception.Message.Trim() + ')') Red; return $false }
    $now = @((Get-ItemProperty -LiteralPath $keyPath -Name $valueName -ErrorAction SilentlyContinue).$valueName | Where-Object { $_ -match '^rz' })
    if ($now.Count -gt 0) { Say ('  ' + $label + ': still lists ' + ($now -join ', ')) Red; return $false }
    Say ('  ' + $label + ': removed ' + ($drop -join ', '))
    return $true
}
$n = 0
foreach ($k in (Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Class' -ErrorAction SilentlyContinue)) {
    foreach ($v in 'UpperFilters', 'LowerFilters') { if (StripClass $k.PSPath $v) { $n++ } }
}
# Per-device filter entries live under Enum, which only Windows itself may write. They go away with the device
# entry in the next step; here they are only shown.
Say '  (reading the device list again)' DarkGray
$cached = @(RazerDevices)
foreach ($d in $cached) {
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $d.InstanceId
    foreach ($v in 'UpperFilters', 'LowerFilters') {
        $vals = @((Get-ItemProperty -LiteralPath $key -Name $v -ErrorAction SilentlyContinue).$v | Where-Object { $_ -match '^rz' })
        if ($vals.Count -gt 0) { Say ('  ' + $d.InstanceId + ' ' + $v + ' lists ' + ($vals -join ', ') + ' - goes away with the device entry below.') Yellow; $n++ }
    }
}
if ($n -eq 0) { Say '  none found.' }

# ---- 5. Remove the device entries, so Windows sets them up again from scratch --------------------------------
Head 'Removing the Razer device entries so Windows sets them up again from scratch...'
$spareKb = @(Get-PnpDevice -Class Keyboard -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -notmatch 'VID_1532' -and $_.FriendlyName -notmatch 'Razer' -and [int]$_.ConfigManagerErrorCode -eq 0 })
$spareMs = @(Get-PnpDevice -Class Mouse -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -notmatch 'VID_1532' -and $_.FriendlyName -notmatch 'Razer' -and [int]$_.ConfigManagerErrorCode -eq 0 })
if ($spareKb.Count -eq 0 -or $spareMs.Count -eq 0) { Say '  No working non-Razer keyboard/mouse is connected. The Razer devices drop out for a moment now; if they do not come back, plug in a spare.' Yellow }
else { Say ('  spare keyboard and mouse found: ' + $spareKb[0].FriendlyName + ', ' + $spareMs[0].FriendlyName) DarkGray }
$removed = 0
foreach ($d in ($cached | Sort-Object { $_.InstanceId.Length } -Descending)) {
    $out = @(& pnputil.exe /remove-device $d.InstanceId /subtree 2>&1 | ForEach-Object { "$_" })
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010) { $out = @(& pnputil.exe /remove-device $d.InstanceId 2>&1 | ForEach-Object { "$_" }) }
    if ($LASTEXITCODE -eq 0) { Say ('  removed ' + $d.InstanceId); $removed++ }
    elseif ($LASTEXITCODE -eq 3010) { Say ('  removed at restart: ' + $d.InstanceId) Yellow; $needReboot = $true }
    else { Say ('  ' + $d.InstanceId + ': already gone, or: ' + (($out | Where-Object { $_ -match '\S' -and $_ -notmatch 'PnP Utility' }) -join ' | ')) DarkGray }
}
if ($cached.Count -eq 0) { Say '  nothing to remove.' }

# ---- 6. Set them up again ----------------------------------------------------------------------------------
Head 'Asking Windows to set the devices up again...'
$null = & pnputil.exe /scan-devices 2>&1
Write-Host '  waiting for Windows to finish (up to 90 seconds) ' -NoNewline
$deadline = (Get-Date).AddSeconds(90)
do {
    Start-Sleep -Seconds 3
    Write-Host '.' -NoNewline
    $now = @(RazerDevices | Where-Object { $_.Present -and $_.InstanceId -match 'VID_1532' })
    $busy = @($now | Where-Object { ([int]$_.ConfigManagerErrorCode) -in 1, 28 })
    $kb = @($now | Where-Object { ([string]$_.Service) -eq 'kbdhid' }).Count
    $ms = @($now | Where-Object { ([string]$_.Service) -eq 'mouhid' }).Count
    $ready = ($now.Count -gt 0 -and $busy.Count -eq 0 -and $kb -gt 0 -and $ms -gt 0)
} while (-not $ready -and (Get-Date) -lt $deadline)
Write-Host ''
ShowDevices 'AFTER - what Windows sees now:' $true

$after = @(RazerDevices | Where-Object { $_.Present -and $_.InstanceId -match 'VID_1532' })
$bad = @($after | Where-Object { [int]$_.ConfigManagerErrorCode -ne 0 })
$onRazer = @($after | Where-Object { ([string]$_.Service) -match '^rz' })
$usb = @(UsbFailures)
Write-Host ''
if ($needReboot) {
    Say 'Part of the clean-up only finishes at restart. RESTART the PC now. Then unplug both Razer devices, wait 10 seconds, plug them back in.' Yellow
} elseif ($after.Count -eq 0) {
    Say 'Windows does not see the Razer mouse or keyboard. Unplug both, wait 10 seconds, plug them into USB ports on the BACK of the PC.' Yellow
    Say 'Then run this file again.' Yellow
} elseif ($bad.Count -eq 0 -and $onRazer.Count -eq 0) {
    Say 'Your Razer mouse and keyboard are set up on Windows'' own drivers.' Green
    Say 'Now unplug both, wait 10 seconds, plug them back in - they stay in Razer''s "driver mode" (dark, no input) until they lose power. Then try them.' Green
} elseif ($onRazer.Count -gt 0) {
    Say 'A Razer driver is still attached to a device (the yellow lines above). RESTART the PC, then run this file again.' Yellow
} else {
    Say 'Some entries still show a problem (the red lines above). Unplug both devices, wait 10 seconds, plug them back in.' Yellow
    Say 'Still broken? RESTART the PC, run this file once more, then send the log file FixRazer2-log.txt from your Desktop.' Yellow
}
if ($usb.Count -gt 0) {
    Say 'A USB device on this PC could not identify itself (the USB failure lines). If that is a Razer device, it is the device, its cable or the port - not a driver. Try another port on the back of the PC.' Yellow
}
if ($inst.Count -gt 0) {
    Write-Host ''
    Say ('Razer software is still installed: ' + ($inst -join ', ')) Yellow
    Say 'Uninstall it (Settings > Apps > Installed apps > Razer ... > Uninstall) and restart. While it is installed it puts the Razer driver back, and on this PC that driver is what stops the devices.' Yellow
}
try { Stop-Transcript | Out-Null } catch {}
