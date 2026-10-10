using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace LiteSynapse;

// "Allow the computer to turn off this device to save power" for the Naga and the BlackWidow Lite, plus the
// active power plan's USB selective suspend (read only: the power plan is the user's). These are the two
// settings that decide whether Windows is allowed to let the mouse idle down between reports.
//
// Why this does not read the registry:
//
//   The per-device values (EnhancedPowerManagementEnabled, SelectiveSuspendEnabled, AllowIdleIrpInD3) live
//   under HKLM\SYSTEM\CurrentControlSet\Enum\USB\..., and the ACL on CurrentControlSet\Enum grants read to
//   SYSTEM only - an administrator is refused. An earlier version of this file walked that key, found
//   nothing, and logged "0 device key(s)" on a PC with both devices plugged in.
//
//   The supported way in is WMI: MSPower_DeviceEnable and MSPower_DeviceWakeEnable in root\WMI are the
//   classes behind those two checkboxes on the Power Management tab in Device Manager. They are readable and
//   writable by an administrator, and they apply immediately with no reboot.
//
// Everything else that used to live here - pointer acceleration, pointer speed, the background raw-input cap,
// the MMCSS network throttle and SystemResponsiveness - is now in the Tweaks catalog, where each one has its
// own checkbox and its own opposite. There is no second copy of those here.
//
// Nothing here touches a device that is not Razer's (VID_1532), and nothing is changed without the user asking.
internal static class DeviceTuning
{
	public const string UsbSuspendSubGroup = "2a737441-1930-4402-8d77-b2bebba308a3";

	public const string UsbSuspendSetting = "48e6b7a6-50f5-4782-a5d4-53bb8f07e226";

	private const string PowerSchemes = "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes";

	// ---- Pure helpers (tested) -------------------------------------------------------------------------

	// A WMI InstanceName (or a USB key name) that belongs to one of the two devices LiteSynapse drives.
	// Example: USB\VID_1532&PID_008D\6&1ad0a2c8&0&3_0
	internal static bool IsOurDevice(string instance)
	{
		if (string.IsNullOrWhiteSpace(instance) || instance.IndexOf("VID_1532", StringComparison.OrdinalIgnoreCase) < 0)
		{
			return false;
		}
		return instance.IndexOf("PID_008D", StringComparison.OrdinalIgnoreCase) >= 0
			|| instance.IndexOf("PID_0235", StringComparison.OrdinalIgnoreCase) >= 0;
	}

	// One line of the query's output: "True|USB\VID_1532&PID_008D\6&x&0&3_0|Razer Naga". Returns null for a
	// line that is not one of our devices or is malformed, so a stray line of PowerShell noise is ignored
	// rather than shown as a device.
	internal static TunedDevice ParseDevice(string line)
	{
		if (string.IsNullOrWhiteSpace(line))
		{
			return null;
		}
		string[] parts = line.Split('|');
		if (parts.Length < 2)
		{
			return null;
		}
		string enable = parts[0].Trim();
		string instance = parts[1].Trim();
		if (!IsOurDevice(instance))
		{
			return null;
		}
		bool unknown = enable == "?";
		bool? on = enable.Equals("True", StringComparison.OrdinalIgnoreCase) ? true
			: (enable.Equals("False", StringComparison.OrdinalIgnoreCase) ? false : (bool?)null);
		if (!on.HasValue && !unknown)
		{
			return null;
		}
		string name = (parts.Length > 2) ? parts[2].Trim() : "";
		// "USB Input Device" three times over says nothing about which device it is; lead with ours.
		string known = Describe(instance);
		return new TunedDevice
		{
			Instance = instance,
			Name = (name.Length == 0 || name == known) ? known : (known + " - " + name),
			// MSPower_DeviceEnable.Enable true = Windows is allowed to turn the device off to save power.
			PowerSaving = on ?? false,
			Known = !unknown
		};
	}

