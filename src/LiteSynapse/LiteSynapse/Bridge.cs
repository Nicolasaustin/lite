using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiteSynapse;

// The C# side of docs/bridge-api.md (v1.2). Every command runs on the window's UI thread; device I/O and other
// slow work runs on the service's device worker or a background task and replies when it is done, so the UI
// thread never waits. Edits follow the classic window (MainForm) and go through the service, which saves them
// and brings the devices and the keyboard hook up to date.
internal sealed class Bridge
{
	private const string MouseName = "Naga Left-Handed Edition";

	private const string KeyboardName = "BlackWidow Lite";

	// Commands that change nothing, so no state push follows them.
	private static readonly HashSet<string> ReadOnlyCommands = new HashSet<string>(StringComparer.Ordinal)
	{
		"getState", "getDeviceInfo", "runningApps", "getLog", "probeMouseLighting", "exportProfile", "calibrate",
		"openConfigFolder", "openMouseProperties", "openClassicWindow", "runSelfTest", "exitApp"
	};

	private static readonly string AppVersion = ReadVersion();

	private readonly Service svc;

	private readonly UiPrefs prefs;

	private readonly WebWindow window;

	private readonly Dictionary<string, Func<WebArgs, Task<object>>> commands;

	private DeviceInfoDto lastDeviceInfo;

	private bool onboardBusy;

	public Bridge(Service svc, UiPrefs prefs, WebWindow window)
	{
		this.svc = svc;
		this.prefs = prefs;
		this.window = window;
		commands = new Dictionary<string, Func<WebArgs, Task<object>>>(StringComparer.Ordinal)
		{
			["getState"] = Now(_ => State()),
			["getDeviceInfo"] = GetDeviceInfo,
			["selectProfile"] = Now(SelectProfile),
			["setAuto"] = Now(a =>
			{
				svc.SetAuto(a.Bool("on"));
				return State();
			}),
			["setLiveWrite"] = Now(a =>
			{
				svc.SetLiveWrite(a.Bool("on"));
				return State();
			}),
			["setStartup"] = Now(SetStartup),
			["setDpiStages"] = Now(SetDpiStages),
			["setPolling"] = Now(SetPolling),
			["setButton"] = Now(SetButton),
			["setKbBrightness"] = Now(SetKbBrightness),
			["setKbEffect"] = Now(SetKbEffect),
			["setSnapTap"] = Now(SetSnapTap),
			["setMacroKey"] = Now(SetMacroKey),
			["addProfile"] = Now(AddProfile),
			["renameProfile"] = Now(RenameProfile),
			["duplicateProfile"] = Now(DuplicateProfile),
			["deleteProfile"] = Now(DeleteProfile),
			["resetProfile"] = Now(ResetProfile),
			["setStartingProfile"] = Now(SetStartingProfile),
			["exportProfile"] = Now(ExportProfile),
			["importProfiles"] = ImportProfiles,
			["addGame"] = Now(AddGame),
			["setGameExes"] = Now(SetGameExes),
			["setGameKeyboard"] = Now(SetGameKeyboard),
			["removeGame"] = Now(RemoveGame),
			["runningApps"] = RunningApps,
			["saveMacro"] = Now(SaveMacro),
			["deleteMacro"] = Now(DeleteMacro),
			["onboardWrite"] = OnboardWrite,
			["calibrate"] = Now(Calibrate),
			["probeMouseLighting"] = ProbeMouseLighting,
			["setMouseLighting"] = Now(SetMouseLighting),
			["setCompetitiveSafe"] = Now(a =>
			{
				svc.SetCompetitiveSafe(a.Bool("on"));
				return State();
			}),
			["setGameKeyboardDefault"] = Now(SetGameKeyboardDefault),
			["linkRunningGame"] = Now(LinkRunningGame),
			["razerCheck"] = RazerCheck,
			["razerCleanup"] = RazerCleanup,
			["tuneDevices"] = TuneDevices,
			["runSelfTest"] = RunSelfTest,
			["openConfigFolder"] = Now(_ =>
			{
				Shell("explorer.exe", "\"" + Cfg.Dir + "\"");
				return null;
			}),
			["reloadConfig"] = Now(ReloadConfig),
			["openMouseProperties"] = Now(_ =>
			{
				Shell("control.exe", "main.cpl");
				return null;
			}),
			["openClassicWindow"] = Now(_ =>
			{
				UiHost.ShowClassicWindow(svc);
				return null;
			}),
			["getLog"] = GetLog,
			["setAccent"] = Now(SetAccent),
			// "Close this window when a game starts" (state.closeOnGame).
			["setCloseOnGame"] = Now(a =>
			{
				prefs.CloseOnGame = a.Bool("on");
				prefs.Save();
				return State();
			}),
			// Same as the tray menu's Exit: the tray loop ends and the service puts the mouse back on its own slot.
			["exitApp"] = Now(_ =>
			{
				if (!TrayHost.RequestExit())
				{
					throw new BridgeException("LiteSynapse is not running in the tray, so there is nothing to exit.");
				}
				return null;
			})
		};
		// Synapse may have been started or quit since the last check: look again whenever the window opens
		// (registry, services and the process list; a few milliseconds, off the UI thread).
		Task.Run(svc.RefreshRazer);
	}

