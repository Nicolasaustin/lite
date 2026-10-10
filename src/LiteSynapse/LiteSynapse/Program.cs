using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace LiteSynapse;

internal static class Program
{
	// Held by "LiteSynapse.exe testall" while it drives the devices.
	public const string SelfTestMutex = "Local\\LiteSynapse.SelfTest";

	private static class Cursor
	{
		public static int X
		{
			get
			{
				W32.GetCursorPos(out var p);
				return p.x;
			}
		}

		public static int Y
		{
			get
			{
				W32.GetCursorPos(out var p);
				return p.y;
			}
		}
	}

	[STAThread]
	private static int Main(string[] args)
	{
		bool flag = args.Length >= 1;
		if (flag)
		{
			bool flag2;
			switch (args[0])
			{
			case "import":
			case "status":
			case "selftest":
				flag2 = true;
				break;
			default:
				flag2 = false;
				break;
			}
			flag = flag2;
		}
		if (flag)
		{
			AttachConsole(-1);
		}
		if (args.Length >= 2 && args[0] == "makeicon")
		{
			AppIcon.WriteIco(args[1]);
			return 0;
		}
		if (args.Length >= 1 && args[0] == "testall")
		{
			AttachConsole(-1);
			// The self-test writes and switches the mouse's slots itself. A running LiteSynapse would drive the
			// same mouse at the same time and both would then trust slot records the other overwrote, so the
			// test refuses to run beside it rather than pretending the other copy stands back.
			if (Mutex.TryOpenExisting("LiteSynapse.single", out Mutex running))
			{
				running.Dispose();
				Console.WriteLine("LiteSynapse is running. Exit it from the tray first: the self-test drives the mouse itself.");
				return 2;
			}
			using Mutex selfTest = new Mutex(initiallyOwned: true, SelfTestMutex, out bool owned);
			try
			{
				int transport = TransportCheck();
				int all = Tests.TestAll();
				Cfg.FlushLog();
				return (transport != 0) ? 1 : all;
			}
			finally
			{
				if (owned)
				{
					selfTest.ReleaseMutex();
				}
			}
		}
		if (args.Length >= 1 && args[0] == "transport")
		{
			AttachConsole(-1);
			int result = TransportCheck();
			Cfg.FlushLog();
			return result;
		}
		if (args.Length >= 1 && args[0] == "razer")
		{
			AttachConsole(-1);
			Console.WriteLine(RazerLeftovers.Check());
			Console.WriteLine("driver script: " + (RazerLeftovers.FindDriverScript() ?? "not found"));
			Cfg.FlushLog();
			return 0;
		}
		if (args.Length >= 2 && args[0] == "import-stages")
		{
			AttachConsole(-1);
			Console.WriteLine(Importer.UpdateStages(args[1..]));
			return 0;
		}
		if (args.Length >= 3 && args[0] == "dpitrace")
		{
			AttachConsole(-1);
			return Tests.DpiTrace(args[1], args[2]);
		}
		if (args.Length >= 3 && args[0] == "checkslot")
		{
			AttachConsole(-1);
			return Tests.CheckSlot(int.Parse(args[1]), args[2]);
		}
		if (args.Length >= 3 && args[0] == "livetest")
		{
			AttachConsole(-1);
			return Tests.LiveTest(args[1], args[2]);
		}
		if (args.Length >= 3 && args[0] == "writeslot")
		{
			AttachConsole(-1);
			return Tests.WriteSlot(int.Parse(args[1]), args[2]);
		}
		if (args.Length >= 1 && args[0] == "cleanup-defaults")
		{
			AttachConsole(-1);
			return CleanupDefaults();
		}
		if (args.Length >= 1 && args[0] == "uitest")
		{
			AttachConsole(-1);
			ApplicationConfiguration.Initialize();
			return UiTests.Run((args.Length >= 2) ? args[1] : null);
		}
		if (args.Length >= 1 && args[0] == "test")
		{
			AttachConsole(-1);
			ApplicationConfiguration.Initialize();
			return Tests.Run();
		}
		if (args.Length >= 1 && args[0] == "selftest")
		{
			SelfTest();
			return 0;
		}
		if (args.Length >= 2 && args[0] == "import")
		{
			Console.WriteLine(Importer.Run(args[1..]));
			return 0;
		}
		if (args.Length >= 1 && args[0] == "status")
		{
			Status();
			return 0;
		}
		// The installer's and uninstaller's "Start with Windows": the sign-in task runs as administrator, so only an
		// elevated process can register or remove it - this one is (app.manifest), and it does nothing else.
		if (args.Length >= 1 && (args[0] == "--register-startup" || args[0] == "--unregister-startup"))
		{
			string error = Service.SetStartup(args[0] == "--register-startup");
			Cfg.FlushLog();
			return (error == null) ? 0 : 1;
		}
		bool first;
		Mutex single;
		try
		{
			single = new Mutex(initiallyOwned: true, "LiteSynapse.single", out first);
		}
		catch (UnauthorizedAccessException)
		{
			// The mutex exists but belongs to an elevated LiteSynapse, so this process may not even open it.
			// Same as "already running".
			single = null;
			first = false;
		}
		using (single)
		{
			if (!first)
			{
				// Already running, usually in the tray: a shortcut or a second start shows its window instead of
				// doing nothing. The autostart copy (--tray) just leaves.
				if (!args.Contains("--tray"))
				{
					// Every copy runs as administrator now (app.manifest), so the running one simply shows its window.
					TrayHost.ShowRunningInstance();
				}
				return 0;
			}
			// After Restart Manager closed it for an update (for example of the .NET runtime), Windows starts it
			// again in the tray. Not after a crash or hang (it restarts itself) and not after a reboot (the Run value).
			RegisterApplicationRestart("--tray", RESTART_NO_CRASH | RESTART_NO_HANG | RESTART_NO_REBOOT);
			TaskScheduler.UnobservedTaskException += (object? _, UnobservedTaskExceptionEventArgs e) =>
			{
				Cfg.Log("task error: " + e.Exception.GetBaseException().Message);
				e.SetObserved();
			};
			AppDomain.CurrentDomain.UnhandledException += (object _, UnhandledExceptionEventArgs e) =>
			{
				Cfg.Log("FATAL: " + e.ExceptionObject);
				Cfg.FlushLog(500);
				try
				{
					string path = Path.Combine(Cfg.Dir, "last_restart.txt");
					if (!File.Exists(path) || DateTime.Now - File.GetLastWriteTime(path) > TimeSpan.FromMinutes(1L))
					{
						File.WriteAllText(path, DateTime.Now.ToString("o"));
						Process.Start(new ProcessStartInfo("cmd.exe", "/c ping -n 4 127.0.0.1 >nul & \"" + Environment.ProcessPath + "\" --tray")
						{
							CreateNoWindow = true,
							UseShellExecute = false
						});
					}
				}
				catch
				{
				}
			};
			List<string> cl = args.ToList();
			Service svc = new Service();
			if (cl.Contains("--crash"))
			{
				new Thread(() =>
				{
					Thread.Sleep(3000);
					throw new InvalidOperationException("crash test");
				}).Start();
			}
			int ipr = cl.IndexOf("--profile");
			if (ipr >= 0 && ipr + 1 < cl.Count)
			{
				svc.Auto = false;
				svc.ApplyNames(cl[ipr + 1], svc.DefaultKeyboard, inGame: true);
			}
			if (!cl.Contains("--tray"))
			{
				int ip = cl.IndexOf("--page");
				int it = cl.IndexOf("--tab");
				UiHost.ShowWindow(svc, (ip >= 0 && ip + 1 < cl.Count) ? int.Parse(cl[ip + 1]) : (-1), (it >= 0 && it + 1 < cl.Count) ? int.Parse(cl[it + 1]) : 0, cl.Contains("--mem"));
			}
			if (cl.Contains("--flyout"))
			{
				UiHost.ToggleFlyout(svc, Cursor.X, Cursor.Y);
			}
			new TrayHost(svc).Run();
			// Windows close the normal way (the main window saves its size and state) before the process ends.
			UiHost.CloseAll(2000);
			svc.Shutdown();
			Cfg.FlushLog();
			return 0;
		}
	}

