using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace LiteSynapse;

// Every Windows setting the Performance page offers, as data.
//
// How this works, and why it is built this way:
//
//  * Each entry can be turned ON and turned OFF on its own. There is no "apply everything" and no
//    "put everything back": the window shows one checkbox per entry with its current state, and the user
//    ticks the ones they want. Nothing is changed that was not ticked.
//
//  * Every entry says where it comes from (Source) and what it costs (Cost). A setting is not called
//    "harmful" because it differs from the Windows default: the optimization guides change these on purpose
//    and several of them measurably help. Entries that give something up - security, a Windows feature,
//    stability on some machines - say so in Cost, and carry Risk so the window can mark them.
//
//  * Nothing here deletes a file, removes a driver, or touches the HID stack the mouse and keyboard run on
//    (mouclass, mouhid, kbdclass, kbdhid, mshidkmdf, HidUsb). Every change is a registry value, a service
//    start type, a power setting or a bcdedit value, and every one of them has a written-out opposite.
//
// Sources, as cited per entry:
//   fr33thy  - FR33THY's Ultimate Windows Optimization Guide (github.com/FR33THYFR33THY/Ultimate)
//   winutil  - Chris Titus Tech's WinUtil (github.com/ChrisTitusTech/winutil), config/tweaks.json
//   ms       - documented by Microsoft
//   lite     - LiteSynapse's own, for this mouse and keyboard
internal static class Tweaks
{
	// What a value should be set to, or Delete to remove it so Windows decides for itself again.
	internal const string Delete = "\u0000delete";

	// An Off that means "anything but On": a setting with one right value and no single wrong one (the
	// pointer-speed notch). Nothing is written for it, so such a setting is one-way - see Tweak.OneWay.
	internal const string NotOn = "\u0000noton";

	// Reg.Absent for a value whose absence means different things on different PCs (Windows picks per
	// driver or per install), so a missing value says nothing either way.
	internal const string AbsentUnknown = "\u0000absent-unknown";

	// A command prefix: run it, but do not count a non-zero exit code as a failure (see Tweak.OnCommands).
	internal const string Optional = "?";

	// Which half of a selection a step belongs in. HKCU values have to be written by the signed-in user:
	// on a standard-user account the UAC prompt collects an administrator's credentials, and an elevated
	// PowerShell's HKCU: is then that administrator's profile, not the user's. So an entry owning both hives
	// is split, and each half runs where it belongs.
	internal const string UserScope = "user";

	internal const string MachineScope = "machine";

	// Risk, for the badge the window shows and the warning before it applies.
	internal const string Safe = "safe";

	internal const string Caution = "caution";

	internal const string Risky = "risky";

	// Groups, in the order the window lists them.
	internal static readonly (string Id, string Title, string Blurb)[] Groups = new (string, string, string)[]
	{
		("input", "Mouse and keyboard input",
			"How Windows handles what the mouse and keyboard send. The settings with the most direct effect on aim."),
		("sched", "CPU and scheduling",
			"How Windows shares the processor between the game in front and everything behind it."),
		("gpu", "Graphics and presentation",
			"How a frame gets from the game to the monitor. Exclusive fullscreen and the flip model matter most."),
		("net", "Network",
			"Nothing here raises frame rate. These stop other software on the PC from taking the connection while you play."),
		("bg", "Background work",
			"Windows jobs that run while you play: indexing, prefetching, telemetry, update uploads."),
		("sec", "Security trade-offs",
			"Each of these buys frame rate by turning off a protection. They are real gains on CPU-bound machines and real losses in safety: read Cost before ticking one.")
	};

	// One registry value an entry owns. On/Off are what it is set to in each state; Delete removes it.
	internal sealed class Reg
	{
		public string Hive;        // HKLM | HKCU

		public string Key;

		public string Name;

		// dword | string | qword | keylist. "keylist" is a single REG_SZ holding "key=value;key=value;" that
		// Windows manages as well - DirectXUserGlobalSettings also carries the user's Auto HDR preference -
		// so only our own keys inside it are written, and the rest of the list is left alone.
		public string Kind;

		public string On;

		public string Off;

		// What Windows does when the value is not there at all, which is how most of these ship. null means
		// "the same as Off" (Off is Windows' default); AbsentUnknown means Windows decides per PC. Without
		// this a stock PC read as "set to something else" - Game Mode, for one, is on when its values are
		// missing, and the row said a tweak tool had changed it.
		public string Absent;

		// Other values that count as On. Another optimizer (FR33THY's script, WinUtil, a tweak pack) often
		// picks a different but equally good number - a mouse buffer of 20 rather than 16, a foreground boost
		// of 2A hex rather than 26 - and a PC tuned that way has done the job. It read "set by something else"
		// before, as if nothing had been done. A numeric value in [OnMin, OnMax], or any of OnAny, is On.
		public long? OnMin;

		public long? OnMax;

		public string[] OnAny;

		public Reg(string hive, string key, string name, string kind, string on, string off)
		{
			Hive = hive;
			Key = key;
			Name = name;
			Kind = kind;
			On = on;
			Off = off;
		}
	}

	internal sealed class Tweak
	{
		public string Id = "";

		public string Group = "";

		public string Title = "";

		// The thing this setting switches, named so that "ON" and "OFF" mean what they say: "Pointer
		// acceleration", not "Pointer acceleration off". The Performance tab shows whether the thing is on or
		// off and whether that is helping or hurting, so the name must never carry a direction of its own.
		public string Thing = "";

		// Can only be applied, not undone from here: its Off is NotOn ("any other value"), so there is no
		// value to put back. The Performance tab offers the switch only while it is not applied.
		public bool OneWay;

		// Which side of the thing helps performance. Applying this setting puts the thing on this side:
		// "Pointer acceleration" helps when OFF, "Game Mode" helps when ON.
		public bool PerfOn;

		// What it changes and why someone would want it.
		public string What = "";

		// What it gives up. Empty when it gives up nothing.
		public string Cost = "";

		public string Risk = Safe;

		public string Source = "lite";

		public bool NeedsReboot;

		// Needs admin rights (anything under HKLM, bcdedit, services, power settings).
		public bool NeedsAdmin;

		public Reg[] Values = Array.Empty<Reg>();

		// Extra commands, run after the registry values. Already-quoted PowerShell.
		//
		// A command may start with Optional ("?"), which means "run it, but do not count a non-zero exit as a
		// failure". That is for the steps whose job is to remove something: bcdedit /deletevalue on a value
		// that was never set, and powercfg /delete on a plan that is not there, both exit non-zero, and both
		// mean the setting is already in the state we asked for.
		public string[] OnCommands = Array.Empty<string>();

		public string[] OffCommands = Array.Empty<string>();
	}

	// ---- The catalog ---------------------------------------------------------------------------------

	private const string Mouse = "Control Panel\\Mouse";

	private const string SystemProfile = "SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile";

	private const string PriorityControl = "SYSTEM\\CurrentControlSet\\Control\\PriorityControl";

	private const string MemoryManagement = "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management";

	private const string Kernel = "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\kernel";

	private const string GameConfig = "System\\GameConfigStore";

	private const string GraphicsDrivers = "SYSTEM\\CurrentControlSet\\Control\\GraphicsDrivers";

	private const string MouClass = "SYSTEM\\CurrentControlSet\\Services\\mouclass\\Parameters";

	private const string KbdClass = "SYSTEM\\CurrentControlSet\\Services\\kbdclass\\Parameters";

	// The power plan LiteSynapse creates, kept at a fixed GUID so it can be found and deleted again.
	internal const string PlanGuid = "e7c0a1a7-11e5-4d39-9e4c-5a1e5e0d0001";

