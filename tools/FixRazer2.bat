@echo off
setlocal
set "FIXRAZER_SELF=%~f0"
title Razer Input Fix 2
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath $env:FIXRAZER_SELF -Verb RunAs"
  exit /b
)
echo ============================================================
echo   Razer input fix 2
echo   Shows what Windows sees, clears what is left of the Razer
echo   drivers, and sets the mouse and keyboard up again on
echo   Windows' own drivers. Takes about a minute.
echo ============================================================
echo.
echo   Keep BOTH Razer devices plugged in. Please wait...
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

# Device Manager's problem codes, in words.
$codes = @{ 0 = 'OK'; 1 = 'not configured'; 3 = 'driver corrupt'; 10 = 'cannot start'; 14 = 'needs restart'; 18 = 'reinstall driver';
    19 = 'registry damaged'; 22 = 'disabled'; 24 = 'not present'; 28 = 'no driver'; 31 = 'cannot load driver'; 32 = 'driver service disabled';
    37 = 'driver failed to start'; 38 = 'old driver still loaded'; 39 = 'driver missing or corrupt'; 43 = 'device reported failure';
    45 = 'not connected'; 47 = 'needs restart'; 48 = 'driver blocked'; 52 = 'driver not signed' }

# Every device entry that is Razer's: the Naga and BlackWidow by USB id (1532), anything named Razer, and
# anything bound to a Razer driver. Includes entries for devices that are not plugged in right now.
function RazerDevices {
    @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
            $_.InstanceId -match 'VID_1532' -or $_.FriendlyName -match 'Razer' -or ([string]$_.Manufacturer) -match 'Razer' -or ([string]$_.Service) -match '^rz'
        })
}

function ShowDevices($title) {
    Head $title
    $devs = RazerDevices
    if ($devs.Count -eq 0) { Say '  Windows sees no Razer device at all. Are they plugged in?' Yellow; return }
    foreach ($d in ($devs | Sort-Object InstanceId)) {
        $code = [int]$d.ConfigManagerErrorCode
        $what = if ($codes.ContainsKey($code)) { $codes[$code] } else { 'code ' + $code }
        if (-not $d.Present) { $what = 'not plugged in'; $color = 'DarkGray' }
        elseif ($code -eq 0) { $color = 'Green' } else { $color = 'Red' }
        Say ('  [' + $what + ']  ' + $d.FriendlyName + '   driver=' + $d.Service + '   ' + $d.InstanceId) $color
    }
}

ShowDevices 'BEFORE - what Windows sees right now:'

