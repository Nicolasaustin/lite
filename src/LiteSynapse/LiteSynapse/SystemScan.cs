using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace LiteSynapse;

// What the scanner looks for and what it is allowed to turn off.
//
// LiteSynapse is not an antivirus and never says a file is malware. It reports facts about what starts with
// Windows and what uses the network — where a program lives, whether it carries a signature and whose, whether
// it starts itself — and leaves the verdict to Microsoft Defender, which is already on the PC. "Worth a look"
// here means exactly that.
//
// Turning something off is only ever offered for things on the lists below. Nothing else gets a button, so the
// scanner can never suggest switching off a part of Windows that matters.
internal static class ScanRules
{
	// Folders a normal program does not start itself from. A signed installer may use Temp briefly, but
	// something that starts with Windows from there every time is worth a look.
	internal static readonly string[] LooseFolders = new string[6]
	{
		"\\appdata\\local\\temp\\", "\\windows\\temp\\", "\\downloads\\", "\\users\\public\\",
		"\\appdata\\roaming\\microsoft\\windows\\start menu\\programs\\startup\\", "\\programdata\\temp\\"
	};

	// Programs that keep uploading or downloading in the background. Matched on the process or service name.
	internal static readonly (string Match, string Title, string What)[] NetworkHogs = new (string, string, string)[]
	{
		("GoogleUpdate", "Google updater", "Checks for Chrome updates and downloads them in the background."),
		("AdobeARM", "Adobe updater", "Checks for Adobe updates in the background."),
		("AdobeUpdateService", "Adobe update service", "Downloads Adobe updates in the background."),
		("NvTelemetry", "NVIDIA telemetry", "Uploads driver usage data."),
		("NvContainerLocalSystem", "NVIDIA container", "Runs GeForce Experience's background jobs, including downloads."),
		("SteamService", "Steam service", "Steam's background service; Steam itself also downloads and uploads updates."),
		("EpicOnlineServices", "Epic Online Services", "Epic's background service."),
		("EpicWebHelper", "Epic web helper", "Epic launcher's background helper."),
		("Dropbox", "Dropbox", "Syncs files: can use your whole upload."),
		("OneDrive", "OneDrive", "Syncs files: can use your whole upload."),
		("GoogleDriveFS", "Google Drive", "Syncs files: can use your whole upload."),
		("Creative Cloud", "Adobe Creative Cloud", "Syncs files and downloads updates."),
		("Discord", "Discord", "Stays connected; its updater downloads in the background."),
		("RazerAppEngine", "Razer Synapse", "Signs in and checks for updates in the background.")
	};

	// ---- Pure helpers (tested) -------------------------------------------------------------------------

	// A program that starts itself from one of these folders is worth a look.
	internal static bool IsLooseLocation(string path)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return false;
		}
		string p = path.Replace('/', '\\').ToLowerInvariant();
		return LooseFolders.Any(p.Contains);
	}

	// What the scanner says about an item that starts with Windows. Never "malware": these are the facts that
	// make something worth checking with Defender.
	//  signed: the file carries an Authenticode signature; publisher: who signed it (may be empty).
	internal static string Flag(bool fileExists, bool signed, string publisher, string path)
	{
		if (!fileExists)
		{
			return "missing";
		}
		if (IsLooseLocation(path))
		{
			return "location";
		}
		if (!signed)
		{
			return "unsigned";
		}
		return "";
	}

	// Items with a flag are listed first, then by name.
	internal static int Rank(string flag)
	{
		switch (flag)
		{
		case "location":
			return 0;
		case "unsigned":
			return 1;
		case "missing":
			return 2;
		default:
			return 3;
		}
	}

	// Plain words for a flag, shown next to the item.
	internal static string FlagText(string flag)
	{
		switch (flag)
		{
		case "location":
			return "Starts from a temporary or downloads folder, which a normal program does not do.";
		case "unsigned":
			return "Carries no digital signature, so Windows cannot tell you who made it.";
		case "missing":
			return "Its program file is gone: the entry does nothing but is left over.";
		default:
			return "";
		}
	}

	// A known background uploader/downloader, or null.
	internal static (string Match, string Title, string What)? NetworkHog(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		string n = name.Trim();
		foreach (var row in NetworkHogs)
		{
			if (n.IndexOf(row.Match, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return row;
			}
		}
		return null;
	}

	// Windows Update's Delivery Optimization download mode (HKLM policy / DoSvc setting):
	// 0 = from Microsoft only, 1 = also PCs on your network, 2 = your network only (domain),
	// 3 = also PCs on the internet, 99 = simple, 100 = bypass. 1 and 3 upload to other PCs.
	internal static bool UploadsUpdates(int? mode)
	{
		// No value means the Windows default, which is 1 (LAN peering) on a home PC.
		int m = mode ?? 1;
		return m == 1 || m == 2 || m == 3;
	}

	internal static string DeliveryText(int? mode)
	{
		switch (mode ?? 1)
		{
		case 0:
			return "Downloads from Microsoft only. Nothing is uploaded.";
		case 1:
			return "Also shares updates with PCs on your own network, uploading to them.";
		case 2:
			return "Shares updates with PCs on your network (domain), uploading to them.";
		case 3:
			return "Shares updates with PCs on the internet: it uploads to strangers, which eats your upload.";
		case 99:
			return "Simple mode: downloads from Microsoft only.";
		case 100:
			return "Bypassed.";
		default:
			return "Set to an unknown mode.";
		}
	}
}