	// Ultimate Performance, duplicated to our GUID, then every setting that can idle something down turned off.
	// Setting GUIDs are Microsoft's documented ones (powercfg /q lists them).
	private static readonly string[] PowerPlanOn = new string[]
	{
		// Already there from a previous run: the duplicate fails and everything after it still applies to the
		// plan that exists, so this one does not count as a failure either.
		Optional + "powercfg.exe /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61 " + PlanGuid,
		// Processor: minimum state 100%, core parking off, no throttling.
		Ac("54533251-82be-4824-96c1-47b60b740d00", "893dee8e-2bef-41e0-89c6-b55d0929964c", "100"),
		Ac("54533251-82be-4824-96c1-47b60b740d00", "bc5038f7-23e0-4960-96da-33abaf5935ec", "100"),
		Ac("54533251-82be-4824-96c1-47b60b740d00", "0cc5b647-c1df-4637-891a-dec35c318583", "100"),
		Ac("54533251-82be-4824-96c1-47b60b740d00", "ea062031-0e34-4ff1-9b6d-eb1059334028", "100"),
		// USB: selective suspend off, no hub idle, no setting-off.
		Ac("2a737441-1930-4402-8d77-b2bebba308a3", "48e6b7a6-50f5-4782-a5d4-53bb8f07e226", "0"),
		Ac("2a737441-1930-4402-8d77-b2bebba308a3", "d4e98f31-5ffe-4ce1-be31-1b38b384c009", "0"),
		Ac("2a737441-1930-4402-8d77-b2bebba308a3", "0853a681-27c8-4100-a2fd-82013e970683", "0"),
		// PCI Express: link state power management off.
		Ac("501a4d13-42af-4429-9fd1-a8218c268e20", "ee12f906-d277-404b-b6da-e5fa1a576df5", "0"),
		// Hard disk: never spin down.
		Ac("0012ee47-9041-4b5d-9b77-535fba8b1442", "6738e2c4-e8a5-4a42-b16a-e040e769756e", "0"),
		// /setactive comes BEFORE the /change calls, because powercfg /change edits whichever scheme is
		// active. With it last, the three timeouts below landed on whatever plan was active instead - on a
		// first run that is Balanced, so our plan never got them (the monitor still blanked mid-round) and
		// the user's Balanced plan was left unable to sleep, with nothing to put it back.
		"powercfg.exe /setactive " + PlanGuid,
		// Display and sleep off, so nothing dims mid-round.
		"powercfg.exe /change monitor-timeout-ac 0",
		"powercfg.exe /change standby-timeout-ac 0",
		"powercfg.exe /change disk-timeout-ac 0"
	};

	private static string Ac(string group, string setting, string value)
	{
		return $"powercfg.exe /setacvalueindex {PlanGuid} {group} {setting} {value}";
	}

