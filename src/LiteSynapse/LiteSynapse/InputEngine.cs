using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LiteSynapse;

internal class InputEngine : IDisposable
{
	private record struct Pending(int Sc, bool Ext, bool Up, int Vk, long Tick);

	private record struct RawKey(int Sc, bool Ext, bool Up, bool Naga, long Tick);

	public static readonly string[] ButtonIds = new string[17]
	{
		"Side1", "Side2", "Side3", "Side4", "Side5", "Side6", "Side7", "Side8", "Side9", "Side10",
		"Side11", "Side12", "Scroll", "M1", "M2", "TiltL", "TiltR"
	};

	private Native.HookProc hookProc;

	private nint hook;

	private readonly Dictionary<nint, bool> nagaCache = new Dictionary<nint, bool>();

	private readonly List<Pending> pending = new List<Pending>();

	private readonly List<RawKey> recentRaw = new List<RawKey>();

	private Dictionary<(int, bool), KeyTarget> map = new Dictionary<(int, bool), KeyTarget>();

	private Dictionary<string, Macro> macros = new Dictionary<string, Macro>();

	private KbProfile kb;

	private bool snapTapActive;

	private volatile bool macroRunning;

	private readonly HashSet<int> macroKeysDown = new HashSet<int>();

	public Action<int, bool> Calibrating;

	public Dictionary<string, BaseKey> Base = new Dictionary<string, BaseKey>();

	private readonly HashSet<int> outHeld = new HashSet<int>();

	private readonly HashSet<int> physHeld = new HashSet<int>();

	private const int VK_A = 65;

	private const int VK_D = 68;

	private Thread thread;

	private int threadId;

	private nint hwnd;

	private W32.WndProc keepAlive;

	private bool sweepOn;

	private bool disposed;

	private readonly ConcurrentQueue<Action> queue = new ConcurrentQueue<Action>();

	internal bool MacroBusy => macroRunning;

