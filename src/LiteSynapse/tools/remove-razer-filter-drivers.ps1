#Requires -RunAsAdministrator
<#
  LiteSynapse: Razer filter-driver helper.

  Lists (default) or removes (-Remove) the Razer kernel filter-driver packages of the
  Razer Naga Left-Handed Edition (PID 008D), the BlackWidow Lite (PID 0235) and the shared
  RzCommon "Razer Control Device" driver. LiteSynapse needs none of them: it talks to the
  devices through the standard Windows drivers. Nothing else is touched; in particular the
  Microsoft driver mshidkmdf is never changed.

  List only (safe, changes nothing):
      powershell -ExecutionPolicy Bypass -File .\remove-razer-filter-drivers.ps1
  Remove (exports a backup first and asks before deleting):
      powershell -ExecutionPolicy Bypass -File .\remove-razer-filter-drivers.ps1 -Remove

  Before -Remove:
    1. Uninstall Razer Synapse and Razer App Engine (Settings > Apps). While they are
       installed they put these drivers back; this script refuses to run.
    2. Quit LiteSynapse (tray icon > Exit). It holds the devices open.
    3. Keep a second keyboard or mouse nearby: each device drops out for a moment.

  Undo:
      pnputil /add-driver "<backup folder>\*.inf" /subdirs /install
  then unplug and replug the devices (or reboot).
#>
[CmdletBinding()]
param(
    [switch]$Remove,
    [string]$BackupRoot = $env:PUBLIC
)

# Not 'Stop': Windows PowerShell 5.1 turns a native program's stderr into terminating errors under 'Stop'.
$ErrorActionPreference = 'Continue'

# The only packages this script will ever list or delete (original INF names).
$targets = @(
    'rzdevu_008d_mou.inf', 'rzdevu_008d_kbd.inf', 'rzdevu_008d_dkm.inf', 'rzdevu_008d_mpos.inf', 'rzdevu_008d_vcon.inf',
    'rzdevu_0235_mou.inf', 'rzdevu_0235_kbd.inf', 'rzdevu_0235_dkm.inf', 'rzdevu_0235_vcon.inf',
    'rzcommonu.inf')

function Get-InfName($p) { [IO.Path]::GetFileName([string]$p.OriginalFileName).ToLowerInvariant() }

function Write-Step($text) { Write-Host ''; Write-Host $text -ForegroundColor Cyan }

# Razer Synapse / Razer App Engine still installed (uninstall entries) or running.
function Get-SynapseLeftovers {
    $found = @()
    $keys = @(
        'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*',
        'HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*')
    foreach ($k in $keys) {
        $found += @(Get-ItemProperty -Path $k -ErrorAction SilentlyContinue |
                Where-Object { $_.DisplayName -match 'Razer Synapse|RazerAppEngine|Razer App Engine' } |
                ForEach-Object { "installed: $($_.DisplayName)" })
    }
    $found += @(Get-Process -Name 'RazerAppEngine', 'Razer Synapse', 'Razer Synapse 3', 'RzSynapse' -ErrorAction SilentlyContinue |
            ForEach-Object { "running: $($_.ProcessName)" })
    return @($found | Select-Object -Unique)
}

# Razer filters attached to the devices right now (for information).
function Show-AttachedFilters {
    $devices = @(Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -match '^(USB|HID)\\VID_1532&PID_(008D|0235)' })
    $rows = foreach ($d in $devices) {
        $upper = (Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName 'DEVPKEY_Device_UpperFilters' -ErrorAction SilentlyContinue).Data
        $lower = (Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName 'DEVPKEY_Device_LowerFilters' -ErrorAction SilentlyContinue).Data
        $razer = @($upper) + @($lower) | Where-Object { $_ -match '^Rz' }
        if ($razer) {
            [pscustomobject]@{ Device = $d.FriendlyName; Status = $d.Status; Filters = ($razer -join ', '); InstanceId = $d.InstanceId }
        }
    }
    if ($rows) {
        Write-Step 'Razer filters attached to your devices now:'
        $rows | Format-Table Device, Status, Filters, InstanceId -AutoSize | Out-Host
    } else {
        Write-Step 'No Razer filter is attached to the Naga or the BlackWidow Lite right now.'
    }
}

Write-Host 'LiteSynapse - Razer filter-driver helper' -ForegroundColor Green
Write-Host ('Mode: ' + $(if ($Remove) { 'REMOVE (backup first, asks before deleting)' } else { 'list only - nothing is changed' }))

Show-AttachedFilters

# Get-WindowsDriver property names are not localized (pnputil text output is).
Write-Step 'Looking for Razer driver packages in the driver store (this can take a few seconds)...'
try {
    $all = @(Get-WindowsDriver -Online -ErrorAction Stop)
} catch {
    Write-Host "Could not read the driver store: $($_.Exception.Message)" -ForegroundColor Red
    exit 5
}
$pkgs = @($all | Where-Object {
        ($targets -contains (Get-InfName $_)) -and ([string]$_.ProviderName -match 'Razer') -and ((Get-InfName $_) -notmatch 'mshidkmdf')
    })

if ($pkgs.Count -eq 0) {
    Write-Host 'No Razer 008D / 0235 / RzCommon driver packages are installed. Nothing to do.' -ForegroundColor Green
    return
}