	internal static readonly Tweak[] Catalog = new Tweak[]
	{
		// ---- input ----------------------------------------------------------------------------------
		new Tweak
		{
			Id = "accel",
			Group = "input",
			Title = "Pointer acceleration off",
			Thing = "Pointer acceleration",
			PerfOn = false,
			What = "Turns off \"Enhance pointer precision\", so the same hand movement always moves the cursor the " +
				"same distance. Every aim guide starts here.",
			Source = "fr33thy",
			Values = new[]
			{
				new Reg("HKCU", Mouse, "MouseSpeed", "string", "0", "1"),
				new Reg("HKCU", Mouse, "MouseThreshold1", "string", "0", "6"),
				new Reg("HKCU", Mouse, "MouseThreshold2", "string", "0", "10")
			},
			OnCommands = new[] { "[LiteSynapse.Pointer]::Acceleration($false)" },
			OffCommands = new[] { "[LiteSynapse.Pointer]::Acceleration($true)" }
		},
		new Tweak
		{
			Id = "pointerspeed",
			Group = "input",
			Title = "Pointer speed at the middle notch (6/11)",
			Thing = "Pointer speed locked at 6/11 (1:1 aim)",
			PerfOn = true,
			What = "Puts the Windows pointer-speed slider in the middle, the only setting that does not scale or " +
				"drop counts the mouse sends. Any other notch multiplies them.",
			Source = "fr33thy",
			// Any other notch is the "off" side: there is no single value to put back, so this one only
			// goes one way, and a notch someone chose reads OFF (and hurting) rather than "set by something else".
			OneWay = true,
			Values = new[] { new Reg("HKCU", Mouse, "MouseSensitivity", "string", "10", NotOn) },
			OnCommands = new[] { "[LiteSynapse.Pointer]::Speed(10)" }
		},
		new Tweak
		{
			Id = "rawthrottle",
			Group = "input",
			Title = "Background raw-input cap off",
			Thing = "Background raw-input cap",
			PerfOn = false,
			What = "Windows 11 caps raw mouse input at 125 Hz for a window that is not in front. A 1000 Hz mouse " +
				"drops to 125 Hz whenever the game loses focus - alt-tabbed, a borderless overlay, a second monitor. " +
				"This removes the cap.",
			Cost = "Takes effect at the next sign-in.",
			Source = "fr33thy",
			NeedsReboot = true,
			Values = new[] { new Reg("HKCU", Mouse, "RawMouseThrottleEnabled", "dword", "0", Delete) }
		},
		new Tweak
		{
			Id = "mousequeue",
			Group = "input",
			Title = "Mouse buffer at 16 reports",
			Thing = "Smaller mouse buffer (16 reports)",
			PerfOn = true,
			What = "How many mouse reports the driver holds in non-paged memory before the system reads them. " +
				"Windows uses 100; the guides use 16 so a backlog cannot build up.",
			Cost = "Below 16 Windows starts dropping input. If the System log ever reports a ring-buffer overflow, " +
				"turn this back off.",
			Risk = Caution,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			Values = new[] { new Reg("HKLM", MouClass, "MouseDataQueueSize", "dword", "16", "100") { OnMin = 1, OnMax = 99 } }
		},
		new Tweak
		{
			Id = "kbdqueue",
			Group = "input",
			Title = "Keyboard buffer at 16 reports",
			Thing = "Smaller keyboard buffer (16 reports)",
			PerfOn = true,
			What = "The same buffer for the keyboard.",
			Cost = "Takes effect after a restart.",
			Risk = Caution,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			Values = new[] { new Reg("HKLM", KbdClass, "KeyboardDataQueueSize", "dword", "16", "100") { OnMin = 1, OnMax = 99 } }
		},
		new Tweak
		{
			Id = "keydelay",
			Group = "input",
			Title = "Keyboard repeat delay at the shortest setting",
			Thing = "Shortest key-repeat delay",
			PerfOn = true,
			What = "Shortens the pause before a held key starts repeating.",
			Source = "winutil",
			// REG_SZ, not a DWord: everything under Control Panel\Keyboard and Control Panel\Mouse is a
			// legacy win.ini-mapped string. Writing a DWord over Windows' own REG_SZ leaves a value of the
			// wrong type that Windows and the Control Panel slider both ignore.
			Values = new[] { new Reg("HKCU", "Control Panel\\Keyboard", "KeyboardDelay", "string", "0", "1") }
		},

		// ---- sched ----------------------------------------------------------------------------------
		new Tweak
		{
			Id = "priority",
			Group = "sched",
			Title = "Foreground boost (Win32PrioritySeparation = 26 hex)",
			Thing = "Foreground boost (Win32PrioritySeparation 26 hex)",
			PerfOn = true,
			What = "Short time slices with a 3:1 boost for the window in front. On Windows 10 and 11 the default (2) " +
				"already means exactly that, the same as the guides' 26 hex, so a stock PC reads ON. It reads OFF when " +
				"something set long or unboosted slices (a server-style value), and switching it ON puts 26 hex back.",
			Cost = "",
			Source = "fr33thy",
			NeedsAdmin = true,
			// One-way: the Windows default already counts as on, so there is nothing to switch back to.
			OneWay = true,
			// Short quantum with the 3:1 boost, whichever spelling: 2 (client default), 22, 26 and 2A hex.
			Values = new[] { new Reg("HKLM", PriorityControl, "Win32PrioritySeparation", "dword", "0x26", NotOn)
			{
				Absent = "2",
				OnAny = new[] { "2", "0x22", "0x2A" }
			} }
		},
		new Tweak
		{
			Id = "responsiveness",
			Group = "sched",
			Title = "No CPU reserved for background work",
			Thing = "CPU reserved for background work",
			PerfOn = false,
			What = "SystemResponsiveness is the share of the processor Windows keeps back for background tasks. " +
				"It ships at 20%; 10 gives most of it to the game. Audio keeps its own reservation either way.",
			Source = "ms",
			NeedsAdmin = true,
			Values = new[] { new Reg("HKLM", SystemProfile, "SystemResponsiveness", "dword", "10", "20") { OnMin = 0, OnMax = 10 } }
		},
		new Tweak
		{
			Id = "timerres",
			Group = "sched",
			Title = "System-wide 0.5 ms timer (GlobalTimerResolutionRequests)",
			Thing = "System-wide 0.5 ms timer requests",
			PerfOn = true,
			What = "Windows 11 made timer-resolution requests per-process, so one program asking for a 0.5 ms timer " +
				"no longer speeds up the clock for everything. This restores the old system-wide behaviour: any " +
				"program's request - a browser playing video, Discord - speeds up the clock for the whole PC, also " +
				"in the background. The game in front already gets the resolution it asks for either way.",
			Cost = "Takes effect after a restart. More timer interrupts on every core all day: higher idle power and heat, " +
				"and less boost headroom. Left out of FIX ALL - try it and judge by frame times.",
			Source = "fr33thy",
			Risk = Caution,
			NeedsAdmin = true,
			NeedsReboot = true,
			Values = new[] { new Reg("HKLM", Kernel, "GlobalTimerResolutionRequests", "dword", "1", Delete) }
		},
		new Tweak
		{
			Id = "dynamictick",
			Group = "sched",
			Title = "Dynamic tick off",
			Thing = "Dynamic tick",
			PerfOn = false,
			What = "Stops Windows from skipping timer interrupts while idle. Some machines get steadier frame " +
				"pacing from this; others see no change.",
			Cost = "A boot setting, so it needs a restart, and it is genuinely machine-dependent. Test it with " +
				"frame-time graphs rather than average FPS.",
			Risk = Caution,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			OnCommands = new[] { "bcdedit.exe /set disabledynamictick yes" },
			OffCommands = new[] { Optional + "bcdedit.exe /deletevalue disabledynamictick" }
		},
		new Tweak
		{
			Id = "platformclock",
			Group = "sched",
			Title = "HPET not forced as the system clock",
			Thing = "HPET forced as the system clock",
			PerfOn = false,
			What = "Leaves the platform clock alone so Windows picks the fastest timer the CPU offers (the " +
				"invariant TSC on anything modern) instead of being pinned to HPET, which is slower to read. " +
				"Turning this ON removes any useplatformclock / useplatformtick / tscsyncpolicy value another " +
				"tool set.",
			Cost = "Needs a restart. If your BIOS has HPET off as well, this changes nothing.",
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			OnCommands = new[]
			{
				Optional + "bcdedit.exe /deletevalue useplatformclock",
				Optional + "bcdedit.exe /deletevalue useplatformtick",
				Optional + "bcdedit.exe /deletevalue tscsyncpolicy"
			},
			OffCommands = new[] { "bcdedit.exe /set useplatformclock true" }
		},
		new Tweak
		{
			Id = "gamemode",
			Group = "sched",
			Title = "Game Mode on",
			Thing = "Game Mode",
			PerfOn = true,
			What = "Windows holds back driver installs and background work while a game is in front. Keep this on: " +
				"it is the one piece of Windows gaming support that does what it says.",
			Source = "winutil",
			Values = new[]
			{
				// Game Mode is on when these are missing, which is how Windows ships.
				new Reg("HKCU", "Software\\Microsoft\\GameBar", "AllowAutoGameMode", "dword", "1", "0") { Absent = "1" },
				new Reg("HKCU", "Software\\Microsoft\\GameBar", "AutoGameModeEnabled", "dword", "1", "0") { Absent = "1" }
			}
		},
		new Tweak
		{
			Id = "powerplan",
			Group = "sched",
			Title = "Low-latency power plan",
			Thing = "Low-latency power plan",
			PerfOn = true,
			What = "Builds a power plan from Ultimate Performance and sets it active: processor never below 100%, " +
				"core parking off, USB selective suspend off, PCIe link-state power management off, no disk or " +
				"monitor sleep. Clocks that drop while you play are a common cause of stutter.",
			Cost = "Higher idle power draw and a warmer PC, and the PC no longer goes to sleep by itself on mains power. " +
				"On a laptop, use it on mains only. Switching it off puts Windows' Balanced plan back.",
			Source = "fr33thy",
			NeedsAdmin = true,
			OnCommands = PowerPlanOn,
			OffCommands = new[]
			{
				"powercfg.exe /setactive scheme_balanced",
				Optional + "powercfg.exe /delete " + PlanGuid
			}
		},
		new Tweak
		{
			Id = "prefetch",
			Group = "sched",
			Title = "Prefetcher and memory compression off",
			Thing = "Prefetcher and memory compression",
			PerfOn = false,
			What = "Stops Windows reading programs into memory ahead of time, pre-launching Store apps, combining " +
				"identical pages and compressing memory. All of it is CPU and disk work while you play, and on an " +
				"SSD with enough RAM it buys nothing.",
			Cost = "With less than 16 GB of RAM, memory compression is doing real work - leave this off. Programs " +
				"may open a touch slower.",
			Risk = Caution,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			Values = new[]
			{
				new Reg("HKLM", MemoryManagement + "\\PrefetchParameters", "EnablePrefetcher", "dword", "0", "3")
			},
			// -ErrorAction Stop on every one: without it a non-terminating error is folded into the success
			// stream by 2>&1, thrown away by Out-Null, never reaches the catch, and leaves $LASTEXITCODE at
			// the 0 the line before set - so a refused step was reported as applied.
			//
			// Pre-launch depends on launch prefetching, so the order is not symmetric: turning off, prelaunch
			// goes first; turning back on, prefetching goes first.
			OnCommands = new[]
			{
				"Disable-MMAgent -MemoryCompression -ErrorAction Stop",
				"Disable-MMAgent -PageCombining -ErrorAction Stop",
				"Disable-MMAgent -ApplicationPreLaunch -ErrorAction Stop",
				"Disable-MMAgent -ApplicationLaunchPrefetching -ErrorAction Stop"
			},
			OffCommands = new[]
			{
				"Enable-MMAgent -MemoryCompression -ErrorAction Stop",
				"Enable-MMAgent -PageCombining -ErrorAction Stop",
				"Enable-MMAgent -ApplicationLaunchPrefetching -ErrorAction Stop",
				"Enable-MMAgent -ApplicationPreLaunch -ErrorAction Stop"
			}
		},

		// ---- gpu ------------------------------------------------------------------------------------
		new Tweak
		{
			Id = "fse",
			Group = "gpu",
			Title = "Real exclusive fullscreen",
			Thing = "Real exclusive fullscreen",
			PerfOn = true,
			What = "Stops Windows quietly turning a game's exclusive fullscreen into a borderless window. " +
				"Exclusive fullscreen hands the display to the game and takes the desktop compositor out of the " +
				"path, which is a frame of latency on its own.",
			Source = "fr33thy",
			Values = new[]
			{
				new Reg("HKCU", GameConfig, "GameDVR_FSEBehavior", "dword", "2", Delete),
				new Reg("HKCU", GameConfig, "GameDVR_FSEBehaviorMode", "dword", "2", "0"),
				new Reg("HKCU", GameConfig, "GameDVR_HonorUserFSEBehaviorMode", "dword", "1", "0"),
				new Reg("HKCU", GameConfig, "GameDVR_DXGIHonorFSEWindowsCompatible", "dword", "1", "0")
			}
		},
		new Tweak
		{
			Id = "gamedvr",
			Group = "gpu",
			Title = "Game DVR and background recording off",
			Thing = "Game DVR and background recording",
			PerfOn = false,
			What = "The Xbox Game Bar's always-on capture. It hooks every game's present path to be ready to " +
				"record the last 30 seconds, whether or not you ever use it.",
			Cost = "No Game Bar recording or screenshots. Game Mode is a separate setting and stays on.",
			Source = "winutil",
			NeedsAdmin = true,
			Values = new[]
			{
				new Reg("HKCU", GameConfig, "GameDVR_Enabled", "dword", "0", "1"),
				new Reg("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\GameDVR", "AppCaptureEnabled", "dword", "0", "1"),
				new Reg("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\GameDVR", "AllowGameDVR", "dword", "0", Delete)
			}
		},
		new Tweak
		{
			Id = "hags",
			Group = "gpu",
			Title = "Hardware-accelerated GPU scheduling on",
			Thing = "Hardware-accelerated GPU scheduling",
			PerfOn = true,
			What = "Lets the GPU manage its own work queue instead of the CPU driver doing it. Needed for NVIDIA " +
				"Reflex to work properly, and it takes scheduling work off the CPU.",
			Cost = "Needs a restart, a recent driver and a GPU that supports it. A few older games dislike it.",
			Source = "ms",
			NeedsAdmin = true,
			NeedsReboot = true,
			// Missing means "the driver's default", which is on for some GPUs and off for others.
			Values = new[] { new Reg("HKLM", GraphicsDrivers, "HwSchMode", "dword", "2", "1") { Absent = AbsentUnknown } }
		},
		new Tweak
		{
			Id = "mpo",
			Group = "gpu",
			Title = "Multiplane overlay off",
			Thing = "Multiplane overlay (MPO)",
			PerfOn = false,
			What = "MPO lets the display engine compose several layers at once. It is a well-known cause of " +
				"flickering, black screens and stutter on multi-monitor and mixed-refresh setups.",
			Cost = "A little more GPU work composing the desktop. Only worth it if you actually see flicker or " +
				"stutter on the desktop.",
			Risk = Caution,
			Source = "fr33thy",
			NeedsAdmin = true,
			Values = new[] { new Reg("HKLM", "SOFTWARE\\Microsoft\\Windows\\Dwm", "OverlayTestMode", "dword", "5", Delete) }
		},
		new Tweak
		{
			Id = "flip",
			Group = "gpu",
			Title = "Optimizations for windowed games off",
			Thing = "Optimizations for windowed games",
			PerfOn = false,
			What = "Windows 11 upgrades a windowed game's swap chain to the flip model and adds its own frame " +
				"pacing. For a game that already uses exclusive fullscreen it is one more thing between the frame " +
				"and the panel. This turns off that upgrade and the variable-refresh-rate optimization with it, " +
				"which is how the guide sets it.",
			Cost = "If you play borderless-windowed, this is the setting that gives borderless its low latency - " +
				"leave it off in that case. It also turns off the VRR optimization for windowed games, so on a " +
				"G-Sync or FreeSync monitor judder in windowed games is worth watching for.",
			Risk = Caution,
			Source = "fr33thy",
			Values = new[]
			{
				// Off removes the value rather than writing the other half of it: with no value Windows uses
				// its own defaults for both settings, which is what "off" has to mean. Writing
				// "VRROptimizeEnable=0;SwapEffectUpgradeEnable=1;" would have left the VRR optimization
				// switched off in the off state, which is not the Windows default and not what was asked for.
				new Reg("HKCU", "Software\\Microsoft\\DirectX\\UserGpuPreferences", "DirectXUserGlobalSettings",
					"keylist", "VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;", Delete)
			}
		},
		new Tweak
		{
			Id = "visualfx",
			Group = "gpu",
			Title = "Window animations off",
			Thing = "Window animations",
			PerfOn = false,
			What = "Menus, window minimise and the taskbar stop animating, so a click acts immediately instead of " +
				"after a 200 ms slide. Nothing to do with frame rate; everything to do with how the PC feels.",
			Cost = "Windows looks plainer.",
			Source = "winutil",
			Values = new[]
			{
				new Reg("HKCU", "Control Panel\\Desktop", "MenuShowDelay", "string", "0", "400"),
				new Reg("HKCU", "Control Panel\\Desktop", "DragFullWindows", "string", "0", "1"),
				new Reg("HKCU", "Control Panel\\Desktop\\WindowMetrics", "MinAnimate", "string", "0", "1"),
				new Reg("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\VisualEffects", "VisualFXSetting", "dword", "3", "1"),
				new Reg("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\Advanced", "TaskbarAnimations", "dword", "0", "1"),
				new Reg("HKCU", "Software\\Microsoft\\Windows\\DWM", "EnableAeroPeek", "dword", "0", "1")
			}
		},

		// ---- net ------------------------------------------------------------------------------------
		new Tweak
		{
			Id = "netthrottle",
			Group = "net",
			Title = "Network throttling off",
			Thing = "Network throttling",
			PerfOn = false,
			What = "While a multimedia program runs, Windows limits every other socket on the PC to about ten " +
				"packets per millisecond. A game counts as multimedia, so Windows is throttling its own traffic.",
			Source = "ms",
			NeedsAdmin = true,
			Values = new[] { new Reg("HKLM", SystemProfile, "NetworkThrottlingIndex", "dword", "0xFFFFFFFF", "10") }
		},
		new Tweak
		{
			Id = "nagle",
			Group = "net",
			Title = "Nagle's algorithm off",
			Thing = "Nagle's algorithm",
			PerfOn = false,
			What = "Stops Windows holding small TCP packets back to bundle them with the next one, and acknowledges " +
				"every segment at once. It only touches TCP: CS2's game traffic is UDP, which Nagle never delays, so the " +
				"gain is for TCP games and voice tools, not CS2.",
			Cost = "More upstream packets during downloads (Steam, browser). Applied to every network adapter on the " +
				"PC. Left out of FIX ALL.",
			Source = "fr33thy",
			Risk = Caution,
			NeedsAdmin = true,
			OnCommands = new[] { "[LiteSynapse.Net]::Nagle($true)" },
			OffCommands = new[] { "[LiteSynapse.Net]::Nagle($false)" }
		},
		new Tweak
		{
			Id = "autotuning",
			Group = "net",
			Title = "TCP receive-window auto-tuning on",
			Thing = "TCP receive-window auto-tuning",
			PerfOn = true,
			What = "Lets Windows size the TCP receive window for the connection it actually has. Tools that set " +
				"this to disabled or restricted cap download speed badly on anything fast.",
			Source = "ms",
			NeedsAdmin = true,
			OnCommands = new[] { "netsh.exe interface tcp set global autotuninglevel=normal" },
			OffCommands = new[] { "netsh.exe interface tcp set global autotuninglevel=disabled" }
		},
		new Tweak
		{
			Id = "nicpower",
			Group = "net",
			Title = "Network adapter power saving off",
			Thing = "Network adapter power saving",
			PerfOn = false,
			What = "Stops the network card from idling down, sleeping between packets or green-ethernet " +
				"negotiating. Each of those shows up as an occasional ping spike.",
			Cost = "A watt or two. Wake-on-LAN stops working. Switching it back removes these driver settings so the driver's " +
				"own defaults apply - it does not restore values you had set by hand.",
			Source = "fr33thy",
			NeedsAdmin = true,
			OnCommands = new[] { "[LiteSynapse.Net]::AdapterPower($false)" },
			OffCommands = new[] { "[LiteSynapse.Net]::AdapterPower($true)" }
		},
		new Tweak
		{
			Id = "delivery",
			Group = "net",
			Title = "Windows Update uploads off",
			Thing = "Windows Update uploads to other PCs",
			PerfOn = false,
			What = "Delivery Optimization uses your upload to send Windows updates to other PCs - on your network, " +
				"and on mode 3 to strangers on the internet. Your upload is also what carries your game traffic.",
			Cost = "Updates download from Microsoft only, which can be a little slower.",
			Source = "winutil",
			NeedsAdmin = true,
			Values = new[]
			{
				new Reg("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\DeliveryOptimization", "DODownloadMode", "dword", "0", Delete)
			}
		},

		// ---- bg -------------------------------------------------------------------------------------
		new Tweak
		{
			Id = "telemetry",
			Group = "bg",
			Title = "Telemetry and advertising ID off",
			Thing = "Telemetry and advertising ID",
			PerfOn = false,
			What = "Stops the usage reporting that runs in the background and uploads while you play.",
			Source = "winutil",
			NeedsAdmin = true,
			Values = new[]
			{
				new Reg("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\DataCollection", "AllowTelemetry", "dword", "0", Delete),
				new Reg("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "PublishUserActivities", "dword", "0", Delete),
				new Reg("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\System", "UploadUserActivities", "dword", "0", Delete),
				new Reg("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\AdvertisingInfo", "Enabled", "dword", "0", Delete),
				new Reg("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\Privacy", "TailoredExperiencesWithDiagnosticDataEnabled", "dword", "0", Delete)
			}
		},
		new Tweak
		{
			Id = "bgapps",
			Group = "bg",
			Title = "Store apps in the background off",
			Thing = "Store apps running in the background",
			PerfOn = false,
			What = "Stops every installed Store app from running while you are not using it.",
			Cost = "Store apps stop giving live tiles and notifications.",
			Source = "winutil",
			Values = new[]
			{
				new Reg("HKCU", "Software\\Microsoft\\Windows\\CurrentVersion\\BackgroundAccessApplications", "GlobalUserDisabled", "dword", "1", "0")
			}
		},
		new Tweak
		{
			Id = "consumer",
			Group = "bg",
			Title = "Suggested apps and auto-installs off",
			Thing = "Suggested apps and auto-installs",
			PerfOn = false,
			What = "Stops Windows installing promoted apps on its own and putting suggestions in the Start menu. " +
				"Those installs happen in the background, whenever Windows feels like it.",
			Source = "winutil",
			NeedsAdmin = true,
			Values = new[]
			{
				new Reg("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\CloudContent", "DisableWindowsConsumerFeatures", "dword", "1", Delete)
			}
		},
		new Tweak
		{
			Id = "razerautoinstall",
			Group = "bg",
			Title = "Stop Windows reinstalling Razer Synapse",
			Thing = "Windows auto-installing Razer Synapse",
			PerfOn = false,
			What = "Windows Update fetches manufacturer apps and co-installers for devices it finds. That is how " +
				"Synapse comes back after you remove it. This stops both.",
			Cost = "Windows stops pulling optional apps and co-installers for any device, so some vendor software " +
				"will not install itself any more. Drivers still come through Windows Update.",
			Source = "winutil",
			NeedsAdmin = true,
			Values = new[]
			{
				new Reg("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\DriverSearching", "SearchOrderConfig", "dword", "0", "1"),
				new Reg("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Device Installer", "DisableCoInstallers", "dword", "1", "0"),
				new Reg("HKLM", "SOFTWARE\\Policies\\Microsoft\\Windows\\Device Metadata", "PreventDeviceMetadataFromNetwork", "dword", "1", Delete)
			}
		},
		new Tweak
		{
			Id = "hibernate",
			Group = "bg",
			Title = "Hibernation off",
			Thing = "Hibernation",
			PerfOn = false,
			What = "Frees a hiberfil.sys the size of your RAM and stops fast-startup restoring a stale session, " +
				"which is a common source of \"it only stutters until I reboot properly\".",
			Cost = "No hibernate and no fast startup, so a cold boot takes a few seconds longer.",
			Source = "winutil",
			NeedsAdmin = true,
			OnCommands = new[] { "powercfg.exe /hibernate off" },
			OffCommands = new[] { "powercfg.exe /hibernate on" }
		},

		// ---- sec ------------------------------------------------------------------------------------
		new Tweak
		{
			Id = "vbs",
			Group = "sec",
			Title = "Memory integrity (HVCI) off",
			Thing = "Memory integrity (HVCI)",
			PerfOn = false,
			What = "Memory integrity runs driver code inside a hypervisor check. Turning it off is the single " +
				"biggest measured FPS win on this list for a CPU-bound game: reported gains run from under 2% on a " +
				"current CPU up to 10-15% on an older one.",
			Cost = "A malicious or vulnerable driver is no longer contained by the hypervisor. This is a real " +
				"reduction in security, not a cosmetic one. Do not do this on a PC you bank on. Needs a restart, " +
				"and Windows Security will report the PC as needing attention.",
			Risk = Risky,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			Values = new[]
			{
				// Missing means "as installed": on for clean Windows 11 installs on supported hardware, off otherwise.
				new Reg("HKLM", "SYSTEM\\CurrentControlSet\\Control\\DeviceGuard\\Scenarios\\HypervisorEnforcedCodeIntegrity", "Enabled", "dword", "0", "1") { Absent = AbsentUnknown }
			}
		},
		new Tweak
		{
			Id = "mitigations",
			Group = "sec",
			Title = "Spectre / Meltdown mitigations off",
			Thing = "Spectre / Meltdown mitigations",
			PerfOn = false,
			What = "Turns off the CPU side-channel mitigations Windows applies in software. On processors from " +
				"before about 2019 these cost real performance on every system call, which a game makes constantly.",
			Cost = "The PC becomes vulnerable to the Spectre and Meltdown class of attacks, which read memory " +
				"across process boundaries. On a 2020-or-newer CPU the mitigations are mostly in silicon anyway, so " +
				"you give up safety for very little. Needs a restart.",
			Risk = Risky,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			Values = new[]
			{
				new Reg("HKLM", MemoryManagement, "FeatureSettingsOverride", "dword", "3", Delete),
				new Reg("HKLM", MemoryManagement, "FeatureSettingsOverrideMask", "dword", "3", Delete)
			}
		},
		new Tweak
		{
			Id = "uac",
			Group = "sec",
			Title = "User Account Control off",
			Thing = "User Account Control",
			PerfOn = false,
			What = "No more consent prompt when a program asks for administrator rights. The guides turn this off " +
				"because the prompt itself steals focus, which can drop you out of a game.",
			Cost = "Any program you run gets administrator rights without asking. This is the change on this page " +
				"most likely to be regretted. Needs a restart, and some Store apps stop working with it off.",
			Risk = Risky,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			Values = new[]
			{
				new Reg("HKLM", "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Policies\\System", "EnableLUA", "dword", "0", "1")
			}
		},
		new Tweak
		{
			Id = "dep",
			Group = "sec",
			Title = "Data Execution Prevention off",
			Thing = "Data Execution Prevention",
			PerfOn = false,
			What = "Stops Windows marking data pages as non-executable.",
			Cost = "Removes one of the oldest and cheapest defences against code-execution exploits, and it costs " +
				"almost nothing to leave on - the gain here is the smallest on this page and the loss is not. " +
				"FACEIT's anti-cheat will not start with DEP off. Needs a restart.",
			Risk = Risky,
			Source = "fr33thy",
			NeedsAdmin = true,
			NeedsReboot = true,
			OnCommands = new[] { "bcdedit.exe /set nx AlwaysOff" },
			OffCommands = new[] { Optional + "bcdedit.exe /deletevalue nx" }
		}
	};

	// ---- Lookup -------------------------------------------------------------------------------------

	internal static Tweak Find(string id)
	{
		if (string.IsNullOrWhiteSpace(id))
		{
			return null;
		}
		string want = id.Trim();
		foreach (Tweak t in Catalog)
		{
			if (string.Equals(t.Id, want, StringComparison.OrdinalIgnoreCase))
			{
				return t;
			}
		}
		return null;
	}

	internal static string GroupTitle(string id)
	{
		foreach ((string gid, string title, string _) in Groups)
		{
			if (gid == id)
			{
				return title;
			}
		}
		return id;
	}

	internal static string SourceText(string source)
	{
		switch (source)
		{
		case "fr33thy":
			return "FR33THY's Ultimate Windows Optimization Guide";
		case "winutil":
			return "Chris Titus Tech's WinUtil";
		case "ms":
			return "documented by Microsoft";
		default:
			return "LiteSynapse";
		}
	}

	internal static string RiskText(string risk)
	{
		switch (risk)
		{
		case Risky:
			return "Gives up security";
		case Caution:
			return "Depends on your PC";
		default:
			return "Safe";
		}
	}

	// ---- State --------------------------------------------------------------------------------------

	// Whether a value read from the registry matches what the On state wants. Values are compared as numbers
	// when both look numeric, so 0x26 and 38 are the same value.
	internal static bool Matches(string want, object actual)
	{
		if (want == Delete)
		{
			return actual == null;
		}
		if (actual == null)
		{
			return false;
		}
		string got = actual is byte[] bytes
			? string.Concat(bytes.Select((byte b) => b.ToString("x2", CultureInfo.InvariantCulture)))
			: Convert.ToString(actual, CultureInfo.InvariantCulture);
		if (TryNumber(want, out long a) && TryNumber(got, out long b))
		{
			return a == b;
		}
		// A "key=value;key=value;" list is a set, not a string: Windows writes its own order and puts its own
		// keys in the same value. Every key we asked for has to be there with the value we asked for; what
		// else the list holds is the user's business.
		if (IsKeyList(want) && IsKeyList(got))
		{
			return Pairs(got) is Dictionary<string, string> theirs
				&& Pairs(want).All((KeyValuePair<string, string> kv) =>
					theirs.TryGetValue(kv.Key, out string v) && string.Equals(v, kv.Value, StringComparison.OrdinalIgnoreCase));
		}
		return string.Equals(want?.Trim(), got?.Trim(), StringComparison.OrdinalIgnoreCase);
	}

	// "VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;" and the like.
	internal static bool IsKeyList(string s)
	{
		return s != null && s.IndexOf('=') > 0 && s.IndexOf(';') > 0;
	}

	internal static Dictionary<string, string> Pairs(string list)
	{
		Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (string part in (list ?? "").Split(';'))
		{
			int at = part.IndexOf('=');
			if (at <= 0)
			{
				continue;
			}
			map[part.Substring(0, at).Trim()] = part.Substring(at + 1).Trim();
		}
		return map;
	}

	internal static bool TryNumber(string s, out long value)
	{
		value = 0L;
		if (string.IsNullOrWhiteSpace(s))
		{
			return false;
		}
		string t = s.Trim();
		if (t.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
		{
			if (ulong.TryParse(t.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hex))
			{
				// Narrowed to 32 bits, because that is what a DWORD is and what the registry hands back. The
				// write side already truncated 0xFFFFFFFF to -1; the read side did not, so it compared -1
				// against 4294967295 and "Network throttling off" read as half-applied forever, however many
				// times it was applied.
				value = (hex <= uint.MaxValue) ? unchecked((int)(uint)hex) : unchecked((long)hex);
				return true;
			}
			return false;
		}
		if (long.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out long dec))
		{
			value = dec;
			return true;
		}
		return false;
	}

	// on    = every value this entry owns is already at its On setting
	// off   = every value is at its Off setting
	// part  = a mix of the two, which is what a half-applied tweak from another tool looks like
	// other = set to something that is neither, so some other tool chose its own value. Kept apart from
	//         "part" because for an entry with one value "half set" would be plainly wrong.
	//
	// An entry with no registry values at all (only commands, e.g. bcdedit or the power plan) cannot be read
	// this way and comes back as the state the caller found by other means, or "unknown".
	internal static string StateOf(Tweak t, Func<Reg, object> read)
	{
		if (t == null || t.Values.Length == 0)
		{
			return "unknown";
		}
		int on = 0;
		int off = 0;
		int known = 0;
		foreach (Reg r in t.Values)
		{
			object actual = read(r);
			// A key list is ours when our keys are in it with our values, and "off" otherwise - whatever
			// else Windows keeps in the same value (Auto HDR, its own toggle for these keys) is not ours.
			if (r.Kind == "keylist")
			{
				known++;
				if (Matches(r.On, actual))
				{
					on++;
				}
				else
				{
					off++;
				}
				continue;
			}
			// A missing value is Windows' default, which is how most of these ship.
			if (actual == null && r.On != Delete && r.Off != Delete)
			{
				string absent = r.Absent ?? ((r.Off == NotOn) ? null : r.Off);
				if (absent == null || absent == AbsentUnknown)
				{
					// Says nothing either way: the rest of the entry's values decide.
					if (r.Off != NotOn)
					{
						continue;
					}
				}
				else
				{
					actual = absent;
				}
			}
			known++;
			bool isOn = Matches(r.On, actual) || AlsoOn(r, actual);
			if (isOn)
			{
				on++;
			}
			// The two states can want the same value, so this is not an else - except for NotOn, which is
			// by definition everything that is not On (missing included).
			if ((r.Off == NotOn) ? !isOn : Matches(r.Off, actual))
			{
				off++;
			}
		}
		if (known == 0)
		{
			return "unknown";
		}
		if (on == known)
		{
			return "on";
		}
		if (off == known)
		{
			return "off";
		}
		if (on == 0 && off == 0)
		{
			return "other";
		}
		return "part";
	}

	// ---- What only an administrator can read ----------------------------------------------------------

	// The boot settings (bcdedit) and TCP auto-tuning have no value an ordinary account can read, so on a
	// PC someone already tuned - with FR33THY's script, WinUtil or by hand - those rows could only say
	// "can't tell". This reads them with administrator rights into a file the scan picks up: on its own
	// (CHECK AS ADMIN on the Windows page) and at the end of every elevated apply, so what was just set is
	// read back rather than assumed.
	public static string AdminReadFile => System.IO.Path.Combine(Cfg.Dir, "perf-admin.txt");

	internal static string AdminReadScript(string file)
	{
		return "& {\n" +
			"  'BCD'\n" +
			"  try { bcdedit.exe /enum '{current}' } catch { }\n" +
			"  try { 'autotuning|' + (Get-NetTCPSetting -SettingName Internet -ErrorAction Stop).AutoTuningLevelLocal } catch { }\n" +
			"} | Set-Content -LiteralPath " + RazerLeftovers.Ps(file) + " -Encoding UTF8 -ErrorAction SilentlyContinue\n";
	}

	// "yes" in the languages bcdedit prints its booleans in.
	private static readonly HashSet<string> YesWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"yes", "ja", "oui", "si", "sí", "sim", "tak", "да", "是", "はい", "예", "evet", "ano", "igen", "kyllä", "ναι", "on", "true"
	};

