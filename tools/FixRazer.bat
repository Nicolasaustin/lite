@echo off
title Razer Input Fix
net session >nul 2>&1
if %errorlevel% neq 0 (
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
  exit /b
)
echo ============================================================
echo   Razer input fix - this removes the broken Razer drivers
echo ============================================================
echo.
powershell -NoProfile -ExecutionPolicy Bypass -Command "Write-Host 'Stopping Razer services...' -ForegroundColor Cyan; Get-Process -Name RazerAppEngine,RzEngineMon,'Razer Synapse','Razer Synapse 3',RzSynapse -ErrorAction SilentlyContinue | ForEach-Object { try { $_.Kill() } catch {} }; Get-Service -ErrorAction SilentlyContinue | Where-Object { ($_.Name -match '^(Rz|Razer)' -or $_.DisplayName -match 'Razer') -and $_.Name -notmatch 'Audiosrv|AudioEndpointBuilder' } | ForEach-Object { try { Stop-Service $_.Name -Force -ErrorAction SilentlyContinue } catch {}; & sc.exe config $_.Name start= disabled | Out-Null; Write-Host ('  disabled ' + $_.Name) }; Write-Host 'Finding Razer filter drivers (please wait)...' -ForegroundColor Cyan; $d = @(Get-WindowsDriver -Online | Where-Object { $_.ProviderName -match 'Razer' -and ([IO.Path]::GetFileName([string]$_.OriginalFileName).ToLower()) -match '^(rzdevu|rzcommonu)' }); if ($d.Count -eq 0) { Write-Host 'No Razer filter drivers found - nothing to remove.' -ForegroundColor Yellow } else { foreach ($p in $d) { Write-Host ('  removing ' + [IO.Path]::GetFileName([string]$p.OriginalFileName)); & pnputil.exe /delete-driver $p.Driver /uninstall /force | Out-Null }; Write-Host 'Razer filter drivers removed.' -ForegroundColor Green }; & pnputil.exe /scan-devices | Out-Null"
echo.
echo ============================================================
echo   DONE.  Now do this:
echo     1. RESTART your PC.
echo     2. Plug your Razer mouse and keyboard back in.
echo     3. Leave Razer Synapse uninstalled so this cannot recur.
echo ============================================================
echo.
pause
