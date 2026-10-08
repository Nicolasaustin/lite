using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace LiteSynapse;

internal sealed class Service
{
	private class AppSettings
	{
		public bool LiveWrite { get; set; } = true;

		public string DefaultMouse { get; set; } = "CS2";

		public string DefaultKeyboard { get; set; } = "gamer - S3";
	}

	private readonly InputEngine engine = new InputEngine();

	private RazerDevice mouse = RazerDevice.Find(141, "Naga");

	private RazerDevice keyboard = RazerDevice.Find(565, "BlackWidow Lite");

	private ProfileStore store;

	private List<GameRule> games;

	private string currentKey = "\0";

	private AppSettings settings;

	private static readonly string selfExe = Path.GetFileName(Environment.ProcessPath ?? "LiteSynapse.exe");

	public bool Auto = true;

	public string ActiveMouse = "";

	public string ActiveKeyboard = "";

	public bool InGame;

	public string LiveStatus = "";

	public bool LiveWrite = true;

	public bool DryRun;

	private readonly LiveProfile liveProfile = new LiveProfile();

	private readonly object liveLock = new object();

	private readonly object stateLock = new object();

	private int liveToken;

	private const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	public string TrayText { get; private set; } = "LiteSynapse";

	public ProfileStore Store => store;

	public List<GameRule> Games => games;

	public RazerDevice Mouse => mouse;

	public RazerDevice Kb => keyboard;

	public InputEngine Engine => engine;

	public Dictionary<string, BaseKey> Base => engine.Base;

	public string DefaultMouse
	{
		get
		{
			string text;
			if (!store.Mouse.Any((MouseProfile p) => p.Name == settings.DefaultMouse))
			{
				text = store.Mouse.FirstOrDefault()?.Name;
				if (text == null)
				{
					return "";
				}
			}
			else
			{
				text = settings.DefaultMouse;
			}
			return text;
		}
	}

	public string DefaultKeyboard
	{
		get
		{
			if (!store.Keyboard.Any((KbProfile p) => p.Name == settings.DefaultKeyboard))
			{
				return "";
			}
			return settings.DefaultKeyboard;
		}
	}

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

	public void Reload()
	{
		store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		games = Cfg.Load<List<GameRule>>(Cfg.Games);
		if (games.Count == 0)
		{
			games = DefaultGames.Make(store);
			Cfg.Save(Cfg.Games, games);
		}
		foreach (GameRule g in games)
		{
			if (!string.IsNullOrEmpty(g.KeyboardProfile) && !store.Keyboard.Any((KbProfile p) => p.Name == g.KeyboardProfile))
			{
				g.KeyboardProfile = "";
			}
		}
		engine.Base = Cfg.Load<Dictionary<string, BaseKey>>(Cfg.Calib);
	}

	public void ForceRecheck()
	{
		currentKey = "\0";
	}

	public void SaveGames()
	{
		Cfg.Save(Cfg.Games, games);
	}

	public void SetCalibration(Dictionary<string, BaseKey> learned)
	{
		Cfg.Save(Cfg.Calib, learned);
		engine.Base = learned;
		ForceRecheck();
	}

	public void SetDefaults(string mouseName, string keyboardName)
	{
		if (mouseName != null)
		{
			settings.DefaultMouse = mouseName;
		}
		if (keyboardName != null)
		{
			settings.DefaultKeyboard = keyboardName;
		}
		Cfg.Save(SettingsFile, settings);
		Notify();
	}

	public void EnsureDevices()
	{
		bool changed = false;
		if (mouse == null || !mouse.Alive())
		{
			RazerDevice m = RazerDevice.Find(141, "Naga");
			if (m != null)
			{
				mouse = m;
				changed = true;
				Cfg.Log("Naga connected");
			}
			else
			{
				mouse = null;
			}
		}
		if (keyboard == null || !keyboard.Alive())
		{
			RazerDevice k = RazerDevice.Find(565, "BlackWidow Lite");
			if (k != null)
			{
				keyboard = k;
				changed = true;
				Cfg.Log("BlackWidow Lite connected");
			}
			else
			{
				keyboard = null;
			}
		}
		if (changed)
		{
			lock (liveLock)
			{
				liveProfile.Forget();
			}
			ForceRecheck();
			ApplyNames(ActiveMouse, ActiveKeyboard, InGame);
		}
	}

	public void Start()
	{
		Check();
		if (Auto && currentKey == "\0")
		{
			currentKey = "";
			ApplyNames(DefaultMouse, DefaultKeyboard, inGame: false);
		}
	}