	// The file AdminReadScript writes, as tweak states (on = applied): dynamictick, platformclock, dep and
	// autotuning, each only when it could be read. bcdedit's value names are not translated; its yes/no are.
	internal static Dictionary<string, string> ParseAdminRead(string text)
	{
		Dictionary<string, string> states = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		string t = text ?? "";
		if (System.Text.RegularExpressions.Regex.IsMatch(t, "\\{current\\}", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
		{
			string tick = BcdValue(t, "disabledynamictick");
			states["dynamictick"] = (tick != null && YesWords.Contains(tick)) ? "on" : "off";
			string clock = BcdValue(t, "useplatformclock");
			states["platformclock"] = (clock != null && YesWords.Contains(clock)) ? "off" : "on";
			string nx = BcdValue(t, "nx");
			states["dep"] = (nx == null || nx.Equals("OptIn", StringComparison.OrdinalIgnoreCase)) ? "off"
				: (nx.Equals("AlwaysOff", StringComparison.OrdinalIgnoreCase) ? "on" : "other");
		}
		System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(t, "^\\s*autotuning\\|(\\S+)",
			System.Text.RegularExpressions.RegexOptions.Multiline);
		if (m.Success)
		{
			string level = m.Groups[1].Value;
			states["autotuning"] = level.Equals("Normal", StringComparison.OrdinalIgnoreCase) ? "on"
				: (level.Equals("Disabled", StringComparison.OrdinalIgnoreCase) ? "off" : "other");
		}
		return states;
	}

	private static string BcdValue(string text, string name)
	{
		System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text,
			"^\\s*" + name + "\\s+(\\S+)", System.Text.RegularExpressions.RegexOptions.Multiline | System.Text.RegularExpressions.RegexOptions.IgnoreCase);
		return m.Success ? m.Groups[1].Value : null;
	}

	// A value another tool chose that is as good as ours (Reg.OnMin/OnMax/OnAny).
	internal static bool AlsoOn(Reg r, object actual)
	{
		if (actual == null || actual is byte[])
		{
			return false;
		}
		string got = Convert.ToString(actual, CultureInfo.InvariantCulture);
		if (r.OnAny != null && r.OnAny.Any((string v) => Matches(v, got)))
		{
			return true;
		}
		if ((r.OnMin.HasValue || r.OnMax.HasValue) && TryNumber(got, out long n))
		{
			return n >= (r.OnMin ?? long.MinValue) && n <= (r.OnMax ?? long.MaxValue);
		}
		return false;
	}

	// ---- The script ---------------------------------------------------------------------------------

	// Is there anything for this scope to do? Lets the caller skip a consent prompt, or a PowerShell start,
	// for a selection whose work all belongs in the other half.
	internal static bool HasWork(IEnumerable<(string Id, bool On)> choices, string scope)
	{
		if (choices == null)
		{
			return false;
		}
		foreach ((string id, bool on) in choices)
		{
			Tweak t = Find(id);
			if (t == null)
			{
				continue;
			}
			if (t.Values.Any((Reg r) => InScope(r, scope)) || CommandsInScope(t, scope))
			{
				return true;
			}
		}
		return false;
	}

	// HKCU belongs to the signed-in user, HKLM to the machine.
	private static bool InScope(Reg r, string scope)
	{
		return scope == UserScope ? (r.Hive == "HKCU") : (r.Hive == "HKLM");
	}

	// A command has no hive of its own. The ones that belong to the user are the pointer calls, which are
	// exactly the commands of the entries that need no administrator rights; everything else (bcdedit,
	// powercfg, netsh, the adapter walk) is machine-wide and must be elevated.
	private static bool CommandsInScope(Tweak t, string scope)
	{
		bool user = !t.NeedsAdmin;
		bool wantUser = scope == UserScope;
		return user == wantUser && (t.OnCommands.Length > 0 || t.OffCommands.Length > 0);
	}

	// PowerShell for the chosen changes: (id, turn it on) pairs. Unknown ids are skipped. Each step counts its
	// own failure into $failed, which the exit code carries back, so one refused value does not hide the rest.
	//
	// scope picks which half of the work to emit: UserScope for this account's own registry (run without
	// elevation) or MachineScope for everything else. An entry owning values in both hives is split between
	// the two, because an elevated PowerShell's HKCU: is the elevating administrator's profile, which on a
	// standard-user account is somebody else's.
	//
	// Nothing in here is built from text the user typed or from anything read off the PC: every key, value and
	// command comes from the catalog above, and strings are still quoted through Ps() so a stray apostrophe
	// cannot end a literal.
	//
	// resultFile: when given, the script also writes there the ids of the settings whose every step succeeded,
	// one per line. The exit code only says how many steps failed, not which, and a setting Windows cannot be
	// asked about afterwards (a bcdedit value, a netsh setting) needs to know it went through.
	//
	// force: write the result file even when there is no setting to apply, because a services half merged in
	// front of this one reports through it too.
	internal static string Script(IEnumerable<(string Id, bool On)> choices, string scope = MachineScope, string resultFile = null,
		bool force = false)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append("$failed = 0\n");
		string writeResult = string.IsNullOrEmpty(resultFile) ? ""
			: ("if ($null -eq $okIds) { $okIds = @() }\n" +
				"try { Set-Content -LiteralPath " + RazerLeftovers.Ps(resultFile) + " -Value $okIds -Encoding UTF8 -ErrorAction Stop } catch { }\n");
		if (choices == null)
		{
			sb.Append(force ? writeResult : "");
			sb.Append("exit $failed\n");
			return sb.ToString();
		}
		bool needPointer = false;
		bool needNet = false;
		List<(Tweak T, bool On)> list = new List<(Tweak, bool)>();
		foreach ((string id, bool on) in choices)
		{
			Tweak t = Find(id);
			if (t == null)
			{
				continue;
			}
			bool values = t.Values.Any((Reg r) => InScope(r, scope));
			bool commands = CommandsInScope(t, scope);
			if (!values && !commands)
			{
				continue;
			}
			list.Add((t, on));
			if (!commands)
			{
				continue;
			}
			foreach (string cmd in on ? t.OnCommands : t.OffCommands)
			{
				if (cmd.Contains("[LiteSynapse.Pointer]"))
				{
					needPointer = true;
				}
				if (cmd.Contains("[LiteSynapse.Net]"))
				{
					needNet = true;
				}
			}
		}
		if (list.Count == 0)
		{
			sb.Append(force ? writeResult : "");
			sb.Append("exit $failed\n");
			return sb.ToString();
		}
		if (needPointer)
		{
			sb.Append(PointerHelper);
		}
		if (needNet)
		{
			sb.Append(NetHelper);
		}
		bool report = !string.IsNullOrEmpty(resultFile);
		if (report)
		{
			// Not "= @()" outright: in a merged script the services half before this one has already added its own.
			sb.Append("if ($null -eq $okIds) { $okIds = @() }\n");
		}
		foreach ((Tweak t, bool on) in list)
		{
			sb.Append("# ").Append(t.Id).Append(on ? ": on\n" : ": off\n");
			if (report)
			{
				sb.Append("$step = $failed\n");
			}
			foreach (Reg r in t.Values)
			{
				if (!InScope(r, scope))
				{
					continue;
				}
				string want = on ? r.On : r.Off;
				if (want == NotOn)
				{
					// No single value to put back (see Tweak.OneWay).
					continue;
				}
				sb.Append(want == Delete ? RemoveValue(r) : SetValue(r, want));
			}
			if (CommandsInScope(t, scope))
			{
				foreach (string cmd in on ? t.OnCommands : t.OffCommands)
				{
					sb.Append(Run(cmd));
				}
			}
			if (report)
			{
				sb.Append("if ($failed -eq $step) { $okIds += ").Append(RazerLeftovers.Ps(t.Id)).Append(" }\n");
			}
		}
		sb.Append(writeResult);
		sb.Append("exit $failed\n");
		return sb.ToString();
	}