	private static Func<WebArgs, Task<object>> Now(Func<WebArgs, object> command)
	{
		return a => Task.FromResult(command(a));
	}

	// Runs one message from the page and sends exactly one reply (later, for commands that wait for work).
	public async void Handle(string json)
	{
		if (!WebProtocol.TryParse(json, out WebRequest request, out string error))
		{
			Cfg.Log("window: bad message: " + error);
			if (request.Id >= 0)
			{
				window.Send(WebProtocol.Error(request.Id, error));
			}
			return;
		}
		if (!commands.TryGetValue(request.Cmd, out Func<WebArgs, Task<object>> command))
		{
			window.Send(WebProtocol.Error(request.Id, $"Unknown command \"{request.Cmd}\"."));
			return;
		}
		string reply;
		try
		{
			object result = await command(request.Args);
			reply = WebProtocol.Reply(request.Id, result);
		}
		catch (BridgeException ex)
		{
			reply = WebProtocol.Error(request.Id, ex.Message);
		}
		catch (Exception ex)
		{
			Cfg.Log($"window: {request.Cmd} failed: {ex}");
			reply = WebProtocol.Error(request.Id, $"{request.Cmd} failed: {ex.Message}");
		}
		window.Send(reply);
		if (!ReadOnlyCommands.Contains(request.Cmd))
		{
			window.RequestStatePush();
		}
	}

	// ---- State ---------------------------------------------------------------------------------------------

	public StateDto State()
	{
		StateDto s = new StateDto
		{
			Version = AppVersion,
			Mouse = new DeviceDto
			{
				Connected = svc.Mouse != null,
				Name = MouseName
			},
			Keyboard = new DeviceDto
			{
				Connected = svc.Kb != null,
				Name = KeyboardName
			},
			Auto = svc.Auto,
			InGame = svc.InGame,
			LiveWrite = svc.LiveWrite,
			Startup = IsStartup(),
			LiveStatus = svc.LiveStatus ?? "",
			ActiveMouse = svc.ActiveMouse ?? "",
			ActiveKeyboard = svc.ActiveKeyboard ?? "",
			DefaultMouse = svc.DefaultMouse ?? "",
			DefaultKeyboard = svc.DefaultKeyboard ?? "",
			Calibration = CalibrationLabels(),
			ButtonIds = InputEngine.ButtonIds,
			Onboard = OnboardNames(),
			LiveSlots = svc.LiveSlots ?? new int[0],
			Accent = prefs.Accent,
			CompetitiveSafe = svc.CompetitiveSafe,
			GameKeyboardDefault = svc.GameKeyboardDefault ?? "",
			LastGameExe = svc.LastGameExe ?? "",
			ElevatedGame = svc.ElevatedGame,
			Razer = Razer(svc.Razer),
			Tuning = Tuning(svc.Tuning),
			CloseOnGame = prefs.CloseOnGame
		};
		WebState.FillStore(s, svc.Store, svc.Games);
		return s;
	}

	// Button id -> name of the key the button sends (as Windows names it), for calibrated buttons.
	private Dictionary<string, string> CalibrationLabels()
	{
		Dictionary<string, string> labels = new Dictionary<string, string>();
		foreach (KeyValuePair<string, BaseKey> kv in svc.Base ?? new Dictionary<string, BaseKey>())
		{
			if (kv.Value == null)
			{
				continue;
			}
			string name = Program.KeyName(kv.Value.Sc, kv.Value.Ext);
			labels[kv.Key] = string.IsNullOrEmpty(name) ? $"scan code {kv.Value.Sc}" : name;
		}
		return labels;
	}