	// A name for a device whose FriendlyName could not be read.
	internal static string Describe(string instance)
	{
		if (instance != null && instance.IndexOf("PID_008D", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "Razer Naga Left-Handed Edition";
		}
		if (instance != null && instance.IndexOf("PID_0235", StringComparison.OrdinalIgnoreCase) >= 0)
		{
			return "Razer BlackWidow Lite";
		}
		return "Razer device";
	}

	// What the window shows for the power plan's USB selective suspend: on | off | unknown.
	internal static string SuspendState(int? value)
	{
		if (!value.HasValue)
		{
			return "unknown";
		}
		return (value.Value == 0) ? "off" : "on";
	}

	// ---- Reading -----------------------------------------------------------------------------------------

	// Lists the power-management checkbox for every Razer device, one line each, so one PowerShell start
	// covers the lot. -ErrorAction SilentlyContinue throughout: on a PC without the device, or without rights
	// to root\WMI, this prints nothing instead of failing, and the caller reports that it could not read
	// rather than claiming the devices are fine.
	// The first line is always READABLE|0 or READABLE|1, because the process exit code cannot carry that:
	// with -ErrorAction SilentlyContinue the script succeeds whether or not the query returned anything, so
	// "no devices" and "could not read root\WMI" would look identical from the outside - and the window would
	// tell the user to plug the mouse in when the real problem was that it had no rights to ask.
	internal const string QueryScript =
		"$ok = 0\n" +
		"$d = $null\n" +
		"try {\n" +
		"  $d = @(Get-CimInstance -Namespace root/WMI -ClassName MSPower_DeviceEnable -ErrorAction Stop)\n" +
		"  $ok = 1\n" +
		"} catch { }\n" +
		"\"READABLE|$ok\"\n" +
		"foreach ($x in $d) {\n" +
		"  if ($x.InstanceName -notlike '*VID_1532*') { continue }\n" +
		// The trailing _<n> is the power-management instance suffix, not part of the PnP instance id. It is
		// _0 on a single-function device and _1, _2 ... on a composite one.
		"  $id = $x.InstanceName -replace '_\\d+$', ''\n" +
		"  $n = ''\n" +
		"  try { $n = (Get-PnpDevice -InstanceId $id -ErrorAction SilentlyContinue).FriendlyName } catch { }\n" +
		"  \"$($x.Enable)|$($x.InstanceName)|$n\"\n" +
		"}\n" +
		// Without administrator rights root\WMI says nothing, but Plug and Play will still list the devices.
		// They are shown with their state unknown ("?") instead of not at all, so the page can still offer
		// the change - which runs elevated anyway, and reads the real state back as it does.
		// LISTED|1 says the listing itself ran, so "nothing listed" means nothing is plugged in rather than
		// "could not ask" - and an old administrator read is then not shown as if the devices were there.
		"if ($ok -eq 0) {\n" +
		"  try {\n" +
		"    $l = @(Get-PnpDevice -PresentOnly -ErrorAction Stop | Where-Object { $_.InstanceId -like 'USB\\VID_1532*' })\n" +
		"    'LISTED|1'\n" +
		"    foreach ($p in $l) {\n" +
		"      \"?|$($p.InstanceId)|$($p.FriendlyName)\"\n" +
		"    }\n" +
		"  } catch { 'LISTED|0' }\n" +
		"}\n";

	// Where the elevated steps leave what they read with administrator rights, so the page can show it
	// afterwards: the unelevated query cannot read root\WMI, and before this the Device page never showed a
	// device at all on an ordinary account - so there was never anything to tick.
	public static string SnapshotFile => System.IO.Path.Combine(Cfg.Dir, "device-power.txt");

	// The query, run inside an elevated script, with its output written to a file instead of the console
	// (an elevated process started through UAC has no console the app can read).
	internal static string QueryToFile(string file)
	{
		return "& {\n" + QueryScript + "} | Set-Content -LiteralPath " + RazerLeftovers.Ps(file) +
			" -Encoding UTF8 -ErrorAction SilentlyContinue\n";
	}

	// "VID_1532&PID_008D|<location>" for an instance id: the rows of one physical device on one port - the
	// composite parent and its interfaces share the product and the location - group together.
	internal static string Product(string instance)
	{
		string[] parts = (instance ?? "").Split('\\');
		if (parts.Length < 3)
		{
			return instance ?? "";
		}
		System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(parts[1], "VID_[0-9A-F]{4}&PID_[0-9A-F]{4}",
			System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		return m.Success ? m.Value : parts[1];
	}

	// A WMI InstanceName without its power-management suffix, so it compares equal to the Plug and Play
	// instance id the unelevated listing returns.
	internal static string Normalize(string instance)
	{
		return System.Text.RegularExpressions.Regex.Replace(instance ?? "", "_\\d+$", "");
	}

	// The READABLE line: true when the WMI query itself ran, false when it was refused. null for any other
	// line, so the caller can tell "not this line" from "it said no".
	internal static bool? ParseReadable(string line)
	{
		if (string.IsNullOrWhiteSpace(line) || !line.TrimStart().StartsWith("READABLE|", StringComparison.Ordinal))
		{
			return null;
		}
		return line.Trim().EndsWith("|1", StringComparison.Ordinal);
	}

	// The query's output as devices. Not readable unless the READABLE line said so.
	internal static DeviceTuningState Parse(string output)
	{
		DeviceTuningState s = new DeviceTuningState();
		foreach (string line in (output ?? "").Split('\n'))
		{
			string trimmed = line.Trim().TrimStart('\uFEFF');
			bool? readable = ParseReadable(trimmed);
			if (readable.HasValue)
			{
				s.Readable = readable.Value;
				continue;
			}
			if (trimmed == "LISTED|1")
			{
				s.Listed = true;
				continue;
			}
			TunedDevice d = ParseDevice(line);
			if (d != null && !s.Devices.Any((TunedDevice x) => string.Equals(Normalize(x.Instance), Normalize(d.Instance), StringComparison.OrdinalIgnoreCase)))
			{
				s.Devices.Add(d);
			}
		}
		return s;
	}

	// Fills in what an unelevated read could not see from the last read made with administrator rights.
	// Devices are matched on their instance id, so a device plugged in since then stays unknown rather than
	// borrowing another one's state.
	internal static void Merge(DeviceTuningState s, DeviceTuningState admin, DateTime adminAtUtc)
	{
		if (s == null || admin == null || !admin.Readable)
		{
			return;
		}
		if (s.Devices.Count == 0)
		{
			// Plug and Play listed nothing: nothing is plugged in, and an old read must not say otherwise.
			if (!s.Listed)
			{
				s.Devices.AddRange(admin.Devices);
			}
		}
		else
		{
			foreach (TunedDevice d in s.Devices.Where((TunedDevice x) => !x.Known))
			{
				TunedDevice a = admin.Devices.FirstOrDefault((TunedDevice x) =>
					string.Equals(Normalize(x.Instance), Normalize(d.Instance), StringComparison.OrdinalIgnoreCase));
				if (a != null)
				{
					d.PowerSaving = a.PowerSaving;
					d.Known = true;
					// Keep the WMI name: it is the one the elevated step matches on.
					d.Instance = a.Instance;
				}
			}
			// What Plug and Play lists but WMI has no power switch for (a HID child, say) has nothing to change -
			// but only for a device the read did see. A device it never saw (plugged in since, or moved to
			// another port, which gives it a new instance id) keeps its rows as unknown, so it can still be
			// switched and read back.
			HashSet<string> seen = new HashSet<string>(s.Devices.Where((TunedDevice x) => x.Known)
				.Select((TunedDevice x) => Product(x.Instance)), StringComparer.OrdinalIgnoreCase);
			s.Devices.RemoveAll((TunedDevice x) => !x.Known && seen.Contains(Product(x.Instance)));
		}
		s.AdminAtUtc = adminAtUtc;
	}

	public static DeviceTuningState Check()
	{
		DeviceTuningState s = new DeviceTuningState();
		try
		{
			string output = RazerLeftovers.RunPowerShell(QueryScript, 20000, out int code);
			// The script could not even be started (or timed out): nothing is known either way.
			s = Parse(output);
			if (!s.Readable)
			{
				try
				{
					string file = SnapshotFile;
					if (System.IO.File.Exists(file))
					{
						Merge(s, Parse(System.IO.File.ReadAllText(file)), System.IO.File.GetLastWriteTimeUtc(file));
					}
				}
				catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
				{
					Cfg.Log("device tuning (last admin read): " + ex.Message);
				}
			}
			if (code != 0)
			{
				Cfg.Log($"device tuning: the power-management query exited with {code}");
			}
			if (s.Devices.Count == 0)
			{
				Cfg.Log("device tuning: no Razer device answered the power-management query" +
					(s.Readable ? " (is it plugged in?)" : " (root\\WMI could not be read)"));
			}
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
		s.Checked = true;
		return s;
	}

	// The active power plan's "USB selective suspend setting" for mains power, or null when it cannot be read.
	private static int? ActiveSchemeUsbSuspend()
	{
		string active = ActiveScheme();
		if (active == null)
		{
			return null;
		}
		using RegistryKey schemes = Registry.LocalMachine.OpenSubKey(PowerSchemes);
		if (schemes == null)
		{
			return null;
		}
		using RegistryKey setting = schemes.OpenSubKey(active + "\\" + UsbSuspendSubGroup + "\\" + UsbSuspendSetting);
		if (setting?.GetValue("ACSettingIndex") is int v)
		{
			return v;
		}
		// A plan nobody changed has no override of its own: the value is the plan's shipped default.
		using RegistryKey defaults = Registry.LocalMachine.OpenSubKey("SYSTEM\\CurrentControlSet\\Control\\Power\\PowerSettings\\" +
			UsbSuspendSubGroup + "\\" + UsbSuspendSetting + "\\DefaultPowerSchemeValues\\" + active);
		return (defaults?.GetValue("ACSettingIndex") is int d) ? d : null;
	}

	private static string ActiveScheme()
	{
		using RegistryKey k = Registry.LocalMachine.OpenSubKey(PowerSchemes);
		return k?.GetValue("ActivePowerScheme") as string;
	}

	// Restarts the Naga's and the BlackWidow Lite's USB devices (the composite device, so every interface), which
	// starts Razer's filter driver on them afresh: a remap Razer Synapse left in it goes with it. pnputil
	// /restart-device (Windows 10 2004 and later), else disable and enable. Returns how many were restarted, -1 when
	// none could be found. LiteSynapse runs as administrator, so this needs no prompt.
	internal static string RestartScript(string pnputil)
	{
		return
			"$failed = 0\n" +
			"$ids = @(Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -match '^USB\\\\VID_1532&PID_(008D|0235)\\\\' } | ForEach-Object { $_.InstanceId })\n" +
			"foreach ($id in $ids) {\n" +
			"  & " + RazerLeftovers.Ps(pnputil) + " /restart-device $id | Out-Null\n" +
			"  if ($LASTEXITCODE -ne 0) {\n" +
			"    try { Disable-PnpDevice -InstanceId $id -Confirm:$false -ErrorAction Stop; Start-Sleep -Milliseconds 500; " +
			"Enable-PnpDevice -InstanceId $id -Confirm:$false -ErrorAction Stop } catch { $failed++ }\n" +
			"  }\n" +
			"}\n" +
			"Write-Output ('RESTARTED|' + $ids.Count + '|' + $failed)\n" +
			"exit $failed\n";
	}

	public static int RestartRazerDevices(out string problem)
	{
		problem = null;
		string script = RestartScript(System.IO.Path.Combine(Environment.SystemDirectory, "pnputil.exe"));
		try
		{
			string output = RazerLeftovers.RunPowerShell(script, 30000, out int code);
			System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(output ?? "", "RESTARTED\\|(\\d+)\\|(\\d+)");
			if (!m.Success)
			{
				problem = (code == -2) ? "it did not finish in time" : "it did not report back";
				return -1;
			}
			int found = int.Parse(m.Groups[1].Value);
			int failed = int.Parse(m.Groups[2].Value);
			Cfg.Log($"resync: restarted {found - failed} of {found} Razer USB device(s)");
			if (found == 0)
			{
				problem = "no Razer device was found";
				return -1;
			}
			if (failed > 0)
			{
				problem = failed + " device(s) could not be restarted";
			}
			return found - failed;
		}
		catch (Exception ex) when (ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is System.IO.IOException)
		{
			problem = ex.Message;
			Cfg.Log("resync (restart devices): " + ex.Message);
			return -1;
		}
	}

	// ---- Changing ----------------------------------------------------------------------------------------

	// The elevated script. instances: the WMI InstanceNames to change. allowPowerSaving true puts Windows'
	// own behaviour back; false keeps the device awake. The power plan's USB selective suspend is only read
	// (Check): the power plan is the user's, and nothing here writes it.
	//
	// Instance names are matched with -eq against the value WMI itself returned, and are quoted, so nothing
	// here depends on text the user typed.
	internal static string Script(IEnumerable<string> instances, bool allowPowerSaving)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("$failed = 0\n");
		string want = allowPowerSaving ? "$true" : "$false";
		foreach (string instance in instances ?? Enumerable.Empty<string>())
		{
			if (!IsOurDevice(instance))
			{
				continue;
			}
			// Compared without the power-management suffix, so an id from the unelevated Plug and Play listing
			// finds its WMI instance too.
			string q = RazerLeftovers.Ps(Normalize(instance));
			// Both checkboxes on the Power Management tab: may Windows turn the device off, and may the device
			// wake the PC. A device that cannot be turned off cannot wake anything either, so they move together.
			foreach (string cls in new[] { "MSPower_DeviceEnable", "MSPower_DeviceWakeEnable" })
			{
				sb.Append("try {\n");
				sb.Append($"  $i = Get-CimInstance -Namespace root/WMI -ClassName {cls} -ErrorAction Stop | " +
					$"Where-Object {{ ($_.InstanceName -replace '_\\d+$', '') -eq {q} }}\n");
				sb.Append($"  if ($i) {{ $i | Set-CimInstance -Property @{{ Enable = {want} }} -ErrorAction Stop }}\n");
				sb.Append("} catch { $failed++ }\n");
			}
		}
		sb.Append("exit $failed\n");
		return sb.ToString();
	}

	// Applies the user's choice in one elevated run, and reads the devices back in the same run.
	//
	// awake: devices to keep awake (Windows may not power them down); allow: devices handed back to Windows.
	// Nothing the user did not switch is touched, and the power plan never is. after is the state read back,
	// null when nothing ran.
	public static string Apply(IReadOnlyList<string> awake, IReadOnlyList<string> allow, out DeviceTuningState after)
	{
		after = null;
		int count = (awake?.Count ?? 0) + (allow?.Count ?? 0);
		if (count == 0)
		{
			return "Nothing was switched.";
		}
		try
		{
			DateTime started = DateTime.UtcNow.AddSeconds(-2);
			int code = RazerLeftovers.RunElevated(MixedScript(awake, allow, SnapshotFile), 60000);
			Cfg.Log($"device tuning: {awake?.Count ?? 0} kept awake, {allow?.Count ?? 0} handed back -> exit code {code}");
			after = Check();
			if (code == -2)
			{
				return "It did not finish in time and may still be running - check again in a moment. " + Summary(after);
			}
			if (code == RazerLeftovers.Tampered)
			{
				return "The script file was changed before it ran, so it was not run.";
			}
			// The read-back goes through a file; an old file means this run could not write it, and what the page
			// would show is the state from before the change.
			string stale = Fresh(started) ? "" : " The devices could not be read back, so what is shown is from before the change.";
			if (code > 0)
			{
				return $"{code} step(s) did not apply. " + Summary(after) + stale;
			}
			return Summary(after) + stale;
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return "Cancelled: Windows did not get administrator permission, so nothing was changed.";
		}
		catch (Exception ex2)
		{
			Cfg.Log("device tuning: " + ex2.Message);
			return "Could not change it: " + ex2.Message;
		}
	}

	private static bool Fresh(DateTime since)
	{
		try
		{
			return System.IO.File.Exists(SnapshotFile) && System.IO.File.GetLastWriteTimeUtc(SnapshotFile) >= since;
		}
		catch (Exception ex) when (ex is System.IO.IOException || ex is UnauthorizedAccessException)
		{
			return false;
		}
	}

	// Both directions and the read-back in one script, so a mixed change costs one consent prompt.
	internal static string MixedScript(IEnumerable<string> awake, IEnumerable<string> allow, string file)
	{
		return "$failed = 0\n" +
			Body(Script(awake, allowPowerSaving: false)) +
			Body(Script(allow, allowPowerSaving: true)) +
			QueryToFile(file) +
			"exit $failed\n";
	}

	private static string Body(string script)
	{
		const string head = "$failed = 0\n";
		const string tail = "exit $failed\n";
		string body = script ?? "";
		if (body.StartsWith(head, StringComparison.Ordinal))
		{
			body = body.Substring(head.Length);
		}
		if (body.EndsWith(tail, StringComparison.Ordinal))
		{
			body = body.Substring(0, body.Length - tail.Length);
		}
		return body;
	}

	// Reads the devices with administrator rights (one UAC prompt) into the snapshot the page falls back on.
	// Returns null when it ran, or what went wrong.
	public static string ReadAsAdmin()
	{
		try
		{
			DateTime started = DateTime.UtcNow.AddSeconds(-2);
			int code = RazerLeftovers.RunElevated("$failed = 0\n" + QueryToFile(SnapshotFile) + "exit $failed\n", 60000);
			Cfg.Log($"device tuning: read as administrator -> exit code {code}");
			if (code == 0 && !Fresh(started))
			{
				return "It ran but could not save what it read.";
			}
			return (code == 0) ? null : ((code == -2) ? "It did not finish in time." : $"It exited with {code}.");
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return "Cancelled: Windows did not get administrator permission, so nothing was read.";
		}
		catch (Exception ex2)
		{
			Cfg.Log("device tuning: " + ex2.Message);
			return "Could not read it: " + ex2.Message;
		}
	}

	internal static string Summary(DeviceTuningState s)
	{
		if (s == null || s.Devices.Count == 0)
		{
			// Now that the query says whether it could read at all, do not tell someone to plug in a mouse
			// that is already plugged in when the real problem was that Windows refused the question.
			return (s != null && !s.Readable)
				? "Windows would not let LiteSynapse read the devices' power settings. Start LiteSynapse as administrator and try again."
				: "No Razer device answered. Plug the mouse in and check again.";
		}
		if (s.Devices.All((TunedDevice d) => !d.Known))
		{
			return "Windows lists the devices but will not say how they are set without administrator rights. " +
				"Press Check as admin, or just switch them: the change reads them back when it runs.";
		}
		int sleeping = s.Devices.Count((TunedDevice d) => d.Known && d.PowerSaving);
		if (sleeping == 0)
		{
			return (s.Devices.Count == 1)
				? "Windows may no longer power the device down."
				: $"Windows may no longer power any of the {s.Devices.Count} devices down.";
		}
		return $"{sleeping} of {s.Devices.Count} device(s) may still be powered down by Windows.";
	}
}

// One USB device of ours and whether Windows may put it to sleep.
public sealed class TunedDevice
{
	// Its WMI InstanceName, e.g. USB\VID_1532&PID_008D\6&1ad0a2c8&0&3_0 (not shown to the user).
	public string Instance { get; set; }

	public string Name { get; set; }

	// Windows is allowed to turn this device off to save power. Meaningless when Known is false.
	public bool PowerSaving { get; set; }

	// False when only Plug and Play listed it and nothing with administrator rights has read it yet.
	public bool Known { get; set; } = true;
}

public sealed class DeviceTuningState
{
	public bool Checked { get; set; }

	// The power-management query ran. False means root\WMI could not be read, and an empty device list below
	// then says nothing about how the devices are set.
	public bool Readable { get; set; }

	// The unelevated Plug and Play listing ran (whether or not it found anything).
	public bool Listed { get; set; }

	public List<TunedDevice> Devices { get; set; } = new List<TunedDevice>();

	// The active power plan's USB selective suspend: on | off | unknown.
	public string UsbSuspend { get; set; } = "unknown";

	// When the device states came from the last read made with administrator rights (UTC), or null when
	// they were read just now.
	public DateTime? AdminAtUtc { get; set; }

	// Something here would keep the mouse awake if it were changed.
	public bool NeedsWork => Devices.Any((TunedDevice d) => d.Known && d.PowerSaving) || UsbSuspend == "on";
}