	// The ids a script's result file lists (see Script). Blank lines and anything that is not a catalog id are
	// dropped, so a stray line cannot mark a setting as applied.
	internal static HashSet<string> ReadResult(string text)
	{
		HashSet<string> ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (string line in (text ?? "").Split('\n'))
		{
			string id = line.Trim().TrimStart('\uFEFF');
			if (id.Length == 0)
			{
				continue;
			}
			if (id.StartsWith("svc:", StringComparison.OrdinalIgnoreCase))
			{
				ServiceCatalog.Row row = ServiceCatalog.Find(id.Substring(4));
				if (row != null)
				{
					ids.Add("svc:" + row.Service);
				}
				continue;
			}
			if (Find(id) != null)
			{
				ids.Add(Find(id).Id);
			}
		}
		return ids;
	}

	private static string Path(Reg r)
	{
		return RazerLeftovers.Ps((r.Hive == "HKCU" ? "HKCU:\\" : "HKLM:\\") + r.Key);
	}

	private static string SetValue(Reg r, string want)
	{
		if (r.Kind == "keylist")
		{
			return MergeKeyList(r, want);
		}
		string type = r.Kind switch
		{
			"string" => "String",
			"qword" => "QWord",
			_ => "DWord"
		};
		// A DWord is written as a number so 0xFFFFFFFF arrives as the value Windows expects, not as text.
		string value = r.Kind == "string" ? RazerLeftovers.Ps(want) : NumberLiteral(want, r.Kind);
		return "try {\n" +
			$"  if (-not (Test-Path -LiteralPath {Path(r)})) {{ New-Item -Path {Path(r)} -Force -ErrorAction Stop | Out-Null }}\n" +
			$"  New-ItemProperty -LiteralPath {Path(r)} -Name {RazerLeftovers.Ps(r.Name)} -PropertyType {type} -Value {value} -Force -ErrorAction Stop | Out-Null\n" +
			"} catch { $failed++ }\n";
	}