Head 'Razer driver entries in the services list (information only):'
$any = $false
foreach ($k in (Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Services' -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^rz' })) {
    $v = Get-ItemProperty -LiteralPath $k.PSPath -ErrorAction SilentlyContinue
    $img = [string]$v.ImagePath
    $file = $img -replace '^\\\?\?\\', '' -replace '^\\SystemRoot\\', ($env:SystemRoot + '\') -replace '^System32\\', ($env:SystemRoot + '\System32\')
    $exists = if ($file -and (Test-Path -LiteralPath $file)) { 'file present' } else { 'FILE MISSING' }
    Say ('  ' + $k.PSChildName + '  start=' + $v.Start + '  ' + $img + '  ' + $exists)
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
    try { Stop-Service -Name $s.Name -Force -ErrorAction SilentlyContinue } catch {}
    & sc.exe config $s.Name start= disabled | Out-Null
    Say ('  service off: ' + $s.Name)
}
Get-ScheduledTask -ErrorAction SilentlyContinue | Where-Object { $_.TaskName -match 'Razer' -or $_.TaskPath -match 'Razer' } | ForEach-Object {
    Disable-ScheduledTask -InputObject $_ -ErrorAction SilentlyContinue | Out-Null
    Say ('  task off: ' + $_.TaskPath + $_.TaskName)
}
$skip = @('PSPath', 'PSParentPath', 'PSChildName', 'PSDrive', 'PSProvider')
foreach ($rk in 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run', 'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run', 'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run') {
    $props = Get-ItemProperty -LiteralPath $rk -ErrorAction SilentlyContinue
    if ($null -eq $props) { continue }
    foreach ($pp in $props.PSObject.Properties) {
        if ($skip -contains $pp.Name) { continue }
        if ($pp.Name -match 'Razer' -or ([string]$pp.Value) -match 'Razer') {
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
if (-not (Test-Path -LiteralPath $ds)) { New-Item -Path $ds -Force -ErrorAction SilentlyContinue | Out-Null }
Set-ItemProperty -LiteralPath $ds -Name 'SearchOrderConfig' -Value 0 -Type DWord -ErrorAction SilentlyContinue
Say '  done. (This is the "Device Installation Settings" switch in Windows; it can be turned back on there any time.)' DarkGray

# ---- 3. Razer driver packages ------------------------------------------------------------------------------
Head 'Checking the driver store for Razer driver packages...'
$pk = @()
try {
    $pk = @(Get-WindowsDriver -Online -ErrorAction Stop | Where-Object {
            ([string]$_.ProviderName) -match 'Razer' -and ([IO.Path]::GetFileName([string]$_.OriginalFileName)) -match '^rz'
        })
} catch { Say ('  could not read the driver store: ' + $_.Exception.Message) Red }
if ($pk.Count -eq 0) { Say '  none installed.' Green }
else {
    Say ('  ' + $pk.Count + ' Razer package(s) found. Removing them...') Yellow
    foreach ($p in $pk) {
        $n = [IO.Path]::GetFileName([string]$p.OriginalFileName)
        if ($n -match 'mshidkmdf') { continue }
        $null = & pnputil.exe /delete-driver $p.Driver /uninstall /force 2>&1
        Say ('  removed ' + $p.Driver + ' (' + $n + ')')
    }
}

# ---- 4. Leftover filter names ------------------------------------------------------------------------------
# A device whose UpperFilters/LowerFilters name a driver that is no longer there cannot start (code 39/31).
# Only names starting with "rz" (Razer's) are taken out; Windows' own (mouclass, kbdclass, ...) stay.
Head 'Clearing leftover Razer filter entries...'
function StripRazer($keyPath, $valueName) {
    $p = Get-ItemProperty -LiteralPath $keyPath -Name $valueName -ErrorAction SilentlyContinue
    if ($null -eq $p) { return $false }
    $vals = @($p.$valueName)
    $drop = @($vals | Where-Object { $_ -match '^rz' })
    if ($drop.Count -eq 0) { return $false }
    $keep = @($vals | Where-Object { $_ -and ($_ -notmatch '^rz') })
    if ($keep.Count -eq 0) { Remove-ItemProperty -LiteralPath $keyPath -Name $valueName -ErrorAction SilentlyContinue }
    else { Set-ItemProperty -LiteralPath $keyPath -Name $valueName -Value ([string[]]$keep) -Type MultiString -ErrorAction SilentlyContinue }
    Say ('  ' + ($keyPath -replace '^.*CurrentControlSet\\', '') + ' ' + $valueName + ': removed ' + ($drop -join ', '))
    return $true
}
$n = 0
foreach ($k in (Get-ChildItem 'HKLM:\SYSTEM\CurrentControlSet\Control\Class' -ErrorAction SilentlyContinue)) {
    foreach ($v in 'UpperFilters', 'LowerFilters') { if (StripRazer $k.PSPath $v) { $n++ } }
}
foreach ($d in (RazerDevices)) {
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Enum\' + $d.InstanceId
    foreach ($v in 'UpperFilters', 'LowerFilters') { if (StripRazer $key $v) { $n++ } }
}
if ($n -eq 0) { Say '  none found.' }

# ---- 5. Remove the device entries, so Windows sets them up again from scratch --------------------------------
Head 'Removing the Razer device entries so Windows sets them up again from scratch...'
$devs = @(RazerDevices | Sort-Object { $_.InstanceId.Length } -Descending)
foreach ($d in $devs) {
    $null = & pnputil.exe /remove-device $d.InstanceId /subtree 2>&1
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010) { $null = & pnputil.exe /remove-device $d.InstanceId 2>&1 }
    $r = if ($LASTEXITCODE -eq 0 -or $LASTEXITCODE -eq 3010) { 'removed' } else { '(already gone, or exit ' + $LASTEXITCODE + ')' }
    Say ('  ' + $d.InstanceId + '  ' + $r)
}
if ($devs.Count -eq 0) { Say '  nothing to remove.' }

# ---- 6. Set them up again ----------------------------------------------------------------------------------
Head 'Asking Windows to set the devices up again (a few seconds)...'
$null = & pnputil.exe /scan-devices 2>&1
Start-Sleep -Seconds 8
ShowDevices 'AFTER - what Windows sees now:'

$after = @(RazerDevices | Where-Object { $_.Present })
$bad = @($after | Where-Object { [int]$_.ConfigManagerErrorCode -ne 0 })
Write-Host ''
if ($after.Count -eq 0) {
    Say 'Windows does not see the Razer mouse or keyboard. Unplug both, wait 10 seconds, plug them into USB ports on the BACK of the PC.' Yellow
    Say 'Then run this file again.' Yellow
} elseif ($bad.Count -eq 0) {
    Say 'Your Razer mouse and keyboard are set up on Windows'' own drivers. Try them now.' Green
    Say 'If one still does nothing: unplug it, wait 10 seconds, plug it back in.' Green
} else {
    Say 'Some Razer entries still show a problem (the red lines above). Unplug both devices, wait 10 seconds, plug them back in.' Yellow
    Say 'Still broken? RESTART the PC, then send the log file FixRazer2-log.txt from your Desktop.' Yellow
}
if ($inst.Count -gt 0) {
    Write-Host ''
    Say ('Razer software is still installed: ' + ($inst -join ', ')) Yellow
    Say 'Uninstall it so this cannot happen again: Settings > Apps > Installed apps > Razer ... > Uninstall. Then restart.' Yellow
}
try { Stop-Transcript | Out-Null } catch {}
