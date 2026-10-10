using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LiteSynapse;

// The app's state: which profiles are active, what the keyboard hook does, and what the devices should show.
//
// Threads:
//  - tray thread: Check() on every foreground change (fast: one process query and a rule lookup);
//  - UI threads: the setters below;
//  - device worker: every HID transfer (profile apply/restore, lighting, brightness, onboard writes). Only the
//    latest pending request of each kind runs, and no other thread ever waits for it.
// State is guarded by stateLock, which is never held during device I/O. Changed is raised outside the lock.
internal sealed class Service
{
	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	private const int NagaPid = 0x008D;

	private const int KeyboardPid = 0x0235;

	private readonly InputEngine engine = new InputEngine();

	private readonly DeviceWorker worker = new DeviceWorker();

	private volatile RazerDevice mouse = RazerDevice.Find(NagaPid, "Naga");

	private volatile RazerDevice keyboard = RazerDevice.Find(KeyboardPid, "BlackWidow Lite");

	private ProfileStore store;

	private List<GameRule> games;

	private AppSettings settings;

	private readonly object stateLock = new object();

	// Used on the device worker only.
	private readonly LiveProfile liveProfile = new LiveProfile();

	private static readonly string selfExe = Path.GetFileName(Environment.ProcessPath ?? "LiteSynapse.exe");

	private static readonly bool selfElevated = SafeIsElevated();

	// ---- Foreground (written on the tray thread) ----

	// The last foreground program that is not LiteSynapse, and its game rule (null = not a linked game).
	private string fgExe;

	// The window the last Check looked at, so a repeated event for it costs two calls and nothing else.
	private nint fgWindow;

	private string fgPath;

	private uint fgPid;

	private GameRule fgRule;

	private uint elevatedLoggedPid;

	// The program really in front (LiteSynapse included) is a linked game: no flash writes now.
	private volatile bool gameInFront;

	// A pre-write skipped or stopped because a game came to the front, tried again when it leaves.
	private volatile string skippedPrewrite;

	private string currentKey = "\0";

	// ---- Target state (stateLock) ----
	//
	// While LiteSynapse runs (and live profiles are on) the mouse always runs ActiveMouse from a live slot: the
	// desktop profile, a game's profile or one picked by hand. Only exiting puts it back on the user's own slot.

	// The last mouse request was for a game's profile, to know when going back to the desktop profile should wait.
	private bool mouseShownGame;

	// LiteSynapse's own window is in front: the keyboard hook leaves keys alone, so typing, key capture and macro
	// recording see the real keys.
	private bool selfInFront;

	// The live write failed: the keyboard hook remaps the mouse buttons in software instead.
	private bool liveFailed;

	private int generation;

	private EngineInputs engineInputs;

	private bool engineSet;

	private bool competitiveBlocked;

	private bool sleeping;

	private bool locked;

	private int shutdown;

	// ---- Device worker only ----

	private string lastLighting;

	// What each slot's own lighting storage is known to hold (MouseLighting.Describe), as sent by this run of
	// LiteSynapse; null = unknown (never sent, or the slot was rewritten since). Device worker only.
	private readonly string[] slotLighting = new string[OnboardSlots.SlotCount + 1];

	// Self-tests running in a second LiteSynapse process (they drive the mouse themselves).
	private int selfTests;

	private string lastMouseName;

	private string lastKbName;

	private string lastKbEffect;

	private int kbForce;

	public bool Auto = true;

	public string ActiveMouse = "";

	public string ActiveKeyboard = "";

	// A linked game is in front. Snap Tap, macros and software remaps are on then, and everywhere (except in
	// LiteSynapse's own window) while a profile picked by hand is active.
	public bool InGame;

	public string LiveStatus = "";

	public bool LiveWrite = true;

	public bool DryRun;

	public string TrayText { get; private set; } = "LiteSynapse";

	public ProfileStore Store => store;

	public List<GameRule> Games => games;

	// The linked-game rule for a program, read under the same lock the list is edited under. For callers on
	// other threads - the window's own foreground hook - which must not walk the list while linking a game
	// adds to it.
	public GameRule FindGame(string exe, string path)
	{
		lock (stateLock)
		{
			return GameRule.Find(games, exe, path);
		}
	}

	public RazerDevice Mouse => mouse;

	public RazerDevice Kb => keyboard;

	public InputEngine Engine => engine;

	public Dictionary<string, BaseKey> Base => engine.Base;

	public string DefaultMouse
	{
		get
		{
			if (store.Mouse.Any((MouseProfile p) => p.Name == settings.DefaultMouse))
			{
				return settings.DefaultMouse;
			}
			return store.Mouse.FirstOrDefault()?.Name ?? "";
		}
	}

	public string DefaultKeyboard
	{
		get
		{
			if (!KeyboardExists(settings.DefaultKeyboard))
			{
				return "";
			}
			return settings.DefaultKeyboard;
		}
	}

	public bool CompetitiveSafe => settings.CompetitiveSafe;

	// Keyboard profile for games whose rule has none ("" = none).
	public string GameKeyboardDefault
	{
		get
		{
			if (!KeyboardExists(settings.GameKeyboardDefault))
			{
				return "";
			}
			return settings.GameKeyboardDefault;
		}
	}

	// The last program in front that is not LiteSynapse or part of the Windows shell (for "link running game").
	public string LastGameExe { get; private set; }

	// The linked game in front runs as administrator and LiteSynapse does not: Windows (UIPI) blocks the input
	// LiteSynapse sends to it, so Snap Tap, macros and remaps cannot work there.
	public bool ElevatedGame { get; private set; }

	public RazerLeftovers Razer { get; private set; } = new RazerLeftovers();

	// Windows power saving that would make the mouse answer late (checked with the Razer leftovers).
	public DeviceTuningState Tuning { get; private set; } = new DeviceTuningState();