	// Reads the list that is there, replaces only the keys we own, and writes it back - so turning this on
	// does not throw away whatever else Windows put in the same value. Overwriting the whole string dropped
	// the user's global Auto HDR preference, and nothing put it back.
	private static string MergeKeyList(Reg r, string want)
	{
		string path = Path(r);
		string name = RazerLeftovers.Ps(r.Name);
		StringBuilder sb = new StringBuilder();
		sb.Append("try {\n");
		sb.Append($"  if (-not (Test-Path -LiteralPath {path})) {{ New-Item -Path {path} -Force -ErrorAction Stop | Out-Null }}\n");
		sb.Append("  $map = [ordered]@{}\n");
		sb.Append($"  $cur = (Get-ItemProperty -LiteralPath {path} -Name {name} -ErrorAction SilentlyContinue).{r.Name}\n");
		sb.Append("  foreach ($part in ($cur -split ';')) {\n");
		sb.Append("    $at = $part.IndexOf('=')\n");
		sb.Append("    if ($at -gt 0) { $map[$part.Substring(0, $at)] = $part.Substring($at + 1) }\n");
		sb.Append("  }\n");
		foreach (KeyValuePair<string, string> kv in Pairs(want))
		{
			sb.Append($"  $map[{RazerLeftovers.Ps(kv.Key)}] = {RazerLeftovers.Ps(kv.Value)}\n");
		}
		sb.Append("  $out = ''\n");
		sb.Append("  foreach ($k in $map.Keys) { $out += \"$k=$($map[$k]);\" }\n");
		sb.Append($"  New-ItemProperty -LiteralPath {path} -Name {name} -PropertyType String -Value $out -Force -ErrorAction Stop | Out-Null\n");
		sb.Append("} catch { $failed++ }\n");
		return sb.ToString();
	}