// One Windows setting from the catalog, with the state it is in on this PC right now.
public sealed class TweakRow
{
	public string Id { get; set; } = "";

	// input | sched | gpu | net | bg | sec
	public string Group { get; set; } = "";

	public string Title { get; set; } = "";

	// The thing the setting switches, and which side of it helps performance (see Tweaks.Tweak.Thing).
	public string Thing { get; set; } = "";

	public bool PerfOn { get; set; }

	// What it changes, and why someone would want it.
	public string What { get; set; } = "";

	// What it gives up. Empty when it gives up nothing.
	public string Cost { get; set; } = "";

	// safe | caution | risky
	public string Risk { get; set; } = Tweaks.Safe;

	// Where the setting comes from (see Tweaks.SourceText).
	public string Source { get; set; } = "lite";

	public bool NeedsReboot { get; set; }

	public bool NeedsAdmin { get; set; }

	// on | off | part | other | unknown. "part" is a setting another tool applied only halfway.
	public string State { get; set; } = "unknown";

	// Windows would not say, so State is what LiteSynapse last set (see PerfLedger).
	public bool Remembered { get; set; }

	// LiteSynapse changed it since this boot and it takes effect after a restart.
	public bool Restart { get; set; }
}

// One thing the scan found.
public sealed class ScanItem
{
	// startup | service | network
	public string Kind { get; set; } = "startup";

	// What the window shows as the name.
	public string Name { get; set; } = "";

	// Where it lives (a program path, or a service name).
	public string Path { get; set; } = "";

	// Who signed the file, "" when it carries no signature.
	public string Publisher { get; set; } = "";

	// "" | unsigned | location | missing (see ScanRules.Flag). Never a claim that something is malware.
	public string Flag { get; set; } = "";

	// What it costs while it runs.
	public string Costs { get; set; } = "";

	// What the user gives up by turning it off.
	public string Lose { get; set; } = "";

	// on | off | running | stopped | auto | manual | disabled
	public string State { get; set; } = "on";

	// LiteSynapse may turn this one off (a Run value, or a service from the catalog).
	public bool CanDisable { get; set; }

	// It is off now and LiteSynapse can put it back.
	public bool CanEnable { get; set; }

	// safe | caution | risky, for services from the catalog.
	public string Risk { get; set; } = Tweaks.Safe;

	// The Run value's name, or the service name: what the turn-off step works on.
	public string Id { get; set; } = "";

	// HKCU | HKLM: which Run key the value is in.
	public string Scope { get; set; } = "";

	// It holds a network connection right now.
	public bool Network { get; set; }
}

public sealed class ScanResult
{
	public bool Checked { get; set; }

	// on | off | unknown
	public string Defender { get; set; } = "unknown";

	public List<ScanItem> Items { get; set; } = new List<ScanItem>();

	// Windows Update's Delivery Optimization mode, and whether it uploads to other PCs.
	public int DeliveryMode { get; set; } = 1;

	public bool UploadsUpdates { get; set; }

	public string DeliveryText { get; set; } = "";

	// Every Windows setting the Performance page offers, with the state it is in now.
	public List<TweakRow> Tweaks { get; set; } = new List<TweakRow>();

	public int Flagged => Items.Count(i => i.Flag.Length > 0);
}

