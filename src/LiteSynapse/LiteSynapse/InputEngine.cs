using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LiteSynapse;

// Keyboard side of LiteSynapse: arrow-key macros, Snap Tap and software remaps of Naga keys.
//
// Latency rules:
//  - The low-level keyboard hook is only installed while something needs it (a game profile with macro keys,
//    Snap Tap or software remaps). On the desktop, or in games that need none of it, no hook exists at all,
//    so keystrokes reach the game with zero added work.
//  - The raw-input sink (used to tell Naga keys from real keyboard keys) is only registered for software
//    remaps and calibration; normally button remaps live in the mouse's own memory and cost nothing.
//  - The hook path does no allocation, LINQ or logging, and real key events are passed through wherever
//    possible instead of being swallowed and re-injected.
//  - Macros run on one pre-started thread, send simultaneous steps in a single SendInput call, and time
//    delays with a high-resolution waitable timer instead of raising the system timer resolution.
internal class InputEngine : IDisposable
{
	private struct Pending
	{
		public int Sc;

		public bool Ext;

		public bool Up;

		public int Vk;

		public long Tick;
	}

	private struct RawKey
	{
		public int Sc;

		public bool Ext;

		public bool Up;

		public bool Naga;

		public long Tick;
	}

	public static readonly string[] ButtonIds = new string[17]
	{
		"Side1", "Side2", "Side3", "Side4", "Side5", "Side6", "Side7", "Side8", "Side9", "Side10",
		"Side11", "Side12", "Scroll", "M1", "M2", "TiltL", "TiltR"
	};

	private const int VK_A = 65;

	private const int VK_D = 68;

	private const uint WM_INPUT = 255u;

	private const uint WM_TIMER = 275u;

	private const uint WM_RUN = 32768u;

	private const uint RIDEV_REMOVE = 1u;

	private const uint RIDEV_INPUTSINK = 256u;

	private Native.HookProc hookProc;

	private nint hook;

	private bool rawOn;

	private bool enabled = true;

	private readonly Dictionary<nint, bool> nagaCache = new Dictionary<nint, bool>();

	private readonly List<Pending> pending = new List<Pending>();

	private readonly List<RawKey> recentRaw = new List<RawKey>();

	private byte[] rawBuf = new byte[64];

	private Dictionary<(int, bool), KeyTarget> map = new Dictionary<(int, bool), KeyTarget>();

	private Dictionary<string, Macro> macros = new Dictionary<string, Macro>();

	// A macro with its send batches built ahead of time, so triggering it costs one SendInput per segment.
	private sealed class PreparedMacro
	{
		public readonly MacroPlan Plan;

		public readonly Native.INPUT[][] Batches;

		public readonly Native.INPUT[] Release;

		public PreparedMacro(Macro m)
		{
			Plan = new MacroPlan(m.Steps);
			Batches = new Native.INPUT[Plan.Segments.Length][];
			for (int i = 0; i < Batches.Length; i++)
			{
				MacroPlan.Key[] keys = Plan.Segments[i].Keys;
				Batches[i] = new Native.INPUT[keys.Length];
				for (int j = 0; j < keys.Length; j++)
				{
					Batches[i][j] = Native.KeyInput(keys[j].Vk, keys[j].Ext, keys[j].Down);
				}
			}
			Release = new Native.INPUT[Plan.LeftDown.Length];
			for (int k = 0; k < Release.Length; k++)
			{
				Release[k] = Native.KeyInput(Plan.LeftDown[k].Vk, Plan.LeftDown[k].Ext, down: false);
			}
		}
	}

	// Macro bound to each arrow key, indexed by vk - 37 (left, up, right, down).
	private readonly PreparedMacro[] arrowMacros = new PreparedMacro[4];

	private bool anyArrowMacro;

	private bool snapTapActive;

	private readonly HashSet<int> macroKeysDown = new HashSet<int>();

	private Action<int, bool> calibrating;

	public Dictionary<string, BaseKey> Base = new Dictionary<string, BaseKey>();

	private readonly SnapTap snap = new SnapTap(VK_A, VK_D);

	private readonly Action<int, bool> sendPlain = (int vk, bool down) => Native.SendKey(vk, ext: false, down);