	// DWord values are unsigned in the registry but New-ItemProperty takes a signed int, so 0xFFFFFFFF is
	// written as -1 - which TryNumber now does for us, so both sides of the comparison narrow identically.
	// A QWord is 64-bit and must not be narrowed. Anything that is not a number is quoted as text.
	private static string NumberLiteral(string want, string kind)
	{
		if (!TryNumber(want, out long n))
		{
			return RazerLeftovers.Ps(want);
		}
		if (kind != "qword" && n > int.MaxValue)
		{
			n = unchecked((int)n);
		}
		string text = n.ToString(CultureInfo.InvariantCulture);
		// -Value -1 binds correctly but reads like a parameter; the brackets remove the doubt.
		return (n < 0) ? ("(" + text + ")") : text;
	}

	// Removing a value that is not there is not a failure: that is already the state we want. A removal that
	// was refused - a policy-locked key, no rights - is one, so the value is read back afterwards rather than
	// trusting SilentlyContinue, which hid both cases equally.
	private static string RemoveValue(Reg r)
	{
		string path = Path(r);
		string name = RazerLeftovers.Ps(r.Name);
		return $"Remove-ItemProperty -LiteralPath {path} -Name {name} -Force -ErrorAction SilentlyContinue\n" +
			$"if ($null -ne (Get-ItemProperty -LiteralPath {path} -Name {name} -ErrorAction SilentlyContinue)) " +
			"{ $failed++ }\n";
	}