// Runs the scan on Windows and turns things off. Registry and service reads plus one signature check per
// start-up program: about a second on a normal PC, and only when the user asks for it.
internal static class SystemScanner
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const string ServicesKey = "SYSTEM\\CurrentControlSet\\Services";

	private const string DeliveryPolicy = "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization";

	private const string DeliverySetting = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DeliveryOptimization\\Settings";

	public static ScanResult Check()
	{
		ScanResult r = new ScanResult();
		try
		{
			r.Defender = DefenderState();
		}
		catch (Exception ex)
		{
			Cfg.Log("scan (defender): " + ex.Message);
		}
		try
		{
			int? mode = DeliveryMode();
			r.DeliveryMode = mode ?? 1;
			r.UploadsUpdates = ScanRules.UploadsUpdates(mode);
			r.DeliveryText = ScanRules.DeliveryText(mode);
		}
		catch (Exception ex2)
		{
			Cfg.Log("scan (delivery optimization): " + ex2.Message);
		}
		HashSet<string> online = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		try
		{
			online = Net.ProcessesWithConnections();
		}
		catch (Exception ex3)
		{
			Cfg.Log("scan (connections): " + ex3.Message);
		}
		try
		{
			r.Items.AddRange(StartupItems());
		}
		catch (Exception ex4)
		{
			Cfg.Log("scan (startup): " + ex4.Message);
		}
		try
		{
			r.Items.AddRange(ServiceItems(online));
		}
		catch (Exception ex5)
		{
			Cfg.Log("scan (services): " + ex5.Message);
		}
		foreach (ScanItem item in r.Items)
		{
			string exe = System.IO.Path.GetFileNameWithoutExtension(item.Path ?? "");
			if (exe.Length > 0 && online.Contains(exe))
			{
				item.Network = true;
			}
		}
		r.Items.Sort((ScanItem a, ScanItem b) =>
		{
			int byFlag = ScanRules.Rank(a.Flag).CompareTo(ScanRules.Rank(b.Flag));
			return (byFlag != 0) ? byFlag : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
		});
		try
		{
			r.Tweaks = TweakRows();
		}
		catch (Exception ex6)
		{
			Cfg.Log("scan (windows settings): " + ex6.Message);
		}
		r.Checked = true;
		int applied = r.Tweaks.Count((TweakRow t) => t.State == "on");
		Cfg.Log($"system scan: {r.Items.Count} item(s), {r.Flagged} worth a look, " +
			$"{applied}/{r.Tweaks.Count} windows setting(s) on, Defender {r.Defender}");
		return r;
	}

	// on | off | unknown. Read from the registry; policy or tamper protection can hide it, and then it is
	// reported as unknown rather than guessed at.
	private static string DefenderState()
	{
		using RegistryKey policy = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Policies\\Microsoft\\Windows Defender");
		if (policy?.GetValue("DisableAntiSpyware") is int off && off != 0)
		{
			return "off";
		}
		using RegistryKey rtp = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows Defender\\Real-Time Protection");
		if (rtp?.GetValue("DisableRealtimeMonitoring") is int rt)
		{
			return (rt != 0) ? "off" : "on";
		}
		using RegistryKey def = Registry.LocalMachine.OpenSubKey("SOFTWARE\\Microsoft\\Windows Defender");
		return (def != null) ? "on" : "unknown";
	}

	private static int? DeliveryMode()
	{
		using (RegistryKey p = Registry.LocalMachine.OpenSubKey(DeliveryPolicy))
		{
			if (p?.GetValue("DODownloadMode") is int v)
			{
				return v;
			}
		}
		using RegistryKey s = Registry.LocalMachine.OpenSubKey(DeliverySetting);
		return (s?.GetValue("DownloadMode") is int w) ? w : null;
	}

	// Everything in the Run keys plus the two Startup folders.
	private static List<ScanItem> StartupItems()
	{
		List<ScanItem> list = new List<ScanItem>();
		foreach ((RegistryHive hive, string scope) in new[] { (RegistryHive.CurrentUser, "HKCU"), (RegistryHive.LocalMachine, "HKLM") })
		{
			foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
			{
				using RegistryKey root = RegistryKey.OpenBaseKey(hive, view);
				using RegistryKey run = root.OpenSubKey(RunKey);
				if (run == null)
				{
					continue;
				}
				foreach (string name in run.GetValueNames())
				{
					// The key's own unnamed default value is not a start-up entry.
					if (string.IsNullOrWhiteSpace(name))
					{
						continue;
					}
					if (list.Any((ScanItem i) => i.Id == name && i.Scope == scope))
					{
						continue;
					}
					// Per item: an unreadable value or a Task Manager switch that will not open loses that one
					// row, not the whole list.
					try
					{
						string command = run.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
						ScanItem item = Describe(name, command, scope);
						// LiteSynapse does not put itself on the list.
						if (!string.Equals(name, "LiteSynapse", StringComparison.OrdinalIgnoreCase))
						{
							list.Add(item);
						}
					}
					catch (Exception ex)
					{
						Cfg.Log($"scan (startup {scope}\\{name}): " + ex.Message);
					}
				}
			}
		}
		foreach (Environment.SpecialFolder folder in new[] { Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup })
		{
			string dir = Environment.GetFolderPath(folder);
			if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
			{
				continue;
			}
			// One unreadable Startup folder must not take the Run entries with it.
			string[] files;
			try
			{
				files = Directory.GetFiles(dir);
			}
			catch (Exception ex)
			{
				Cfg.Log("scan (startup folder " + dir + "): " + ex.Message);
				continue;
			}
			foreach (string file in files)
			{
				string name = System.IO.Path.GetFileNameWithoutExtension(file);
				if (string.Equals(name, "LiteSynapse", StringComparison.OrdinalIgnoreCase))
				{
					continue;
				}
				list.Add(new ScanItem
				{
					Kind = "startup",
					Name = name,
					Id = file,
					Scope = "folder",
					Path = file,
					Costs = "Starts with Windows from the Startup folder.",
					Lose = "It no longer starts by itself. You can still open it yourself.",
					State = "on",
					CanDisable = false,
					Flag = ScanRules.IsLooseLocation(file) ? "location" : ""
				});
			}
		}
		return list;
	}

	// One Run value: where its program is, who signed it, and whether Task Manager has it turned off.
	private static ScanItem Describe(string name, string command, string scope)
	{
		string path = RazerLeftovers.BinaryPath(Environment.ExpandEnvironmentVariables(command ?? ""));
		bool exists = false;
		try
		{
			exists = path.Length > 0 && File.Exists(path);
		}
		catch
		{
		}
		string publisher = exists ? Signer(path) : "";
		string state = (scope == "HKCU") ? StartupApproved.State(name) : "enabled";
		return new ScanItem
		{
			Kind = "startup",
			Name = name,
			Id = name,
			Scope = scope,
			Path = path,
			Publisher = publisher,
			Flag = ScanRules.Flag(exists, publisher.Length > 0, publisher, path),
			Costs = "Starts with Windows" + ((scope == "HKLM") ? " for every account." : "."),
			Lose = "It no longer starts by itself. You can still open it yourself.",
			State = (state == "disabled") ? "off" : "on",
			// Turning a Run value off is what Task Manager's Startup apps does, and only for this account.
			CanDisable = scope == "HKCU" && state != "disabled",
			CanEnable = scope == "HKCU" && state == "disabled"
		};
	}

	// Who signed the file ("" when it carries no signature). This reads the signature's subject; it does not
	// check the certificate chain, so it says who claims to have signed, not that Windows trusts it.
	private static string Signer(string path)
	{
		try
		{
			using X509Certificate cert = X509Certificate.CreateFromSignedFile(path);
			string subject = cert.Subject ?? "";
			foreach (string part in subject.Split(','))
			{
				string t = part.Trim();
				if (t.StartsWith("CN=", StringComparison.OrdinalIgnoreCase))
				{
					return t.Substring(3).Trim('"');
				}
			}
			return subject;
		}
		catch
		{
			return "";
		}
	}

	// The services in the catalog, plus any background uploader we know by name. Each row says which start
	// type Windows ships, so a service that is off can be put back from the same list.
	private static List<ScanItem> ServiceItems(HashSet<string> online)
	{
		List<ScanItem> list = new List<ScanItem>();
		using RegistryKey services = Registry.LocalMachine.OpenSubKey(ServicesKey);
		if (services == null)
		{
			return list;
		}
		foreach (string name in services.GetSubKeyNames())
		{
			ServiceCatalog.Row row = ServiceCatalog.Find(name);
			var hog = ScanRules.NetworkHog(name);
			// A background uploader that is also one Windows must keep is reported, never offered.
			if (row == null && (!hog.HasValue || ServiceCatalog.IsProtected(name)))
			{
				continue;
			}
			try
			{
				using RegistryKey k = services.OpenSubKey(name);
				if (k == null)
				{
					continue;
				}
				int start = (k.GetValue("Start") is int st) ? st : 3;
				string image = k.GetValue("ImagePath", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
				string state = (start == 4) ? "disabled" : ((start == 3) ? "manual" : "auto");
				list.Add(new ScanItem
				{
					Kind = "service",
					Name = row?.Title ?? (hog?.Title + " (service)"),
					Id = name,
					Scope = "service",
					Path = RazerLeftovers.BinaryPath(image),
					Costs = row?.Costs ?? hog?.What ?? "",
					Lose = row?.Lose ?? "It stops running in the background.",
					Risk = row?.Risk ?? Tweaks.Caution,
					State = state,
					CanDisable = row != null && start != 4,
					// Nothing to put back when the start type Windows ships is "disabled" too: offering it
					// would write the value it already has and then report a change that did not happen.
					CanEnable = row != null && start == 4 && row.Default != ServiceCatalog.Disabled,
					Network = hog.HasValue || online.Contains(name)
				});
			}
			catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
			{
			}
		}
		return list;
	}

	// The whole catalog, each entry with the state this PC is in. Reading is registry-only and takes a few
	// milliseconds. Entries that have no registry values of their own (bcdedit, the power plan, netsh, the
	// network walks) are read the way Windows itself reports them where it will (see Probe), and otherwise
	// show what LiteSynapse last set, flagged as such - never a blank.
	private static List<TweakRow> TweakRows()
	{
		List<TweakRow> rows = new List<TweakRow>();
		PerfLedger ledger = PerfLedger.Load();
		DateTime boot = PerfLedger.BootUtc;
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			string read = Tweaks.StateOf(t, ReadValue);
			bool running = false;
			if (read == "unknown")
			{
				(string probed, bool isRunning) = Probe(t);
				read = probed ?? "unknown";
				running = isRunning;
			}
			(string state, bool remembered, bool restart) = PerfLedger.Resolve(read, ledger.Get(t.Id), t.NeedsReboot, running, boot);
			rows.Add(new TweakRow
			{
				Id = t.Id,
				Group = t.Group,
				Title = t.Title,
				Thing = t.Thing,
				PerfOn = t.PerfOn,
				What = t.What,
				Cost = t.Cost,
				Risk = t.Risk,
				Source = t.Source,
				NeedsReboot = t.NeedsReboot,
				NeedsAdmin = t.NeedsAdmin,
				State = state,
				Remembered = remembered,
				Restart = restart
			});
		}
		return rows;
	}

	private const string TcpInterfaces = "SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\\Interfaces";

	private const string NetClass = "SYSTEM\\CurrentControlSet\\Control\\Class\\{4d36e972-e325-11ce-bfc1-08002be10318}";

	private const string PowerKey = "SYSTEM\\CurrentControlSet\\Control\\Power";

	private const string PowerSchemesKey = "SYSTEM\\CurrentControlSet\\Control\\Power\\User\\PowerSchemes";

	// The HPET's frequency. When useplatformclock forces it as the system clock, QueryPerformanceFrequency
	// reports exactly this; the invariant TSC Windows uses otherwise reports 10 MHz.
	private const long HpetFrequency = 14318180;

	[DllImport("kernel32.dll")]
	private static extern int GetSystemDEPPolicy();

	[DllImport("kernel32.dll")]
	private static extern bool QueryPerformanceFrequency(out long frequency);

	// Reads a command-only setting the way Windows reports it, without administrator rights. Returns the
	// state (on = the setting is applied), or null when Windows will not say; running is true when the answer
	// describes the running boot rather than the stored configuration (see PerfLedger.Resolve).
	private static (string State, bool Running) Probe(Tweaks.Tweak t)
	{
		try
		{
			switch (t.Id)
			{
			case "powerplan":
			{
				using RegistryKey k = Registry.LocalMachine.OpenSubKey(PowerSchemesKey);
				return (k?.GetValue("ActivePowerScheme") is string active)
					? (string.Equals(active, Tweaks.PlanGuid, StringComparison.OrdinalIgnoreCase) ? "on" : "off", false)
					: (null, false);
			}
			case "hibernate":
			{
				using RegistryKey k = Registry.LocalMachine.OpenSubKey(PowerKey);
				return (k?.GetValue("HibernateEnabled") is int h) ? ((h == 0) ? "on" : "off", false) : (null, false);
			}
			case "nagle":
				// The helper writes both values on every interface, so "on" is every interface having both.
				return (Count(TcpInterfaces, (RegistryKey k) => true,
					(RegistryKey k) => k.GetValue("TcpAckFrequency") is int a && a == 1 && k.GetValue("TCPNoDelay") is int d && d == 1), false);
			case "nicpower":
				// The helper writes PnPCapabilities = 24 on every adapter that has a driver description.
				return (Count(NetClass, (RegistryKey k) => k.GetValue("DriverDesc") != null,
					(RegistryKey k) => k.GetValue("PnPCapabilities") is int p && p == 24), false);
			case "dep":
				// 0 AlwaysOff (applied), 2 OptIn (Windows' default), 1 AlwaysOn / 3 OptOut set by something else.
				return (GetSystemDEPPolicy() switch
				{
					0 => "on",
					2 => "off",
					_ => "other"
				}, true);
			case "platformclock":
				return QueryPerformanceFrequency(out long f) ? ((f == HpetFrequency) ? "off" : "on", true) : (null, false);
			}
		}
		catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException
			|| ex is EntryPointNotFoundException || ex is DllNotFoundException)
		{
		}
		return (null, false);
	}

	// on when every key the filter picks matches, off when none does, part for a mix, null when no key could
	// be read at all.
	private static string Count(string parent, Func<RegistryKey, bool> pick, Func<RegistryKey, bool> match)
	{
		using RegistryKey root = Registry.LocalMachine.OpenSubKey(parent);
		if (root == null)
		{
			return null;
		}
		int all = 0;
		int hit = 0;
		foreach (string name in root.GetSubKeyNames())
		{
			try
			{
				using RegistryKey k = root.OpenSubKey(name);
				if (k == null || !pick(k))
				{
					continue;
				}
				all++;
				if (match(k))
				{
					hit++;
				}
			}
			catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
			{
			}
		}
		if (all == 0)
		{
			return null;
		}
		return (hit == all) ? "on" : ((hit == 0) ? "off" : "part");
	}

	// One registry value for Tweaks.StateOf; null when the key or the value is not there, or cannot be read.
	private static object ReadValue(Tweaks.Reg r)
	{
		try
		{
			RegistryKey root = (r.Hive == "HKCU") ? Registry.CurrentUser : Registry.LocalMachine;
			using RegistryKey k = root.OpenSubKey(r.Key);
			return k?.GetValue(r.Name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
		}
		catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
		{
			return null;
		}
	}

	// ---- Turning things on and off --------------------------------------------------------------------

	// One call for everything the Performance page can change: start-up entries, services and Windows
	// settings, each with its own on/off. Only what the user ticked is touched - there is no "apply all".
	//
	// Every choice carries the KIND of thing it is ("start", "svc" or "tweak") as well as its id, and is
	// dispatched on that kind alone. Matching a bare id against all three in turn was a real hole: a start-up
	// entry's id is a Run value name read straight out of the registry, and any program can create
	// HKCU\...\Run\accel or \Run\Spooler without administrator rights. Ticking "Pointer acceleration off"
	// then found the start-up entry first and reported "already like that" without applying the tweak, and
	// ticking a service could disable a start-up program the user never ticked while leaving the service
	// running.
	//
	// Start-up entries are this account's own (Task Manager's switch, no elevation). Services and settings go
	// into one elevated script, so the whole selection costs at most one consent prompt - except the settings
	// that live in this account's registry, which need none.
	public static string Apply(IReadOnlyList<(string Kind, string Id, bool On)> choices)
	{
		if (choices == null || choices.Count == 0)
		{
			return "Nothing was selected.";
		}
		ScanResult before = Check();
		List<string> done = new List<string>();
		List<string> refused = new List<string>();
		List<string> already = new List<string>();
		bool reboot = false;
		List<(string Service, bool On)> svc = new List<(string, bool)>();
		List<(string Id, bool On)> tweaks = new List<(string, bool)>();

		foreach ((string kind, string id, bool on) in choices)
		{
			switch (kind)
			{
			case "start":
			{
				ScanItem item = before.Items.FirstOrDefault((ScanItem i) =>
					i.Kind == "startup" && string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
				if (item == null || string.IsNullOrWhiteSpace(item.Id))
				{
					continue;
				}
				if (on ? !item.CanEnable : !item.CanDisable)
				{
					// Already in the state that was asked for. Saying so beats "nothing was changed",
					// which reads like a failure.
					already.Add(item.Name);
					continue;
				}
				try
				{
					if (on)
					{
						StartupApproved.Enable(item.Id);
					}
					else
					{
						StartupApproved.Disable(item.Id);
					}
					done.Add(item.Name + (on ? " ON" : " OFF"));
					Cfg.Log($"system scan: start-up \"{item.Id}\" turned {(on ? "on" : "off")}");
				}
				catch (Exception ex)
				{
					Cfg.Log("system scan (startup " + item.Id + "): " + ex.Message);
					refused.Add(item.Name);
				}
				continue;
			}
			case "svc":
			{
				ScanItem item = before.Items.FirstOrDefault((ScanItem i) =>
					i.Kind == "service" && string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
				if (item == null)
				{
					continue;
				}
				if (on ? item.CanEnable : item.CanDisable)
				{
					svc.Add((item.Id, on));
					done.Add(item.Name + (on ? " ON" : " OFF"));
				}
				else
				{
					already.Add(item.Name);
				}
				continue;
			}
			case "tweak":
			{
				Tweaks.Tweak t = Tweaks.Find(id);
				if (t == null)
				{
					continue;
				}
				tweaks.Add((t.Id, on));
				reboot |= t.NeedsReboot;
				continue;
			}
			}
		}

		// Values in this account's own registry need no administrator rights - and must not be written by an
		// elevated process either, because on a standard-user account the UAC prompt collects a different
		// account's credentials and HKCU: would then resolve to that administrator's profile. So the split is
		// by hive, not by entry: an entry owning both hives has each half run in the right place.
		// Each half reports which settings it applied in full (see Tweaks.Script), so the tab can show a setting
		// Windows will not describe afterwards as set, and a refused one is named rather than counted.
		HashSet<string> okUser = null;
		HashSet<string> okMachine = null;
		if (Tweaks.HasWork(tweaks, Tweaks.UserScope))
		{
			string result = ResultFile();
			try
			{
				RazerLeftovers.RunPowerShell(Tweaks.Script(tweaks, Tweaks.UserScope, result), 60000, out int lcode);
				Cfg.Log($"system scan: settings for this account -> exit code {lcode}");
				okUser = TakeResult(result);
				if (okUser.Count == 0)
				{
					Problem(lcode, refused, "the settings for this account");
				}
			}
			catch (Exception ex0)
			{
				Cfg.Log("system scan (this account): " + ex0.Message);
				refused.Add("the settings for this account");
			}
		}
		if (svc.Count > 0 || Tweaks.HasWork(tweaks, Tweaks.MachineScope))
		{
			string result = ResultFile();
			string script = Combine(ServiceCatalog.Script(svc), Tweaks.Script(tweaks, Tweaks.MachineScope, result));
			try
			{
				int code = RazerLeftovers.RunElevated(script, 180000);
				Cfg.Log($"system scan: {svc.Count} service(s), {tweaks.Count} windows setting(s) -> exit code {code}");
				okMachine = TakeResult(result);
				// Settings name themselves below; this line is for what they cannot account for (a service, a
				// timeout, a script that never started).
				bool namedAll = code > 0 && tweaks.Any(((string Id, bool On) x) =>
					Tweaks.HasWork(new[] { x }, Tweaks.MachineScope) && !okMachine.Contains(x.Id)) && svc.Count == 0;
				if (!namedAll)
				{
					Problem(code, refused, "the settings that need administrator rights");
				}
			}
			catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
			{
				Record(tweaks, okUser, null);
				bool any = done.Count > 0 || (okUser?.Count ?? 0) > 0;
				return "Cancelled: Windows did not get administrator permission, so " +
					(any ? "only the changes that do not need it were made." : "nothing was changed.");
			}
			catch (Exception ex2)
			{
				Cfg.Log("system scan: " + ex2.Message);
				Record(tweaks, okUser, null);
				return "Could not finish: " + ex2.Message;
			}
		}
		foreach ((string id, bool on) in tweaks)
		{
			Tweaks.Tweak t = Tweaks.Find(id);
			if (Applied(id, on, okUser, okMachine))
			{
				// Named by the thing and the side it is now on: "Pointer acceleration OFF".
				bool thingOn = on ? t.PerfOn : !t.PerfOn;
				done.Add(t.Thing + (thingOn ? " ON" : " OFF"));
			}
			else
			{
				refused.Add(t.Thing);
			}
		}
		Record(tweaks, okUser, okMachine);
		return Report(done, refused, already, reboot);
	}

	// A setting counts as applied when every half it has work in reported it in full. A half that never ran
	// (cancelled consent) reports nothing, so its settings stay unapplied.
	private static bool Applied(string id, bool on, HashSet<string> okUser, HashSet<string> okMachine)
	{
		(string, bool)[] one = new[] { (id, on) };
		bool user = !Tweaks.HasWork(one, Tweaks.UserScope) || (okUser != null && okUser.Contains(id));
		bool machine = !Tweaks.HasWork(one, Tweaks.MachineScope) || (okMachine != null && okMachine.Contains(id));
		return user && machine;
	}

	// What LiteSynapse set goes into the ledger, so a setting Windows will not describe still shows its state.
	private static void Record(List<(string Id, bool On)> tweaks, HashSet<string> okUser, HashSet<string> okMachine)
	{
		if (tweaks.Count == 0)
		{
			return;
		}
		PerfLedger ledger = PerfLedger.Load();
		DateTime now = DateTime.UtcNow;
		bool any = false;
		foreach ((string id, bool on) in tweaks)
		{
			if (Applied(id, on, okUser, okMachine))
			{
				ledger.Set(id, on, now);
				any = true;
			}
		}
		if (any)
		{
			ledger.Save();
		}
	}

	private static string ResultFile()
	{
		return Path.Combine(Cfg.Dir, "perf-result-" + Guid.NewGuid().ToString("N") + ".txt");
	}

	// Reads and deletes a script's result file. Missing (the script never got that far) is an empty set.
	private static HashSet<string> TakeResult(string file)
	{
		try
		{
			if (!File.Exists(file))
			{
				return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			}
			HashSet<string> ids = Tweaks.ReadResult(File.ReadAllText(file));
			File.Delete(file);
			return ids;
		}
		catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
		{
			Cfg.Log("system scan: result file: " + ex.Message);
			return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		}
	}

	// What a script's exit code means. It is the number of steps that failed, but the runner also uses -2 for
	// "it did not finish in time" and -1 for "it never started" - and those used to pass the `code > 0` test
	// and be reported as complete success, with the "Changed:" list having been built before the script ran.
	private static void Problem(int code, List<string> refused, string what)
	{
		if (code == 0)
		{
			return;
		}
		if (code == -2)
		{
			refused.Add(what + " (it did not finish in time and may still be running - check again in a moment)");
			return;
		}
		if (code == RazerLeftovers.Tampered)
		{
			refused.Add(what + " (the script file was changed before it ran, so it was not run)");
			return;
		}
		if (code < 0)
		{
			refused.Add(what + " (it could not be started)");
			return;
		}
		refused.Add($"{code} step(s) Windows would not apply");
	}

	// Two scripts into one, so the whole selection costs a single consent prompt. Each half counts its own
	// failures into $failed and the exit code carries the total.
	internal static string Combine(string first, string second)
	{
		string a = Strip(first);
		string b = Strip(second);
		return "$failed = 0\n" + a + b + "exit $failed\n";
	}

	private static string Strip(string script)
	{
		string body = script ?? "";
		const string head = "$failed = 0\n";
		if (body.StartsWith(head, StringComparison.Ordinal))
		{
			body = body.Substring(head.Length);
		}
		const string tail = "exit $failed\n";
		if (body.EndsWith(tail, StringComparison.Ordinal))
		{
			body = body.Substring(0, body.Length - tail.Length);
		}
		return body;
	}

	private static string Report(List<string> done, List<string> refused, List<string> already, bool reboot)
	{
		List<string> parts = new List<string>();
		if (done.Count > 0)
		{
			parts.Add("Changed: " + string.Join(", ", done) + ".");
		}
		if (already != null && already.Count > 0)
		{
			parts.Add("Already like that: " + string.Join(", ", already) + ".");
		}
		if (refused.Count > 0)
		{
			parts.Add("Not changed: " + string.Join(", ", refused) + ".");
		}
		if (parts.Count == 0)
		{
			return "Nothing was changed.";
		}
		if (reboot)
		{
			parts.Add("Restart the PC for all of it to take effect.");
		}
		return string.Join(" ", parts);
	}

	// Asks Microsoft Defender to scan one file or folder. LiteSynapse never judges a file itself.
	public static string DefenderScan(string path)
	{
		string target = (path ?? "").Trim();
		if (target.Length == 0 || !File.Exists(target))
		{
			return "That program's file is not there any more, so there is nothing to scan.";
		}
		try
		{
			string script = "Start-MpScan -ScanType CustomScan -ScanPath " + RazerLeftovers.Ps(target) + "\n" +
				"$t = Get-MpThreatDetection -ErrorAction SilentlyContinue | Where-Object { $_.Resources -like " +
				RazerLeftovers.Ps("*" + System.IO.Path.GetFileName(target) + "*") + " }\n" +
				"if ($t) { exit 9 }\n" + "exit 0\n";
			int code = RazerLeftovers.RunElevated(script, 180000);
			Cfg.Log($"defender scan of {target} -> exit code {code}");
			if (code == 9)
			{
				return "Microsoft Defender reported something for this file. Open Windows Security › Protection history to see what it found and what to do.";
			}
			if (code == 0)
			{
				return "Microsoft Defender scanned it and found nothing.";
			}
			if (code == -2)
			{
				return "The scan is still running. Look in Windows Security › Protection history in a moment.";
			}
			return $"The scan did not finish (exit code {code}). Run it from Windows Security instead.";
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return "Cancelled: Windows did not get administrator permission, so nothing was scanned.";
		}
		catch (Exception ex2)
		{
			return "Could not start the scan: " + ex2.Message;
		}
	}

	// Which programs hold a network connection right now (names without .exe).
	private static class Net
	{
		[DllImport("iphlpapi.dll", SetLastError = true)]
		private static extern uint GetExtendedTcpTable(nint table, ref int size, bool order, int af, int cls, int reserved);

		private const int AF_INET = 2;

		private const int TCP_TABLE_OWNER_PID_ALL = 5;

		public static HashSet<string> ProcessesWithConnections()
		{
			HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			int size = 0;
			GetExtendedTcpTable(IntPtr.Zero, ref size, order: false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
			if (size <= 0)
			{
				return names;
			}
			nint buffer = Marshal.AllocHGlobal(size);
			try
			{
				if (GetExtendedTcpTable(buffer, ref size, order: false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
				{
					return names;
				}
				int rows = Marshal.ReadInt32(buffer);
				// MIB_TCPROW_OWNER_PID: state, local addr, local port, remote addr, remote port, owning pid.
				const int rowSize = 24;
				Dictionary<int, string> byPid = Processes();
				for (int i = 0; i < rows; i++)
				{
					int pid = Marshal.ReadInt32(buffer, 4 + i * rowSize + 20);
					if (byPid.TryGetValue(pid, out string name))
					{
						names.Add(name);
					}
				}
			}
			finally
			{
				Marshal.FreeHGlobal(buffer);
			}
			return names;
		}

		private static Dictionary<int, string> Processes()
		{
			Dictionary<int, string> map = new Dictionary<int, string>();
			foreach (System.Diagnostics.Process p in System.Diagnostics.Process.GetProcesses())
			{
				using (p)
				{
					try
					{
						map[p.Id] = p.ProcessName;
					}
					catch
					{
					}
				}
			}
			return map;
		}
	}
}