	// What starts with Windows and what uses the network (only when the user runs the scan).
	public ScanResult Scan { get; private set; } = new ScanResult();

	// The keyboard hook is on (a game or a profile picked by hand): nothing may pause the process now.
	public bool EngineBusy => engineSet && !engineInputs.IsOff;

	// Slot number -> profile name last written there.
	public Dictionary<string, string> OnboardNames => liveProfile.Names();

	// Slots LiteSynapse uses for live profiles.
	public int[] LiveSlots => LiveWrite ? OnboardSlots.LiveSlots.ToArray() : Array.Empty<int>();

	private static string SettingsFile => Path.Combine(Cfg.Dir, "settings.json");

	public event Action Changed;

	public Service()
	{
		RepointStartup();
		settings = Cfg.Load<AppSettings>(SettingsFile);
		LiveWrite = settings.LiveWrite;
		Reload();
		ActiveMouse = DefaultMouse;
		ActiveKeyboard = DefaultKeyboard;
		// A slot write (live, pre-write or by hand) replaces that slot's stored lighting.
		liveProfile.SlotWritten += slot =>
		{
			if (slot >= 1 && slot <= OnboardSlots.SlotCount)
			{
				slotLighting[slot] = null;
			}
		};
		if (selfElevated)
		{
			Cfg.Log("running as administrator");
		}
	}

	private static bool SafeIsElevated()
	{
		try
		{
			return Native.IsCurrentProcessElevated();
		}
		catch
		{
			return false;
		}
	}

	private void Notify()
	{
		try
		{
			Changed?.Invoke();
		}
		catch (Exception ex)
		{
			Cfg.Log("changed: " + ex.Message);
		}
	}

	private MouseProfile FindMouse(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		return store.Mouse.FirstOrDefault((MouseProfile p) => p.Name == name);
	}