$pkgs | Sort-Object { Get-InfName $_ } |
    Format-Table Driver, @{ n = 'OriginalName'; e = { Get-InfName $_ } }, ProviderName, Version, Date, ClassName -AutoSize | Out-Host

if (-not $Remove) {
    Write-Step 'Nothing was changed. To remove these packages:'
    Write-Host '  1. Uninstall Razer Synapse and Razer App Engine (Settings > Apps), if they are still installed.'
    Write-Host '  2. Quit LiteSynapse (tray icon > Exit).'
    Write-Host '  3. Keep a second keyboard or mouse nearby; each device drops out for a moment.'
    Write-Host '  4. In an administrator PowerShell, run:'
    Write-Host ("     powershell -ExecutionPolicy Bypass -File `"{0}`" -Remove" -f $PSCommandPath) -ForegroundColor Yellow
    Write-Host '     It backs the packages up first and asks before deleting anything.'
    $left = @(Get-SynapseLeftovers)
    if ($left.Count -gt 0) {
        Write-Host ''
        Write-Host 'Razer Synapse is still on this PC, so -Remove would refuse to run:' -ForegroundColor Yellow
        $left | ForEach-Object { Write-Host "  - $_" }
    }
    return
}

# ---- Removal -------------------------------------------------------------------------------------------

$left = @(Get-SynapseLeftovers)
if ($left.Count -gt 0) {
    Write-Host 'Refusing to remove: Razer Synapse is still on this PC and would install these drivers again.' -ForegroundColor Red
    $left | ForEach-Object { Write-Host "  - $_" }
    Write-Host 'Uninstall Razer Synapse and Razer App Engine first (Settings > Apps), then run this again.'
    exit 2
}
if (Get-Process -Name 'LiteSynapse' -ErrorAction SilentlyContinue) {
    Write-Host 'Refusing to remove: LiteSynapse is running and holds the devices open.' -ForegroundColor Red
    Write-Host 'Quit it (tray icon > Exit), run this again, then start LiteSynapse afterwards.'
    exit 3
}

$backup = Join-Path $BackupRoot ('RazerDriverBackup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
Write-Step "Backing up the packages to $backup ..."
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($p in $pkgs) {
    $out = & pnputil.exe /export-driver $p.Driver $backup 2>&1
    $code = $LASTEXITCODE
    $out | Out-Host
    $name = Get-InfName $p
    $copy = Get-ChildItem -Path $backup -Recurse -Filter '*.inf' -ErrorAction SilentlyContinue | Where-Object { $_.Name.ToLowerInvariant() -eq $name }
    if ($code -ne 0 -or -not $copy) {
        Write-Host "Backup of $($p.Driver) ($name) failed (exit code $code). Nothing was removed." -ForegroundColor Red
        exit 4
    }
}
Write-Host "Backup complete: $backup" -ForegroundColor Green

Write-Step 'Creating a system restore point...'
try {
    Checkpoint-Computer -Description 'Before removing Razer filter drivers (LiteSynapse)' -RestorePointType 'MODIFY_SETTINGS'
    Write-Host 'Restore point created.' -ForegroundColor Green
} catch {
    Write-Host "No restore point: $($_.Exception.Message)" -ForegroundColor Yellow
    Write-Host 'The driver backup above is enough to undo the removal.'
}

Write-Step 'These packages will be deleted (the devices switch to the standard Windows drivers):'
$pkgs | ForEach-Object { Write-Host ("  {0}  {1}" -f $_.Driver, (Get-InfName $_)) }
$answer = Read-Host 'Type YES to delete them'
if ($answer -cne 'YES') {
    Write-Host 'Cancelled. Nothing was removed (the backup stays in place).'
    exit 1
}

# Order: RzCommon -> virtual consumer device -> upper filters -> lower filters.
function Get-Rank($n) {
    if ($n -eq 'rzcommonu.inf') { return 0 }
    if ($n -like '*_vcon.inf') { return 1 }
    if ($n -like '*_mou.inf' -or $n -like '*_kbd.inf') { return 2 }
    return 3
}
$failed = 0
foreach ($p in ($pkgs | Sort-Object { Get-Rank (Get-InfName $_) })) {
    $name = Get-InfName $p
    if ($targets -notcontains $name -or $name -match 'mshidkmdf') { continue }
    Write-Host "Removing $($p.Driver) ($name)"
    & pnputil.exe /delete-driver $p.Driver /uninstall 2>&1 | Out-Host
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010) {
        Write-Host "  pnputil exit code $LASTEXITCODE" -ForegroundColor Yellow
        $failed++
    }
}

# Re-enumerate so the inbox drivers (msmouse / keyboard / input.inf) bind again.
& pnputil.exe /scan-devices 2>&1 | Out-Host

Show-AttachedFilters
Write-Host ''
if ($failed -gt 0) {
    Write-Host "$failed package(s) could not be removed; a reboot may finish the job. Run this script again afterwards to check." -ForegroundColor Yellow
} else {
    Write-Host 'Done.' -ForegroundColor Green
}
Write-Host 'If a device does not come back, unplug and replug it, or reboot. Then start LiteSynapse again.'
Write-Host 'To undo:'
Write-Host ("  pnputil /add-driver `"{0}\*.inf`" /subdirs /install" -f $backup) -ForegroundColor Yellow
