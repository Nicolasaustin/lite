using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LiteSynapse;

// Razer software left on the PC that costs performance: the Game Manager service (GMS3), the RzDev kernel filter
// drivers on the Naga and the BlackWidow Lite, the RzCommon driver, Synapse / RazerAppEngine itself, its sign-in
// autostart, and the background services, upgrade task and CPUID driver it installs (docs/razer-analysis.md).
// The properties are what the UI shows (state.razer); the static part checks the PC and runs the fixes.
public class RazerLeftovers
{
	public const string GmsServiceName = "Razer Game Manager Service 3";

	public const string DriverScript = "remove-razer-filter-drivers.ps1";

	// Synapse 4 starts itself at sign-in through this HKCU Run value (SysUtilsNative.setApplicationAutoStart).
	public const string SynapseRunValue = "RazerAppEngine";

	// Scheduled task of Razer's updater for its elevation service.
	public const string UpgradeTaskName = "Razer Upgrade Elevation Service";

	// Kernel driver of the CPUID SDK that comes with Synapse ("RazerAppEngine cpuz"). The SDK registers it at runtime
	// and its uninstaller never removes it; it gives ring-0 access to MSRs, PCI and SMBus. CPU-Z and HWMonitor
	// register their driver again when they start, so removing the service is safe.
	public const string CpuzDriver = "cpuz165";

	// Values an interrupted cpuz install or uninstall leaves in HKLM RunOnce: they run at the next sign-in.
	internal static readonly string[] CpuzRunOnce = new string[2] { "RazerAppEngine cpuz-installer", "RazerAppEngine cpuz-uninstaller" };

	// On Razer's RzPowerTool allowlist but not Razer's: never touched, whatever their path.
	internal static readonly string[] ProtectedServices = new string[4] { "Audiosrv", "AudioEndpointBuilder", "VSSrv", "XTU3SERVICE" };

	// The clean-up steps the window can ask for.
	internal static readonly string[] Actions = new string[3] { "disableServices", "disableGameManager", "driverGuide" };

	// The filter-driver services LiteSynapse looks for, and how the UI names them.
	internal static readonly (string Service, string Label)[] Filters = new (string, string)[2]
	{
		("RzDev_008d", "RzDev_008d (Naga)"),
		("RzDev_0235", "RzDev_0235 (BlackWidow Lite)")
	};

	public bool Checked { get; set; }

	public bool SynapseInstalled { get; set; }

	// Razer Synapse is running now: it can change the lighting and switch the mouse's onboard slots.
	public bool SynapseRunning { get; set; }

	// Processes of Razer Synapse 4 (RazerAppEngine, its engine monitor) and Synapse 3 that drive the devices.
	internal static readonly string[] SynapseProcesses = new string[6]
	{
		"RazerAppEngine", "RzEngineMon", "Razer Synapse 3", "Razer Synapse Service", "Razer Synapse", "RzSynapse"
	};

	// absent | running | stopped | disabled
	public string GmsService { get; set; } = "absent";

	public List<string> FilterDrivers { get; set; } = new List<string>();

	public bool RzCommon { get; set; }

	// HKCU Run values that start Razer software at sign-in and are on (Task Manager's Startup apps has not turned
	// them off). Usually just "RazerAppEngine".
	public List<string> Autostart { get; set; } = new List<string>();

	// Razer background services other than Game Manager (program in a Razer folder) and the CPUID driver.
	public List<RazerService> Services { get; set; } = new List<RazerService>();

	// absent | ready | disabled
	public string UpgradeTask { get; set; } = "absent";

	// CpuzRunOnce values present in HKLM RunOnce (either registry view).
	public List<string> RunOnce { get; set; } = new List<string>();

	// ---- Pure helpers (tested) ------------------------------------------------------------------------

	// startType: the service's Start value (2 automatic, 3 manual, 4 disabled); state: SERVICE_STATUS
	// dwCurrentState (1 stopped, 2 start pending, 3 stop pending, 4 running, ...), 0 = unknown.
	internal static string ServiceState(bool exists, int startType, int state)
	{
		if (!exists)
		{
			return "absent";
		}
		if (state >= 2)
		{
			return "running";
		}
		if (startType == 4)
		{
			return "disabled";
		}
		return "stopped";
	}

	// What a background service does now: running | auto (starts with Windows) | manual (starts when a program asks
	// for it) | disabled | removing (deleted; it goes away with the next restart).
	internal static string ServiceMode(int startType, int state, bool deletePending)
	{
		if (deletePending)
		{
			return "removing";
		}
		if (state >= 2)
		{
			return "running";
		}
		switch (startType)
		{
		case 4:
			return "disabled";
		case 3:
			return "manual";
		default:
			return "auto";
		}
	}

	// True for a service mode that still lets the service run.
	internal static bool IsOn(string mode)
	{
		return mode == "running" || mode == "auto" || mode == "manual";
	}