	private Dictionary<string, string> OnboardNames()
	{
		try
		{
			return svc.OnboardNames ?? new Dictionary<string, string>();
		}
		catch (Exception ex)
		{
			Cfg.Log("window: onboard slots: " + ex.Message);
			return new Dictionary<string, string>();
		}
	}

	private static TuningDto Tuning(DeviceTuningState t)
	{
		if (t == null)
		{
			return new TuningDto();
		}
		return new TuningDto
		{
			Checked = t.Checked,
			UsbSuspend = string.IsNullOrEmpty(t.UsbSuspend) ? "unknown" : t.UsbSuspend,
			BackgroundCap = t.BackgroundCap,
			Acceleration = t.Acceleration,
			PointerSpeed = t.PointerSpeed,
			Devices = (t.Devices ?? new List<TunedDevice>()).Where((TunedDevice d) => d != null).Select((TunedDevice d) => new TunedDeviceDto
			{
				Name = d.Name ?? "",
				PowerSaving = d.PowerSaving
			}).ToList()
		};
	}

	private static RazerDto Razer(RazerLeftovers r)
	{
		if (r == null)
		{
			return new RazerDto();
		}
		return new RazerDto
		{
			Checked = r.Checked,
			SynapseInstalled = r.SynapseInstalled,
			SynapseRunning = r.SynapseRunning,
			GmsService = string.IsNullOrEmpty(r.GmsService) ? "absent" : r.GmsService,
			FilterDrivers = (r.FilterDrivers ?? new List<string>()).ToList(),
			RzCommon = r.RzCommon,
			Autostart = (r.Autostart ?? new List<string>()).ToList(),
			Services = (r.Services ?? new List<RazerService>()).Where((RazerService s) => s != null).Select((RazerService s) => new RazerServiceDto
			{
				Name = s.Name ?? "",
				Label = s.Label ?? s.Name ?? "",
				State = s.State ?? "auto",
				Driver = s.Driver,
				Network = s.Network
			}).ToList(),
			UpgradeTask = string.IsNullOrEmpty(r.UpgradeTask) ? "absent" : r.UpgradeTask,
			RunOnce = (r.RunOnce ?? new List<string>()).ToList(),
			Tasks = (r.Tasks ?? new List<RazerTask>()).Where((RazerTask t) => t != null).Select((RazerTask t) => new RazerTaskDto
			{
				Path = t.Path ?? "",
				State = t.State ?? "ready"
			}).ToList(),
			FirewallRules = r.FirewallRules
		};
	}

	private static bool IsStartup()
	{
		try
		{
			return Service.IsStartup();
		}
		catch
		{
			return false;
		}
	}

	private static string ReadVersion()
	{
		string v = typeof(Bridge).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
		int plus = v.IndexOf('+');
		return (plus >= 0) ? v.Substring(0, plus) : v;
	}

	// ---- Profiles ------------------------------------------------------------------------------------------

	private ProfileStore Store => svc.Store;

	private MouseProfile ActiveMouseProfile()
	{
		return Store.Mouse.FirstOrDefault(p => p != null && p.Name == svc.ActiveMouse) ?? throw new BridgeException("No mouse profile is active.");
	}

	private KbProfile ActiveKeyboardProfile()
	{
		return Store.Keyboard.FirstOrDefault(p => p != null && p.Name == svc.ActiveKeyboard) ?? throw new BridgeException("No keyboard profile is active. Pick one first.");
	}

