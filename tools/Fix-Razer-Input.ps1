<#
  Fix-Razer-Input.ps1  -  LiteSynapse input recovery

  Run this when your Razer keyboard/mouse stopped working after a reboot (the Razer
  filter-driver stack failed to load). It:
    1. Stops and disables the Razer background services and closes Razer Synapse.
    2. Backs up the Razer filter-driver packages, then makes a System Restore point.
    3. Removes ONLY Razer's filter-driver packages for the Naga (008D), BlackWidow Lite
       (0235) and the shared RzCommon device, so Windows' own drivers take over.
    4. Rescans, so the devices come back on the standard Windows drivers.

  It never touches Windows' own driver (mshidkmdf) or any non-Razer package. Everything it
  removes is backed up first, and a restore point is made, so it is fully reversible.

  Easiest way to run it: double-click Fix-Razer-Input.bat (it asks for administrator rights
  and runs this). Then REBOOT. Afterwards your Naga and BlackWidow work as a normal mouse and
  keyboard. Install LiteSynapse (or Razer Synapse) again only when you want - run ONE of them,
  never both.

  Undo (if you ever want the Razer drivers back):
      pnputil /add-driver "C:\Users\Public\RazerDriverBackup-<date>\*.inf" /subdirs /install
  then reboot.
#>
[CmdletBinding()]
param([string]$BackupRoot = $env:PUBLIC)

$ErrorActionPreference = 'Continue'

function Say($t, $c = 'Gray') { Write-Host $t -ForegroundColor $c }
function Head($t) { Write-Host ''; Write-Host $t -ForegroundColor Cyan }

# Must be elevated (the .bat does this; this is a backstop).
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Say 'Run this as administrator (double-click Fix-Razer-Input.bat).' Red
    Read-Host 'Press Enter to close'
    exit 5
}

Write-Host 'LiteSynapse - Razer input recovery' -ForegroundColor Green
Write-Host 'This fixes a keyboard/mouse that stopped working after re-enabling Razer and rebooting.'

# ---- 1. Stop Razer software and services -------------------------------------------------------------
Head 'Closing Razer Synapse and stopping its services...'
foreach ($p in 'RazerAppEngine', 'RzEngineMon', 'Razer Synapse 3', 'Razer Synapse', 'RzSynapse', 'LiteSynapse') {
    Get-Process -Name $p -ErrorAction SilentlyContinue | ForEach-Object {
        try { $_.Kill(); Say "  closed $($_.ProcessName)" } catch {}
    }
}
# Only Razer's own services: name starts Rz / Razer, or the display name says Razer. Nothing else.
$svcs = @(Get-Service -ErrorAction SilentlyContinue | Where-Object {
        ($_.Name -match '^(Rz|Razer)' -or $_.DisplayName -match 'Razer') -and $_.Name -notmatch 'Audiosrv|AudioEndpointBuilder'
    })
foreach ($s in $svcs) {
    try { Stop-Service -Name $s.Name -Force -ErrorAction SilentlyContinue } catch {}
    & sc.exe config $s.Name start= disabled | Out-Null
    Say "  disabled service: $($s.Name)  ($($s.DisplayName))"
}
if (-not $svcs) { Say '  no Razer services found (fine).' }

# ---- 2. Find Razer filter-driver packages ------------------------------------------------------------
Head 'Looking for Razer filter-driver packages (a few seconds)...'
try { $all = @(Get-WindowsDriver -Online -ErrorAction Stop) }
catch { Say "Could not read the driver store: $($_.Exception.Message)" Red; Read-Host 'Press Enter to close'; exit 4 }

function InfName($p) { [IO.Path]::GetFileName([string]$p.OriginalFileName).ToLowerInvariant() }

# Razer's filter drivers only: provider Razer, original name rzdev*/rzcommon*. mshidkmdf can never match.
$pkgs = @($all | Where-Object {
        ([string]$_.ProviderName -match 'Razer') -and
        ((InfName $_) -match '^(rzdevu|rzcommonu)') -and
        ((InfName $_) -notmatch 'mshidkmdf')
    })

if ($pkgs.Count -eq 0) {
    Say 'No Razer filter-driver packages are installed. Nothing to remove.' Green
    Say 'If your Razer devices still do not work, reboot once, or try a different USB port.'
    Read-Host 'Press Enter to close'
    exit 0
}
Say ('Found ' + $pkgs.Count + ' Razer package(s):') Yellow
$pkgs | ForEach-Object { Say ('  {0}  {1}  {2}' -f $_.Driver, (InfName $_), $_.ProviderName) }

# ---- 3. Back up, then restore point ------------------------------------------------------------------
$backup = Join-Path $BackupRoot ('RazerDriverBackup-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
Head "Backing up the packages to $backup ..."
New-Item -ItemType Directory -Path $backup -Force | Out-Null
foreach ($p in $pkgs) {
    & pnputil.exe /export-driver $p.Driver $backup 2>&1 | Out-Null
    $name = InfName $p
    $copy = Get-ChildItem -Path $backup -Recurse -Filter '*.inf' -ErrorAction SilentlyContinue |
        Where-Object { $_.Name.ToLowerInvariant() -eq $name }
    if (-not $copy) { Say "Backup of $name failed. Nothing was removed." Red; Read-Host 'Press Enter to close'; exit 3 }
}
Say "Backup complete: $backup" Green

Head 'Making a System Restore point...'
try {
    Enable-ComputerRestore -Drive "$env:SystemDrive\" -ErrorAction SilentlyContinue
    Checkpoint-Computer -Description 'Before Razer input recovery (LiteSynapse)' -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop
    Say 'Restore point created.' Green
} catch { Say "No restore point ($($_.Exception.Message)); the backup above is enough to undo." Yellow }

# ---- 4. Remove the Razer packages --------------------------------------------------------------------
# Order: RzCommon first, then the virtual consumer device, then the per-device upper/lower filters.
function Rank($n) {
    if ($n -match '^rzcommonu') { return 0 }
    if ($n -like '*_vcon.inf') { return 1 }
    if ($n -like '*_mou.inf' -or $n -like '*_kbd.inf') { return 2 }
    return 3
}
Head 'Removing the Razer filter drivers (your devices switch to Windows'' own drivers)...'
$failed = 0
foreach ($p in ($pkgs | Sort-Object { Rank (InfName $_) })) {
    $name = InfName $p
    if ($name -match 'mshidkmdf') { continue }   # never
    Say "  removing $($p.Driver)  ($name)"
    & pnputil.exe /delete-driver $p.Driver /uninstall /force 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 3010) { $failed++; Say "    (exit code $LASTEXITCODE - a reboot should finish it)" Yellow }
}
& pnputil.exe /scan-devices 2>&1 | Out-Null

Head 'Done.'
if ($failed -gt 0) {
    Say "$failed package(s) need a reboot to finish removing." Yellow
} else {
    Say 'All Razer filter drivers removed.' Green
}
Write-Host ''
Say 'NEXT:' Cyan
Say '  1. REBOOT the PC now.'
Say '  2. Plug your Razer Naga and BlackWidow back in. They work as a normal mouse/keyboard.'
Say '  3. Keep Razer Synapse uninstalled (Settings > Apps) so this cannot happen again.'
Say "To undo: pnputil /add-driver `"$backup\*.inf`" /subdirs /install   then reboot." DarkGray
Read-Host 'Press Enter to close'
