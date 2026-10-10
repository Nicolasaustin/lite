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

	// Services that are safe to turn off on a gaming PC, with what each costs and what the user gives up. The
	// scanner offers nothing outside this list.
	internal static readonly (string Service, string Title, string Costs, string Lose)[] Optional = new (string, string, string, string)[]
	{
		("DiagTrack", "Connected User Experiences and Telemetry",
			"Collects usage data and uploads it to Microsoft in the background, which uses your connection.",
			"Nothing you use. Some feedback and diagnostics features stop reporting."),
		("dmwappushservice", "Device Management WAP Push",
			"Part of the same telemetry path.",
			"Nothing on a home PC."),
		("SysMain", "SysMain (Superfetch)",
			"Reads programs into memory ahead of time; on an SSD it mostly means background disk work.",
			"Nothing noticeable on an SSD. On a hard disk, programs may open a little slower."),
		("WSearch", "Windows Search",
			"Indexes your files in the background: disk and CPU while you play.",
			"Searching in the Start menu and File Explorer gets slower and less complete."),
		("XblAuthManager", "Xbox Live Auth Manager",
			"Runs for the Xbox app and Game Pass.",
			"Xbox app, Game Pass and Xbox sign-in stop working. Leave it on if you use them."),
		("XblGameSave", "Xbox Live Game Save",
			"Syncs Xbox game saves to the cloud.",
			"Xbox cloud saves. Leave it on if you use Game Pass."),
		("XboxNetApiSvc", "Xbox Live Networking",
			"Networking for the Xbox app.",
			"Xbox app networking. Leave it on if you use it."),
		("Spooler", "Print Spooler",
			"Runs all the time even with no printer, and is a known target for attacks.",
			"Printing. Only turn it off if you do not print from this PC.")
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

	// A service LiteSynapse may offer to turn off, or null.
	internal static (string Service, string Title, string Costs, string Lose)? OptionalService(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return null;
		}
		foreach (var row in Optional)
		{
			if (string.Equals(row.Service, name.Trim(), StringComparison.OrdinalIgnoreCase))
			{
				return row;
			}
		}
		return null;
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

// One harmful setting found on the PC, and what putting it back costs.
public sealed class BadTweak
{
	// boot | mitigations | uac | priority | queues | timer | dwm | csrss | autotuning
	public string Id { get; set; } = "";

	public string Name { get; set; } = "";

	// What it does to the PC now.
	public string Harm { get; set; } = "";

	// What LiteSynapse would set it back to.
	public string Fix { get; set; } = "";

	// security | performance
	public string Kind { get; set; } = "performance";
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

	// LiteSynapse may turn this one off (a Run value, or a service on the allowlist).
	public bool CanDisable { get; set; }

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

	// Harmful settings another optimizer left behind.
	public List<BadTweak> Bad { get; set; } = new List<BadTweak>();

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
			r.Bad = FindBadTweaks();
		}
		catch (Exception ex6)
		{
			Cfg.Log("scan (bad tweaks): " + ex6.Message);
		}
		r.Checked = true;
		Cfg.Log($"system scan: {r.Items.Count} item(s), {r.Flagged} worth a look, {r.Bad.Count} harmful tweak(s), Defender {r.Defender}");
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
					if (list.Any((ScanItem i) => i.Id == name && i.Scope == scope))
					{
						continue;
					}
					string command = run.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
					ScanItem item = Describe(name, command, scope);
					// LiteSynapse does not put itself on the list.
					if (!string.Equals(name, "LiteSynapse", StringComparison.OrdinalIgnoreCase))
					{
						list.Add(item);
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
			foreach (string file in Directory.GetFiles(dir))
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
			CanDisable = scope == "HKCU" && state != "disabled"
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

	// The services on the allowlist, plus any background uploader we know by name.
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
			var optional = ScanRules.OptionalService(name);
			var hog = ScanRules.NetworkHog(name);
			if (!optional.HasValue && !hog.HasValue)
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
					Name = optional?.Title ?? (hog?.Title + " (service)"),
					Id = name,
					Scope = "service",
					Path = RazerLeftovers.BinaryPath(image),
					Costs = optional?.Costs ?? hog?.What ?? "",
					Lose = optional?.Lose ?? "It stops running in the background.",
					State = state,
					CanDisable = optional.HasValue && start != 4,
					Network = hog.HasValue || online.Contains(name)
				});
			}
			catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException || ex is IOException)
			{
			}
		}
		return list;
	}

	// Settings another optimizer left behind that are worth undoing. Registry only: the boot ones need
	// administrator rights to read, so they are cleared by the repair step whether or not they are there.
	private static List<BadTweak> FindBadTweaks()
	{
		List<BadTweak> bad = new List<BadTweak>();
		int? Value(string key, string name)
		{
			using RegistryKey k = Registry.LocalMachine.OpenSubKey(key);
			return (k?.GetValue(name) is int v) ? v : null;
		}
		if (BadTweaks.MitigationsOff(Value(BadTweaks.MemoryManagement, "FeatureSettingsOverride"), Value(BadTweaks.MemoryManagement, "FeatureSettingsOverrideMask")))
		{
			bad.Add(new BadTweak
			{
				Id = "mitigations",
				Kind = "security",
				Name = "CPU security fixes turned off",
				Harm = "Windows' protection against Spectre and Meltdown has been switched off to gain a few frames. Any program on the PC can read memory it should not, including passwords.",
				Fix = "Turn the protection back on (the Windows default)."
			});
		}
		if (BadTweaks.UacOff(Value(BadTweaks.PoliciesSystem, "EnableLUA")))
		{
			bad.Add(new BadTweak
			{
				Id = "uac",
				Kind = "security",
				Name = "User Account Control turned off",
				Harm = "Nothing asks permission before changing the PC any more, and every program you run has full rights. It does not gain performance.",
				Fix = "Turn User Account Control back on (needs a restart)."
			});
		}
		int? sep = Value(BadTweaks.PriorityControl, "Win32PrioritySeparation");
		if (BadTweaks.PrioritySeparationOdd(sep))
		{
			bad.Add(new BadTweak
			{
				Id = "priority",
				Name = $"Thread priority changed (Win32PrioritySeparation = {sep})",
				Harm = "A guide changed how Windows shares the CPU between the window in front and everything else. The values these guides use often make frame pacing worse, not better.",
				Fix = "Put it back to 2, the Windows desktop default."
			});
		}
		if (BadTweaks.QueueSizeOdd(Value(BadTweaks.MouseClass, "MouseDataQueueSize")) || BadTweaks.QueueSizeOdd(Value(BadTweaks.KeyboardClass, "KeyboardDataQueueSize")))
		{
			bad.Add(new BadTweak
			{
				Id = "queues",
				Name = "Mouse or keyboard queue size changed",
				Harm = "A guide made the buffer Windows uses for mouse and keyboard reports smaller. It does not lower latency, and a buffer that is too small drops input when the PC is busy.",
				Fix = "Put both back to 100, the Windows default."
			});
		}
		using (RegistryKey k = Registry.LocalMachine.OpenSubKey(BadTweaks.KernelKey))
		{
			if (k?.GetValue("GlobalTimerResolutionRequests") != null)
			{
				bad.Add(new BadTweak
				{
					Id = "timer",
					Name = "Global timer resolution forced",
					Harm = "Every program on the PC is given the 1 ms timer, which raises the timer interrupt rate and power use for everything, not just your game.",
					Fix = "Remove the override and let each program ask for what it needs."
				});
			}
		}
		using (RegistryKey k = Registry.LocalMachine.OpenSubKey(BadTweaks.Dwm))
		{
			if (k?.GetValue("OverlayTestMode") != null)
			{
				bad.Add(new BadTweak
				{
					Id = "dwm",
					Name = "Desktop compositor test mode",
					Harm = "A debugging switch inside Windows' window compositor was left on. It is not a supported setting and can break how frames are presented.",
					Fix = "Remove it."
				});
			}
		}
		using (RegistryKey k = Registry.LocalMachine.OpenSubKey(BadTweaks.Csrss + "\\PerfOptions"))
		{
			if (k != null)
			{
				bad.Add(new BadTweak
				{
					Id = "csrss",
					Name = "csrss.exe priority changed",
					Harm = "A guide raised the priority of a core Windows process. Starving the rest of Windows of CPU is a known cause of stutter and hangs.",
					Fix = "Remove the override."
				});
			}
		}
		return bad;
	}

	// ---- Turning things off ----------------------------------------------------------------------------

	// ids: Run value names (this account) and service names from the allowlist. delivery: stop Windows
	// uploading updates to other PCs. Returns a message for the window.
	public static string Apply(IReadOnlyList<string> ids, bool delivery)
	{
		ScanResult before = Check();
		List<ScanItem> wanted = before.Items
			.Where((ScanItem i) => i.CanDisable && ids != null && ids.Contains(i.Id, StringComparer.OrdinalIgnoreCase))
			.ToList();
		if (wanted.Count == 0 && !delivery)
		{
			return "Nothing was selected.";
		}
		List<string> done = new List<string>();
		// Start-up entries belong to this account: Task Manager's own switch, no administrator rights.
		foreach (ScanItem item in wanted.Where((ScanItem i) => i.Kind == "startup"))
		{
			try
			{
				StartupApproved.Disable(item.Id);
				done.Add(item.Name);
				Cfg.Log("system scan: start-up \"" + item.Id + "\" turned off");
			}
			catch (Exception ex)
			{
				Cfg.Log("system scan (startup " + item.Id + "): " + ex.Message);
			}
		}
		List<string> svc = wanted.Where((ScanItem i) => i.Kind == "service").Select((ScanItem i) => i.Id).ToList();
		if (svc.Count > 0 || delivery)
		{
			try
			{
				int code = RazerLeftovers.RunElevated(Script(svc, delivery), 60000);
				Cfg.Log($"system scan: {svc.Count} service(s){(delivery ? " + delivery optimization" : "")} -> exit code {code}");
				done.AddRange(wanted.Where((ScanItem i) => i.Kind == "service").Select((ScanItem i) => i.Name));
				if (delivery)
				{
					done.Add("Windows Update uploading to other PCs");
				}
			}
			catch (Win32Exception ex2) when (ex2.NativeErrorCode == 1223)
			{
				return Report(done, "The services were not changed: Windows did not get administrator permission.");
			}
			catch (Exception ex3)
			{
				Cfg.Log("system scan: " + ex3.Message);
				return Report(done, "Could not finish: " + ex3.Message);
			}
		}
		return Report(done, null);
	}

	private static string Report(List<string> done, string problem)
	{
		List<string> parts = new List<string>();
		if (done.Count > 0)
		{
			parts.Add("Turned off: " + string.Join(", ", done) + ".");
		}
		if (problem != null)
		{
			parts.Add(problem);
		}
		if (parts.Count == 0)
		{
			return "Nothing was changed.";
		}
		parts.Add("Sign out and back in for the start-up changes to take effect.");
		return string.Join(" ", parts);
	}

	// Disabled, then stopped, so nothing starts them again in between.
	internal static string Script(IReadOnlyList<string> services, bool delivery)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("$failed = 0\n");
		if (services != null && services.Count > 0)
		{
			sb.Append("$names = @(" + string.Join(", ", services.Select(RazerLeftovers.Ps)) + ")\n");
			sb.Append("foreach ($n in $names) { sc.exe config $n start= disabled | Out-Null; if ($LASTEXITCODE -ne 0) { $failed++ }; sc.exe stop $n | Out-Null }\n");
		}
		if (delivery)
		{
			// 0 = download from Microsoft only, so Windows never uploads updates to other PCs.
			sb.Append("New-Item -Path 'HKLM:\\" + DeliveryPolicy.Replace("\\", "\\\\") + "' -Force -ErrorAction SilentlyContinue | Out-Null\n");
			sb.Append("New-ItemProperty -Path 'HKLM:\\" + DeliveryPolicy.Replace("\\", "\\\\") + "' -Name 'DODownloadMode' -PropertyType DWord -Value 0 -Force -ErrorAction SilentlyContinue | Out-Null\n");
		}
		sb.Append("exit $failed\n");
		return sb.ToString();
	}

	// Puts the chosen harmful settings back to their Windows default (one UAC prompt). The boot overrides are
	// always cleared when "boot" is asked for, because reading them needs administrator rights anyway.
	public static string Repair(IReadOnlyList<string> ids)
	{
		if (ids == null || ids.Count == 0)
		{
			return "Nothing was selected.";
		}
		try
		{
			int code = RazerLeftovers.RunElevated(BadTweaks.Script(ids), 60000);
			Cfg.Log($"bad tweaks repair: {string.Join(", ", ids)} -> exit code {code}");
			ScanResult after = Check();
			List<string> left = after.Bad.Where((BadTweak b) => ids.Contains(b.Id, StringComparer.OrdinalIgnoreCase)).Select((BadTweak b) => b.Name).ToList();
			string msg = (left.Count == 0)
				? "Put back to the Windows default. "
				: "Still set: " + string.Join(", ", left) + ". ";
			return msg + "Restart the PC so Windows picks all of it up.";
		}
		catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
		{
			return "Cancelled: Windows did not get administrator permission, so nothing was changed.";
		}
		catch (Exception ex2)
		{
			Cfg.Log("bad tweaks repair: " + ex2.Message);
			return "Could not run the repair: " + ex2.Message;
		}
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

// Settings that popular "optimizer" guides apply and that make a PC slower, less stable or less safe. Putting
// them back to the Windows default is the fix; every one here is a documented Windows setting.
//
// LiteSynapse never sets any of these: it only finds them and offers to undo them.
internal static class BadTweaks
{
	internal const string MemoryManagement = "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management";

	internal const string PriorityControl = "SYSTEM\\CurrentControlSet\\Control\\PriorityControl";

	internal const string KernelKey = "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel";

	internal const string PoliciesSystem = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System";

	internal const string MouseClass = "SYSTEM\\CurrentControlSet\\Services\\mouclass\\Parameters";

	internal const string KeyboardClass = "SYSTEM\\CurrentControlSet\\Services\\kbdclass\\Parameters";

	internal const string Dwm = "SOFTWARE\\Microsoft\\Windows\\Dwm";

	internal const string Csrss = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Image File Execution Options\\csrss.exe";

	// The boot settings a guide may have forced. Modern CPUs keep better time on their own; forcing these makes
	// timing worse and can cost frames.
	internal static readonly string[] BootOverrides = new string[4]
	{
		"useplatformclock", "useplatformtick", "disabledynamictick", "tscsyncpolicy"
	};

	// ---- Pure helpers (tested) -------------------------------------------------------------------------

	// Win32PrioritySeparation: 2 is the Windows desktop default ("Programs"). Guides set 26, 38 and the like.
	internal static bool PrioritySeparationOdd(int? value)
	{
		return value.HasValue && value.Value != 2;
	}

	// mouclass / kbdclass queue size: 100 is the Windows default. Guides lower it claiming less latency, with
	// no evidence, and a too-small queue drops input under load.
	internal static bool QueueSizeOdd(int? value)
	{
		return value.HasValue && value.Value != 100;
	}

	// FeatureSettingsOverride turns the CPU vulnerability mitigations (Spectre, Meltdown) off.
	internal static bool MitigationsOff(int? over, int? mask)
	{
		return over.HasValue || mask.HasValue;
	}

	// EnableLUA 0 turns User Account Control off: no more "do you want to allow" prompts, and every program
	// the user runs gets full rights.
	internal static bool UacOff(int? enableLua)
	{
		return enableLua.HasValue && enableLua.Value == 0;
	}

	// netsh output for "Receive Window Auto-Tuning Level". Anything but normal slows real downloads down.
	internal static bool AutoTuningBroken(string netshOutput)
	{
		if (string.IsNullOrWhiteSpace(netshOutput))
		{
			return false;
		}
		foreach (string line in netshOutput.Split('\n'))
		{
			if (line.IndexOf("Auto-Tuning Level", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return line.IndexOf("normal", StringComparison.OrdinalIgnoreCase) < 0;
			}
		}
		return false;
	}

	// The elevated repair. Every step puts a Windows default back; nothing new is turned on.
	internal static string Script(IReadOnlyList<string> ids)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("$failed = 0\n");
		bool Has(string id) => ids != null && ids.Contains(id, StringComparer.OrdinalIgnoreCase);
		if (Has("boot"))
		{
			// A value that is not set makes bcdedit complain; that is fine and must not count as a failure.
			foreach (string v in BootOverrides)
			{
				sb.Append("bcdedit.exe /deletevalue " + v + " 2>$null | Out-Null\n");
			}
		}
		if (Has("mitigations"))
		{
			sb.Append(Remove(MemoryManagement, "FeatureSettingsOverride"));
			sb.Append(Remove(MemoryManagement, "FeatureSettingsOverrideMask"));
		}
		if (Has("uac"))
		{
			sb.Append(Set(PoliciesSystem, "EnableLUA", 1));
		}
		if (Has("priority"))
		{
			sb.Append(Set(PriorityControl, "Win32PrioritySeparation", 2));
		}
		if (Has("queues"))
		{
			sb.Append(Set(MouseClass, "MouseDataQueueSize", 100));
			sb.Append(Set(KeyboardClass, "KeyboardDataQueueSize", 100));
		}
		if (Has("timer"))
		{
			sb.Append(Remove(KernelKey, "GlobalTimerResolutionRequests"));
		}
		if (Has("dwm"))
		{
			sb.Append(Remove(Dwm, "OverlayTestMode"));
		}
		if (Has("csrss"))
		{
			sb.Append("Remove-Item -Path 'HKLM:\\" + Csrss + "\\PerfOptions' -Recurse -Force -ErrorAction SilentlyContinue | Out-Null\n");
		}
		if (Has("autotuning"))
		{
			sb.Append("netsh.exe interface tcp set global autotuninglevel=normal | Out-Null\n");
			sb.Append("if ($LASTEXITCODE -ne 0) { $failed++ }\n");
		}
		sb.Append("exit $failed\n");
		return sb.ToString();
	}

	private static string Set(string key, string name, int value)
	{
		string path = "HKLM:\\" + key;
		return "try { New-Item -Path " + RazerLeftovers.Ps(path) + " -Force -ErrorAction Stop | Out-Null; " +
			"New-ItemProperty -Path " + RazerLeftovers.Ps(path) + " -Name " + RazerLeftovers.Ps(name) +
			" -PropertyType DWord -Value " + value + " -Force -ErrorAction Stop | Out-Null } catch { $failed++ }\n";
	}

	private static string Remove(string key, string name)
	{
		return "Remove-ItemProperty -Path " + RazerLeftovers.Ps("HKLM:\\" + key) + " -Name " + RazerLeftovers.Ps(name) +
			" -Force -ErrorAction SilentlyContinue | Out-Null\n";
	}
}