	// Same as the tray panel: picking a profile turns automatic switching off.
	private object SelectProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string name = a.Str("name");
		if (mouse)
		{
			WebOps.FindMouse(Store, name);
			svc.SelectMouse(name);
		}
		else
		{
			if (name.Length > 0)
			{
				WebOps.FindKeyboard(Store, name);
			}
			svc.SelectKeyboard((name.Length == 0) ? "(none)" : name);
		}
		return State();
	}

	private object SetStartup(WebArgs a)
	{
		bool on = a.Bool("on");
		try
		{
			Service.SetStartup(on);
		}
		catch (Exception ex)
		{
			throw new BridgeException("Could not change the Windows startup entry: " + ex.Message);
		}
		return State();
	}

	// The service saves the profile and puts it on the mouse (after a short pause that folds quick edits).
	private object SetDpiStages(WebArgs a)
	{
		MouseProfile p = ActiveMouseProfile();
		List<int> stages = a.Ints("stages", WebOps.MinDpi, WebOps.MaxDpi);
		int active = a.Int("active", 1, WebOps.MaxDpiStages);
		WebOps.SetDpiStages(p, stages, active);
		svc.SaveProfiles(p.Name);
		svc.ApplyMousePerformance();
		return State();
	}

	private object SetPolling(WebArgs a)
	{
		MouseProfile p = ActiveMouseProfile();
		WebOps.SetPolling(p, a.Int("hz", 125, 1000));
		svc.SaveProfiles(p.Name);
		svc.ApplyMousePerformance();
		return State();
	}

	private object SetButton(WebArgs a)
	{
		MouseProfile p = ActiveMouseProfile();
		WebOps.SetButton(p, InputEngine.ButtonIds, a.Str("id"), a.Str("key", optional: true), a.Strings("mods", optional: true));
		svc.SaveProfiles(p.Name);
		return State();
	}

	private object SetKbBrightness(WebArgs a)
	{
		KbProfile p = ActiveKeyboardProfile();
		p.Brightness = a.Int("value", 0, 100);
		svc.SaveProfiles();
		svc.ApplyKbLighting(p);
		return State();
	}

	private object SetKbEffect(WebArgs a)
	{
		KbProfile p = ActiveKeyboardProfile();
		WebOps.SetKbEffect(p, a.Str("effect"), a.Int("speed", 1, 3, Math.Clamp(p.EffectSpeed, 1, 3)));
		svc.SaveProfiles();
		svc.ApplyKbLighting(p);
		return State();
	}

	private object SetSnapTap(WebArgs a)
	{
		KbProfile p = ActiveKeyboardProfile();
		p.SnapTap = a.Bool("on");
		svc.SaveProfiles();
		svc.Reapply();
		return State();
	}

	private object SetMacroKey(WebArgs a)
	{
		KbProfile p = ActiveKeyboardProfile();
		WebOps.SetMacroKey(Store, p, a.Str("key"), a.Str("macro", optional: true));
		svc.SaveProfiles();
		svc.Reapply();
		return State();
	}

	// The new profile becomes the active one (as in the classic window), so the user edits it next.
	private object AddProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string name = a.Str("name");
		if (mouse)
		{
			MouseProfile template = Store.Mouse.FirstOrDefault(p => p != null && p.Name == svc.ActiveMouse);
			MouseProfile p = WebOps.AddMouse(Store, name, template);
			svc.SaveProfiles();
			svc.SelectMouse(p.Name);
		}
		else
		{
			KbProfile template = Store.Keyboard.FirstOrDefault(p => p != null && p.Name == svc.ActiveKeyboard);
			KbProfile p = WebOps.AddKeyboard(Store, name, template);
			svc.SaveProfiles();
			svc.SelectKeyboard(p.Name);
		}
		return State();
	}

	private object RenameProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string from = a.Str("from");
		string to = a.Str("to");
		if (mouse)
		{
			// Read before the rename: DefaultMouse falls back to the first profile once the name is gone.
			bool wasStart = from == svc.DefaultMouse;
			string name = WebOps.RenameMouse(Store, svc.Games, from, to);
			if (name == from)
			{
				return State();
			}
			if (svc.ActiveMouse == from)
			{
				svc.ActiveMouse = name;
			}
			if (wasStart)
			{
				svc.SetDefaults(name, null);
			}
		}
		else
		{
			bool wasStart = from == svc.DefaultKeyboard;
			bool wasGameDefault = from == svc.GameKeyboardDefault;
			string name = WebOps.RenameKeyboard(Store, svc.Games, from, to);
			if (name == from)
			{
				return State();
			}
			if (svc.ActiveKeyboard == from)
			{
				svc.ActiveKeyboard = name;
			}
			if (wasStart)
			{
				svc.SetDefaults(null, name);
			}
			if (wasGameDefault)
			{
				svc.SetGameKeyboardDefault(name);
			}
		}
		svc.SaveProfiles();
		svc.SaveGames();
		svc.ForceRecheck();
		return State();
	}

	// Result: { name } of the copy, which becomes the active profile (as in the classic window).
	private object DuplicateProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string name = a.Str("name");
		string copy;
		if (mouse)
		{
			copy = WebOps.DuplicateMouse(Store, name).Name;
			svc.SaveProfiles();
			svc.SelectMouse(copy);
		}
		else
		{
			copy = WebOps.DuplicateKeyboard(Store, name).Name;
			svc.SaveProfiles();
			svc.SelectKeyboard(copy);
		}
		return new NameResult(copy);
	}

	private object DeleteProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string name = a.Str("name");
		if (mouse)
		{
			WebOps.FindMouse(Store, name);
			if (name == svc.DefaultMouse)
			{
				throw new BridgeException(StartingProfileMessage(name));
			}
			WebOps.DeleteMouse(Store, svc.Games, name);
			if (svc.ActiveMouse == name)
			{
				svc.ApplyNames(svc.DefaultMouse, svc.ActiveKeyboard, inGame: false);
			}
		}
		else
		{
			WebOps.FindKeyboard(Store, name);
			if (name == svc.DefaultKeyboard)
			{
				throw new BridgeException(StartingProfileMessage(name));
			}
			bool wasGameDefault = name == svc.GameKeyboardDefault;
			WebOps.DeleteKeyboard(Store, svc.Games, name);
			if (wasGameDefault)
			{
				svc.SetGameKeyboardDefault("");
			}
			if (svc.ActiveKeyboard == name)
			{
				svc.ApplyNames(svc.ActiveMouse, svc.DefaultKeyboard, svc.InGame);
			}
		}
		svc.SaveProfiles();
		svc.SaveGames();
		svc.ForceRecheck();
		return State();
	}

	private static string StartingProfileMessage(string name)
	{
		return $"\"{name}\" is the starting profile (used when LiteSynapse starts and when no game is running). Choose another starting profile first, then delete this one.";
	}

	// Mouse: clears the button remaps. Keyboard: clears the macro keys.
	private object ResetProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string name = a.Str("name");
		if (mouse)
		{
			WebOps.ResetMouse(WebOps.FindMouse(Store, name));
			svc.SaveProfiles(name);
		}
		else
		{
			WebOps.ResetKeyboard(WebOps.FindKeyboard(Store, name));
			svc.SaveProfiles();
			svc.Reapply();
		}
		return State();
	}

	private object SetStartingProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string name = a.Str("name");
		if (mouse)
		{
			WebOps.FindMouse(Store, name);
			svc.SetDefaults(name, null);
		}
		else
		{
			if (name.Length > 0)
			{
				WebOps.FindKeyboard(Store, name);
			}
			svc.SetDefaults(null, name);
		}
		if (!svc.InGame)
		{
			// Nothing is running: the new starting profile applies right away.
			svc.ForceRecheck();
			svc.ApplyNames(mouse ? name : svc.ActiveMouse, mouse ? svc.ActiveKeyboard : name, inGame: false);
		}
		return State();
	}

	// Native Save dialog; writes the profile as LiteSynapse stores it. Result: { path } (null = cancelled).
	private object ExportProfile(WebArgs a)
	{
		bool mouse = a.IsMouseKind();
		string name = a.Str("name");
		object profile = mouse ? WebOps.FindMouse(Store, name) : WebOps.FindKeyboard(Store, name);
		string path;
		using (SaveFileDialog dlg = new SaveFileDialog
		{
			Title = "Export profile",
			FileName = WebOps.SafeFileName(name) + ".json",
			Filter = "JSON (*.json)|*.json",
			OverwritePrompt = true
		})
		{
			if (window.ShowModal(dlg) != DialogResult.OK)
			{
				return new PathResult(null);
			}
			path = dlg.FileName;
		}
		try
		{
			File.WriteAllText(path, JsonSerializer.Serialize(profile, profile.GetType(), new JsonSerializerOptions
			{
				WriteIndented = true
			}));
		}
		catch (Exception ex)
		{
			throw new BridgeException("Could not write the file: " + ex.Message);
		}
		Cfg.Log("exported \"" + name + "\" to " + path);
		return new PathResult(path);
	}

	// Native Open dialog for Synapse exports (.synapse4 profiles, .xml macros). Result: { message } ("" = cancelled).
	private async Task<object> ImportProfiles(WebArgs a)
	{
		string[] files;
		using (OpenFileDialog dlg = new OpenFileDialog
		{
			Title = "Import Synapse profiles",
			Filter = "Synapse exports (*.synapse4;*.xml)|*.synapse4;*.xml",
			Multiselect = true
		})
		{
			if (window.ShowModal(dlg) != DialogResult.OK)
			{
				return new MessageResult("");
			}
			files = dlg.FileNames;
		}
		string summary;
		try
		{
			summary = await Task.Run(() => Importer.Run(files));
		}
		catch (Exception ex)
		{
			Cfg.Log("import failed: " + ex);
			throw new BridgeException("Import failed: " + ex.Message);
		}
		Cfg.Log("import: " + summary.Trim().Replace(Environment.NewLine, "; ").Replace("\n", "; "));
		svc.Reload();
		svc.ForceRecheck();
		svc.Reapply();
		return new MessageResult(summary.Trim());
	}

	// ---- Games ---------------------------------------------------------------------------------------------

	private object AddGame(WebArgs a)
	{
		string profile = a.Str("profile");
		WebOps.AddGame(Store, svc.Games, profile);
		svc.SaveGames(profile);
		return State();
	}

	private object SetGameExes(WebArgs a)
	{
		string profile = a.Str("profile");
		WebOps.SetGameExes(Store, svc.Games, profile, a.Strings("exes"));
		svc.SaveGames(profile);
		svc.ForceRecheck();
		return State();
	}

	private object SetGameKeyboard(WebArgs a)
	{
		string profile = a.Str("profile");
		WebOps.SetGameKeyboard(Store, svc.Games, profile, a.Str("keyboardProfile", optional: true) ?? "");
		svc.SaveGames(profile);
		svc.ForceRecheck();
		return State();
	}

	private object RemoveGame(WebArgs a)
	{
		WebOps.RemoveGame(svc.Games, a.Str("profile"));
		svc.SaveGames();
		svc.ForceRecheck();
		return State();
	}

	private object SetGameKeyboardDefault(WebArgs a)
	{
		string name = a.Str("keyboardProfile", optional: true) ?? "";
		if (name.Length > 0)
		{
			WebOps.FindKeyboard(Store, name);
		}
		svc.SetGameKeyboardDefault(name);
		return State();
	}

	// Result: { exe } that was linked, or null when no program was in front since LiteSynapse started.
	private object LinkRunningGame(WebArgs a)
	{
		string profile = a.Str("profile");
		WebOps.FindMouse(Store, profile);
		return new ExeResult(svc.LinkRunningGame(profile));
	}

	// Programs with a visible window (name.exe), sorted, each once. Process enumeration is slow: background.
	private static async Task<object> RunningApps(WebArgs a)
	{
		return await Task.Run(() =>
		{
			HashSet<int> withWindow = WebNative.ProcessesWithWindows();
			List<(int, string)> processes = new List<(int, string)>();
			foreach (Process p in Process.GetProcesses())
			{
				try
				{
					if (withWindow.Contains(p.Id))
					{
						processes.Add((p.Id, p.ProcessName));
					}
				}
				catch
				{
					// The process exited or cannot be queried.
				}
				finally
				{
					p.Dispose();
				}
			}
			string selfExe = Path.GetFileName(Environment.ProcessPath ?? "LiteSynapse.exe");
			return (object)WebOps.RunningAppNames(processes, withWindow, Environment.ProcessId, selfExe);
		});
	}

	// ---- Macros --------------------------------------------------------------------------------------------

	private object SaveMacro(WebArgs a)
	{
		string original = a.Str("originalName", optional: true);
		WebArgs m = a.Obj("macro");
		List<MacroStep> steps = m.Objects("steps").Select(s => new MacroStep
		{
			Vk = s.Int("vk", 0, 255),
			Ext = s.Has("ext") && s.Bool("ext"),
			Down = s.Has("down") && s.Bool("down"),
			DelayMs = s.Int("delayMs", 0, WebOps.MaxMacroDelayMs, 0)
		}).ToList();
		Macro macro = WebOps.CleanMacro(m.Str("name"), steps);
		WebOps.SaveMacro(Store, string.IsNullOrEmpty(original) ? null : original, macro);
		svc.SaveProfiles();
		svc.Reapply();
		return State();
	}

	private object DeleteMacro(WebArgs a)
	{
		WebOps.DeleteMacro(Store, a.Str("name"));
		svc.SaveProfiles();
		svc.Reapply();
		return State();
	}

	// ---- Mouse: onboard memory, calibration, lighting --------------------------------------------------------

	// Writes a profile into a slot on the device worker; progress texts are pushed while it runs.
	// Result: { ok, message }.
	private async Task<object> OnboardWrite(WebArgs a)
	{
		int slot = a.Int("slot", 2, 5);
		string name = a.Str("profile");
		WebOps.FindMouse(Store, name);
		WebOps.CheckOnboardSlot(slot, svc.LiveSlots);
		if (svc.Mouse == null)
		{
			throw new BridgeException("The Naga is not connected.");
		}
		if (onboardBusy)
		{
			throw new BridgeException("A profile is already being written to the mouse. Wait until it is done.");
		}
		onboardBusy = true;
		try
		{
			window.Send(WebProtocol.Progress("onboard", $"Writing \"{name}\" to slot {slot}..."));
			string err = await svc.WriteOnboardSlot(slot, name, text => window.SendFromAnyThread(WebProtocol.Progress("onboard", ProgressText(text))));
			if (err != null)
			{
				return new OnboardResult(false, "Failed: " + err);
			}
			return new OnboardResult(true, $"Wrote \"{name}\" to slot {slot} ({OnboardMemory.SlotNames[slot]}). Press Cycle Up Profile on the mouse to use it.");
		}
		finally
		{
			onboardBusy = false;
		}
	}

	private static string ProgressText(string text)
	{
		text = (text ?? "").Trim();
		if (text.Length == 0 || text.EndsWith(".") || text.StartsWith("Not written", StringComparison.OrdinalIgnoreCase))
		{
			return text;
		}
		return text + "...";
	}

	// Opens the native calibration window; the state is pushed again when it closes.
	private object Calibrate(WebArgs a)
	{
		UiHost.Calibrate(svc, window.RequestStatePush);
		return null;
	}

	// Read-only probe of the lighting zones the mouse answers for. Result: { zones: [{ id, name }] }.
	private async Task<object> ProbeMouseLighting(WebArgs a)
	{
		if (svc.Mouse == null)
		{
			throw new BridgeException("The Naga is not connected.");
		}
		// The mouse may be unplugged by the time the worker gets to it.
		List<(int Id, string Name)> zones = await svc.RunOnDevices("uiLightingProbe", () => (svc.Mouse is RazerDevice m) ? MouseLighting.Probe(m) : null);
		return new ZonesResult((zones ?? new List<(int, string)>()).Select(z => new ZoneDto(z.Id, z.Name)).ToList());
	}

	// Saved on that mouse profile (default: the active one); the service shows it now if the profile is active.
	private object SetMouseLighting(WebArgs a)
	{
		string name = a.Str("profile", optional: true);
		MouseProfile p = string.IsNullOrEmpty(name) ? ActiveMouseProfile() : WebOps.FindMouse(Store, name);
		// zone: "all" (default) or one zone ("scroll", "logo", "side"). Color and brightness keep that zone's values
		// when left out (e.g. switching the effect to "off").
		string zone = a.Str("zone", optional: true);
		MouseLightingSettings current = (string.IsNullOrEmpty(zone) || zone == "all") ? p.Lighting : p.Lighting?.For(zone);
		string color = a.Str("color", optional: true) ?? current?.Color ?? "#00FF00";
		int brightness = a.Int("brightness", 0, 100, Math.Clamp(current?.Brightness ?? 100, 0, 100));
		WebOps.SetMouseLight(p, zone, WebOps.MouseLight(a.Str("effect"), color, brightness));
		svc.SaveProfiles();
		string err = svc.ApplyMouseLighting(p);
		if (err != null)
		{
			throw new BridgeException("Saved, but the lighting could not be applied: " + err);
		}
		return State();
	}

	// Result: { mouse: { dpi, polling, slot, slotName } | null, keyboard: { brightness } | null }.
	private async Task<object> GetDeviceInfo(WebArgs a)
	{
		DeviceInfoDto info = await svc.RunOnDevices("uiDeviceInfo", () =>
		{
			DeviceInfoDto d = new DeviceInfoDto();
			RazerDevice m = svc.Mouse;
			if (m != null)
			{
				int? slot = m.GetActiveSlot();
				d.Mouse = new MouseInfoDto
				{
					Dpi = m.GetDpi()?.x,
					Polling = m.GetPolling(),
					Slot = slot,
					SlotName = (slot >= 1 && slot <= 5) ? OnboardMemory.SlotNames[slot.Value] : null
				};
			}
			RazerDevice k = svc.Kb;
			if (k != null)
			{
				d.Keyboard = new KeyboardInfoDto
				{
					Brightness = k.GetBrightness()
				};
			}
			return d;
		});
		// null: a newer request replaced this one before it ran.
		if (info != null)
		{
			lastDeviceInfo = info;
		}
		return info ?? lastDeviceInfo ?? new DeviceInfoDto();
	}

	// ---- Settings, Razer leftovers, tools --------------------------------------------------------------------

	private object SetAccent(WebArgs a)
	{
		string accent = a.Str("accent");
		if (!WebOps.IsAccent(accent))
		{
			throw new BridgeException($"Unknown accent \"{accent}\".");
		}
		prefs.Accent = accent;
		prefs.Save();
		return State();
	}

	private async Task<object> RazerCheck(WebArgs a)
	{
		await Task.Run(svc.RefreshRazer);
		return State();
	}

	// Runs the elevated step (UAC prompt) in the background. Result: { message }.
	private async Task<object> RazerCleanup(WebArgs a)
	{
		string action = a.Str("action");
		if (!RazerLeftovers.Actions.Contains(action))
		{
			throw new BridgeException($"Unknown clean-up step \"{action}\".");
		}
		string message = await svc.RazerCleanupAsync(action);
		return new MessageResult(message ?? "");
	}

	// Turns Windows' USB power saving off for the two devices (UAC prompt). Result: { message }.
	private async Task<object> TuneDevices(WebArgs a)
	{
		string message = await svc.TuneDevicesAsync();
		return new MessageResult(message ?? "");
	}

	// A console window running "LiteSynapse.exe testall" (device and logic self-test). The test drives the mouse
	// itself, so this process leaves the devices alone until it ends (it holds Program.SelfTestMutex meanwhile)
	// and then syncs them again.
	private async Task<object> RunSelfTest(WebArgs a)
	{
		string exe = Environment.ProcessPath;
		if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
		{
			throw new BridgeException("Cannot find LiteSynapse.exe.");
		}
		await Task.Run(svc.BeginSelfTest);
		try
		{
			// start /b /wait: the test attaches to this console and the prompt waits until it finished.
			string args = $"/s /k \"title LiteSynapse self-test & start \"\" /b /wait \"{exe}\" testall\"";
			Shell("cmd.exe", args, Path.GetDirectoryName(exe));
		}
		catch
		{
			svc.EndSelfTest();
			throw;
		}
		_ = Task.Run(() =>
		{
			try
			{
				WaitForSelfTest();
			}
			finally
			{
				svc.EndSelfTest();
			}
		});
		return null;
	}

	// Returns once the self-test process has started (it holds Program.SelfTestMutex) and ended: at most 20 s for
	// it to start, 15 minutes to run.
	private static void WaitForSelfTest()
	{
		Mutex m;
		Stopwatch sw = Stopwatch.StartNew();
		while (!Mutex.TryOpenExisting(Program.SelfTestMutex, out m))
		{
			if (sw.Elapsed > TimeSpan.FromSeconds(20))
			{
				return;
			}
			Thread.Sleep(200);
		}
		using (m)
		{
			try
			{
				if (m.WaitOne(TimeSpan.FromMinutes(15)))
				{
					m.ReleaseMutex();
				}
			}
			catch (AbandonedMutexException)
			{
				// The test window was closed half way: the mutex is ours now.
				m.ReleaseMutex();
			}
		}
	}

	private object ReloadConfig(WebArgs a)
	{
		svc.Reload();
		svc.ForceRecheck();
		svc.Reapply();
		return State();
	}

	private static async Task<object> GetLog(WebArgs a)
	{
		int lines = a.Int("lines", 1, 5000, 200);
		return await Task.Run(() => (object)Cfg.ReadLogTail(lines));
	}

	private static void Shell(string file, string args, string workingDir = null)
	{
		try
		{
			ProcessStartInfo psi = new ProcessStartInfo(file, args)
			{
				UseShellExecute = true
			};
			if (workingDir != null)
			{
				psi.WorkingDirectory = workingDir;
			}
			Process.Start(psi)?.Dispose();
		}
		catch (Exception ex)
		{
			throw new BridgeException($"Could not start {file}: {ex.Message}");
		}
	}
}