	// The program of a service ImagePath or a Run command: the quoted part, or everything up to ".exe" / ".sys".
	internal static string BinaryPath(string command)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return "";
		}
		string s = command.Trim();
		if (s.StartsWith('"'))
		{
			int end = s.IndexOf('"', 1);
			return (end > 0) ? s.Substring(1, end - 1) : s.Substring(1);
		}
		foreach (string ext in new string[2] { ".exe", ".sys" })
		{
			int i = s.IndexOf(ext, StringComparison.OrdinalIgnoreCase);
			if (i > 0)
			{
				return s.Substring(0, i + ext.Length);
			}
		}
		return s;
	}

	// True when the program sits in a folder named "Razer" or "Razer ..." (C:\Program Files (x86)\Razer\...,
	// ...\Razer Chroma SDK\...).
	internal static bool InRazerFolder(string command)
	{
		string[] parts = BinaryPath(command).Split('\\', '/');
		for (int i = 0; i < parts.Length - 1; i++)
		{
			string p = parts[i].Trim();
			if (p.Equals("Razer", StringComparison.OrdinalIgnoreCase) || p.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}
		return false;
	}

	// A service "Turn off Razer services" may change: a Windows service whose program is in a Razer folder, or the
	// CPUID driver. Never Game Manager (its own row), the filter drivers (the driver guide) or a protected service.
	// type: the service key's Type value (1 kernel driver, 2 file-system driver, 0x10/0x20 own/shared process,
	// 0x50/0x60 per-user template, | 0x80 per-user instance).
	internal static bool IsRazerService(string name, int type, string imagePath)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return false;
		}
		string n = name.Trim();
		if (ProtectedServices.Contains(n, StringComparer.OrdinalIgnoreCase) || n.Equals(GmsServiceName, StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		if (n.Equals(CpuzDriver, StringComparison.OrdinalIgnoreCase))
		{
			return (type & 0x03) != 0;
		}
		return (type & 0x30) != 0 && (type & 0x80) == 0 && InRazerFolder(imagePath);
	}

	// A service's display name unless it is a resource reference ("@file.dll,-101").
	internal static string ServiceLabel(string name, string displayName)
	{
		if (string.Equals(name, CpuzDriver, StringComparison.OrdinalIgnoreCase))
		{
			return "CPUID driver (" + name + ")";
		}
		if (!string.IsNullOrWhiteSpace(displayName) && !displayName.TrimStart().StartsWith('@'))
		{
			return displayName.Trim();
		}
		return name;
	}

	// An HKCU Run value that starts Razer software: Synapse 4's own, or any program in a Razer folder.
	internal static bool IsRazerRunEntry(string name, string command)
	{
		if (string.IsNullOrWhiteSpace(name) || name.Trim().Equals("LiteSynapse", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}
		return name.Trim().Equals(SynapseRunValue, StringComparison.OrdinalIgnoreCase) || InRazerFolder(command);
	}

	// schtasks /Query /XML: exit code 0 and the task's XML, otherwise the task is absent. The task's own Enabled
	// element is in <Settings> (triggers have their own).
	internal static string TaskStateFromXml(int exitCode, string xml)
	{
		if (exitCode != 0 || string.IsNullOrWhiteSpace(xml) || xml.IndexOf("<Task", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return "absent";
		}
		int start = xml.IndexOf("<Settings>", StringComparison.OrdinalIgnoreCase);
		int end = (start >= 0) ? xml.IndexOf("</Settings>", start, StringComparison.OrdinalIgnoreCase) : -1;
		if (start < 0 || end < 0)
		{
			return "ready";
		}
		string settings = xml.Substring(start, end - start);
		return settings.Contains("<Enabled>false</Enabled>", StringComparison.OrdinalIgnoreCase) ? "disabled" : "ready";
	}

	// Console output of a tool: UTF-16 when it has a BOM or NUL bytes, otherwise UTF-8 (ASCII-compatible).
	internal static string DecodeOutput(byte[] data, int length)
	{
		if (data == null || length <= 0)
		{
			return "";
		}
		length = Math.Min(length, data.Length);
		if (length >= 2 && data[0] == 0xFF && data[1] == 0xFE)
		{
			return Encoding.Unicode.GetString(data, 2, length - 2);
		}
		int zeros = 0;
		for (int i = 1; i < length; i += 2)
		{
			if (data[i] == 0)
			{
				zeros++;
			}
		}
		return (zeros * 4 > length) ? Encoding.Unicode.GetString(data, 0, length & ~1) : Encoding.UTF8.GetString(data, 0, length);
	}

	// Labels of the Razer filters found in a device's UpperFilters/LowerFilters values.
	internal static IEnumerable<string> FilterLabels(IEnumerable<string> filters)
	{
		foreach (string f in filters ?? Enumerable.Empty<string>())
		{
			foreach (var (service, label) in Filters)
			{
				if (string.Equals(f?.Trim(), service, StringComparison.OrdinalIgnoreCase))
				{
					yield return label;
				}
			}
		}
	}

	// True for a process name (without .exe) that belongs to Razer Synapse.
	internal static bool IsSynapseProcess(string processName)
	{
		return !string.IsNullOrWhiteSpace(processName) && SynapseProcesses.Any((string p) => string.Equals(p, processName.Trim(), StringComparison.OrdinalIgnoreCase));
	}

	// True for the uninstall entries of Razer Synapse (3 or 4) and RazerAppEngine.
	internal static bool IsSynapseEntry(string displayName)
	{
		if (string.IsNullOrWhiteSpace(displayName))
		{
			return false;
		}
		return displayName.Contains("Razer Synapse", StringComparison.OrdinalIgnoreCase) || displayName.Contains("RazerAppEngine", StringComparison.OrdinalIgnoreCase) || displayName.Contains("Razer App Engine", StringComparison.OrdinalIgnoreCase);
	}

	// REG_MULTI_SZ bytes (UTF-16, NUL separated) -> strings.
	internal static List<string> ParseMultiSz(byte[] data, int length)
	{
		List<string> list = new List<string>();
		if (data == null || length <= 0)
		{
			return list;
		}
		string all = Encoding.Unicode.GetString(data, 0, Math.Min(length, data.Length));
		foreach (string s in all.Split('\0'))
		{
			if (s.Length > 0)
			{
				list.Add(s);
			}
		}
		return list;
	}

	public override string ToString()
	{
		if (!Checked)
		{
			return "not checked";
		}
		List<RazerService> found = (Services ?? new List<RazerService>()).Where((RazerService s) => s != null).ToList();
		string services = (found.Count == 0) ? "none" : string.Join(", ", found.Select((RazerService s) => s.Name + " " + s.State));
		return $"Synapse installed: {SynapseInstalled}; Synapse running: {SynapseRunning}; {GmsServiceName}: {GmsService}; filter drivers: {((FilterDrivers.Count == 0) ? "none" : string.Join(", ", FilterDrivers))}; RzCommon: {RzCommon}; " +
			$"autostart: {(((Autostart?.Count ?? 0) == 0) ? "off" : string.Join(", ", Autostart))}; services: {services}; upgrade task: {UpgradeTask}; RunOnce: {(((RunOnce?.Count ?? 0) == 0) ? "none" : string.Join(", ", RunOnce))}";
	}

	// ---- "Turn off Razer services" (planned and worded here, tested; run below) -----------------------------

	// What the step changes, worked out from a check.
	internal sealed class ServicesPlan
	{
		// HKCU Run values to turn off like Task Manager does (in this process, no administrator rights needed).
		public List<string> Autostart { get; } = new List<string>();

		// Services to set to Disabled, then stop.
		public List<string> Disable { get; } = new List<string>();

		// Driver services to stop and delete (the CPUID driver).
		public List<string> Delete { get; } = new List<string>();

		public bool DisableTask { get; set; }

		// HKLM RunOnce values to delete.
		public List<string> RunOnce { get; } = new List<string>();

		// Service name -> label for the message.
		public Dictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		public bool NeedsAdmin => Disable.Count > 0 || Delete.Count > 0 || DisableTask || RunOnce.Count > 0;

		public bool IsEmpty => Autostart.Count == 0 && !NeedsAdmin;
	}

	internal static ServicesPlan PlanServices(RazerLeftovers r)
	{
		ServicesPlan p = new ServicesPlan();
		if (r == null || !r.Checked)
		{
			return p;
		}
		p.Autostart.AddRange((r.Autostart ?? new List<string>()).Where((string v) => !string.IsNullOrWhiteSpace(v)));
		if (r.GmsService == "running" || r.GmsService == "stopped")
		{
			p.Disable.Add(GmsServiceName);
			p.Labels[GmsServiceName] = GmsServiceName;
		}
		foreach (RazerService s in r.Services ?? new List<RazerService>())
		{
			if (s == null || string.IsNullOrWhiteSpace(s.Name) || !IsOn(s.State) || ProtectedServices.Contains(s.Name.Trim(), StringComparer.OrdinalIgnoreCase) || s.Name.Trim().Equals(GmsServiceName, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}
			(s.Driver ? p.Delete : p.Disable).Add(s.Name);
			p.Labels[s.Name] = string.IsNullOrWhiteSpace(s.Label) ? s.Name : s.Label;
		}
		p.DisableTask = r.UpgradeTask == "ready";
		p.RunOnce.AddRange((r.RunOnce ?? new List<string>()).Where((string v) => CpuzRunOnce.Contains(v, StringComparer.OrdinalIgnoreCase)));
		return p;
	}

	// A PowerShell single-quoted string.
	internal static string Ps(string s)
	{
		return "'" + (s ?? "").Replace("'", "''") + "'";
	}

	// The administrator part of a plan as a PowerShell script. Exit code: the number of steps that failed.
	internal static string ServicesScript(ServicesPlan p)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("$failed = 0\n");
		if (p.Disable.Count > 0)
		{
			sb.Append("$names = @(" + string.Join(", ", p.Disable.Select(Ps)) + ")\n");
			// Disabled first, so nothing starts them again while they stop.
			sb.Append("foreach ($n in $names) { sc.exe config $n start= disabled | Out-Null; if ($LASTEXITCODE -ne 0) { $failed++ } }\n");
			// Twice: a service another one depends on stops once that one has stopped.
			sb.Append("foreach ($pass in 1, 2) { foreach ($n in $names) { sc.exe stop $n | Out-Null }; Start-Sleep -Milliseconds 300 }\n");
			sb.Append("$until = (Get-Date).AddSeconds(15)\n");
			sb.Append("foreach ($n in $names) { while ((Get-Date) -lt $until) { $s = Get-Service -Name $n -ErrorAction SilentlyContinue; if (-not $s -or $s.Status -eq 'Stopped') { break }; Start-Sleep -Milliseconds 250 } }\n");
		}
		foreach (string n in p.Delete)
		{
			// 1072: already marked for deletion.
			sb.Append("sc.exe stop " + Ps(n) + " | Out-Null\n");
			sb.Append("sc.exe delete " + Ps(n) + " | Out-Null\n");
			sb.Append("if ($LASTEXITCODE -ne 0 -and $LASTEXITCODE -ne 1072) { $failed++ }\n");
		}
		if (p.DisableTask)
		{
			sb.Append("schtasks.exe /Change /TN " + Ps(UpgradeTaskName) + " /DISABLE | Out-Null\n");
			sb.Append("if ($LASTEXITCODE -ne 0) { $failed++ }\n");
		}
		foreach (string v in p.RunOnce)
		{
			sb.Append("foreach ($view in '/reg:64', '/reg:32') { reg.exe delete 'HKLM\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce' /v " + Ps(v) + " /f $view 2>$null | Out-Null }\n");
		}
		sb.Append("exit $failed\n");
		return sb.ToString();
	}

	// The message once the step ran, from a check made afterwards. adminRan: the administrator part ran (false
	// when there was none or the UAC prompt was declined). exitCode -2: it was still running.
	internal static string ServicesOutcome(ServicesPlan p, RazerLeftovers before, RazerLeftovers after, int exitCode, bool adminRan)
	{
		List<string> off = new List<string>();
		List<string> on = new List<string>();
		foreach (string v in p.Autostart)
		{
			bool stillOn = (after?.Autostart ?? new List<string>()).Contains(v, StringComparer.OrdinalIgnoreCase);
			(stillOn ? on : off).Add(v.Equals(SynapseRunValue, StringComparison.OrdinalIgnoreCase) ? "Synapse autostart" : v + " autostart");
		}
		if (adminRan)
		{
			foreach (string n in p.Disable.Concat(p.Delete))
			{
				string now;
				if (n.Equals(GmsServiceName, StringComparison.OrdinalIgnoreCase))
				{
					now = (after?.GmsService == "running") ? "running" : (after?.GmsService == "stopped") ? "auto" : null;
				}
				else
				{
					now = (after?.Services ?? new List<RazerService>()).FirstOrDefault((RazerService s) => string.Equals(s?.Name, n, StringComparison.OrdinalIgnoreCase))?.State;
				}
				string label = p.Labels.TryGetValue(n, out string l) ? l : n;
				if (now != null && IsOn(now))
				{
					on.Add(label + " (" + now + ")");
				}
				else
				{
					off.Add(label);
				}
			}
			if (p.DisableTask)
			{
				((after?.UpgradeTask == "ready") ? on : off).Add("upgrade task");
			}
			foreach (string v in p.RunOnce)
			{
				((after?.RunOnce ?? new List<string>()).Contains(v, StringComparer.OrdinalIgnoreCase) ? on : off).Add(v + " (next sign-in)");
			}
		}
		List<string> parts = new List<string>();
		if (off.Count > 0)
		{
			parts.Add("Turned off: " + string.Join(", ", off) + ".");
		}
		if (on.Count > 0)
		{
			parts.Add("Still on: " + string.Join(", ", on) + ".");
		}
		if (p.NeedsAdmin && !adminRan)
		{
			parts.Add("The Razer services were not changed: Windows did not get administrator permission.");
		}
		else if (exitCode == -2)
		{
			parts.Add("The step is still running. Check again in a moment.");
		}
		if (before?.SynapseInstalled == true && (off.Count > 0 || on.Count > 0))
		{
			parts.Add("Razer Synapse may turn them back on: uninstall it in Windows Settings › Apps to keep them off.");
		}
		return (parts.Count == 0) ? "Nothing was changed." : string.Join(" ", parts);
	}

	// ---- Checking the PC (Windows) -------------------------------------------------------------------------

	// Registry, SetupAPI and service-manager reads (schtasks only when the task cache can't say whether the upgrade
	// task exists); a few tens of milliseconds. Never throws.
	public static RazerLeftovers Check()
	{
		RazerLeftovers r = new RazerLeftovers();
		try
		{
			r.SynapseInstalled = Win.SynapseInstalled();
		}
		catch (Exception ex)
		{
			Cfg.Log("razer check (apps): " + ex.Message);
		}
		try
		{
			r.SynapseRunning = Win.SynapseRunning();
		}
		catch (Exception exRun)
		{
			Cfg.Log("razer check (processes): " + exRun.Message);
		}
		try
		{
			r.GmsService = Win.GmsState();
		}
		catch (Exception ex2)
		{
			Cfg.Log("razer check (service): " + ex2.Message);
		}
		try
		{
			r.FilterDrivers = Win.FilterDrivers();
		}
		catch (Exception ex3)
		{
			Cfg.Log("razer check (drivers): " + ex3.Message);
		}
		try
		{
			r.RzCommon = Win.ServiceExists("RzCommon");
		}
		catch (Exception ex4)
		{
			Cfg.Log("razer check (RzCommon): " + ex4.Message);
		}
		try
		{
			r.Autostart = Win.RazerAutostart();
		}
		catch (Exception ex5)
		{
			Cfg.Log("razer check (autostart): " + ex5.Message);
		}
		try
		{
			r.Services = Win.RazerServices();
		}
		catch (Exception ex6)
		{
			Cfg.Log("razer check (services): " + ex6.Message);
		}
		try
		{
			r.UpgradeTask = Win.TaskState(UpgradeTaskName);
		}
		catch (Exception ex7)
		{
			Cfg.Log("razer check (task): " + ex7.Message);
		}
		try
		{
			r.RunOnce = Win.RunOnceLeftovers();
		}
		catch (Exception ex8)
		{
			Cfg.Log("razer check (RunOnce): " + ex8.Message);
		}
		r.Checked = true;
		return r;
	}

	// Full path of the driver helper script shipped with LiteSynapse, or null.
	public static string FindDriverScript()
	{
		string baseDir = AppContext.BaseDirectory;
		string[] candidates = new string[2]
		{
			Path.Combine(baseDir, "tools", DriverScript),
			Path.Combine(baseDir, DriverScript)
		};
		return candidates.FirstOrDefault(File.Exists);
	}

	// Runs one elevated fix (the user sees a UAC prompt) and returns a message for the UI. Blocks until the UAC
	// prompt is answered and the step finished (at most ~30 s): call it off the UI thread.
	public static string Cleanup(string action, RazerLeftovers current)
	{
		try
		{
			switch (action)
			{
			case "disableServices":
				return DisableServices();
			case "disableGameManager":
				return DisableGameManager(current);
			case "driverGuide":
				return DriverGuide(current);
			default:
				return "Unknown action \"" + action + "\".";
			}
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return "Cancelled: Windows did not get administrator permission, so nothing was changed.";
		}
		catch (Exception ex2)
		{
			Cfg.Log("razer cleanup " + action + ": " + ex2.Message);
			return "Could not run the step: " + ex2.Message;
		}
	}

	private static string DisableGameManager(RazerLeftovers current)
	{
		string state = current?.Checked == true ? current.GmsService : Win.GmsState();
		if (state == "absent")
		{
			return GmsServiceName + " is not installed. Nothing to do.";
		}
		if (state == "disabled")
		{
			return GmsServiceName + " is already stopped and disabled.";
		}
		ServicesPlan plan = new ServicesPlan();
		plan.Disable.Add(GmsServiceName);
		int code = RunElevatedPowerShell(ServicesScript(plan), hidden: true, waitMs: 30000);
		Cfg.Log($"razer cleanup: disable {GmsServiceName} -> exit code {code}");
		string after = Win.GmsState();
		if (after == "disabled")
		{
			return GmsServiceName + " is stopped and disabled. It no longer starts with Windows." + (current?.SynapseInstalled == true ? " Razer Synapse may turn it back on; uninstall Synapse to keep it off." : "");
		}
		if (code == -2)
		{
			return "The step is still running. Check again in a moment.";
		}
		return $"{GmsServiceName} could not be disabled (now: {after}, exit code {code}).";
	}

	// Turns off Razer's sign-in autostart (no UAC), then with one UAC prompt: Game Manager and the other Razer
	// services (Disabled, stopped), the CPUID driver (deleted), the upgrade task (disabled) and the cpuz RunOnce
	// values (deleted). Works from a fresh check, not from what the window showed.
	private static string DisableServices()
	{
		RazerLeftovers before = Check();
		ServicesPlan plan = PlanServices(before);
		if (plan.IsEmpty)
		{
			return "No Razer service, task or sign-in autostart is on. Nothing to do.";
		}
		foreach (string v in plan.Autostart)
		{
			StartupApproved.Disable(v);
			Cfg.Log("razer cleanup: autostart \"" + v + "\" turned off (Task Manager's Startup apps can turn it back on)");
		}
		int code = 0;
		bool adminRan = false;
		if (plan.NeedsAdmin)
		{
			try
			{
				code = RunElevatedPowerShell(ServicesScript(plan), hidden: true, waitMs: 60000);
				adminRan = true;
				Cfg.Log("razer cleanup: disable " + string.Join(", ", plan.Disable) + "; delete " + string.Join(", ", plan.Delete) + (plan.DisableTask ? "; upgrade task" : "") + ((plan.RunOnce.Count > 0) ? "; RunOnce " + string.Join(", ", plan.RunOnce) : "") + " -> exit code " + code);
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
			{
				Cfg.Log("razer cleanup: services not changed (UAC prompt declined)");
			}
		}
		return ServicesOutcome(plan, before, Check(), code, adminRan);
	}

	private static string DriverGuide(RazerLeftovers current)
	{
		string path = FindDriverScript();
		if (path == null)
		{
			return DriverScript + " was not found next to LiteSynapse.exe. Reinstall LiteSynapse to get it back.";
		}
		ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -ExecutionPolicy Bypass -NoExit -File \"" + path + "\"")
		{
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = ProcessWindowStyle.Normal
		};
		using (Process.Start(psi))
		{
		}
		Cfg.Log("razer cleanup: opened the driver guide (" + path + ")");
		string msg = "An administrator PowerShell window now lists the Razer filter-driver packages. It does not change anything; follow the steps it shows to back them up and remove them.";
		if (current?.SynapseInstalled == true)
		{
			msg += " Uninstall Razer Synapse first: while it is installed it puts the drivers back, and the script refuses to remove them.";
		}
		return msg;
	}

	// Exit code of the script; -2 when it did not finish within waitMs.
	private static int RunElevatedPowerShell(string script, bool hidden, int waitMs)
	{
		string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
		ProcessStartInfo psi = new ProcessStartInfo("powershell.exe", "-NoProfile -NonInteractive -ExecutionPolicy Bypass " + (hidden ? "-WindowStyle Hidden " : "") + "-EncodedCommand " + encoded)
		{
			UseShellExecute = true,
			Verb = "runas",
			WindowStyle = hidden ? ProcessWindowStyle.Hidden : ProcessWindowStyle.Normal
		};
		using Process p = Process.Start(psi);
		if (p == null)
		{
			return -1;
		}
		if (!p.WaitForExit(waitMs))
		{
			return -2;
		}
		return p.ExitCode;
	}

	// Windows-only probing.
	private static class Win
	{
		private struct SERVICE_STATUS
		{
			public int ServiceType;

			public int CurrentState;

			public int ControlsAccepted;

			public int Win32ExitCode;

			public int ServiceSpecificExitCode;

			public int CheckPoint;

			public int WaitHint;
		}

		private struct SP_DEVINFO_DATA
		{
			public int cbSize;

			public Guid ClassGuid;

			public int DevInst;

			public nint Reserved;
		}

		private const uint SC_MANAGER_CONNECT = 0x0001;

		private const uint SERVICE_QUERY_STATUS = 0x0004;

		private const int ERROR_SERVICE_DOES_NOT_EXIST = 1060;

		private const uint DIGCF_ALLCLASSES = 0x0004;

		private const int SPDRP_HARDWAREID = 0x01;

		private const int SPDRP_UPPERFILTERS = 0x11;

		private const int SPDRP_LOWERFILTERS = 0x12;

		[DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern nint OpenSCManager(string machine, string database, uint access);

		[DllImport("advapi32", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern nint OpenService(nint scm, string name, uint access);

		[DllImport("advapi32", SetLastError = true)]
		private static extern bool QueryServiceStatus(nint service, out SERVICE_STATUS status);

		[DllImport("advapi32")]
		private static extern bool CloseServiceHandle(nint h);

		[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern nint SetupDiGetClassDevs(nint classGuid, string enumerator, nint hwnd, uint flags);

		[DllImport("setupapi.dll", SetLastError = true)]
		private static extern bool SetupDiEnumDeviceInfo(nint set, uint index, ref SP_DEVINFO_DATA data);

		[DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
		private static extern bool SetupDiGetDeviceRegistryProperty(nint set, ref SP_DEVINFO_DATA data, int property, out int regType, byte[] buffer, int size, out int required);

		[DllImport("setupapi.dll")]
		private static extern bool SetupDiDestroyDeviceInfoList(nint set);

		public static bool ServiceExists(string name)
		{
			using RegistryKey k = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + name);
			return k != null;
		}

		// dwCurrentState of a service (1 stopped ... 4 running), 0 when it cannot be read.
		private static int RunState(nint scm, string name)
		{
			if (scm == IntPtr.Zero)
			{
				return 0;
			}
			nint svc = OpenService(scm, name, SERVICE_QUERY_STATUS);
			if (svc == IntPtr.Zero)
			{
				return 0;
			}
			try
			{
				return QueryServiceStatus(svc, out var st) ? st.CurrentState : 0;
			}
			finally
			{
				CloseServiceHandle(svc);
			}
		}

		// Razer services besides Game Manager, and the CPUID driver: one pass over the services key.
		public static List<RazerService> RazerServices()
		{
			List<RazerService> list = new List<RazerService>();
			using RegistryKey services = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services");
			if (services == null)
			{
				return list;
			}
			nint scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
			try
			{
				foreach (string name in services.GetSubKeyNames())
				{
					try
					{
						using RegistryKey k = services.OpenSubKey(name);
						if (k == null)
						{
							continue;
						}
						int type = (k.GetValue("Type") is int t) ? t : 0;
						string image = k.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
						if (!IsRazerService(name, type, image))
						{
							continue;
						}
						int start = (k.GetValue("Start") is int st) ? st : 3;
						bool deleting = k.GetValue("DeleteFlag") is int d && d != 0;
						list.Add(new RazerService
						{
							Name = name,
							Label = ServiceLabel(name, k.GetValue("DisplayName") as string),
							State = ServiceMode(start, RunState(scm, name), deleting),
							Driver = (type & 0x03) != 0
						});
					}
					catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
					{
					}
				}
			}
			finally
			{
				if (scm != IntPtr.Zero)
				{
					CloseServiceHandle(scm);
				}
			}
			list.Sort((RazerService a, RazerService b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
			return list;
		}

		// HKCU Run values that start Razer software and that Task Manager's Startup apps has not turned off.
		public static List<string> RazerAutostart()
		{
			List<string> on = new List<string>();
			using RegistryKey run = Registry.CurrentUser.OpenSubKey(StartupApproved.RunKey);
			if (run == null)
			{
				return on;
			}
			using RegistryKey approved = Registry.CurrentUser.OpenSubKey(StartupApproved.Key);
			foreach (string name in run.GetValueNames())
			{
				string command = run.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
				if (IsRazerRunEntry(name, command) && !StartupApproved.IsDisabled(approved?.GetValue(name) as byte[]))
				{
					on.Add(name);
				}
			}
			on.Sort(StringComparer.OrdinalIgnoreCase);
			return on;
		}

		// absent | ready | disabled. The task cache says "absent" without starting a program; schtasks reads the
		// task's XML for its enabled flag.
		public static string TaskState(string name)
		{
			try
			{
				using RegistryKey tree = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Schedule\\TaskCache\\Tree");
				if (tree != null)
				{
					using RegistryKey task = tree.OpenSubKey(name);
					if (task == null)
					{
						return "absent";
					}
				}
			}
			catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
			{
			}
			string xml = RunTool(Path.Combine(Environment.SystemDirectory, "schtasks.exe"), "/Query /TN \"" + name + "\" /XML", 5000, out int code);
			return TaskStateFromXml(code, xml);
		}

		// CpuzRunOnce values in HKLM RunOnce, 64- or 32-bit view (the cpuz installer is 32-bit NSIS).
		public static List<string> RunOnceLeftovers()
		{
			SortedSet<string> found = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (RegistryView view in new RegistryView[2]
			{
				RegistryView.Registry64,
				RegistryView.Registry32
			})
			{
				using RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
				using RegistryKey k = root.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\RunOnce");
				foreach (string v in CpuzRunOnce)
				{
					if (k?.GetValue(v) != null)
					{
						found.Add(v);
					}
				}
			}
			return found.ToList();
		}

		// Runs a console tool without a window and returns what it printed ("" when it did not finish in time).
		private static string RunTool(string exe, string args, int timeoutMs, out int exitCode)
		{
			exitCode = -1;
			ProcessStartInfo psi = new ProcessStartInfo(exe, args)
			{
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			using Process p = Process.Start(psi);
			if (p == null)
			{
				return "";
			}
			using MemoryStream ms = new MemoryStream();
			Task copy = p.StandardOutput.BaseStream.CopyToAsync(ms);
			Task drain = p.StandardError.BaseStream.CopyToAsync(Stream.Null);
			if (!p.WaitForExit(timeoutMs))
			{
				try
				{
					p.Kill();
				}
				catch
				{
				}
				return "";
			}
			Task.WaitAll(new Task[2] { copy, drain }, 1000);
			exitCode = p.ExitCode;
			return DecodeOutput(ms.GetBuffer(), (int)ms.Length);
		}

		public static string GmsState()
		{
			nint scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
			if (scm == IntPtr.Zero)
			{
				// No access to the service manager: fall back to the registry (no running state).
				bool exists = ServiceExists(GmsServiceName);
				return ServiceState(exists, StartType(GmsServiceName), 0);
			}
			try
			{
				nint svc = OpenService(scm, GmsServiceName, SERVICE_QUERY_STATUS);
				if (svc == IntPtr.Zero)
				{
					if (Marshal.GetLastWin32Error() == ERROR_SERVICE_DOES_NOT_EXIST)
					{
						return "absent";
					}
					return ServiceState(ServiceExists(GmsServiceName), StartType(GmsServiceName), 0);
				}
				try
				{
					int state = QueryServiceStatus(svc, out var st) ? st.CurrentState : 0;
					return ServiceState(exists: true, StartType(GmsServiceName), state);
				}
				finally
				{
					CloseServiceHandle(svc);
				}
			}
			finally
			{
				CloseServiceHandle(scm);
			}
		}

		private static int StartType(string name)
		{
			using RegistryKey k = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Services\\" + name);
			return (k?.GetValue("Start") is int v) ? v : 0;
		}

		public static bool SynapseRunning()
		{
			bool found = false;
			foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
			{
				using (p)
				{
					if (!found && IsSynapseProcess(p.ProcessName))
					{
						found = true;
					}
				}
			}
			return found;
		}

		public static bool SynapseInstalled()
		{
			foreach (RegistryHive hive in new RegistryHive[2]
			{
				RegistryHive.LocalMachine,
				RegistryHive.CurrentUser
			})
			{
				foreach (RegistryView view in new RegistryView[2]
				{
					RegistryView.Registry64,
					RegistryView.Registry32
				})
				{
					using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
					using RegistryKey uninstall = root.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Uninstall");
					if (uninstall == null)
					{
						continue;
					}
					foreach (string sub in uninstall.GetSubKeyNames())
					{
						using RegistryKey app = uninstall.OpenSubKey(sub);
						if (IsSynapseEntry(app?.GetValue("DisplayName") as string))
						{
							return true;
						}
					}
				}
			}
			return false;
		}

		// Razer filters attached to the Naga's and the BlackWidow Lite's device nodes (also unplugged ones).
		public static List<string> FilterDrivers()
		{
			SortedSet<string> found = new SortedSet<string>(StringComparer.Ordinal);
			bool enumerated = false;
			foreach (string enumerator in new string[2] { "USB", "HID" })
			{
				nint set = SetupDiGetClassDevs(IntPtr.Zero, enumerator, IntPtr.Zero, DIGCF_ALLCLASSES);
				if (set == new IntPtr(-1))
				{
					continue;
				}
				enumerated = true;
				try
				{
					SP_DEVINFO_DATA data = new SP_DEVINFO_DATA
					{
						cbSize = Marshal.SizeOf<SP_DEVINFO_DATA>()
					};
					for (uint i = 0u; SetupDiEnumDeviceInfo(set, i, ref data); i++)
					{
						List<string> ids = Property(set, ref data, SPDRP_HARDWAREID);
						if (!ids.Any((string id) => id.Contains("VID_1532", StringComparison.OrdinalIgnoreCase)))
						{
							continue;
						}
						foreach (string label in FilterLabels(Property(set, ref data, SPDRP_UPPERFILTERS).Concat(Property(set, ref data, SPDRP_LOWERFILTERS))))
						{
							found.Add(label);
						}
					}
				}
				finally
				{
					SetupDiDestroyDeviceInfoList(set);
				}
			}
			if (!enumerated)
			{
				// SetupAPI unavailable: report the installed filter services instead.
				foreach (var (service, label) in Filters)
				{
					if (ServiceExists(service))
					{
						found.Add(label);
					}
				}
			}
			return found.ToList();
		}

		private static List<string> Property(nint set, ref SP_DEVINFO_DATA data, int property)
		{
			byte[] buf = new byte[1024];
			if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, buf, buf.Length, out var need))
			{
				if (need <= buf.Length)
				{
					return new List<string>();
				}
				buf = new byte[need];
				if (!SetupDiGetDeviceRegistryProperty(set, ref data, property, out _, buf, buf.Length, out need))
				{
					return new List<string>();
				}
			}
			return ParseMultiSz(buf, need);
		}
	}
}

// One Razer background service. State: running | auto (starts with Windows) | manual (starts when a program asks for
// it) | disabled | removing (deleted; gone after the next restart).
public sealed class RazerService
{
	public string Name { get; set; }

	public string Label { get; set; }

	public string State { get; set; }

	// A kernel driver (the CPUID driver): deleted rather than disabled.
	public bool Driver { get; set; }
}