	// Keys held down by remapped output (not Snap Tap), so ReleaseAll only lifts what we pressed.
	private readonly HashSet<int> injected = new HashSet<int>();

	private readonly Thread thread;

	private int threadId;

	private nint hwnd;

	private W32.WndProc keepAlive;

	private bool sweepOn;

	private volatile bool disposed;

	private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

	private readonly Thread macroThread;

	private readonly AutoResetEvent macroSignal = new AutoResetEvent(initialState: false);

	private PreparedMacro macroNext;

	private volatile bool macroRunning;

	internal bool MacroBusy => macroRunning;

	public Action<int, bool> Calibrating
	{
		get
		{
			return calibrating;
		}
		set
		{
			OnEngine(() =>
			{
				calibrating = value;
				UpdateRegistration();
			});
		}
	}

	public InputEngine()
	{
		using ManualResetEventSlim ready = new ManualResetEventSlim();
		thread = new Thread(() =>
		{
			threadId = Environment.CurrentManagedThreadId;
			(nint, W32.WndProc) tuple = W32.CreateWindow(messageOnly: true, OnMessage);
			hwnd = tuple.Item1;
			keepAlive = tuple.Item2;
			hookProc = Hook;
			ready.Set();
			W32.RunLoop();
		})
		{
			IsBackground = true,
			Name = "LiteSynapse input",
			Priority = ThreadPriority.Highest
		};
		thread.Start();
		ready.Wait();
		macroThread = new Thread(MacroLoop)
		{
			IsBackground = true,
			Name = "LiteSynapse macro",
			Priority = ThreadPriority.AboveNormal
		};
		macroThread.Start();
	}

	private nint? OnMessage(uint m, nint w, nint l)
	{
		switch (m)
		{
		case WM_INPUT:
			try
			{
				OnRaw(l);
			}
			catch (Exception ex)
			{
				Cfg.Log("raw: " + ex.Message);
			}
			return null;
		case WM_TIMER:
			if ((long)w == 1)
			{
				Sweep();
				return IntPtr.Zero;
			}
			break;
		case WM_RUN:
		{
			Action a;
			while (queue.TryDequeue(out a))
			{
				try
				{
					a();
				}
				catch (Exception ex2)
				{
					Cfg.Log("engine: " + ex2.Message);
				}
			}
			return IntPtr.Zero;
		}
		}
		return null;
	}

	private void OnEngine(Action a)
	{
		if (disposed)
		{
			return;
		}
		if (Environment.CurrentManagedThreadId == threadId)
		{
			a();
			return;
		}
		using ManualResetEventSlim done = new ManualResetEventSlim();
		queue.Enqueue(() =>
		{
			try
			{
				a();
			}
			finally
			{
				done.Set();
			}
		});
		W32.PostMessage(hwnd, WM_RUN, IntPtr.Zero, IntPtr.Zero);
		done.Wait(2000);
	}

