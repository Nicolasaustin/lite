using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class Tests
{
	private static int fails;

	private static readonly List<(string what, bool down, long ms)> seen = new List<(string, bool, long)>();

	private static nint hook;

	private static Native.HookProc proc;

	private static void Check(string name, bool ok, string detail = "")
	{
		Console.WriteLine((ok ? "PASS" : "FAIL") + "  " + name + ((detail.Length > 0) ? ("  - " + detail) : ""));
		if (!ok)
		{
			fails++;
		}
	}

	private static nint OnKey(int n, nint w, nint l)
	{
		if (n >= 0 && Marshal.ReadIntPtr(l, 16) == Native.MAGIC)
		{
			int sc = Marshal.ReadInt32(l, 4);
			int fl = Marshal.ReadInt32(l, 8);
			string keyName = $"{Native.MapVirtualKey((uint)sc, 3u):X2}{(((fl & 1) != 0) ? "e" : "")}";
			lock (seen)
			{
				seen.Add((keyName, (fl & 0x80) == 0, Stopwatch.GetTimestamp()));
			}
			return 1;
		}
		return Native.CallNextHookEx(hook, n, w, l);
	}

	private static void Pump(int ms)
	{
		long end = Environment.TickCount64 + ms;
		while (Environment.TickCount64 < end)
		{
			Application.DoEvents();
			Thread.Sleep(5);
		}
	}

	private static string Seq()
	{
		lock (seen)
		{
			return string.Join(" ", seen.Select(((string what, bool down, long ms) s) => (s.down ? "+" : "-") + s.what));
		}
	}

	public static int Run()
	{
		proc = OnKey;
		ManualResetEventSlim up = new ManualResetEventSlim();
		try
		{
			Thread thread = new Thread(() =>
			{
				hook = Native.SetWindowsHookEx(13, proc, Native.GetModuleHandle(null), 0u);
				up.Set();
				Application.Run();
			});
			thread.IsBackground = true;
			thread.SetApartmentState(ApartmentState.STA);
			thread.Start();
			up.Wait();
		}
		finally
		{
			if (up != null)
			{
				((IDisposable)up).Dispose();
			}
		}
		ProfileStore store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		Check("profiles imported", store.Mouse.Count >= 40 && store.Keyboard.Count >= 1 && store.Macros.Count == 2, $"{store.Mouse.Count} mouse, {store.Keyboard.Count} keyboard, {store.Macros.Count} macros");
		MouseProfile cs2 = store.Mouse.FirstOrDefault((MouseProfile p) => p.Name == "CS2");
		Check("CS2: left tilt V, right tilt R", cs2?.Buttons["TiltL"].Key == "KEY_V" && cs2?.Buttons["TiltR"].Key == "KEY_R");
		Check("CS2: scroll=Space M1=Shift M2=Ctrl", cs2?.Buttons["Scroll"].Key == "KEY_SPACEBAR" && cs2?.Buttons["M1"].Key == "KEY_LEFT_SHIFT" && cs2?.Buttons["M2"].Key == "KEY_LEFT_CTRL");
		Check("CS2: all 12 side buttons mapped", Enumerable.Range(1, 12).All((int num2) => cs2?.Buttons.ContainsKey("Side" + num2) ?? false));
		Check("every game profile has valid keys", store.Mouse.All((MouseProfile p) => p.Buttons.Values.All((KeyTarget keyTarget) => Keys2.Vk(keyTarget.Key) != 0 || keyTarget.Key == "DISABLED")), string.Join(",", (from keyTarget in store.Mouse.SelectMany((MouseProfile p) => p.Buttons.Values)
			select keyTarget.Key into k
			where Keys2.Vk(k) == 0
			select k).Distinct()));
		Check("every game profile has onboard-writable keys", store.Mouse.All((MouseProfile p) => p.Buttons.Values.All((KeyTarget keyTarget) => OnboardMemory.Usage(keyTarget.Key) != 0)), string.Join(",", (from keyTarget in store.Mouse.SelectMany((MouseProfile p) => p.Buttons.Values)
			select keyTarget.Key into k
			where OnboardMemory.Usage(k) == 0
			select k).Distinct()));
		InputEngine engine = new InputEngine();
		engine.SetProfile(null, null, store.Macros, inGame: false);
		string[] array = new string[2] { "Jumpthrow", "W Jumpthrow" };
		foreach (string name in array)
		{
			Macro m = store.Macros.First((Macro x) => x.Name == name);
			lock (seen)
			{
				seen.Clear();
			}
			long t0 = Environment.TickCount64;
			engine.TestRunMacro(m);
			Pump(120);
			_ = Environment.TickCount64;
			Seq();
			while (engine.MacroBusy && Environment.TickCount64 - t0 < 3000)
			{
				Pump(10);
			}
			long dt = Environment.TickCount64 - t0;
			string expected = string.Join(" ", m.Steps.Where((MacroStep s) => s.Vk != 0).Select((MacroStep s) =>
			{
				string text = (s.Down ? "+" : "-");
				DefaultInterpolatedStringHandler defaultInterpolatedStringHandler = new DefaultInterpolatedStringHandler(0, 1);
				Native.MapVirtualKey((s.Vk == 13 && s.Ext) ? 13u : ((uint)s.Vk), 0u);
				defaultInterpolatedStringHandler.AppendFormatted(0u + s.Vk, "X2");
				return text + defaultInterpolatedStringHandler.ToStringAndClear();
			}));
			string got = Seq();
			Check($"macro \"{name}\" sends {m.Steps.Count((MacroStep s) => s.Vk != 0)} key events in order", got.Replace("e", "") == expected, got);
			bool hasDelay = m.Steps.Any((MacroStep s) => s.Vk == 0);
			Check("macro \"" + name + "\" timing", !hasDelay || dt >= 190, $"{dt} ms total");
			Check("macro \"" + name + "\" releases every key it pressed", Net(got) == 0, got);
		}
		engine.TestSnapTap(on: true);
		lock (seen)
		{
			seen.Clear();
		}
		engine.TestOutput(65, down: true);
		engine.TestOutput(68, down: true);
		engine.TestOutput(68, down: false);
		engine.TestOutput(65, down: false);
		Pump(50);
		Check("Snap Tap: A down, D down -> A released then D; D up -> A re-pressed", Seq().Replace("e", "") == "+41 -41 +44 -44 +41 -41", Seq());
		engine.TestSnapTap(on: false);
		MethodInfo hm = typeof(InputEngine).GetMethod("Hook", BindingFlags.Instance | BindingFlags.NonPublic);
		Native.HookProc hookFn = (Native.HookProc)Delegate.CreateDelegate(typeof(Native.HookProc), engine, hm);
		nint mem = Marshal.AllocHGlobal(32);
		for (int i = 0; i < 32; i += 8)
		{
			Marshal.WriteInt64(mem, i, 0L);
		}
		Marshal.WriteInt32(mem, 0, 87);
		Marshal.WriteInt32(mem, 4, 17);
		engine.SetProfile(null, null, store.Macros, inGame: false);
		double a = Bench("desktop (nothing active)");
		engine.Base = Cfg.Load<Dictionary<string, BaseKey>>(Cfg.Calib);
		engine.SetProfile(store.Mouse.First((MouseProfile p) => p.Name == "Valorant"), store.Keyboard.First((KbProfile p) => p.Name == "gamer - S3"), store.Macros, inGame: true);
		double b = Bench("in game (remap table + macros + Snap Tap on)");
		Check("keyboard hook adds under 50 microseconds per key press", a < 50.0 && b < 50.0, $"{a:N2} / {b:N2} us");
		Marshal.FreeHGlobal(mem);
		List<long> sent = new List<long>();
		lock (seen)
		{
			seen.Clear();
		}
		Task task = Task.Run(() =>
		{
			Thread.Sleep(150);
			for (int j = 0; j < 30; j++)
			{
				sent.Add(Stopwatch.GetTimestamp());
				Native.SendKey(124, ext: false, down: true);
				Native.SendKey(124, ext: false, down: false);
				Thread.Sleep(15);
			}
		});
		Thread.Sleep(900);
		task.Wait();
		Thread.Sleep(100);
		List<(string, bool, long)> got2;
		lock (seen)
		{
			got2 = seen.Where(((string what, bool down, long ms) s) => s.down).ToList();
		}
		List<double> lat = got2.Take(sent.Count).Select(((string what, bool down, long ms) s, int index) => (double)(s.ms - sent[index]) * 1000.0 / (double)Stopwatch.Frequency).ToList();
		double worst = ((lat.Count > 0) ? lat.Max() : 9999.0);
		double avg = ((lat.Count > 0) ? lat.Average() : 9999.0);
		Check("key events are not delayed while the UI thread is blocked", got2.Count == sent.Count && worst < 20.0, $"{got2.Count}/{sent.Count} events, average {avg:N2} ms, worst {worst:N2} ms");
		Macro m2 = store.Macros.First((Macro x) => x.Name == "W Jumpthrow");
		List<double> spans = new List<double>();
		for (int r = 0; r < 5; r++)
		{
			lock (seen)
			{
				seen.Clear();
			}
			engine.TestRunMacro(m2);
			while (engine.MacroBusy)
			{
				Thread.Sleep(5);
			}
			Thread.Sleep(20);
			lock (seen)
			{
				if (seen.Count >= 2)
				{
					List<(string what, bool down, long ms)> list = seen;
					long item = list[list.Count - 1].ms;
					List<(string what, bool down, long ms)> list2 = seen;
					spans.Add((double)(item - list2[list2.Count - 2].ms) * 1000.0 / (double)Stopwatch.Frequency);
				}
			}
		}
		Check("macro wait is exact (target 200 ms)", spans.Count == 5 && spans.All((double s) => s > 195.0 && s < 212.0), string.Join(", ", spans.Select((double s) => s.ToString("N1") + " ms")));
		bool a2 = Razer("\\\\?\\HID#VID_1532&PID_008D&MI_00#8&2d155794&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 32768);
		bool a3 = Razer("\\\\?\\HID#VID_1532&PID_0235&MI_02#c&21c056b5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 32772);
		bool other = Razer("\\\\?\\HID#VID_046D&PID_C52B&MI_00#7&1234&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 32768);
		bool wrongEvent = Razer("\\\\?\\HID#VID_1532&PID_008D&MI_00#8&2d155794&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}", 7);
		Check("device notifications: Razer plug/unplug triggers the re-check, other devices and other events are ignored", (a2 & a3) && !other && !wrongEvent, $"arrival {a2}, removal {a3}, other vendor {other}, other event {wrongEvent}");
		Dictionary<string, BaseKey> cal = (engine.Base = Cfg.Load<Dictionary<string, BaseKey>>(Cfg.Calib));
		Check("calibration stored for all 17 buttons", cal.Count == InputEngine.ButtonIds.Length, $"{cal.Count}/17");
		MouseProfile valorant = store.Mouse.First((MouseProfile p) => p.Name == "Valorant");
		engine.SetProfile(cs2, null, store.Macros, inGame: true);
		int csCount = engine.DescribeMap().Count();
		Console.WriteLine($"      CS2 table (identity buttons skipped): {csCount} entries");
		engine.SetProfile(valorant, null, store.Macros, inGame: true);
		List<string> vmap = engine.DescribeMap().ToList();
		Check("Valorant table is built from calibration", vmap.Count > 0 && vmap.Count <= 17, $"{vmap.Count} entries");
		foreach (string l in vmap.Take(20))
		{
			Console.WriteLine("      " + l);
		}
		engine.SetProfile(valorant, null, store.Macros, inGame: false);
		Check("desktop (no game) has an empty table", !engine.DescribeMap().Any());
		Check("HID usages: W=1A Z=1D V=19 R=15 Space=2C Tab=2B LShift=E1", OnboardMemory.Usage("KEY_W") == 26 && OnboardMemory.Usage("KEY_Z") == 29 && OnboardMemory.Usage("KEY_V") == 25 && OnboardMemory.Usage("KEY_R") == 21 && OnboardMemory.Usage("KEY_SPACEBAR") == 44 && OnboardMemory.Usage("KEY_TAB") == 43 && OnboardMemory.Usage("KEY_LEFT_SHIFT") == 225);
		Check("HID usages: 1=1E 0=27 F5=3E", OnboardMemory.Usage("KEY_1") == 30 && OnboardMemory.Usage("KEY_0") == 39 && OnboardMemory.Usage("KEY_F5") == 62);
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		RazerDevice kb = RazerDevice.Find(565, "BlackWidow Lite");
		Check("Naga found and answers", mouse?.GetDpi().HasValue ?? false, $"DPI {mouse?.GetDpi()}  polling {mouse?.GetPolling()}");
		Check("BlackWidow Lite found and answers", kb?.GetBrightness().HasValue ?? false, $"brightness {kb?.GetBrightness()}");
		Native.UnhookWindowsHookEx(hook);
		engine.Dispose();
		Console.WriteLine((fails == 0) ? "\nALL CHECKS PASSED" : $"\n{fails} CHECK(S) FAILED");
		return (fails != 0) ? 1 : 0;
		double Bench(string label)
		{
			for (int j = 0; j < 20000; j++)
			{
				hookFn(0, 256, mem);
			}
			int N = 200000;
			long t1 = Stopwatch.GetTimestamp();
			for (int k = 0; k < N; k++)
			{
				hookFn(0, 256, mem);
			}
			double us = (double)(Stopwatch.GetTimestamp() - t1) * 1000000.0 / (double)Stopwatch.Frequency / (double)N;
			Console.WriteLine($"      {label}: {us:N2} microseconds per key event");
			return us;
		}
		static bool Razer(string path, int what)
		{
			int size = 28 + (path.Length + 1) * 2;
			nint mem2 = Marshal.AllocHGlobal(size);
			try
			{
				for (int j = 0; j < size; j++)
				{
					Marshal.WriteByte(mem2, j, 0);
				}
				Marshal.WriteInt32(mem2, 0, size);
				Marshal.WriteInt32(mem2, 4, 5);
				char[] chars = (path + "\0").ToCharArray();
				Marshal.Copy(chars, 0, mem2 + 28, chars.Length);
				return DeviceEvents.IsRazerChange(what, mem2);
			}
			finally
			{
				Marshal.FreeHGlobal(mem2);
			}
		}
	}

	public static int LiveTest(string a, string b)
	{
		ProfileStore store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		LiveProfile live = new LiveProfile();
		MouseProfile pa = store.Mouse.First((MouseProfile x) => x.Name == a);
		MouseProfile pb = store.Mouse.First((MouseProfile x) => x.Name == b);
		int fails = 0;
		int start = mouse.GetActiveSlot() ?? (-1);
		Console.WriteLine($"mouse starts on slot {start}");
		Stopwatch sw = Stopwatch.StartNew();
		string e1 = live.Apply(mouse, pa);
		int s1 = mouse.GetActiveSlot() ?? (-1);
		long t1 = sw.ElapsedMilliseconds;
		string n = "select \"" + a + "\": mouse switches to a live slot and holds all its keys";
		bool flag = e1 == null;
		if (flag)
		{
			bool flag2 = (uint)(s1 - 1) <= 1u;
			flag = flag2;
		}
		Chk(n, flag && Reads(s1, pa), $"slot {s1}, {t1} ms, {e1}");
		sw.Restart();
		string e2 = live.Apply(mouse, pb);
		int s2 = mouse.GetActiveSlot() ?? (-1);
		long t2 = sw.ElapsedMilliseconds;
		n = "select \"" + b + "\": uses the OTHER live slot (nothing half-written is ever active)";
		flag = e2 == null;
		if (flag)
		{
			bool flag2 = (uint)(s2 - 1) <= 1u;
			flag = flag2;
		}
		Chk(n, flag && s2 != s1 && Reads(s2, pb), $"slot {s2}, {t2} ms, {e2}");
		sw.Restart();
		string e3 = live.Apply(mouse, pa);
		int s3 = mouse.GetActiveSlot() ?? (-1);
		long t3 = sw.ElapsedMilliseconds;
		n = "select \"" + a + "\" again: rewrites the idle slot and switches";
		flag = e3 == null;
		if (flag)
		{
			bool flag2 = (uint)(s3 - 1) <= 1u;
			flag = flag2;
		}
		Chk(n, flag && Reads(s3, pa), $"slot {s3}, {t3} ms");
		string e4 = live.Restore(mouse);
		int s4 = mouse.GetActiveSlot() ?? (-1);
		int home = ((start > 2) ? start : live.ReturnSlot);
		Chk("no game: mouse goes back to the slot you were on", e4 == null && s4 == home, $"slot {s4} (started on {start}, own slot {home})");
		Chk("left/right click stay in the left-handed order in the live slots", ClickOk(1) && ClickOk(2), "buttons 1/2 in slots 1 and 2");
		Chk("your saved slots 3 and 5 are untouched", Reads(3, store.Mouse.First((MouseProfile x) => x.Name == "CS2")) && Reads(5, store.Mouse.First((MouseProfile x) => x.Name == "CS2")));
		live.Apply(mouse, pa);
		int stuck = mouse.GetActiveSlot() ?? (-1);
		string e5 = new LiveProfile().Restore(mouse);
		int s5 = mouse.GetActiveSlot() ?? (-1);
		flag = (uint)(stuck - 1) <= 1u;
		Chk("after a crash/restart on a game layout: the mouse is put back on your own slot", flag && e5 == null && s5 == home, $"was on slot {stuck}, now on slot {s5} (own slot {home})");
		Console.WriteLine((fails == 0) ? "LIVE TEST PASSED" : $"{fails} FAILED");
		return fails;
		void Chk(string text, bool ok, string d = "")
		{
			Console.WriteLine((ok ? "PASS  " : "FAIL  ") + text + ((d.Length > 0) ? ("  - " + d) : ""));
			if (!ok)
			{
				fails++;
			}
		}
		bool ClickOk(int slot)
		{
			byte[] c1 = mouse.Command(2, 140, 10, (byte)slot, 1, 0);
			byte[] c2 = mouse.Command(2, 140, 10, (byte)slot, 2, 0);
			if (c1 != null && c2 != null && c1[5] == 2)
			{
				return c2[5] == 1;
			}
			return false;
		}
		bool Reads(int slot, MouseProfile p)
		{
			foreach (var (id, t4) in p.Buttons)
			{
				int code = OnboardMemory.ButtonCode(id);
				int use = OnboardMemory.Usage(t4.Key);
				if (code != 0 && use != 0)
				{
					byte[] r = mouse.Command(2, 140, 10, (byte)slot, (byte)code, 0);
					if (r == null || r[6] != use)
					{
						return false;
					}
				}
			}
			return true;
		}
	}

	public static int TestAll()
	{
		ProfileStore store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		LiveProfile live = new LiveProfile();
		int start = mouse.GetActiveSlot() ?? (-1);
		int bad = 0;
		int buttons = 0;
		int n = 0;
		Stopwatch sw = Stopwatch.StartNew();
		Console.WriteLine($"mouse starts on slot {start}; testing {store.Mouse.Count} profiles...");
		foreach (MouseProfile p in store.Mouse)
		{
			string err = live.Apply(mouse, p);
			int slot = mouse.GetActiveSlot() ?? (-1);
			List<string> problems = new List<string>();
			if (err != null)
			{
				problems.Add(err);
			}
			foreach (var (id, t) in p.Buttons)
			{
				int code = OnboardMemory.ButtonCode(id);
				int use = OnboardMemory.Usage(t.Key);
				if (code != 0 && use != 0)
				{
					buttons++;
					byte[] r = mouse.Command(2, 140, 10, (byte)slot, (byte)code, 0);
					if (r == null || r[3] != 2 || r[6] != use || r[5] != OnboardMemory.ModMask(t.Mods))
					{
						problems.Add(id + "=" + t.Key);
					}
				}
			}
			byte[] c1 = mouse.Command(2, 140, 10, (byte)slot, 1, 0);
			byte[] c2 = mouse.Command(2, 140, 10, (byte)slot, 2, 0);
			if (c1 == null || c2 == null || c1[5] != 2 || c2[5] != 1)
			{
				problems.Add("left/right click order");
			}
			(int, int)? dpi = mouse.GetDpi();
			if (!dpi.HasValue || dpi.Value.Item1 != p.Dpi)
			{
				problems.Add($"DPI {dpi?.Item1} (wanted {p.Dpi})");
			}
			n++;
			if (problems.Count > 0)
			{
				bad++;
				Console.WriteLine("FAIL  " + p.Name + ": " + string.Join(", ", problems));
			}
		}
		live.Restore(mouse);
		int end = mouse.GetActiveSlot() ?? (-1);
		int home = ((start > 2) ? start : live.ReturnSlot);
		Console.WriteLine($"{n - bad}/{n} profiles verified on the mouse ({buttons} key bindings, click order and DPI checked) in {sw.Elapsed.TotalSeconds:N0} s; mouse back on slot {end} (started on {start}{((start > 2) ? "" : $", a live slot; your own slot is {home}")})");
		if (bad != 0 || end != home)
		{
			return 1;
		}
		return 0;
	}

	public static int DpiTrace(string a, string b)
	{
		ProfileStore store = Cfg.Load<ProfileStore>(Cfg.Profiles);
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		LiveProfile live = new LiveProfile();
		string[] array = new string[2] { a, b };
		foreach (string name in array)
		{
			MouseProfile p = store.Mouse.First((MouseProfile x) => x.Name == name);
			Console.WriteLine($"-- applying {name} (wants {p.Dpi}, stages [{string.Join(",", p.DpiStages)}] active #{p.DpiActive})");
			string err = live.Apply(mouse, p);
			Stopwatch sw = Stopwatch.StartNew();
			int[] array2 = new int[4] { 0, 150, 400, 1000 };
			foreach (int wait in array2)
			{
				while (sw.ElapsedMilliseconds < wait)
				{
					Thread.Sleep(10);
				}
				Console.WriteLine($"   +{sw.ElapsedMilliseconds,4} ms: current DPI {mouse.GetDpi(1)?.x}  (slot {mouse.GetActiveSlot()}) {err}");
			}
		}
		return 0;
	}

	public static int CheckSlot(int slot, string profile)
	{
		MouseProfile mouseProfile = Cfg.Load<ProfileStore>(Cfg.Profiles).Mouse.First((MouseProfile x) => x.Name == profile);
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		int bad = 0;
		foreach (var (id, t) in mouseProfile.Buttons)
		{
			int code = OnboardMemory.ButtonCode(id);
			int use = OnboardMemory.Usage(t.Key);
			if (code != 0 && use != 0)
			{
				byte[] r = mouse.Command(2, 140, 10, (byte)slot, (byte)code, 0);
				if (r == null || r[6] != use || r[5] != 0)
				{
					bad++;
					Console.WriteLine($"  slot {slot} {id} ({t.Key}): profile wants {use:X2}, mouse has {((r == null) ? "?" : Convert.ToHexString(r, 0, 8))}");
				}
			}
		}
		Console.WriteLine((bad == 0) ? $"slot {slot} matches \"{profile}\"" : $"slot {slot}: {bad} button(s) differ from \"{profile}\"");
		return bad;
	}

	public static int WriteSlot(int slot, string profile)
	{
		MouseProfile p = Cfg.Load<ProfileStore>(Cfg.Profiles).Mouse.FirstOrDefault((MouseProfile x) => x.Name == profile);
		RazerDevice mouse = RazerDevice.Find(141, "Naga");
		if (p == null || mouse == null)
		{
			Console.WriteLine("profile or mouse not found");
			return 2;
		}
		string err = OnboardMemory.Write(mouse, slot, p, (string s) =>
		{
			Console.WriteLine("  " + s);
		});
		Console.WriteLine((err == null) ? "write: ok" : ("write FAILED: " + err));
		if (err != null)
		{
			return 1;
		}
		int bad = 0;
		int n = 0;
		foreach (var (id, keyTarget2) in p.Buttons)
		{
			int code = OnboardMemory.ButtonCode(id);
			int use = OnboardMemory.Usage(keyTarget2.Key);
			if (code != 0 && use != 0)
			{
				byte[] r = mouse.Command(2, 140, 10, (byte)slot, (byte)code, 0);
				n++;
				if (r == null || r[0] != slot || r[1] != code || r[3] != 2 || r[6] != use)
				{
					bad++;
					Console.WriteLine($"  MISMATCH {id}: wanted {use:X2}, read {((r == null) ? "nothing" : Convert.ToHexString(r, 0, 8))}");
				}
			}
		}
		Console.WriteLine($"read-back: {n - bad}/{n} buttons match for \"{profile}\" in slot {slot}");
		return (bad != 0) ? 1 : 0;
	}

	private static int Net(string seq)
	{
		HashSet<string> held = new HashSet<string>();
		string[] array = seq.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		foreach (string t in array)
		{
			if (t[0] == '+')
			{
				held.Add(t.Substring(1));
			}
			else
			{
				held.Remove(t.Substring(1));
			}
		}
		return held.Count;
	}
}