	public void Check()
	{
		if (!Auto)
		{
			return;
		}
		string exe = Native.ForegroundExe();
		if (exe == null || string.Equals(exe, selfExe, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		GameRule rule = games.FirstOrDefault((GameRule g) => g.Exes.Any((string x) => string.Equals(x, exe, StringComparison.OrdinalIgnoreCase)));
		string key = ((rule == null) ? "" : (rule.Profile + "|" + rule.KeyboardProfile));
		if (!(key == currentKey))
		{
			currentKey = key;
			ApplyNames(rule?.Profile ?? DefaultMouse, (rule == null) ? DefaultKeyboard : (rule.KeyboardProfile ?? ""), rule != null);
		}
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
			Check();
		}
		Notify();
	}

	public void ApplyNames(string mouseName, string kbName, bool inGame, bool forceLive = false)
	{
		MouseProfile mp;
		KbProfile kp;
		bool live;
		lock (stateLock)
		{
			mp = store.Mouse.FirstOrDefault((MouseProfile p) => p.Name == mouseName);
			kp = (string.IsNullOrEmpty(kbName) ? null : store.Keyboard.FirstOrDefault((KbProfile p) => p.Name == kbName));
			live = LiveWrite && mouse != null && mp != null && !DryRun;
			if (!DryRun)
			{
				engine.SetProfile(live ? null : mp, kp, store.Macros, inGame);
			}
			ActiveMouse = mouseName;
			ActiveKeyboard = kbName ?? "";
			InGame = inGame;
			string t = "LiteSynapse - " + mouseName;
			TrayText = ((t.Length > 120) ? t.Substring(0, 120) : t);
		}
		Cfg.Log("profile -> " + mouseName + " / " + (kp?.Name ?? "(none)"));
		if (DryRun)
		{
			Notify();
			return;
		}
		bool writeLive = inGame | forceLive;
		int token = Interlocked.Increment(ref liveToken);
		Task.Run(() =>
		{
			lock (liveLock)
			{
				if (token != liveToken)
				{
					return;
				}
				try
				{
					RazerDevice razerDevice = mouse;
					RazerDevice razerDevice2 = keyboard;
					if (live && razerDevice != null)
					{
						string text = (writeLive ? liveProfile.Apply(razerDevice, mp) : liveProfile.Restore(razerDevice));
						if (text != null)
						{
							Cfg.Log("live profile failed: " + text);
							LiveStatus = "Could not write to the mouse: " + text;
							engine.SetProfile(mp, kp, store.Macros, inGame);
						}
						else
						{
							LiveStatus = liveProfile.Status;
							Cfg.Log("live: " + ((liveProfile.Status.Length > 0) ? liveProfile.Status : "desktop"));
						}
						if (!writeLive && mp != null)
						{
							razerDevice.SetPolling(mp.PollingRate);
						}
					}
					else if (razerDevice != null && mp != null)
					{
						bool value = razerDevice.SetDpi(mp.Dpi);
						bool value2 = razerDevice.SetPolling(mp.PollingRate);
						Cfg.Log($"mouse dpi {mp.Dpi}:{value} poll {mp.PollingRate}:{value2}");
					}
					if (razerDevice2 != null && kp != null)
					{
						Cfg.Log($"kb brightness {kp.Brightness}:{razerDevice2.SetBrightness(kp.Brightness)}");
					}
				}
				catch (Exception ex)
				{
					Cfg.Log("device: " + ex.Message);
				}
			}
			Notify();
		});
		Notify();
	}

	public void SetLiveWrite(bool on)
	{
		LiveWrite = on;
		settings.LiveWrite = on;
		Cfg.Save(SettingsFile, settings);
		ForceRecheck();
		ApplyNames(ActiveMouse, ActiveKeyboard, InGame);
	}

	public void Shutdown()
	{
		try
		{
			if (LiveWrite && mouse != null && !DryRun)
			{
				lock (liveLock)
				{
					liveToken++;
					liveProfile.Restore(mouse);
				}
			}
		}
		catch
		{
		}
		engine.Dispose();
	}

	private static void RepointStartup()
	{
		try
		{
			using RegistryKey k = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
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

	public static bool IsStartup()
	{
		using RegistryKey k = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run");
		return k?.GetValue("LiteSynapse") != null;
	}

	public static void SetStartup(bool on)
	{
		using RegistryKey k = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
		if (on)
		{
			k.SetValue("LiteSynapse", "\"" + Environment.ProcessPath + "\" --tray");
		}
		else
		{
			k.DeleteValue("LiteSynapse", throwOnMissingValue: false);
		}
	}
}