	private KbProfile FindKeyboard(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return null;
		}
		return store.Keyboard.FirstOrDefault((KbProfile p) => p.Name == name);
	}

	private bool KeyboardExists(string name)
	{
		return FindKeyboard(name) != null;
	}

	private void SaveSettings()
	{
		try
		{
			Cfg.Save(SettingsFile, settings);
		}
		catch (Exception ex)
		{
			Cfg.Log("settings: " + ex.Message);
		}
	}

	// ---- Data ---------------------------------------------------------------------------------------------

	public void Reload()
	{
		ProfileStore s = Cfg.Load<ProfileStore>(Cfg.Profiles);
		List<GameRule> g = Cfg.Load<List<GameRule>>(Cfg.Games);
		if (g.Count == 0)
		{
			g = DefaultGames.Make(s);
			Cfg.Save(Cfg.Games, g);
		}
		try
		{
			if (DataMigrations.Apply(s, g))
			{
				string backup = Cfg.BackupFiles(Cfg.Profiles, Cfg.Games);
				if (backup != null)
				{
					Cfg.Save(Cfg.Profiles, s);
					Cfg.Save(Cfg.Games, g);
					Cfg.Log("profiles and games updated; the previous files are in " + backup);
				}
				else
				{
					Cfg.Log("data update not saved because the backup failed; it is used for this session only");
				}
			}
		}
		catch (Exception ex)
		{
			Cfg.Log("data migration: " + ex.Message);
		}
		foreach (GameRule rule in g)
		{
			if (!string.IsNullOrEmpty(rule.KeyboardProfile) && !s.Keyboard.Any((KbProfile p) => p.Name == rule.KeyboardProfile))
			{
				rule.KeyboardProfile = "";
			}
		}
		// Test build 1 offered reactive and starlight, which the BlackWidow Lite does not have: show static instead.
		bool effectsFixed = false;
		foreach (KbProfile kb in s.Keyboard)
		{
			string effect = KbLighting.Normalize(kb?.Effect);
			if (kb != null && kb.Effect != effect)
			{
				Cfg.Log($"keyboard profile \"{kb.Name}\": effect \"{kb.Effect}\" -> \"{effect}\"");
				kb.Effect = effect;
				effectsFixed = true;
			}
		}
		if (effectsFixed)
		{
			Cfg.Save(Cfg.Profiles, s);
		}
		Dictionary<string, BaseKey> calibration = Cfg.Load<Dictionary<string, BaseKey>>(Cfg.Calib);
		lock (stateLock)
		{
			store = s;
			games = g;
			engine.Base = calibration;
			engineSet = false;
		}
	}

	// The next Check() applies the foreground program's profiles again even if they look unchanged.
	public void ForceRecheck()
	{
		// Also forgets which window Check last looked at: the rules or profiles changed, so the program in front
		// has to be matched again even though it is still the same window.
		fgWindow = IntPtr.Zero;
		lock (stateLock)
		{
			currentKey = "\0";
			engineSet = false;
		}
	}

	// Saves games.json. changedProfile: the mouse profile of the rule that changed, which is then written to an
	// idle live slot ahead of time (while no game is in front) so the game starts with only a slot switch.
	public void SaveGames(string changedProfile = null)
	{
		lock (stateLock)
		{
			Cfg.Save(Cfg.Games, games);
		}
		SchedulePrewrite(changedProfile);
	}

	// Saves profiles.json. changedMouseProfile: the mouse profile that was edited; it is put on the mouse again if
	// it is active, or pre-written to an idle live slot if a game uses it.
	public void SaveProfiles(string changedMouseProfile = null)
	{
		lock (stateLock)
		{
			Cfg.Save(Cfg.Profiles, store);
		}
		if (string.IsNullOrEmpty(changedMouseProfile))
		{
			return;
		}
		bool active;
		lock (stateLock)
		{
			active = changedMouseProfile == ActiveMouse;
			if (active)
			{
				UpdateEngineLocked(force: true);
			}
		}
		if (active)
		{
			// A short delay folds several quick edits into one write.
			RequestMouse(1000);
		}
		else
		{
			SchedulePrewrite(changedMouseProfile);
		}
	}

	// Sends the active profile's DPI and polling rate to the mouse now, so a change is felt right away; the slot
	// write that SaveProfiles asks for keeps them on the mouse.
	public void ApplyMousePerformance()
	{
		if (DryRun || mouse == null)
		{
			return;
		}
		worker.Post("mousePerformance", () =>
		{
			RazerDevice m = mouse;
			MouseProfile mp;
			lock (stateLock)
			{
				mp = FindMouse(ActiveMouse);
			}
			if (m == null || mp == null)
			{
				return;
			}
			// DPI is stored per slot: while the mouse is still on one of the user's own slots, the live slot write
			// that follows brings it instead.
			bool toSlot = !LiveWrite || OnboardSlots.IsLiveSlot(m.ActiveStorage());
			int sentTo = 0;
			bool dpiOk = toSlot && m.SetDpi(mp.Dpi, out sentTo);
			if (LiveWrite && sentTo > 0 && OnboardSlots.IsLiveSlot(sentTo))
			{
				// That slot now holds a DPI its recorded profile does not: never switch to it as a match again.
				// The slot write SaveProfiles asked for writes the profile as it is now.
				liveProfile.ForgetSlot(sentTo);
			}
			bool pollOk = m.SetPolling(mp.PollingRate);
			Cfg.Log(toSlot ? $"mouse now: dpi {mp.Dpi}:{dpiOk} poll {mp.PollingRate}:{pollOk}" : $"mouse now: poll {mp.PollingRate}:{pollOk}; dpi {mp.Dpi} comes with the live slot");
		});
	}

	public void SetCalibration(Dictionary<string, BaseKey> learned)
	{
		Cfg.Save(Cfg.Calib, learned);
		lock (stateLock)
		{
			engine.Base = learned;
			currentKey = "\0";
			UpdateEngineLocked(force: true);
		}
	}

	public void SetDefaults(string mouseName, string keyboardName)
	{
		lock (stateLock)
		{
			if (mouseName != null)
			{
				settings.DefaultMouse = mouseName;
			}
			if (keyboardName != null)
			{
				settings.DefaultKeyboard = keyboardName;
			}
			SaveSettings();
		}
		Notify();
	}

	public void SetCompetitiveSafe(bool on)
	{
		lock (stateLock)
		{
			settings.CompetitiveSafe = on;
			SaveSettings();
			UpdateEngineLocked(force: false);
		}
		Cfg.Log("competitive-safe " + (on ? "on: no Snap Tap, macros or remaps in anti-cheat games" : "off"));
		Notify();
	}

	public void SetGameKeyboardDefault(string name)
	{
		lock (stateLock)
		{
			settings.GameKeyboardDefault = name ?? "";
			SaveSettings();
		}
		Evaluate(force: false);
		Notify();
	}

	// Adds the last game that was in front to that profile's game rule (creating the rule if needed) and takes it
	// out of other rules, so it switches to this profile from now on. Returns the exe, or null.
	public string LinkRunningGame(string profile)
	{
		string exe = LastGameExe;
		if (string.IsNullOrEmpty(exe) || string.IsNullOrEmpty(profile))
		{
			return null;
		}
		lock (stateLock)
		{
			if (FindMouse(profile) == null)
			{
				return null;
			}
			foreach (GameRule other in games)
			{
				if (other.Profile != profile && string.IsNullOrEmpty(other.PathContains) && other.Exes != null && other.Exes.RemoveAll((string x) => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase)) > 0)
				{
					Cfg.Log($"link: {exe} removed from \"{other.Profile}\"");
				}
			}
			GameRule rule = games.FirstOrDefault((GameRule x) => x.Profile == profile);
			if (rule == null)
			{
				rule = new GameRule
				{
					Profile = profile
				};
				games.Add(rule);
			}
			rule.Exes ??= new List<string>();
			if (!rule.Exes.Any((string x) => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase)))
			{
				rule.Exes.Add(exe);
			}
			if (fgExe != null)
			{
				fgRule = GameRule.Find(games, fgExe, fgPath);
			}
		}
		Cfg.Log($"link: {exe} -> \"{profile}\"");
		SaveGames(profile);
		Evaluate(force: false);
		Notify();
		return exe;
	}

	// ---- Devices --------------------------------------------------------------------------------------------

	// After a Razer device arrived or left: find the devices again on the worker and re-apply the state.
	public void EnsureDevices()
	{
		worker.Post("devices", () => SyncDevices(reset: true));
	}

	// The self-test runs in a second LiteSynapse process and drives the mouse itself (slot writes, switches): device
	// work here waits until it ends, then the devices are read and sent everything again. Blocks up to ~3 s for a
	// device job that is running: call it off the UI thread.
	public void BeginSelfTest()
	{
		if (Interlocked.Increment(ref selfTests) == 1)
		{
			worker.Cancel("prewrite");
			worker.Pause(3000);
			Cfg.Log("self-test started: LiteSynapse leaves the devices alone until it ends");
		}
	}

	public void EndSelfTest()
	{
		if (Interlocked.Decrement(ref selfTests) == 0)
		{
			worker.Resume();
			Cfg.Log("self-test ended: syncing the devices again");
			EnsureDevices();
		}
	}

	// Runs work on the device worker (e.g. reads for the UI). Only the latest pending request of a kind runs;
	// a replaced one completes with default.
	public Task<T> RunOnDevices<T>(string kind, Func<T> work)
	{
		TaskCompletionSource<T> tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
		worker.Post(kind, () =>
		{
			try
			{
				tcs.TrySetResult(work());
			}
			catch (Exception ex)
			{
				tcs.TrySetException(ex);
			}
		}).ContinueWith((Task<bool> t) =>
		{
			tcs.TrySetResult(default(T));
		}, TaskScheduler.Default);
		return tcs.Task;
	}

	// Writes a profile into an onboard slot on the device worker; progress is called on the worker.
	// Result: null = ok, otherwise why not.
	public Task<string> WriteOnboardSlot(int slot, string profileName, Action<string> progress = null)
	{
		if (slot < 1 || slot > OnboardSlots.SlotCount)
		{
			return Task.FromResult("Slot must be 1-5");
		}
		MouseProfile p;
		lock (stateLock)
		{
			p = FindMouse(profileName);
		}
		if (p == null)
		{
			return Task.FromResult("no profile \"" + profileName + "\"");
		}
		if (DryRun)
		{
			return Task.FromResult("dry run");
		}
		TaskCompletionSource<string> tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		worker.Post("onboard", () =>
		{
			RazerDevice m = mouse;
			string err;
			try
			{
				err = (m == null) ? "Naga not found" : liveProfile.WriteSlot(m, slot, p, progress);
				if (m != null && slot == m.ActiveStorage())
				{
					// The slot the mouse is running now has this profile's DPI and lighting: what was last sent is
					// no longer what the mouse shows, so the next sync sends the active profile's values again.
					m.InvalidateCache();
					lastLighting = null;
				}
			}
			catch (Exception ex)
			{
				err = ex.Message;
			}
			Cfg.Log($"onboard slot {slot} <- {p.Name}: {err ?? "ok"}");
			tcs.TrySetResult(err);
		}).ContinueWith((Task<bool> t) =>
		{
			tcs.TrySetResult("cancelled: another write was requested");
		}, TaskScheduler.Default);
		return tcs.Task;
	}

	// Applies the profile's lighting now if it is the active mouse profile (otherwise it is applied when the
	// profile becomes active). null = ok; only a bad setting is reported here, device errors go to the log.
	public string ApplyMouseLighting(MouseProfile p)
	{
		if (p?.Lighting == null)
		{
			return null;
		}
		string invalid = MouseLighting.Validate(p.Lighting);
		if (invalid != null)
		{
			return invalid;
		}
		if (mouse == null || DryRun)
		{
			return null;
		}
		bool active;
		lock (stateLock)
		{
			active = p.Name == ActiveMouse;
		}
		if (!active)
		{
			SchedulePrewrite(p.Name);
			return null;
		}
		worker.Post("mouseLighting", () =>
		{
			RazerDevice m = mouse;
			MouseLightingSettings s;
			lock (stateLock)
			{
				s = FindMouse(ActiveMouse)?.Lighting;
			}
			if (m != null && s != null)
			{
				ApplyLighting(m, s, force: true);
			}
		});
		if (LiveWrite)
		{
			// The live slot stores the lighting too: write it again once the user stops changing it.
			RequestMouse(1500);
		}
		return null;
	}

	// Applies the keyboard profile's effect and brightness now if it is the active one. False when the keyboard
	// is not connected or the effect is unknown.
	public bool ApplyKbLighting(KbProfile p)
	{
		if (p == null)
		{
			return false;
		}
		if (keyboard == null || DryRun)
		{
			return false;
		}
		bool active;
		lock (stateLock)
		{
			active = p.Name == ActiveKeyboard;
		}
		if (active)
		{
			RequestKeyboard(forceLighting: true);
		}
		return true;
	}

	// ---- Razer leftovers --------------------------------------------------------------------------------------

	public void RefreshRazer()
	{
		RefreshRazer(deep: true);
	}

	// deep: also look for the Razer services, the upgrade task and the RunOnce values (a few hundred registry
	// reads and possibly schtasks.exe). A shallow check keeps what an earlier deep one found.
	public void RefreshRazer(bool deep)
	{
		RazerLeftovers r = RazerLeftovers.Check(deep);
		RazerLeftovers old = Razer;
		if (!deep && old != null && old.Deep)
		{
			r.Deep = true;
			r.Services = old.Services;
			r.UpgradeTask = old.UpgradeTask;
			r.RunOnce = old.RunOnce;
		}
		string before = old?.ToString();
		Razer = r;
		if (r.ToString() != before)
		{
			Cfg.Log("razer software: " + r);
			if (r.SynapseRunning)
			{
				Cfg.Log("Razer Synapse is running: it can change the lighting and switch the mouse's slots behind LiteSynapse");
			}
		}
		Notify();
	}

	// Runs an elevated clean-up step (UAC prompt) and checks again. Blocks until the step finished.
	public string RazerCleanup(string action)
	{
		string msg = RazerLeftovers.Cleanup(action, Razer);
		RefreshRazer();
		return msg;
	}

	public Task<string> RazerCleanupAsync(string action)
	{
		return Task.Run(() => RazerCleanup(action));
	}

	// Looks at the start-up programs, the optional services and the network (a second or so). On demand only.
	public Task RunScanAsync()
	{
		return Task.Run(() =>
		{
			Scan = SystemScanner.Check();
			Notify();
		});
	}

	// Turns the chosen start-up entries, services and Windows settings on or off, then scans again. Each
	// choice is (what kind of thing, which one, turn it on); nothing the user did not tick is touched.
	public Task<string> ScanApplyAsync(IReadOnlyList<(string Kind, string Id, bool On)> choices)
	{
		return Task.Run(() =>
		{
			string msg = SystemScanner.Apply(choices);
			Scan = SystemScanner.Check();
			Notify();
			return msg;
		});
	}

	// Asks Microsoft Defender to scan one program's file.
	public Task<string> DefenderScanAsync(string path)
	{
		return Task.Run(() => SystemScanner.DefenderScan(path));
	}

	// Asks Windows how the devices' power management is set. This starts PowerShell for a WMI query, so it
	// happens only when the Device page is open or a change was just made - never at start-up.
	public void RefreshTuning()
	{
		try
		{
			Tuning = DeviceTuning.Check();
		}
		catch (Exception ex)
		{
			Cfg.Log("device tuning check: " + ex.Message);
		}
	}

	public Task RefreshTuningAsync()
	{
		return Task.Run(RefreshTuning);
	}

	// Keeps some devices awake and hands others back to Windows (one UAC prompt), and keeps the state the same
	// elevated run read back.
	public Task<string> TuneDevicesAsync(IReadOnlyList<string> awake, IReadOnlyList<string> allow, bool? planAllow)
	{
		return Task.Run(() =>
		{
			string msg = DeviceTuning.Apply(awake, allow, planAllow, out DeviceTuningState after);
			if (after != null)
			{
				Tuning = after;
			}
			Notify();
			return msg;
		});
	}

	// Reads the settings only an administrator can see (boot settings, TCP auto-tuning), then scans again.
	public Task<string> CheckSettingsAsAdminAsync()
	{
		return Task.Run(() =>
		{
			string problem = SystemScanner.ReadAsAdmin();
			Scan = SystemScanner.Check();
			Notify();
			return problem ?? "Read with administrator rights.";
		});
	}

	// Reads the devices with administrator rights, for an account whose own read Windows refuses.
	public Task<string> CheckDevicesAsAdminAsync()
	{
		return Task.Run(() =>
		{
			string problem = DeviceTuning.ReadAsAdmin();
			RefreshTuning();
			Notify();
			return problem ?? DeviceTuning.Summary(Tuning);
		});
	}

	private void StartRazerCheck()
	{
		Thread t = new Thread(() =>
		{
			// Let the startup profile apply first; this is a few registry reads, not urgent. The heavy part (every
			// Windows service, the scheduled task) waits until the window asks, so starting a game right after
			// sign-in never competes with it.
			Thread.Sleep(5000);
			try
			{
				RefreshRazer(deep: false);
			}
			catch (Exception ex)
			{
				Cfg.Log("razer check: " + ex.Message);
			}
		})
		{
			IsBackground = true,
			Name = "LiteSynapse razer check",
			Priority = ThreadPriority.BelowNormal
		};
		t.Start();
	}

	// ---- Foreground and profiles ------------------------------------------------------------------------------

	public void Start()
	{
		Check();
		if (currentKey == "\0")
		{
			// The program in front could not be read (or it is LiteSynapse): start on the desktop profiles.
			Evaluate(force: true);
		}
		StartRazerCheck();
	}

	// Called on every foreground change (tray thread). Must stay fast and must never throw.
	public void Check()
	{
		try
		{
			// Windows sends EVENT_SYSTEM_FOREGROUND again for the same window (a game that re-activates itself, an
			// overlay closing, a click on the window that is already in front). Two cheap calls tell us that, and
			// then nothing else runs: no process handle, no path, no rule matching.
			Native.ForegroundWindow(out nint hwnd, out uint fg);
			if (hwnd != IntPtr.Zero && hwnd == fgWindow && fg == fgPid)
			{
				return;
			}
			string path = Native.ForegroundExePath(out uint pid);
			if (path == null)
			{
				return;
			}
			fgWindow = hwnd;
			string exe = Path.GetFileName(path);
			if (string.Equals(exe, selfExe, StringComparison.OrdinalIgnoreCase))
			{
				// LiteSynapse's own window: keep the game's state, but no game is in front now and the keyboard
				// hook leaves keys alone.
				gameInFront = false;
				// fgPid still belongs to the program before this window, so the cheap check above must not match
				// on it next time.
				fgWindow = IntPtr.Zero;
				lock (stateLock)
				{
					if (!selfInFront)
					{
						selfInFront = true;
						UpdateEngineLocked(force: false);
					}
				}
				return;
			}
			GameRule rule;
			lock (stateLock)
			{
				rule = GameRule.Find(games, exe, path);
			}
			gameInFront = rule != null;
			if (!gameInFront && skippedPrewrite != null)
			{
				string again = skippedPrewrite;
				skippedPrewrite = null;
				SchedulePrewrite(again);
			}
			bool elevatedChanged = false;
			if (pid != fgPid || (rule != null) != (fgRule != null))
			{
				bool elevated = rule != null && !selfElevated && Native.IsProcessElevated(pid) == true;
				if (elevated != ElevatedGame)
				{
					ElevatedGame = elevated;
					elevatedChanged = true;
					if (elevated && pid != elevatedLoggedPid)
					{
						elevatedLoggedPid = pid;
						Cfg.Log($"\"{exe}\" runs as administrator and LiteSynapse does not: Windows blocks Snap Tap, macros and remaps there. Run LiteSynapse as administrator to use them in this game.");
					}
				}
			}
			lock (stateLock)
			{
				fgExe = exe;
				fgPath = path;
				fgPid = pid;
				fgRule = rule;
				// Evaluate below gives the keyboard hook its keys back.
				selfInFront = false;
				if (!ServicePlan.IsShellOrSelf(exe, selfExe))
				{
					LastGameExe = exe;
				}
			}
			bool notified = Evaluate(force: false);
			if (elevatedChanged && !notified)
			{
				Notify();
			}
		}
		catch (Exception ex)
		{
			Cfg.Log("foreground: " + ex.Message);
		}
	}

	// Applies the last foreground program's rule. In automatic mode the profiles follow it; when the user picked
	// a profile by hand the profiles stay and only "in game" follows it. Returns true if Changed was raised.
	private bool Evaluate(bool force)
	{
		bool changed = false;
		lock (stateLock)
		{
			GameRule rule = fgRule;
			bool wasInGame = InGame;
			InGame = rule != null;
			if (Auto)
			{
				string kb = (rule == null) ? DefaultKeyboard : ServicePlan.GameKeyboard(rule, settings.GameKeyboardDefault, KeyboardExists);
				string key = (rule == null) ? "" : (rule.Profile + "|" + kb);
				if (force || key != currentKey)
				{
					currentKey = key;
					ApplyLocked(rule?.Profile ?? DefaultMouse, kb, rule != null, debounce: true);
					changed = true;
				}
			}
			if (!changed)
			{
				changed = InGame != wasInGame;
				UpdateEngineLocked(force && !Auto);
			}
		}
		if (changed)
		{
			Notify();
		}
		return changed;
	}

	public void SelectMouse(string name)
	{
		Auto = false;
		ApplyNames(name, ActiveKeyboard, inGame: true);
	}

	public void SelectKeyboard(string name)
	{
		Auto = false;
		ApplyNames(ActiveMouse, (name == "(none)") ? "" : name, inGame: true);
	}

	public void SetAuto(bool on)
	{
		Auto = on;
		if (on)
		{
			ForceRecheck();
			Evaluate(force: true);
		}
		else
		{
			Notify();
		}
	}

	// Makes these profiles active; the mouse runs mouseName from a live slot right away. In automatic mode inGame
	// says whether a linked game is in front; with a profile picked by hand that follows the foreground program.
	public void ApplyNames(string mouseName, string kbName, bool inGame)
	{
		lock (stateLock)
		{
			InGame = Auto ? inGame : (fgRule != null);
			ApplyLocked(mouseName, kbName, InGame, debounce: false);
		}
		Notify();
	}

	// Applies the current profiles to the devices and the keyboard hook again.
	public void Reapply()
	{
		if (Auto)
		{
			Evaluate(force: true);
			return;
		}
		lock (stateLock)
		{
			ApplyLocked(ActiveMouse, ActiveKeyboard, mouseShownGame, debounce: false);
		}
		Notify();
	}

	public void SetLiveWrite(bool on)
	{
		lock (stateLock)
		{
			LiveWrite = on;
			settings.LiveWrite = on;
			SaveSettings();
			ApplyLocked(ActiveMouse, ActiveKeyboard, mouseShownGame, debounce: false);
		}
		Notify();
	}

	// Caller holds stateLock. Sets the target, updates the keyboard hook right away and asks the worker to bring
	// the devices there. game: mouseName is a game's profile. debounce: going from a game's profile back to the
	// desktop profile waits ServicePlan.RestoreDelayMs (cancelled if a game comes back first), so a quick alt-tab
	// does not switch slots twice.
	private void ApplyLocked(string mouseName, string kbName, bool game, bool debounce)
	{
		ActiveMouse = mouseName ?? "";
		ActiveKeyboard = kbName ?? "";
		liveFailed = false;
		generation++;
		string t = "LiteSynapse - " + ActiveMouse;
		TrayText = (t.Length > 120) ? t.Substring(0, 120) : t;
		Cfg.Log("profile -> " + ActiveMouse + " / " + (FindKeyboard(kbName)?.Name ?? "(none)"));
		if (DryRun)
		{
			return;
		}
		UpdateEngineLocked(force: true);
		int delay = ServicePlan.MouseDelay(mouseShownGame, game, debounce);
		mouseShownGame = game;
		RequestMouse(delay);
		RequestKeyboard(forceLighting: false);
	}

	// Caller holds stateLock. Gives the keyboard hook what the current state needs; unchanged inputs are not
	// sent again unless force (a new profile apply refreshes the hook, as it always did).
	private void UpdateEngineLocked(bool force)
	{
		if (DryRun)
		{
			return;
		}
		MouseProfile mp = FindMouse(ActiveMouse);
		KbProfile kp = FindKeyboard(ActiveKeyboard);
		bool onboard = LiveWrite && mouse != null && mp != null && !liveFailed;
		bool game = InGame && !selfInFront;
		// While LiteSynapse's own window is in front, fgExe is still the program before it; the engine is off then.
		string exe = selfInFront ? null : fgExe;
		EngineInputs e = ServicePlan.Engine(mp, kp, game, onboard, settings.CompetitiveSafe, fgRule?.Profile, sleeping || locked, pickedByHand: !Auto && !selfInFront, foregroundExe: exe);
		if (!force && engineSet && e == engineInputs)
		{
			return;
		}
		bool blocked = !sleeping && !locked && ServicePlan.BlockedByCompetitiveSafe(settings.CompetitiveSafe, game, fgRule?.Profile, mp?.Name, exe);
		if (blocked && !competitiveBlocked)
		{
			string what = AntiCheat.IsProtectedExe(exe) ? exe : (fgRule?.Profile ?? ActiveMouse);
			Cfg.Log("competitive-safe: \"" + what + "\" is anti-cheat protected, so no Snap Tap, macros or remaps");
		}
		competitiveBlocked = blocked;
		engineInputs = e;
		engineSet = true;
		engine.SetProfile(e.Mouse, e.Keyboard, store.Macros, e.InGame);
	}

	// ---- Device worker jobs ----------------------------------------------------------------------------------

	private void RequestMouse(int delayMs)
	{
		if (!DryRun)
		{
			worker.Post("mouse", SyncMouse, delayMs);
		}
	}

	private void RequestKeyboard(bool forceLighting)
	{
		if (DryRun)
		{
			return;
		}
		if (forceLighting)
		{
			Interlocked.Exchange(ref kbForce, 1);
		}
		worker.Post("kb", SyncKeyboard);
	}

	private void SchedulePrewrite(string profile)
	{
		if (DryRun || !LiveWrite || string.IsNullOrEmpty(profile))
		{
			return;
		}
		lock (stateLock)
		{
			if (!games.Any((GameRule g) => g.Profile == profile && g.Exes != null && g.Exes.Count > 0))
			{
				return;
			}
		}
		worker.Post("prewrite", () => Prewrite(profile), 2000);
	}

	// Brings the mouse to the active profile: it runs it from a live slot (with live profiles off: DPI and polling
	// are sent to the slot the user picked on the mouse).
	private void SyncMouse()
	{
		MouseProfile mp;
		bool useSlots;
		int gen;
		lock (stateLock)
		{
			mp = FindMouse(ActiveMouse);
			useSlots = LiveWrite;
			gen = generation;
		}
		RazerDevice m = mouse;
		if (m == null || mp == null)
		{
			if (m == null && LiveStatus.Length > 0)
			{
				LiveStatus = "";
				Notify();
			}
			return;
		}
		bool switched = false;
		string status = "";
		if (useSlots)
		{
			string err = liveProfile.Apply(m, mp);
			switched = liveProfile.Switched;
			if (err != null)
			{
				Cfg.Log("live profile failed: " + err);
				status = "Could not write to the mouse: " + err;
			}
			else
			{
				status = liveProfile.Status;
				Cfg.Log("live: " + status);
				// The slot write just put this lighting into the slot: sending it again would be six more
				// flash writes with the game starting.
				if (liveProfile.Wrote && mp.Lighting != null && liveProfile.LiveSlot >= 1 && liveProfile.LiveSlot <= OnboardSlots.SlotCount)
				{
					slotLighting[liveProfile.LiveSlot] = MouseLighting.Describe(mp.Lighting);
				}
			}
			bool failed = err != null;
			lock (stateLock)
			{
				if (gen == generation && failed != liveFailed)
				{
					// Fall back to software remaps while the mouse cannot run the profile itself (and back).
					liveFailed = failed;
					UpdateEngineLocked(force: true);
				}
			}
		}
		else
		{
			// Live profiles are off: leave a live slot the mouse may still be on (also after a restart or a
			// reconnect, when LiveSlot is not known). Costs one read when the mouse is already on its own slot.
			string err = liveProfile.Restore(m);
			switched = liveProfile.Switched;
			if (err != null)
			{
				Cfg.Log("leaving the live slot failed: " + err);
			}
			else if (switched)
			{
				Cfg.Log("live profiles off: " + liveProfile.Status);
			}
			if (mp.Name != lastMouseName)
			{
				// The user may have changed DPI with the mouse's buttons: send the new profile's values.
				m.InvalidateCache();
			}
			bool dpiOk = m.SetDpi(mp.Dpi);
			bool pollOk = m.SetPolling(mp.PollingRate);
			Cfg.Log($"mouse dpi {mp.Dpi}:{dpiOk} poll {mp.PollingRate}:{pollOk}");
		}
		lastMouseName = mp.Name;
		if (mp.Lighting != null)
		{
			ApplyLighting(m, mp.Lighting, force: false, switched: switched);
		}
		if (status != LiveStatus)
		{
			LiveStatus = status;
			Notify();
		}
	}

	// Per-profile mouse lighting, written to the slot the mouse is running (its storage byte is the slot number;
	// 0x01 only reaches slot 1), falling back to 0x01 if the mouse refuses that. Skipped when it is already showing.
	// force: send even if it was sent last (the user changed it). switched: the mouse just moved to another slot,
	// which shows its own stored lighting; nothing is sent when LiteSynapse already put this lighting there, so an
	// alt-tab in and out of a game does not rewrite the slots' lighting each time.
	private void ApplyLighting(RazerDevice m, MouseLightingSettings s, bool force, bool switched = false)
	{
		byte store = m.ActiveStorage();
		if (LiveWrite && !OnboardSlots.IsLiveSlot(store))
		{
			// The mouse is still on one of the user's own slots (it moves to a live slot next): their slot keeps
			// its lighting; the live slot write brings this one.
			lastLighting = null;
			return;
		}
		string want = MouseLighting.Describe(s);
		string key = want + "|" + store;
		if (!force && !switched && key == lastLighting)
		{
			return;
		}
		if (!force && switched && store <= OnboardSlots.SlotCount && slotLighting[store] == want)
		{
			lastLighting = key;
			return;
		}
		string err = MouseLighting.Apply(m, s, store);
		// Remembered either way. A zone the mouse refuses is refused again next time, and retrying on every
		// game start and desktop restore sent up to twelve commands each time. It is tried again when the
		// lighting or the profile changes, or the device is reset. (There is no 0x01 fallback: 0x01 reaches
		// slot 1 only, so for the slot the mouse is on it changed nothing visible and overwrote slot 1.)
		lastLighting = key;
		if (store >= 1 && store <= OnboardSlots.SlotCount)
		{
			slotLighting[store] = want;
		}
		Cfg.Log($"mouse lighting {want} (slot {store}): {err ?? "ok"}");
	}

	private void SyncKeyboard()
	{
		bool force = Interlocked.Exchange(ref kbForce, 0) != 0;
		KbProfile kp;
		lock (stateLock)
		{
			kp = FindKeyboard(ActiveKeyboard);
		}
		RazerDevice k = keyboard;
		if (k == null || kp == null)
		{
			return;
		}
		bool changed = kp.Name != lastKbName;
		if (changed)
		{
			// The brightness keys on the keyboard may have changed it: send the new profile's brightness. The
			// effect is only sent when it differs - two profiles with the same backlight switched on every
			// alt-tab used to send it again each time.
			k.InvalidateCache();
			lastKbName = kp.Name;
		}
		string name = KbLighting.Normalize(kp.Effect);
		string effect = name + "|" + kp.EffectSpeed;
		if (force || effect != lastKbEffect)
		{
			bool ok = KbLighting.Apply(k, name, kp.EffectSpeed, kp.Brightness);
			lastKbEffect = ok ? effect : null;
			Cfg.Log($"kb brightness {kp.Brightness}:{ok} effect {name}");
		}
		else
		{
			bool ok2 = k.SetBrightness(kp.Brightness);
			if (changed || !ok2)
			{
				Cfg.Log($"kb brightness {kp.Brightness}:{ok2}");
			}
		}
	}

	// The active keyboard profile's brightness, through the device worker like every other device write.
	public void ApplyKbBrightness()
	{
		if (keyboard != null && !DryRun)
		{
			RequestKeyboard(forceLighting: false);
		}
	}

	private void Prewrite(string profile)
	{
		MouseProfile mp;
		bool skip;
		lock (stateLock)
		{
			mp = FindMouse(profile);
			skip = !LiveWrite || ActiveMouse == profile;
		}
		RazerDevice m = mouse;
		if (mp == null || skip || m == null)
		{
			return;
		}
		if (gameInFront)
		{
			// Never write the mouse's memory while a game is in front: tried again when the game leaves.
			Cfg.Log("pre-write of \"" + profile + "\" skipped: a game is in front");
			skippedPrewrite = profile;
			return;
		}
		// And stop between commands if one comes to the front while it writes, rather than finishing thirty
		// flash writes under it.
		string err = liveProfile.Prewrite(m, mp, () => gameInFront);
		if (err != null)
		{
			Cfg.Log("pre-write failed: " + err);
			if (gameInFront)
			{
				skippedPrewrite = profile;
			}
		}
		else
		{
			Notify();
		}
	}

	// Finds the devices again. reset: also start over with handles and cached values of devices that are still
	// there (they may have been re-plugged or have lost their settings), and re-apply the state.
	private void SyncDevices(bool reset)
	{
		bool changed = false;
		RazerDevice m = mouse;
		if (m != null && m.Alive())
		{
			if (reset)
			{
				m.Reset();
				changed = true;
			}
		}
		else
		{
			RazerDevice found = RazerDevice.Find(NagaPid, "Naga");
			if (found != null || m != null)
			{
				mouse = found;
				m?.Dispose();
				changed = true;
				Cfg.Log((found != null) ? "Naga connected" : "Naga disconnected");
			}
		}
		RazerDevice k = keyboard;
		if (k != null && k.Alive())
		{
			if (reset)
			{
				k.Reset();
				changed = true;
			}
		}
		else
		{
			RazerDevice found2 = RazerDevice.Find(KeyboardPid, "BlackWidow Lite");
			if (found2 != null || k != null)
			{
				keyboard = found2;
				k?.Dispose();
				changed = true;
				Cfg.Log((found2 != null) ? "BlackWidow Lite connected" : "BlackWidow Lite disconnected");
			}
		}
		if (!changed)
		{
			return;
		}
		liveProfile.Forget();
		lastLighting = null;
		Array.Clear(slotLighting);
		lastMouseName = null;
		lastKbName = null;
		lastKbEffect = null;
		lock (stateLock)
		{
			// The software fallback depends on whether the mouse is there.
			UpdateEngineLocked(force: false);
		}
		RequestMouse(0);
		RequestKeyboard(forceLighting: true);
		Notify();
	}

	// ---- Power and session -----------------------------------------------------------------------------------

	public void OnSuspend()
	{
		lock (stateLock)
		{
			sleeping = true;
			UpdateEngineLocked(force: false);
		}
		worker.Cancel("prewrite");
		Cfg.Log("suspend: keyboard hook off");
	}

	public void OnResume()
	{
		lock (stateLock)
		{
			sleeping = false;
			UpdateEngineLocked(force: true);
		}
		// The devices were powered down and get new handles, nothing cached and the state applied again - once,
		// from TrayHost's settle timer after USB has come back. Posting it here as well ran it twice, the first
		// time often before the devices were back, which logged them as disconnected and flipped the input
		// engine to software remaps and back.
		Cfg.Log("resume");
		Notify();
	}

	public void OnSessionLock()
	{
		lock (stateLock)
		{
			locked = true;
			UpdateEngineLocked(force: false);
		}
		Cfg.Log("session locked: keyboard hook off");
	}

	public void OnSessionUnlock()
	{
		lock (stateLock)
		{
			locked = false;
			UpdateEngineLocked(force: true);
		}
		RequestMouse(0);
		RequestKeyboard(forceLighting: false);
		Cfg.Log("session unlocked");
		Notify();
	}

	// Puts the mouse back on the user's own slot (now, no delay), stops the worker and the keyboard hook.
	// Safe to call twice (session end and normal exit).
	public void Shutdown()
	{
		if (Interlocked.Exchange(ref shutdown, 1) != 0)
		{
			return;
		}
		try
		{
			worker.Cancel("prewrite");
			if (LiveWrite && !DryRun && mouse != null)
			{
				worker.Post("mouse", () =>
				{
					RazerDevice m = mouse;
					if (m != null)
					{
						string err = liveProfile.Restore(m);
						Cfg.Log("exit: " + (err ?? ((liveProfile.Status.Length > 0) ? liveProfile.Status : "mouse on its own slot")));
					}
				});
			}
		}
		catch
		{
		}
		worker.Dispose();
		engine.Dispose();
		mouse?.Dispose();
		keyboard?.Dispose();
		Cfg.FlushLog(500);
	}

	// ---- Start with Windows ----------------------------------------------------------------------------------

	private static void RepointStartup()
	{
		try
		{
			using RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
			string v = k?.GetValue("LiteSynapse") as string;
			string me = Environment.ProcessPath;
			if (v != null && me != null && v.Contains("scratch-workspaces", StringComparison.OrdinalIgnoreCase) && !me.Contains("scratch-workspaces", StringComparison.OrdinalIgnoreCase))
			{
				k.SetValue("LiteSynapse", "\"" + me + "\" --tray");
				Cfg.Log("startup entry re-pointed to " + me);
			}
		}
		catch
		{
		}
	}

	// On when the Run value exists and Task Manager's Startup apps has not turned it off.
	public static bool IsStartup()
	{
		return StartupApproved.State("LiteSynapse") == "enabled";
	}

	public static void SetStartup(bool on)
	{
		if (on)
		{
			using RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey);
			k.SetValue("LiteSynapse", "\"" + Environment.ProcessPath + "\" --tray");
			// Turned off in Task Manager before: Windows would keep skipping it.
			StartupApproved.Enable("LiteSynapse");
		}
		else
		{
			using RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
			k?.DeleteValue("LiteSynapse", throwOnMissingValue: false);
		}
	}
}
