using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime;
using System.Runtime.CompilerServices;
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

	// Sized up front: the hook adds to these, and a collection's first Add allocates its backing store. That
	// would be a GC allocation on the hook thread on the first remapped keystroke of every session.
	private readonly List<Pending> pending = new List<Pending>(16);

	private readonly List<RawKey> recentRaw = new List<RawKey>(32);

	private byte[] rawBuf = new byte[64];

	private Dictionary<(int, bool), KeyTarget> map = new Dictionary<(int, bool), KeyTarget>();

	private Dictionary<string, Macro> macros = new Dictionary<string, Macro>();

	// A macro with its send batches built ahead of time, so triggering it costs one SendInput per segment.
	private sealed class PreparedMacro
	{
		public readonly string Name;

		public readonly MacroPlan Plan;

		public readonly Native.INPUT[][] Batches;

		public readonly Native.INPUT[] Release;

		// Key-ups for what is still held after each segment (a macro stopped part way sends the one for where it was).
		public readonly Native.INPUT[][] ReleaseAfter;

		public PreparedMacro(Macro m)
		{
			Name = m.Name ?? "";
			Plan = new MacroPlan(m.Steps);
			Batches = new Native.INPUT[Plan.Segments.Length][];
			for (int i = 0; i < Batches.Length; i++)
			{
				MacroPlan.Key[] keys = Plan.Segments[i].Keys;
				Batches[i] = new Native.INPUT[keys.Length];
				for (int j = 0; j < keys.Length; j++)
				{
					Batches[i][j] = Input(keys[j]);
				}
			}
			Release = Ups(Plan.LeftDown);
			ReleaseAfter = new Native.INPUT[Plan.HeldAfter.Length][];
			for (int i = 0; i < ReleaseAfter.Length; i++)
			{
				ReleaseAfter[i] = Ups(Plan.HeldAfter[i]);
			}
		}

		private static Native.INPUT[] Ups(MacroPlan.Key[] keys)
		{
			Native.INPUT[] ups = new Native.INPUT[keys.Length];
			for (int k = 0; k < ups.Length; k++)
			{
				ups[k] = Input(new MacroPlan.Key(keys[k].Vk, keys[k].Ext, down: false, keys[k].Mouse));
			}
			return ups;
		}

		// A key step as a keyboard event, a mouse-button step as a mouse event.
		private static Native.INPUT Input(MacroPlan.Key k)
		{
			return (k.Mouse != 0) ? Native.MouseInput(k.Mouse, k.Down) : Native.KeyInput(k.Vk, k.Ext, k.Down);
		}
	}

	// Macro bound to each arrow key, indexed by vk - 37 (left, up, right, down).
	private readonly PreparedMacro[] arrowMacros = new PreparedMacro[4];

	// What the hook was last armed with, as logged ("macros: Up = W Jumpthrow ..."), so the line is written when it
	// changes rather than on every alt-tab. Input thread only.
	private string armedLogged;

	// Macro runs logged so far this session: the first few are logged even when they went through, so a log
	// shows whether a key press reached the macro at all. Macro thread only.
	private int macrosLogged;

	// Presses that arrived while a macro was still running and were dropped (the hook only counts them).
	private int macroSkipped;

	private static readonly string[] ArrowNames = new string[4] { "Left", "Up", "Right", "Down" };

	private bool anyArrowMacro;

	private bool snapTapActive;

	private readonly HashSet<int> macroKeysDown = new HashSet<int>(8);

	// Arrow keys already held when the hook was installed: their auto-repeat must not fire a macro and
	// their release must reach the game.
	private readonly HashSet<int> heldAtInstall = new HashSet<int>(8);

	private GCLatencyMode savedGcMode;

	// A real linked game is in front (not the desktop, not a hand-picked app, not just "run macros anywhere"):
	// only then is the low-latency GC mode held. See GcMode.
	private bool linkedGameActive;

	// The GC is held in low-latency mode while a linked game is in front. Garbage collection here is not concurrent,
	// so a gen-2 collection triggered by any other thread (window, tray, device worker) stops every thread - and a
	// keystroke arriving then waits in Windows for the hook. It is held only during an actual game, so a tray session
	// that keeps the hook armed on the desktop (for "run macros anywhere") does not suppress gen-2 collections for days.
	private bool lowLatencySet;

	private Action<int, bool> calibrating;

	public Dictionary<string, BaseKey> Base = new Dictionary<string, BaseKey>();

	private readonly SnapTap snap = new SnapTap(VK_A, VK_D);

	private readonly Action<int, bool> sendPlain = (int vk, bool down) => Native.SendKey(vk, ext: false, down);

	// A software remap to middle click is holding the middle button down, so ReleaseAll can lift it.
	private bool middleDown;

	// Keys held down by remapped output (not Snap Tap), so ReleaseAll only lifts what we pressed.
	private readonly HashSet<int> injected = new HashSet<int>(16);

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

	// Bumped by ReleaseAll (profile change, engine off, Dispose): a running macro stops after the batch it is
	// sending and lifts the keys it holds. macroNextGen is the value when the waiting macro was triggered.
	private int macroGen;

	private int macroNextGen;

	private readonly ManualResetEvent macroCancel = new ManualResetEvent(initialState: false);

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
			Native.SetCurrentThreadTimeCritical();
			// This thread only: the rest of the process (window, tray, device worker, scans) is background
			// work Windows may run slower or on efficiency cores, away from the game's cores.
			Native.OptOutOfPowerThrottling(process: false);
			(nint, W32.WndProc) tuple = W32.CreateWindow(messageOnly: true, OnMessage);
			hwnd = tuple.Item1;
			keepAlive = tuple.Item2;
			hookProc = Hook;
			WarmUp();
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
			Priority = ThreadPriority.Highest
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

	// Compile the keystroke path up front so the first key press in a game never waits for the JIT.
	private void WarmUp()
	{
		try
		{
			RuntimeHelpers.PrepareMethod(typeof(InputEngine).GetMethod("Hook", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).MethodHandle);
			RuntimeHelpers.PrepareMethod(typeof(InputEngine).GetMethod("Output", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).MethodHandle);
			RuntimeHelpers.PrepareMethod(typeof(SnapTap).GetMethod("OnReal").MethodHandle);
			RuntimeHelpers.PrepareMethod(typeof(SnapTap).GetMethod("OnOutput").MethodHandle);
			RuntimeHelpers.PrepareMethod(typeof(Native).GetMethod("SendKey").MethodHandle);
			RuntimeHelpers.PrepareMethod(typeof(Native).GetMethod("SendKeys").MethodHandle);
			Native.SendKeys(new Native.INPUT[1], 0);
			Native.KeyInput(65, ext: false, down: false);
			// The P/Invoke marshalling stubs are not in the precompiled image: build them here, on this thread,
			// rather than on the first counter-strafe of the first match. Same for the dictionary and set
			// instantiations the hook uses.
			foreach (Type t in new[] { typeof(Native), typeof(W32) })
			{
				try
				{
					System.Runtime.InteropServices.Marshal.PrelinkAll(t);
				}
				catch (Exception ex)
				{
					// One entry point this Windows lacks must not stop the rest.
					Cfg.Log("warm-up (" + t.Name + "): " + ex.Message);
				}
			}
			new Dictionary<(int, bool), KeyTarget>().ContainsKey((0, false));
			heldAtInstall.Contains(0);
			macroKeysDown.Contains(0);
		}
		catch (Exception ex)
		{
			Cfg.Log("warm-up: " + ex.Message);
		}
	}

	// Installs or removes the hook and the raw-input sink to match what the active profile needs.
	// reinstall: put the hook back at the top of the chain even if it exists (Windows silently drops a
	// hook that ever times out, so it is refreshed every time a game profile is applied).
	private void UpdateRegistration(bool reinstall = false)
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
				// nagaCache is keyed by the raw-input device handle, which Windows re-issues on every replug, so a
				// stale entry would otherwise linger for the whole session. OnRaw never runs while the sink is off,
				// and a device that comes back gets a fresh handle and is re-probed, so dropping it here is safe.
				nagaCache.Clear();
			}
		}
		if (needHook && hook != IntPtr.Zero && reinstall)
		{
			Native.UnhookWindowsHookEx(hook);
			hook = IntPtr.Zero;
		}
		if (needHook && hook == IntPtr.Zero)
		{
			heldAtInstall.Clear();
			for (int vk = 37; vk <= 40; vk++)
			{
				if ((Native.GetAsyncKeyState(vk) & 0x8000) != 0)
				{
					heldAtInstall.Add(vk);
				}
			}
			// A Naga button already held when the remap table goes in (push-to-talk while alt-tabbing into the
			// game) reached Windows as its own key: its release has to reach Windows too, or that key stays
			// down until it is pressed again.
			foreach ((int sc, bool ext) in map.Keys)
			{
				int held = (int)Native.MapVirtualKey((uint)(ext ? (0xE000 | sc) : sc), 3u);
				if (held != 0 && (Native.GetAsyncKeyState(held) & 0x8000) != 0)
				{
					heldAtInstall.Add(held);
				}
			}
			hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0u);
			if (hook == IntPtr.Zero)
			{
				Cfg.Log("keyboard hook could not be installed: error " + Marshal.GetLastPInvokeError());
				armedLogged = null;
			}
			else
			{
				LogArmed();
			}
		}
		else if (!needHook && hook != IntPtr.Zero)
		{
			Native.UnhookWindowsHookEx(hook);
			hook = IntPtr.Zero;
			heldAtInstall.Clear();
			Cfg.Log("keyboard hook off");
		}
		GcMode(hook != IntPtr.Zero && linkedGameActive);
	}

	// One line saying what the keyboard hook now does, when that changes: without it a log could not tell a macro
	// that never armed from one that armed and did nothing in the game.
	private void LogArmed()
	{
		List<string> parts = new List<string>();
		List<string> armed = new List<string>();
		for (int i = 0; i < 4; i++)
		{
			if (arrowMacros[i] != null)
			{
				armed.Add(ArrowNames[i] + " arrow = " + arrowMacros[i].Name);
			}
		}
		parts.Add(armed.Count > 0 ? "macros " + string.Join(", ", armed) : "no macros");
		parts.Add(snapTapActive ? "Snap Tap on" : "Snap Tap off");
		parts.Add(map.Count + " software remap(s)");
		string line = "keyboard hook on: " + string.Join("; ", parts);
		if (line != armedLogged)
		{
			armedLogged = line;
			Cfg.Log(line);
		}
	}

	// Keeps gen-2 collections off the hook's back while a linked game is in front; restored as soon as it is not.
	// SustainedLowLatency, not LowLatency: the hook stays on for a whole match, and LowLatency is meant for a short
	// burst - it only holds gen-2 off until memory pressure forces one big blocking collection. SustainedLowLatency
	// is the mode for a window held open indefinitely: gen-2 still runs when it must, but in the background
	// (concurrent, non-compacting), so it never stops the input thread. The engine allocates almost nothing during
	// play, so either way collections are rare; this just makes the rare one not a stall.
	private void GcMode(bool low)
	{
		if (low && !lowLatencySet)
		{
			savedGcMode = GCSettings.LatencyMode;
			GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
			lowLatencySet = true;
		}
		else if (!low && lowLatencySet)
		{
			GCSettings.LatencyMode = savedGcMode;
			lowLatencySet = false;
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

	// snapTapAllowed: whether Snap Tap may run in this context (a linked game, a hand-picked context, or "Run Snap
	//   Tap everywhere too"). Arrow macros run wherever the keyboard profile is given; Snap Tap is gated by this so
	//   it does not reorder A/D while the user is typing on the desktop.
	// linkedGame: a real linked game is in front. The low-latency GC mode is held only then, so a tray-resident
	//   "run macros anywhere" session (hook armed on the desktop for arrow macros) does not suppress gen-2 for days.
	public void SetProfile(MouseProfile mp, KbProfile kp, IEnumerable<Macro> allMacros, bool inGame, bool snapTapAllowed = true, bool linkedGame = true)
	{
		// Build everything on the caller's thread; the input thread only swaps references, so it is never
		// busy with LINQ or allocation while a game is in front.
		Dictionary<(int, bool), KeyTarget> m = new Dictionary<(int, bool), KeyTarget>();
		Dictionary<string, BaseKey> baseKeys = Base;
		// profiles.json is edited by hand sometimes: a null entry anywhere is skipped, never a crash that leaves
		// the game without its profile.
		if (mp?.Buttons != null && inGame && baseKeys != null)
		{
			foreach (var (id, t) in mp.Buttons)
			{
				if (id != null && t?.Key != null && t.Mods != null && baseKeys.TryGetValue(id, out var b) && b != null && !IsIdentity(b, t))
				{
					m[(b.Sc, b.Ext)] = t;
				}
			}
		}
		Dictionary<string, Macro> byName = (allMacros ?? Enumerable.Empty<Macro>())
			.Where((Macro x) => x?.Name != null && x.Steps != null)
			.GroupBy((Macro x) => x.Name).ToDictionary((IGrouping<string, Macro> g) => g.Key, (IGrouping<string, Macro> g) => g.First());
		KbProfile kb = (inGame ? kp : null);
		PreparedMacro[] arrows = new PreparedMacro[4];
		bool anyArrow = false;
		string[] arrowNames = new string[4] { "KEY_LEFT_ARROW", "KEY_UP_ARROW", "KEY_RIGHT_ARROW", "KEY_DOWN_ARROW" };
		for (int i = 0; i < 4; i++)
		{
			arrows[i] = ((kb?.MacroKeys != null && kb.MacroKeys.TryGetValue(arrowNames[i], out var mname) && mname != null && byName.TryGetValue(mname, out var macro) && macro.Steps.Count > 0) ? new PreparedMacro(macro) : null);
			anyArrow |= arrows[i] != null;
		}
		bool snapOn = (kp?.SnapTap ?? false) && inGame && snapTapAllowed;
		OnEngine(() =>
		{
			linkedGameActive = linkedGame;
			// A keyboard layout switched between games is picked up here, not on a keystroke - and the two keys
			// Snap Tap injects are looked up again right away, on this thread, so the first strafe of the match
			// does not pay for it.
			Native.ResetScanCodes();
			Native.KeyInput(VK_A, ext: false, down: false);
			Native.KeyInput(VK_D, ext: false, down: false);
			ReleaseAll();
			map = m;
			macros = byName;
			Array.Copy(arrows, arrowMacros, 4);
			anyArrowMacro = anyArrow;
			snapTapActive = snapOn;
			UpdateRegistration(reinstall: true);
		});
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
					if (heldAtInstall.Contains(vk))
					{
						if (up)
						{
							heldAtInstall.Remove(vk);
						}
						return Native.CallNextHookEx(hook, n, w, l);
					}
					if (up)
					{
						if (!macroKeysDown.Remove(vk))
						{
							return Native.CallNextHookEx(hook, n, w, l);
						}
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
				if (heldAtInstall.Count > 0 && heldAtInstall.Contains(vk))
				{
					// Held since before the remap table: Windows has its down, so it gets its up.
					if (up)
					{
						heldAtInstall.Remove(vk);
					}
					return Native.CallNextHookEx(hook, n, w, l);
				}
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
		if (t.Key == KeyTarget.MouseMiddle)
		{
			// Only on a real state change: the Naga's onboard key auto-repeats while held, and a mouse button is
			// edge-triggered, not auto-repeating - so dedup, or a held button machine-guns MIDDLEDOWN.
			if ((!up) != middleDown)
			{
				Native.SendMiddleButton(!up);
				middleDown = !up;
			}
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
		Interlocked.Increment(ref macroGen);
		macroCancel.Set();
		snap.Reset(sendPlain);
		if (middleDown)
		{
			// A button remapped to middle click was held when the profile changed: the mouse's own release goes to
			// its native key, so the middle button would stay down for every program.
			Native.SendMiddleButton(down: false);
			middleDown = false;
		}
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
			Interlocked.Increment(ref macroSkipped);
			return;
		}
		macroRunning = true;
		macroNextGen = Volatile.Read(ref macroGen);
		Volatile.Write(ref macroNext, m);
		macroSignal.Set();
	}

	private void MacroLoop()
	{
		Native.OptOutOfPowerThrottling(process: false);
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
			int gen = Volatile.Read(ref macroNextGen);
			macroCancel.Reset();
			if (gen != Volatile.Read(ref macroGen) || disposed)
			{
				// The profile changed (or LiteSynapse is closing) between the key press and now.
				macroRunning = false;
				continue;
			}
			bool lowRes = !timer.HighResolution;
			if (lowRes)
			{
				Native.timeBeginPeriod(1u);
			}
			int sent = -1;
			int wanted = 0;
			int took = 0;
			int error = 0;
			bool stopped = false;
			try
			{
				MacroPlan.Segment[] segs = m.Plan.Segments;
				for (int i = 0; i < segs.Length; i++)
				{
					if (gen != Volatile.Read(ref macroGen) || disposed)
					{
						stopped = true;
						break;
					}
					int len = m.Batches[i].Length;
					int n = Native.SendKeysChecked(m.Batches[i], len, out int e);
					wanted += len;
					took += n;
					error = (n < len) ? e : error;
					sent = i;
					if (!timer.Sleep(segs[i].DelayMs, macroCancel))
					{
						stopped = true;
						break;
					}
				}
			}
			catch (Exception ex)
			{
				Cfg.Log("macro: " + ex.Message);
			}
			finally
			{
				// Lifts what the macro still holds where it stopped (all of LeftDown when it ran to the end).
				if (sent >= 0)
				{
					Native.INPUT[] up = m.ReleaseAfter[sent];
					int n = Native.SendKeysChecked(up, up.Length, out int e);
					wanted += up.Length;
					took += n;
					error = (n < up.Length) ? e : error;
				}
				if (lowRes)
				{
					Native.timeEndPeriod(1u);
				}
				macroRunning = false;
			}
			// Logged after the macro, never between its steps. Every refusal is logged; a run that went through
			// is logged for the first few of the session, so a log shows whether the key press reached it.
			if (took < wanted || macrosLogged < 5)
			{
				macrosLogged++;
				int skipped = Interlocked.Exchange(ref macroSkipped, 0);
				Cfg.Log($"macro \"{m.Name}\": Windows took {took} of {wanted} key events" +
					((took < wanted) ? $" (error {error}{(error == 5 ? ": the window in front runs as administrator" : "")})" : "") +
					(stopped ? ", stopped early by a profile change" : "") +
					((skipped > 0) ? $"; {skipped} press(es) dropped while a macro was still running" : ""));
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
			// A macro that was running stops at once (ReleaseAll above) and lifts its keys before the process ends.
			macroThread.Join(1000);
		}
		catch
		{
		}
	}
}