	private static int CleanupDefaults()
	{
		ProfileStore store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		List<GameRule> games = Cfg.Load<List<GameRule>>(Cfg.Games);
		string settingsFile = Path.Combine(Cfg.Dir, "settings.json");
		if (!store.Mouse.Any((MouseProfile p) => p.Name == "CS2"))
		{
			Console.WriteLine("No CS2 mouse profile found - nothing changed.");
			return 1;
		}
		string backup = Path.Combine(Cfg.Dir, "backup-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
		Directory.CreateDirectory(backup);
		string[] array = new string[3]
		{
			Cfg.Profiles,
			Cfg.Games,
			settingsFile
		};
		foreach (string f in array)
		{
			if (File.Exists(f))
			{
				File.Copy(f, Path.Combine(backup, Path.GetFileName(f)));
			}
		}
		int m = store.Mouse.RemoveAll((MouseProfile p) => p.Name.StartsWith("PC-Default", StringComparison.OrdinalIgnoreCase));
		int k = store.Keyboard.RemoveAll((KbProfile p) => p.Name.StartsWith("PC-Default", StringComparison.OrdinalIgnoreCase));
		int g = games.RemoveAll((GameRule gameRule) => (gameRule.Profile ?? "").StartsWith("PC-Default", StringComparison.OrdinalIgnoreCase));
		int r = 0;
		foreach (GameRule x in games)
		{
			if ((x.KeyboardProfile ?? "").StartsWith("PC-Default", StringComparison.OrdinalIgnoreCase))
			{
				x.KeyboardProfile = "";
				r++;
			}
		}
		Cfg.Save(Cfg.Profiles, store);
		Cfg.Save(Cfg.Games, games);
		JsonObject node = (File.Exists(settingsFile) ? (JsonNode.Parse(File.ReadAllText(settingsFile)) as JsonObject) : null);
		if (node == null)
		{
			node = new JsonObject();
		}
		node["DefaultMouse"] = "CS2";
		node["DefaultKeyboard"] = (store.Keyboard.Any((KbProfile p) => p.Name == "gamer - S3") ? "gamer - S3" : "");
		File.WriteAllText(settingsFile, node.ToJsonString(new JsonSerializerOptions
		{
			WriteIndented = true
		}));
		Console.WriteLine($"Removed {m} mouse profile(s), {k} keyboard profile(s), {g} game rule(s); cleared the keyboard profile on {r} game(s).");
		Console.WriteLine($"Starting profiles: mouse CS2, keyboard {node["DefaultKeyboard"]}. Backup: {backup}");
		return 0;
	}

	// Checks the HID transport on the real devices: one handle kept open, the fixed transaction id, short
	// SET/GET round trips, and setters that send nothing when the value is already set. Read-only except for
	// re-sending values the devices already have.
	private static int TransportCheck()
	{
		int fails = 0;
		Console.WriteLine("-- HID transport");
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		RazerDevice kb = RazerDevice.Find(565, "BlackWidow Lite");
		Report("Naga found", mouse != null);
		if (kb == null)
		{
			Console.WriteLine("INFO  BlackWidow Lite not found (keyboard checks skipped)");
		}
		if (mouse != null)
		{
			Device(mouse, 0x1F, () => mouse.GetActiveSlot().HasValue);
			(int, int)? dpi = mouse.GetDpi();
			if (dpi.HasValue)
			{
				int before = mouse.Transactions;
				bool ok = mouse.SetDpi(dpi.Value.Item1);
				Report("mouse: setting the DPI it already has sends nothing", ok && mouse.Transactions == before, $"{mouse.Transactions - before} transaction(s)");
			}
			else
			{
				Report("mouse: DPI read", ok: false);
			}
			int? polling = mouse.GetPolling();
			Report("mouse: polling rate read", polling.HasValue, $"{polling} Hz");
		}
		if (kb != null)
		{
			Device(kb, 0x3F, () => kb.GetBrightness().HasValue);
			int? b = kb.GetBrightness();
			if (b.HasValue)
			{
				int before2 = kb.Transactions;
				bool ok2 = kb.SetBrightness(b.Value);
				Report("keyboard: setting the brightness it already has sends nothing", ok2 && kb.Transactions == before2, $"{kb.Transactions - before2} transaction(s)");
			}
		}
		mouse?.Dispose();
		kb?.Dispose();
		Console.WriteLine((fails == 0) ? "TRANSPORT OK" : $"TRANSPORT: {fails} FAILED");
		return fails;
		void Device(RazerDevice d, byte expectedTxn, Func<bool> read)
		{
			int good = 0;
			for (int i = 0; i < 20; i++)
			{
				if (read())
				{
					good++;
				}
			}
			double avg = (d.Transactions > 0) ? (d.TotalMs / d.Transactions) : 0.0;
			Report($"{d.Name}: 20 reads answered", good == 20, $"{good}/20");
			if (d.TransactionId == expectedTxn)
			{
				Report($"{d.Name}: transaction id 0x{expectedTxn:X2} accepted", ok: true);
			}
			else
			{
				// Works, but through the fallback probe: worth knowing, not a failure.
				Console.WriteLine($"INFO  {d.Name}: transaction id 0x{expectedTxn:X2} was not accepted, using 0x{d.TransactionId:X2}");
			}
			Report($"{d.Name}: one handle kept open", d.Reopens == 0, $"{d.Reopens} reopen(s)");
			Report($"{d.Name}: round trip under 30 ms (was 20-100 ms)", d.Transactions > 0 && d.SlowestMs < 30.0, $"average {avg:N1} ms, slowest {d.SlowestMs:N1} ms");
		}
		void Report(string name, bool ok, string detail = "")
		{
			Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + name + ((detail.Length > 0) ? ("  - " + detail) : ""));
			if (!ok)
			{
				fails++;
			}
		}
	}

