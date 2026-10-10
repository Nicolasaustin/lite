using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace LiteSynapse;

// Windows settings that add delay between the mouse and the PC, and the one step that turns them off.
//
// Two of them matter for a wired mouse:
//  - USB power saving on the device itself ("Allow the computer to turn off this device to save power" in Device
//    Manager). With it on, Windows may suspend the device between reports; the first move after an idle moment
//    then waits for it to wake. Device Manager writes EnhancedPowerManagementEnabled / SelectiveSuspendEnabled /
//    AllowIdleIrpInD3 under the device's "Device Parameters" key.
//  - USB selective suspend in the power plan, which does the same thing plan-wide.
//
// And three Windows mouse settings, all per-user and none needing administrator rights:
//  - RawMouseThrottleEnabled: Windows caps raw mouse input at 125 Hz for a window that is not in front. A game
//    that loses focus for a moment (an overlay, a second monitor) then gets a quarter of the mouse's reports.
//  - "Enhance pointer precision": Windows' mouse acceleration, so the same hand movement gives a different
//    distance depending on speed.
//  - Pointer speed: anything but the middle notch (6 of 11) makes Windows scale the movement, which throws
//    counts away or doubles them.
//
// Nothing here touches a device that is not Razer's (VID_1532), and nothing is changed without the user asking.
internal static class DeviceTuning
{
	// The value names Windows uses for USB power saving on a device, all "1 = may be suspended".
	internal static readonly string[] PowerValues = new string[3]
	{
		"EnhancedPowerManagementEnabled", "SelectiveSuspendEnabled", "AllowIdleIrpInD3"
	};

	public const string UsbSuspendSubGroup = "2a737441-1930-4402-8d77-b2bebba308a3";

	public const string UsbSuspendSetting = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

	private const string EnumUsb = "SYSTEM\\CurrentControlSet\\Enum\\USB";

	private const string PowerSchemes = "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes";

	private const string MouseKey = "Control Panel\\Mouse";

	public const string ThrottleValue = "RawMouseThrottleEnabled";

	// SPI_GETMOUSE / SPI_SETMOUSE take int[3] { threshold1, threshold2, acceleration }; all zero = no
	// acceleration. SPI_GET/SETMOUSESPEED take 1..20, and 10 is the middle notch (6 of 11 in the old dialog).
	private const uint SPI_GETMOUSE = 0x0003u;

	private const uint SPI_SETMOUSE = 0x0004u;

	private const uint SPI_GETMOUSESPEED = 0x0070u;

	private const uint SPI_SETMOUSESPEED = 0x0071u;

	private const uint SPIF_UPDATEINIFILE = 0x0001u;

	private const uint SPIF_SENDCHANGE = 0x0002u;

	public const int DefaultPointerSpeed = 10;