	// Installs or removes the hook and the raw-input sink to match what the active profile needs.
	private void UpdateRegistration()
	{
		bool needRaw = enabled && (calibrating != null || map.Count > 0);
		bool needHook = enabled && (map.Count > 0 || snapTapActive || anyArrowMacro);
		if (needRaw != rawOn)
		{
			Native.RegisterRawInputDevices(new Native.RID[1]
			{
				new Native.RID
				{
					page = 1,
					usage = 6,
					flags = (needRaw ? RIDEV_INPUTSINK : RIDEV_REMOVE),
					hwnd = (needRaw ? hwnd : IntPtr.Zero)
				}
			}, 1u, (uint)Marshal.SizeOf<Native.RID>());
			rawOn = needRaw;
			if (!needRaw)
			{
				recentRaw.Clear();
			}
		}
		if (needHook && hook == IntPtr.Zero)
		{
			hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0u);
			if (hook == IntPtr.Zero)
			{
				Cfg.Log("keyboard hook could not be installed");
			}
			else
			{
				Native.SetInputPriority(high: true);
				Cfg.Log("keyboard hook on");
			}
		}
		else if (!needHook && hook != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(hook);
			hook = IntPtr.Zero;
			Native.SetInputPriority(high: false);
			Cfg.Log("keyboard hook off");
		}
	}

	private void StartSweep()
	{
		if (!sweepOn)
		{
			W32.SetTimer(hwnd, 1, 15u, IntPtr.Zero);
			sweepOn = true;
		}
	}

	private void StopSweep()
	{
		if (sweepOn)
		{
			W32.KillTimer(hwnd, 1);
			sweepOn = false;
		}
	}

	public void Enable(bool on)
	{
		OnEngine(() =>
		{
			enabled = on;
			if (!on)
			{
				ReleaseAll();
			}
			UpdateRegistration();
		});
	}

	public void SetProfile(MouseProfile mp, KbProfile kp, IEnumerable<Macro> allMacros, bool inGame)
	{
		OnEngine(() =>
		{
			SetProfileCore(mp, kp, allMacros, inGame);
		});
	}

	private void SetProfileCore(MouseProfile mp, KbProfile kp, IEnumerable<Macro> allMacros, bool inGame)
	{
		ReleaseAll();
		Dictionary<(int, bool), KeyTarget> m = new Dictionary<(int, bool), KeyTarget>();
		if (mp != null && inGame)
		{
			foreach (var (id, t) in mp.Buttons)
			{
				if (Base.TryGetValue(id, out var b) && !IsIdentity(b, t))
				{
					m[(b.Sc, b.Ext)] = t;
				}
			}
		}
		map = m;
		macros = allMacros.GroupBy((Macro x) => x.Name).ToDictionary((IGrouping<string, Macro> g) => g.Key, (IGrouping<string, Macro> g) => g.First());
		KbProfile kb = (inGame ? kp : null);
		anyArrowMacro = false;
		string[] arrowNames = new string[4] { "KEY_LEFT_ARROW", "KEY_UP_ARROW", "KEY_RIGHT_ARROW", "KEY_DOWN_ARROW" };
		for (int i = 0; i < 4; i++)
		{
			arrowMacros[i] = ((kb != null && kb.MacroKeys.TryGetValue(arrowNames[i], out var mname) && macros.TryGetValue(mname, out var macro) && macro.Steps.Count > 0) ? new PreparedMacro(macro) : null);
			anyArrowMacro |= arrowMacros[i] != null;
		}
		snapTapActive = (kp?.SnapTap ?? false) && inGame;
		UpdateRegistration();
	}

	private static bool IsIdentity(BaseKey b, KeyTarget t)
	{
		if (t.Mods.Count > 0)
		{
			return false;
		}
		int want = Keys2.Vk(t.Key);
		if (want == 0)
		{
			return false;
		}
		int have = (int)Native.MapVirtualKey((uint)(b.Ext ? (0xE000 | b.Sc) : b.Sc), 3u);
		if (have != 0)
		{
			return have == want;
		}
		return false;
	}

	internal IEnumerable<string> DescribeMap()
	{
		return from kv in map.ToArray()
			select $"sc {kv.Key.Item1:X2}{(kv.Key.Item2 ? "(ext)" : "")} -> {string.Join("+", kv.Value.Mods.Concat(new string[1] { kv.Value.Key }))}" into s
			orderby s
			select s;
	}

	internal void TestRunMacro(Macro m)
	{
		RunMacro(new PreparedMacro(m));
	}

	internal void TestSnapTap(bool on)
	{
		OnEngine(() =>
		{
			snapTapActive = on;
		});
	}

	internal void TestOutput(int vk, bool down)
	{
		OnEngine(() =>
		{
			Output(vk, ext: false, down);
		});
	}

	// WH_KEYBOARD_LL callback. Runs on the input thread for every keystroke while installed, so it must stay
	// allocation-free and fast: Windows drops hooks that are slow, and every microsecond here is input lag.
	private nint Hook(int n, nint w, nint l)
	{
		if (n < 0)
		{
			return Native.CallNextHookEx(hook, n, w, l);
		}
		if (Marshal.ReadIntPtr(l, 16) == Native.MAGIC)
		{
			return Native.CallNextHookEx(hook, n, w, l);
		}
		int vk = Marshal.ReadInt32(l);
		int sc = Marshal.ReadInt32(l, 4);
		int fl = Marshal.ReadInt32(l, 8);
		bool up = (fl & 0x80) != 0;
		bool ext = (fl & 1) != 0;
		try
		{
			if (calibrating != null)
			{
				return Native.CallNextHookEx(hook, n, w, l);
			}
			if (anyArrowMacro && vk >= 37 && vk <= 40)
			{
				PreparedMacro macro = arrowMacros[vk - 37];
				if (macro != null)
				{
					if (up)
					{
						macroKeysDown.Remove(vk);
					}
					else if (macroKeysDown.Add(vk))
					{
						RunMacro(macro);
					}
					return 1;
				}
			}
			if (map.Count > 0 && map.ContainsKey((sc, ext)))
			{
				long now = Environment.TickCount64;
				int raw = -1;
				for (int i = 0; i < recentRaw.Count; i++)
				{
					RawKey k = recentRaw[i];
					if (k.Sc == sc && k.Ext == ext && k.Up == up && now - k.Tick < 60)
					{
						raw = i;
						break;
					}
				}
				if (raw < 0)
				{
					pending.Add(new Pending
					{
						Sc = sc,
						Ext = ext,
						Up = up,
						Vk = vk,
						Tick = now
					});
					StartSweep();
					return 1;
				}
				RawKey r = recentRaw[raw];
				recentRaw.RemoveAt(raw);
				if (r.Naga)
				{
					Naga(sc, ext, up);
					return 1;
				}
			}
			if (snapTapActive && snap.Handles(vk) && snap.OnReal(vk, up, sendPlain))
			{
				return 1;
			}
		}
		catch (Exception ex)
		{
			Cfg.Log("hook: " + ex.Message);
		}
		return Native.CallNextHookEx(hook, n, w, l);
	}

	private void OnRaw(nint h)
	{
		uint sz = 0u;
		Native.GetRawInputData(h, 268435459u, null, ref sz, 24u);
		if (sz > rawBuf.Length)
		{
			rawBuf = new byte[sz];
		}
		byte[] b = rawBuf;
		if (Native.GetRawInputData(h, 268435459u, b, ref sz, 24u) == uint.MaxValue)
		{
			return;
		}
		nint dev = (nint)BitConverter.ToInt64(b, 8);
		if (dev == IntPtr.Zero)
		{
			return;
		}
		ushort make = BitConverter.ToUInt16(b, 24);
		ushort flags = BitConverter.ToUInt16(b, 26);
		bool up = (flags & 1) != 0;
		bool ext = (flags & 2) != 0;
		if (!nagaCache.TryGetValue(dev, out var naga))
		{
			uint n = 0u;
			Native.GetRawInputDeviceInfo(dev, 536870919u, null, ref n);
			StringBuilder sb = new StringBuilder((int)n);
			Native.GetRawInputDeviceInfo(dev, 536870919u, sb, ref n);
			naga = (nagaCache[dev] = sb.ToString().Contains("VID_1532&PID_008D", StringComparison.OrdinalIgnoreCase));
		}
		if (calibrating != null)
		{
			if (naga && !up)
			{
				calibrating(make, ext);
			}
			return;
		}
		int idx = -1;
		for (int i = 0; i < pending.Count; i++)
		{
			Pending p = pending[i];
			if (p.Sc == make && p.Ext == ext && p.Up == up)
			{
				idx = i;
				break;
			}
		}
		if (idx >= 0)
		{
			Pending p2 = pending[idx];
			pending.RemoveAt(idx);
			if (naga)
			{
				Naga(p2.Sc, p2.Ext, p2.Up);
			}
			else
			{
				Output(p2.Vk, p2.Ext, !p2.Up);
			}
			return;
		}
		long now = Environment.TickCount64;
		for (int j = recentRaw.Count - 1; j >= 0; j--)
		{
			if (now - recentRaw[j].Tick > 100)
			{
				recentRaw.RemoveAt(j);
			}
		}
		recentRaw.Add(new RawKey
		{
			Sc = make,
			Ext = ext,
			Up = up,
			Naga = naga,
			Tick = now
		});
	}

	private void Sweep()
	{
		if (pending.Count == 0)
		{
			StopSweep();
			return;
		}
		long now = Environment.TickCount64;
		for (int i = 0; i < pending.Count; i++)
		{
			Pending p = pending[i];
			if (now - p.Tick > 40)
			{
				pending.RemoveAt(i);
				i--;
				Output(p.Vk, p.Ext, !p.Up);
			}
		}
	}

	private void Naga(int sc, bool ext, bool up)
	{
		if (!map.TryGetValue((sc, ext), out var t))
		{
			return;
		}
		int vk = Keys2.Vk(t.Key);
		if (vk == 0)
		{
			return;
		}
		if (!up)
		{
			foreach (string mod in t.Mods)
			{
				int mv = Keys2.Vk(mod);
				if (mv != 0)
				{
					Output(mv, Keys2.IsExt(mv), down: true);
				}
			}
		}
		Output(vk, Keys2.IsExt(vk), !up);
		if (!up)
		{
			return;
		}
		foreach (string mod2 in t.Mods)
		{
			int mv2 = Keys2.Vk(mod2);
			if (mv2 != 0)
			{
				Output(mv2, Keys2.IsExt(mv2), down: false);
			}
		}
	}

	// Injects a key for remapped Naga buttons and for delayed keyboard keys, applying Snap Tap to A/D.
	private void Output(int vk, bool ext, bool down)
	{
		if (snapTapActive && snap.Handles(vk))
		{
			snap.OnOutput(vk, down, sendPlain);
			return;
		}
		if (down)
		{
			injected.Add(vk);
		}
		else
		{
			injected.Remove(vk);
		}
		Native.SendKey(vk, ext || Keys2.IsExt(vk), down);
	}

	private void ReleaseAll()
	{
		snap.Reset(sendPlain);
		foreach (int item in injected)
		{
			Native.SendKey(item, Keys2.IsExt(item), down: false);
		}
		injected.Clear();
		pending.Clear();
		macroKeysDown.Clear();
	}

	private void RunMacro(PreparedMacro m)
	{
		if (macroRunning)
		{
			return;
		}
		macroRunning = true;
		Volatile.Write(ref macroNext, m);
		macroSignal.Set();
	}

	private void MacroLoop()
	{
		using Native.PreciseTimer timer = new Native.PreciseTimer();
		while (true)
		{
			macroSignal.WaitOne();
			if (disposed)
			{
				return;
			}
			PreparedMacro m = Interlocked.Exchange(ref macroNext, null);
			if (m == null)
			{
				macroRunning = false;
				continue;
			}
			bool lowRes = !timer.HighResolution;
			if (lowRes)
			{
				Native.timeBeginPeriod(1u);
			}
			try
			{
				MacroPlan.Segment[] segs = m.Plan.Segments;
				for (int i = 0; i < segs.Length; i++)
				{
					Native.SendKeys(m.Batches[i], m.Batches[i].Length);
					timer.Sleep(segs[i].DelayMs);
				}
			}
			catch (Exception ex)
			{
				Cfg.Log("macro: " + ex.Message);
			}
			finally
			{
				Native.SendKeys(m.Release, m.Release.Length);
				if (lowRes)
				{
					Native.timeEndPeriod(1u);
				}
				macroRunning = false;
			}
		}
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}
		try
		{
			OnEngine(() =>
			{
				StopSweep();
				ReleaseAll();
				if (hook != IntPtr.Zero)
				{
					Native.UnhookWindowsHookEx(hook);
					hook = IntPtr.Zero;
					Native.SetInputPriority(high: false);
				}
				if (rawOn)
				{
					Native.RegisterRawInputDevices(new Native.RID[1]
					{
						new Native.RID
						{
							page = 1,
							usage = 6,
							flags = RIDEV_REMOVE,
							hwnd = IntPtr.Zero
						}
					}, 1u, (uint)Marshal.SizeOf<Native.RID>());
					rawOn = false;
				}
				W32.DestroyWindow(hwnd);
				W32.PostQuitMessage(0);
			});
			disposed = true;
			macroSignal.Set();
			thread.Join(2000);
		}
		catch
		{
		}
	}
}