	public InputEngine()
	{
		InputEngine inputEngine = this;
		ManualResetEventSlim ready = new ManualResetEventSlim();
		try
		{
			thread = new Thread(() =>
			{
				inputEngine.threadId = Environment.CurrentManagedThreadId;
				(nint, W32.WndProc) tuple = W32.CreateWindow(messageOnly: true, inputEngine.OnMessage);
				inputEngine.hwnd = tuple.Item1;
				inputEngine.keepAlive = tuple.Item2;
				Native.RegisterRawInputDevices(new Native.RID[1]
				{
					new Native.RID
					{
						page = 1,
						usage = 6,
						flags = 256u,
						hwnd = inputEngine.hwnd
					}
				}, 1u, (uint)Marshal.SizeOf<Native.RID>());
				inputEngine.hookProc = inputEngine.Hook;
				inputEngine.hook = Native.SetWindowsHookEx(13, inputEngine.hookProc, Native.GetModuleHandle(null), 0u);
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
		}
		finally
		{
			if (ready != null)
			{
				((IDisposable)ready).Dispose();
			}
		}
	}

	private nint? OnMessage(uint m, nint w, nint l)
	{
		switch (m)
		{
		case 255u:
			try
			{
				OnRaw(l);
			}
			catch (Exception ex)
			{
				Cfg.Log("raw: " + ex.Message);
			}
			return null;
		case 275u:
			if ((long)w == 1)
			{
				Sweep();
				return IntPtr.Zero;
			}
			break;
		}
		if (m == 32768)
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
		ManualResetEventSlim done = new ManualResetEventSlim();
		try
		{
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
			W32.PostMessage(hwnd, 32768u, IntPtr.Zero, IntPtr.Zero);
			done.Wait(2000);
		}
		finally
		{
			if (done != null)
			{
				((IDisposable)done).Dispose();
			}
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
			EnableCore(on);
		});
	}

	private void EnableCore(bool on)
	{
		if (on && hook == IntPtr.Zero)
		{
			Native.RegisterRawInputDevices(new Native.RID[1]
			{
				new Native.RID
				{
					page = 1,
					usage = 6,
					flags = 256u,
					hwnd = hwnd
				}
			}, 1u, (uint)Marshal.SizeOf<Native.RID>());
			hook = Native.SetWindowsHookEx(13, hookProc, Native.GetModuleHandle(null), 0u);
		}
		else if (!on && hook != IntPtr.Zero)
		{
			ReleaseAll();
			Native.UnhookWindowsHookEx(hook);
			hook = IntPtr.Zero;
			Native.RegisterRawInputDevices(new Native.RID[1]
			{
				new Native.RID
				{
					page = 1,
					usage = 6,
					flags = 1u,
					hwnd = IntPtr.Zero
				}
			}, 1u, (uint)Marshal.SizeOf<Native.RID>());
			recentRaw.Clear();
		}
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
		if ((mp != null) & inGame)
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
		kb = (inGame ? kp : null);
		macros = allMacros.ToDictionary((Macro x) => x.Name, (Macro x) => x);
		snapTapActive = (kp?.SnapTap ?? false) & inGame;
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
		RunMacro(m);
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

	private nint Hook(int n, nint w, nint l)
	{
		if (n < 0)
		{
			return Native.CallNextHookEx(hook, n, w, l);
		}
		int vk = Marshal.ReadInt32(l);
		int sc = Marshal.ReadInt32(l, 4);
		int fl = Marshal.ReadInt32(l, 8);
		if (Marshal.ReadIntPtr(l, 16) == Native.MAGIC)
		{
			return Native.CallNextHookEx(hook, n, w, l);
		}
		bool up = (fl & 0x80) != 0;
		bool ext = (fl & 1) != 0;
		try
		{
			if (Calibrating != null)
			{
				return Native.CallNextHookEx(hook, n, w, l);
			}
			if (kb != null && kb.MacroKeys.Count > 0)
			{
				string keyName = vk switch
				{
					38 => "KEY_UP_ARROW", 
					39 => "KEY_RIGHT_ARROW", 
					37 => "KEY_LEFT_ARROW", 
					40 => "KEY_DOWN_ARROW", 
					_ => null, 
				};
				if (keyName != null && kb.MacroKeys.TryGetValue(keyName, out var mname) && macros.TryGetValue(mname, out var macro))
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
				int raw = recentRaw.FindIndex((RawKey rawKey) => rawKey.Sc == sc && rawKey.Ext == ext && rawKey.Up == up && now - rawKey.Tick < 60);
				if (raw < 0)
				{
					pending.Add(new Pending(sc, ext, up, vk, now));
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
			if (snapTapActive && (vk == 65 || vk == 68))
			{
				Output(vk, ext: false, !up);
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
		byte[] b = new byte[sz];
		Native.GetRawInputData(h, 268435459u, b, ref sz, 24u);
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
		if (Calibrating != null)
		{
			if (naga && !up)
			{
				Calibrating(make, ext);
			}
			return;
		}
		int i = pending.FindIndex((Pending pending) => pending.Sc == make && pending.Ext == ext && pending.Up == up);
		if (i >= 0)
		{
			Pending p = pending[i];
			pending.RemoveAt(i);
			if (naga)
			{
				Naga(p.Sc, p.Ext, p.Up);
			}
			else
			{
				Output(p.Vk, p.Ext, !p.Up);
			}
		}
		else
		{
			long now = Environment.TickCount64;
			recentRaw.RemoveAll((RawKey r) => now - r.Tick > 100);
			recentRaw.Add(new RawKey(make, ext, up, naga, now));
		}
	}

	private void Sweep()
	{
		if (pending.Count == 0)
		{
			StopSweep();
			return;
		}
		long now = Environment.TickCount64;
		foreach (Pending p in pending.Where((Pending pending) => now - pending.Tick > 40).ToList())
		{
			pending.Remove(p);
			Output(p.Vk, p.Ext, !p.Up);
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

	private void Output(int vk, bool ext, bool down)
	{
		if (snapTapActive && (vk == 65 || vk == 68))
		{
			int other = ((vk == 65) ? 68 : 65);
			if (down)
			{
				physHeld.Add(vk);
				if (outHeld.Remove(other))
				{
					Native.SendKey(other, ext: false, down: false);
				}
				outHeld.Add(vk);
				Native.SendKey(vk, ext: false, down: true);
				return;
			}
			physHeld.Remove(vk);
			if (outHeld.Remove(vk))
			{
				Native.SendKey(vk, ext: false, down: false);
			}
			if (physHeld.Contains(other) && outHeld.Add(other))
			{
				Native.SendKey(other, ext: false, down: true);
			}
		}
		else
		{
			if (down)
			{
				outHeld.Add(vk);
			}
			else
			{
				outHeld.Remove(vk);
			}
			Native.SendKey(vk, ext || Keys2.IsExt(vk), down);
		}
	}

	private void ReleaseAll()
	{
		foreach (int item in outHeld.ToList())
		{
			Native.SendKey(item, Keys2.IsExt(item), down: false);
		}
		outHeld.Clear();
		physHeld.Clear();
		pending.Clear();
	}

	private void RunMacro(Macro m)
	{
		if (macroRunning)
		{
			return;
		}
		macroRunning = true;
		Thread thread = new Thread(() =>
		{
			Native.timeBeginPeriod(1u);
			HashSet<(int, bool)> hashSet = new HashSet<(int, bool)>();
			try
			{
				foreach (MacroStep current in m.Steps)
				{
					if (current.Vk == 0)
					{
						if (current.DelayMs > 0)
						{
							Thread.Sleep(current.DelayMs);
						}
					}
					else
					{
						Native.SendKey(current.Vk, current.Ext, current.Down);
						if (current.Down)
						{
							hashSet.Add((current.Vk, current.Ext));
						}
						else
						{
							hashSet.Remove((current.Vk, current.Ext));
						}
						if (current.DelayMs > 0)
						{
							Thread.Sleep(current.DelayMs);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Cfg.Log("macro: " + ex.Message);
			}
			finally
			{
				foreach (var (vk, ext) in hashSet)
				{
					Native.SendKey(vk, ext, down: false);
				}
				macroRunning = false;
				Native.timeEndPeriod(1u);
			}
		});
		thread.IsBackground = true;
		thread.Priority = ThreadPriority.AboveNormal;
		thread.Name = "LiteSynapse macro";
		thread.Start();
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
				}
				hook = IntPtr.Zero;
				W32.DestroyWindow(hwnd);
				W32.PostQuitMessage(0);
			});
			disposed = true;
			thread.Join(2000);
		}
		catch
		{
		}
	}
}