	[DllImport("user32", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
	private static extern bool SystemParametersInfoArray(uint action, uint param, int[] value, uint update);

	[DllImport("user32", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
	private static extern bool SystemParametersInfoRef(uint action, uint param, ref int value, uint update);

	[DllImport("user32", SetLastError = true, EntryPoint = "SystemParametersInfoW")]
	private static extern bool SystemParametersInfoPtr(uint action, uint param, nint value, uint update);

	// ---- Pure helpers (tested) -------------------------------------------------------------------------

	// A USB key name belongs to one of the two devices LiteSynapse drives.
	internal static bool IsOurDevice(string vidPid)
	{
		if (string.IsNullOrWhiteSpace(vidPid) || vidPid.IndexOf("VID_1532", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return false;
		}
		return vidPid.IndexOf("PID_008D", StringComparison.OrdinalIgnoreCase) >= 0
			|| vidPid.IndexOf("PID_0235", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// Power saving is on for a device when any of the three values says it may be suspended. A value that is not
	// there is Windows' default, which for a USB input device means it may be suspended.
	internal static bool PowerSavingOn(IReadOnlyDictionary<string, int?> values)
	{
		if (values == null)
		{
			return false;
		}
		return PowerValues.Any((string n) => !values.TryGetValue(n, out int? v) || !v.HasValue || v.Value != 0);
	}

	// Windows caps raw mouse input for background windows unless RawMouseThrottleEnabled is 0. No value means
	// the cap is on (the Windows default).
	internal static bool ThrottleOn(int? value)
	{
		return !value.HasValue || value.Value != 0;
	}

	// Mouse acceleration is on when any of the three SPI_GETMOUSE numbers is not zero.
	internal static bool AccelerationOn(int[] mouse)
	{
		return mouse != null && mouse.Length >= 3 && (mouse[0] != 0 || mouse[1] != 0 || mouse[2] != 0);
	}

	// What the window shows for the power plan's USB selective suspend: on | off | unknown.
	internal static string SuspendState(int? acSetting)
	{
		if (!acSetting.HasValue)
		{
			return "unknown";
		}
		return (acSetting.Value == 0) ? "off" : "on";
	}

	// The elevated script: zero the three values on each device key, then turn USB selective suspend off in the
	// power plan. Exit code = the number of steps that failed.
	internal static string Script(IEnumerable<string> deviceKeys, bool powerPlan)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("$failed = 0\n");
		foreach (string key in deviceKeys ?? Enumerable.Empty<string>())
		{
			string path = "HKLM:\\" + key + "\\Device Parameters";
			sb.Append("try {\n");
			sb.Append("  New-Item -Path " + RazerLeftovers.Ps(path) + " -Force -ErrorAction Stop | Out-Null\n");
			foreach (string name in PowerValues)
			{
				sb.Append("  New-ItemProperty -Path " + RazerLeftovers.Ps(path) + " -Name " + RazerLeftovers.Ps(name) +
					" -PropertyType DWord -Value 0 -Force -ErrorAction Stop | Out-Null\n");
			}
			sb.Append("} catch { $failed++ }\n");
		}
		if (powerPlan)
		{
			sb.Append("powercfg.exe /setacvalueindex SCHEME_CURRENT " + UsbSuspendSubGroup + " " + UsbSuspendSetting + " 0 | Out-Null\n");
			sb.Append("if ($LASTEXITCODE -ne 0) { $failed++ }\n");
			sb.Append("powercfg.exe /setdcvalueindex SCHEME_CURRENT " + UsbSuspendSubGroup + " " + UsbSuspendSetting + " 0 | Out-Null\n");
			sb.Append("powercfg.exe /setactive SCHEME_CURRENT | Out-Null\n");
		}
		sb.Append("exit $failed\n");
		return sb.ToString();
	}

	// What to tell the user once it ran, from a check made afterwards.
	internal static string Outcome(DeviceTuningState before, DeviceTuningState after, bool adminRan)
	{
		if (before == null || !before.NeedsWork)
		{
			return "The mouse and keyboard already talk to Windows without any power saving in the way. Nothing to do.";
		}
		List<string> done = new List<string>();
		List<string> left = new List<string>();
		if (before.BackgroundCap)
		{
			((after?.BackgroundCap ?? true) ? left : done).Add("the 125 Hz cap on background windows (after the next sign-in)");
		}
		if (before.Acceleration)
		{
			((after?.Acceleration ?? true) ? left : done).Add("mouse acceleration");
		}
		if (before.PointerSpeedOff)
		{
			((after?.PointerSpeedOff ?? true) ? left : done).Add("pointer speed scaling");
		}
		if (before.NeedsAdminFix && !adminRan)
		{
			left.Add("USB power saving (Windows did not get administrator permission)");
		}
		int fixedDevices = adminRan
			? before.Devices.Count((TunedDevice d) => d.PowerSaving) - (after?.Devices.Count((TunedDevice d) => d.PowerSaving) ?? 0)
			: 0;
		if (fixedDevices > 0)
		{
			done.Add(fixedDevices + " device" + ((fixedDevices == 1) ? "" : "s") + " kept awake");
		}
		int stillOn = adminRan ? (after?.Devices.Count((TunedDevice d) => d.PowerSaving) ?? 0) : 0;
		if (stillOn > 0)
		{
			left.Add(stillOn + " device" + ((stillOn == 1) ? "" : "s") + " still allowed to sleep");
		}
		if (before.UsbSuspend == "on" && adminRan)
		{
			((after?.UsbSuspend == "on") ? left : done).Add("USB selective suspend in the power plan");
		}
		List<string> parts = new List<string>();
		if (done.Count > 0)
		{
			parts.Add("Turned off: " + string.Join(", ", done) + ".");
		}
		if (left.Count > 0)
		{
			parts.Add("Still on: " + string.Join(", ", left) + ".");
		}
		if (parts.Count == 0)
		{
			return "Nothing was changed.";
		}
		parts.Add("Sign out and back in (or restart) so every part of Windows picks this up.");
		return string.Join(" ", parts);
	}

	// ---- Checking and changing the PC (Windows) ---------------------------------------------------------

	// Registry reads only; a few milliseconds. Never throws.
	public static DeviceTuningState Check()
	{
		DeviceTuningState s = new DeviceTuningState();
		try
		{
			s.Devices = Devices();
		}
		catch (Exception ex)
		{
			Cfg.Log("device tuning (devices): " + ex.Message);
		}
		try
		{
			s.UsbSuspend = SuspendState(ActiveSchemeUsbSuspend());
		}
		catch (Exception ex2)
		{
			Cfg.Log("device tuning (power plan): " + ex2.Message);
		}
		try
		{
			using RegistryKey mouse = Registry.CurrentUser.OpenSubKey(MouseKey);
			object raw = mouse?.GetValue(ThrottleValue);
			s.BackgroundCap = ThrottleOn((raw is int i) ? i : ((raw is string t && int.TryParse(t, out int n)) ? n : null));
			int[] accel = new int[3];
			if (SystemParametersInfoArray(SPI_GETMOUSE, 0u, accel, 0u))
			{
				s.Acceleration = AccelerationOn(accel);
			}
			int speed = DefaultPointerSpeed;
			if (SystemParametersInfoRef(SPI_GETMOUSESPEED, 0u, ref speed, 0u))
			{
				s.PointerSpeed = speed;
			}
		}
		catch (Exception ex3)
		{
			Cfg.Log("device tuning (mouse settings): " + ex3.Message);
		}
		s.Checked = true;
		return s;
	}

	private static List<TunedDevice> Devices()
	{
		List<TunedDevice> list = new List<TunedDevice>();
		using RegistryKey usb = Registry.LocalMachine.OpenSubKey(EnumUsb);
		if (usb == null)
		{
			return list;
		}
		foreach (string vidPid in usb.GetSubKeyNames())
		{
			if (!IsOurDevice(vidPid))
			{
				continue;
			}
			using RegistryKey dev = usb.OpenSubKey(vidPid);
			if (dev == null)
			{
				continue;
			}
			foreach (string instance in dev.GetSubKeyNames())
			{
				try
				{
					using RegistryKey inst = dev.OpenSubKey(instance);
					if (inst == null)
					{
						continue;
					}
					Dictionary<string, int?> values = new Dictionary<string, int?>(StringComparer.OrdinalIgnoreCase);
					using (RegistryKey parms = inst.OpenSubKey("Device Parameters"))
					{
						foreach (string name in PowerValues)
						{
							values[name] = (parms?.GetValue(name) is int v) ? v : null;
						}
					}
					list.Add(new TunedDevice
					{
						Key = EnumUsb + "\\" + vidPid + "\\" + instance,
						Name = (inst.GetValue("FriendlyName") as string) ?? (inst.GetValue("DeviceDesc") as string) ?? vidPid,
						PowerSaving = PowerSavingOn(values)
					});
				}
				catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException)
				{
				}
			}
		}
		return list;
	}

	// The active power plan's "USB selective suspend setting" for mains power, or null when it cannot be read.
	private static int? ActiveSchemeUsbSuspend()
	{
		using RegistryKey schemes = Registry.LocalMachine.OpenSubKey(PowerSchemes);
		string active = schemes?.GetValue("ActivePowerScheme") as string;
		if (string.IsNullOrEmpty(active))
		{
			return null;
		}
		using RegistryKey setting = schemes.OpenSubKey(active + "\\" + UsbSuspendSubGroup + "\\" + UsbSuspendSetting);
		// No value stored means the plan uses the Windows default, which is "on" for every shipped plan.
		if (setting == null)
		{
			return 1;
		}
		return (setting.GetValue("ACSettingIndex") is int v) ? v : 1;
	}

	// Turns the power saving off (one UAC prompt) and reports what changed. Blocks until it finished.
	public static string Apply()
	{
		DeviceTuningState before = Check();
		if (!before.NeedsWork)
		{
			return Outcome(before, before, adminRan: false);
		}
		// The three mouse settings belong to this Windows account: no administrator rights, and the pointer ones
		// take effect at once (SPIF_SENDCHANGE tells every program).
		if (before.BackgroundCap)
		{
			try
			{
				using RegistryKey mouse = Registry.CurrentUser.CreateSubKey(MouseKey);
				mouse.SetValue(ThrottleValue, 0, RegistryValueKind.DWord);
				Cfg.Log("device tuning: background polling cap off (takes effect after the next sign-in)");
			}
			catch (Exception ex)
			{
				Cfg.Log("device tuning (throttle): " + ex.Message);
			}
		}
		if (before.Acceleration)
		{
			try
			{
				SystemParametersInfoArray(SPI_SETMOUSE, 0u, new int[3], SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
				Cfg.Log("device tuning: mouse acceleration off");
			}
			catch (Exception ex2)
			{
				Cfg.Log("device tuning (acceleration): " + ex2.Message);
			}
		}
		if (before.PointerSpeedOff)
		{
			try
			{
				SystemParametersInfoPtr(SPI_SETMOUSESPEED, 0u, new IntPtr(DefaultPointerSpeed), SPIF_UPDATEINIFILE | SPIF_SENDCHANGE);
				Cfg.Log("device tuning: pointer speed back to the middle notch");
			}
			catch (Exception ex3)
			{
				Cfg.Log("device tuning (pointer speed): " + ex3.Message);
			}
		}
		bool adminRan = false;
		if (before.NeedsAdminFix)
		{
			List<string> keys = before.Devices.Where((TunedDevice d) => d.PowerSaving).Select((TunedDevice d) => d.Key).ToList();
			try
			{
				int code = RazerLeftovers.RunElevated(Script(keys, before.UsbSuspend == "on"), 60000);
				adminRan = true;
				Cfg.Log($"device tuning: {keys.Count} device key(s), power plan {before.UsbSuspend} -> exit code {code}");
			}
			catch (Win32Exception ex4) when (ex4.NativeErrorCode == 1223)
			{
				Cfg.Log("device tuning: USB power saving not changed (UAC prompt declined)");
			}
			catch (Exception ex5)
			{
				Cfg.Log("device tuning: " + ex5.Message);
			}
		}
		return Outcome(before, Check(), adminRan);
	}
}

// One USB device of ours and whether Windows may put it to sleep.
public sealed class TunedDevice
{
	// Its registry key under SYSTEM\CurrentControlSet\Enum (not shown to the user).
	public string Key { get; set; }

	public string Name { get; set; }

	public bool PowerSaving { get; set; }
}

public sealed class DeviceTuningState
{
	public bool Checked { get; set; }

	public List<TunedDevice> Devices { get; set; } = new List<TunedDevice>();

	// on | off | unknown
	public string UsbSuspend { get; set; } = "unknown";

	// Windows caps raw mouse input at 125 Hz while the window is not in front.
	public bool BackgroundCap { get; set; }

	// "Enhance pointer precision" (mouse acceleration) is on.
	public bool Acceleration { get; set; }

	// The pointer speed slider, 1..20; 10 is the middle notch, where Windows scales nothing.
	public int PointerSpeed { get; set; } = DeviceTuning.DefaultPointerSpeed;

	public bool PointerSpeedOff => PointerSpeed != DeviceTuning.DefaultPointerSpeed;

	// The settings that need no administrator rights.
	public bool NeedsUserFix => BackgroundCap || Acceleration || PointerSpeedOff;

	public bool NeedsAdminFix => Devices.Any((TunedDevice d) => d.PowerSaving) || UsbSuspend == "on";

	public bool NeedsWork => NeedsUserFix || NeedsAdminFix;
}