	private static void SelfTest()
	{
		RazerDevice m = RazerDevice.Find(141, "Naga");
		RazerDevice k = RazerDevice.Find(565, "BlackWidow Lite");
		int d0 = m.GetDpi().Value.x;
		int b0 = k.GetBrightness().Value;
		Console.WriteLine($"DPI {d0}->1600 ok={m.SetDpi(1600)} now {m.GetDpi()}; restore ok={m.SetDpi(d0)} now {m.GetDpi()}");
		Console.WriteLine($"Brightness {b0}->40 ok={k.SetBrightness(40)} now {k.GetBrightness()}; restore ok={k.SetBrightness(b0)} now {k.GetBrightness()}");
	}

	private static void Status()
	{
		AttachConsole(-1);
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		RazerDevice kb = RazerDevice.Find(565, "BlackWidow Lite");
		Console.WriteLine("Naga: " + ((mouse == null) ? "not found" : $"DPI {mouse.GetDpi()}  polling {mouse.GetPolling()}  slot {mouse.GetActiveSlot()}"));
		Console.WriteLine("Keyboard: " + ((kb == null) ? "not found" : $"brightness {kb.GetBrightness()}"));
	}

	[DllImport("kernel32")]
	private static extern bool AttachConsole(int pid);

	private const int RESTART_NO_CRASH = 1;

	private const int RESTART_NO_HANG = 2;

	private const int RESTART_NO_REBOOT = 8;

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	private static extern int RegisterApplicationRestart(string commandLine, int flags);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern int GetKeyNameText(int lParam, StringBuilder sb, int n);

	public static string KeyName(int sc, bool ext)
	{
		StringBuilder sb = new StringBuilder(64);
		GetKeyNameText((sc << 16) | (ext ? 16777216 : 0), sb, 64);
		return sb.ToString();
	}
}
