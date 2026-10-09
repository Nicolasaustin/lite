using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace LiteSynapse;

// Razer software left on the PC that costs performance: the Game Manager service (GMS3), the RzDev kernel filter
// drivers on the Naga and the BlackWidow Lite, the RzCommon driver, and Synapse / RazerAppEngine itself.
// The properties are what the UI shows (state.razer); the static part checks the PC and runs the fixes.
public class RazerLeftovers
{
	public const string GmsServiceName = "Razer Game Manager Service 3";

	public const string DriverScript = "remove-razer-filter-drivers.ps1";

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
		return $"Synapse installed: {SynapseInstalled}; Synapse running: {SynapseRunning}; {GmsServiceName}: {GmsService}; filter drivers: {((FilterDrivers.Count == 0) ? "none" : string.Join(", ", FilterDrivers))}; RzCommon: {RzCommon}";
	}

	// ---- Checking the PC (Windows) -------------------------------------------------------------------------

	// Registry and SetupAPI reads only; takes a few milliseconds. Never throws.
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
		string script = "$n = '" + GmsServiceName + "'\n" + "sc.exe stop $n | Out-Null\n" + "sc.exe config $n start= disabled | Out-Null\n" + "exit $LASTEXITCODE\n";
		int code = RunElevatedPowerShell(script, hidden: true, waitMs: 30000);
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