	// $LASTEXITCODE is only set by an external program, and it keeps its old value otherwise. Several of these
	// commands are .NET calls or PowerShell cmdlets, so without clearing it first the check below would read
	// the exit code of whatever external command ran earlier in the script - an "sc.exe stop" on a service that
	// was already stopped, for instance - and count a step as failed that did exactly what it was asked.
	private static string Run(string command)
	{
		bool optional = command.StartsWith(Optional, StringComparison.Ordinal);
		string cmd = optional ? command.Substring(Optional.Length) : command;
		return "try {\n" +
			"  $global:LASTEXITCODE = 0\n" +
			$"  {cmd} 2>&1 | Out-Null\n" +
			(optional ? "" : "  if ($LASTEXITCODE -ne 0) { $failed++ }\n") +
			(optional ? "} catch { }\n" : "} catch { $failed++ }\n");
	}

	// SystemParametersInfo, so acceleration and pointer speed take effect now instead of at the next sign-in.
	// SPI_SETMOUSE = 4 with three zeros turns acceleration off; SPI_SETMOUSESPEED = 113 (0x71) sets the notch.
	private const string PointerHelper = @"if (-not ('LiteSynapse.Pointer' -as [type])) {
  Add-Type -ErrorAction SilentlyContinue -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace LiteSynapse {
  public static class Pointer {
    [DllImport(""user32.dll"", SetLastError = true)]
    static extern bool SystemParametersInfo(uint a, uint b, int[] v, uint f);
    [DllImport(""user32.dll"", SetLastError = true)]
    static extern bool SystemParametersInfo(uint a, uint b, IntPtr v, uint f);
    public static void Acceleration(bool on) {
      int[] v = on ? new int[] { 6, 10, 1 } : new int[] { 0, 0, 0 };
      if (!SystemParametersInfo(4, 0, v, 3)) throw new Exception(""SPI_SETMOUSE failed"");
    }
    public static void Speed(int notch) {
      if (notch < 1) notch = 1;
      if (notch > 20) notch = 20;
      // SPI_SETMOUSESPEED takes the new speed in pvParam and requires uiParam to be zero. Passing it the
      // other way round asked for speed 0, which is outside the valid 1-20 range, so it did nothing at all.
      if (!SystemParametersInfo(113, 0, (IntPtr)notch, 3)) throw new Exception(""SPI_SETMOUSESPEED failed"");
    }
  }
}
'@
}
";

	// TcpAckFrequency / TCPNoDelay per interface (Nagle), and the power-saving values on every adapter's driver
	// key. Both walk keys under Tcpip\Parameters\Interfaces and Class\{4d36e972...}, which are Windows' own.
	private const string NetHelper = @"if (-not ('LiteSynapse.Net' -as [type])) {
  Add-Type -ErrorAction SilentlyContinue -TypeDefinition @'
using System;
using Microsoft.Win32;
namespace LiteSynapse {
  public static class Net {
    const string Interfaces = @""SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces"";
    const string NetClass = @""SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}"";
    // Values every vendor's NIC driver uses for its own idling and wake behaviour.
    static readonly string[] PowerWords = {
      ""EnableGreenEthernet"", ""AdvancedEEE"", ""EEELinkAdvertisement"", ""*EEE"", ""GigaLite"",
      ""PowerSavingMode"", ""ULPMode"", ""SipsEnabled"", ""WakeOnLink"", ""S5WakeOnLan"",
      ""*WakeOnMagicPacket"", ""*WakeOnPattern"", ""*ModernStandbyWoLMagicPacket""
    };
    public static void Nagle(bool off) {
      using (RegistryKey root = Registry.LocalMachine.OpenSubKey(Interfaces, true)) {
        if (root == null) return;
        foreach (string name in root.GetSubKeyNames()) {
          using (RegistryKey k = root.OpenSubKey(name, true)) {
            if (k == null) continue;
            if (off) {
              k.SetValue(""TcpAckFrequency"", 1, RegistryValueKind.DWord);
              k.SetValue(""TCPNoDelay"", 1, RegistryValueKind.DWord);
            } else {
              k.DeleteValue(""TcpAckFrequency"", false);
              k.DeleteValue(""TCPNoDelay"", false);
            }
          }
        }
      }
    }
    public static void AdapterPower(bool on) {
      using (RegistryKey root = Registry.LocalMachine.OpenSubKey(NetClass, true)) {
        if (root == null) return;
        foreach (string name in root.GetSubKeyNames()) {
          if (name.Length != 4) continue;
          using (RegistryKey k = root.OpenSubKey(name, true)) {
            if (k == null) continue;
            if (k.GetValue(""DriverDesc"") == null) continue;
            if (on) {
              foreach (string w in PowerWords) k.DeleteValue(w, false);
              k.DeleteValue(""PnPCapabilities"", false);
              k.DeleteValue(""EnhancedPowerManagementEnabled"", false);
            } else {
              // Only values the driver already declares: writing one it does not know does nothing useful.
              foreach (string w in PowerWords) if (k.GetValue(w) != null) k.SetValue(w, ""0"", RegistryValueKind.String);
              k.SetValue(""PnPCapabilities"", 24, RegistryValueKind.DWord);
              k.SetValue(""EnhancedPowerManagementEnabled"", 0, RegistryValueKind.DWord);
            }
          }
        }
      }
    }
  }
}
'@
}
";
}
