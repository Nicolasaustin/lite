using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace LiteSynapse;

internal static class Native
{
	public delegate nint HookProc(int n, nint w, nint l);

	public delegate void WinEventProc(nint hook, uint evt, nint hwnd, int idObj, int idChild, uint thread, uint time);

	public struct RID
	{
		public ushort page;

		public ushort usage;

		public uint flags;

		public nint hwnd;
	}

	public struct KEYBDINPUT
	{
		public ushort vk;

		public ushort sc;

		public uint flags;

		public uint time;

		public nint extra;
	}

	public struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint data;

		public uint flags;

		public uint time;

		public nint extra;
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct INPUTUNION
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		[FieldOffset(0)]
		public KEYBDINPUT ki;
	}

	public struct INPUT
	{
		public uint type;

		public INPUTUNION u;
	}

	public const int WH_KEYBOARD_LL = 13;

	public static readonly nint MAGIC = new IntPtr(1280527961);

	// SetLastError: called once per install, and a refused install is logged with Windows' reason.
	[DllImport("user32", SetLastError = true)]
	public static extern nint SetWindowsHookEx(int id, HookProc p, nint m, uint t);

	[DllImport("user32")]
	public static extern bool UnhookWindowsHookEx(nint h);

	[DllImport("user32")]
	public static extern nint CallNextHookEx(nint h, int n, nint w, nint l);

	[DllImport("kernel32")]
	public static extern nint GetModuleHandle(string n);

	[DllImport("user32")]
	public static extern uint MapVirtualKey(uint code, uint type);

	// No SetLastError: nothing reads it, and asking for it wraps every injected key in a save/restore of the
	// thread's last-error value.
	[DllImport("user32")]
	public static extern uint SendInput(uint n, INPUT[] i, int size);

	[DllImport("user32")]
	public static extern bool RegisterRawInputDevices(RID[] d, uint n, uint sz);

	[DllImport("user32")]
	public static extern uint GetRawInputData(nint h, uint cmd, byte[] d, ref uint sz, uint hs);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	public static extern uint GetRawInputDeviceInfo(nint dev, uint cmd, StringBuilder d, ref uint sz);

	[DllImport("user32")]
	public static extern nint GetForegroundWindow();

	[DllImport("user32")]
	public static extern short GetAsyncKeyState(int vk);

	[DllImport("winmm")]
	public static extern uint timeBeginPeriod(uint ms);

	[DllImport("winmm")]
	public static extern uint timeEndPeriod(uint ms);

	[DllImport("user32")]
	public static extern nint SetWinEventHook(uint min, uint max, nint mod, WinEventProc p, uint pid, uint tid, uint flags);

	[DllImport("user32")]
	public static extern bool UnhookWinEvent(nint h);

	[DllImport("kernel32")]
	public static extern bool SetProcessWorkingSetSize(nint h, nint min, nint max);

	// Used to force a full GC and page the working set out when a window closed. That blocked for a moment
	// and caused page faults right as the user clicked back into a game, so it now only hints the GC.
	public static void TrimMemory()
	{
		GC.Collect(2, GCCollectionMode.Optimized, blocking: false);
	}

	[DllImport("user32")]
	public static extern uint GetWindowThreadProcessId(nint h, out uint pid);

	[DllImport("kernel32", SetLastError = true)]
	public static extern nint OpenProcess(uint acc, bool inh, uint pid);

	[DllImport("kernel32")]
	public static extern bool CloseHandle(nint h);

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	public static extern bool QueryFullProcessImageName(nint h, uint flags, StringBuilder b, ref uint size);

	private static readonly int InputSize = Marshal.SizeOf<INPUT>();

	[ThreadStatic]
	private static INPUT[] one;

	// Scan code per virtual key, filled on first use. MapVirtualKey is a round trip into win32k, and every key
	// the hook injected - each Snap Tap release and re-press - made one. It answers from the calling thread's
	// keyboard layout, which for the input thread is fixed when the thread starts, so the answer for a key
	// never changes while the thread lives; the cache is still cleared on each profile apply, so a layout
	// switched between games is picked up.
	private static readonly ushort[] scanCodes = new ushort[256];

	public static void ResetScanCodes()
	{
		Array.Clear(scanCodes);
	}

	private static ushort ScanCode(int vk)
	{
		if ((uint)vk > 255u)
		{
			return (ushort)MapVirtualKey((uint)vk, 0u);
		}
		ushort sc = scanCodes[vk];
		if (sc == 0)
		{
			// A key with no scan code maps to 0 and is asked again next time: rare, and never A, D or an arrow.
			sc = (ushort)MapVirtualKey((uint)vk, 0u);
			scanCodes[vk] = sc;
		}
		return sc;
	}

	public static INPUT KeyInput(int vk, bool ext, bool down)
	{
		INPUT i = new INPUT
		{
			type = 1u
		};
		uint upFlag = (!down) ? 2u : 0u;
		if (vk == VkPause)
		{
			// Pause is E1 1D 45 on the wire, which one scan code cannot say: sent by virtual key instead.
			i.u.ki = new KEYBDINPUT
			{
				vk = (ushort)vk,
				sc = 0,
				flags = upFlag,
				extra = MAGIC
			};
			return i;
		}
		// Print Screen's own code is E0 37; MapVirtualKey answers 54, the Alt+PrtSc (SysRq) code.
		bool printScreen = vk == VkSnapshot;
		i.u.ki = new KEYBDINPUT
		{
			vk = 0,
			sc = printScreen ? (ushort)0x37 : ScanCode(vk),
			flags = 8u | upFlag | ((ext || printScreen) ? 1u : 0u),
			extra = MAGIC
		};
		return i;
	}

	private const int VkPause = 0x13;

	private const int VkSnapshot = 0x2C;

	public static void SendKey(int vk, bool ext, bool down)
	{
		INPUT[] a = one ??= new INPUT[1];
		a[0] = KeyInput(vk, ext, down);
		SendInput(1u, a, InputSize);
	}

	// Middle mouse button for remapped Naga buttons (software-remap mode only; onboard remaps need nothing).
	public static void SendMiddleButton(bool down)
	{
		INPUT[] a = one ??= new INPUT[1];
		a[0] = new INPUT
		{
			type = 0u
		};
		a[0].u.mi = new MOUSEINPUT
		{
			flags = (down ? 0x20u : 0x40u),
			extra = MAGIC
		};
		SendInput(1u, a, InputSize);
	}

	// Sends several key events in one call so nothing else can land between them (e.g. jump + release attack).
	public static void SendKeys(INPUT[] batch, int count)
	{
		if (count > 0)
		{
			SendInput((uint)count, batch, InputSize);
		}
	}

	// The same call for the macro thread, which says how many events Windows took and, when it refused some, why
	// (5 = access denied: the window in front runs at a higher integrity level). Not used on the hook path.
	[DllImport("user32", EntryPoint = "SendInput", SetLastError = true)]
	private static extern uint SendInputChecked(uint n, INPUT[] i, int size);

	public static int SendKeysChecked(INPUT[] batch, int count, out int error)
	{
		error = 0;
		if (count <= 0)
		{
			return 0;
		}
		int took = (int)SendInputChecked((uint)count, batch, InputSize);
		if (took < count)
		{
			error = Marshal.GetLastWin32Error();
		}
		return took;
	}

	[DllImport("kernel32")]
	private static extern nint GetCurrentProcess();

	[DllImport("kernel32")]
	private static extern nint GetCurrentThread();

	[DllImport("kernel32")]
	private static extern bool SetThreadPriority(nint h, int priority);

	[DllImport("kernel32")]
	private static extern bool SetProcessInformation(nint h, int cls, ref PowerThrottling info, int size);

	[DllImport("kernel32")]
	private static extern bool SetThreadInformation(nint h, int cls, ref PowerThrottling info, int size);

	private struct PowerThrottling
	{
		public uint Version;

		public uint ControlMask;

		public uint StateMask;
	}

	// The keyboard hook thread runs at time-critical priority so it answers instantly even when a game
	// saturates every core; it only wakes for a keystroke and returns in microseconds.
	public static void SetCurrentThreadTimeCritical()
	{
		SetThreadPriority(GetCurrentThread(), 15);
	}

	// Opt out of Windows power throttling (EcoQoS): never run LiteSynapse on efficiency cores or with
	// coalesced timers. Process-wide (execution speed + timer resolution) and for the calling thread.
	public static void OptOutOfPowerThrottling(bool process)
	{
		try
		{
			PowerThrottling t = new PowerThrottling
			{
				Version = 1u,
				ControlMask = 1u,
				StateMask = 0u
			};
			SetThreadInformation(GetCurrentThread(), 3, ref t, Marshal.SizeOf<PowerThrottling>());
			if (process)
			{
				PowerThrottling p = new PowerThrottling
				{
					Version = 1u,
					ControlMask = 5u,
					StateMask = 0u
				};
				SetProcessInformation(GetCurrentProcess(), 4, ref p, Marshal.SizeOf<PowerThrottling>());
			}
		}
		catch (EntryPointNotFoundException)
		{
		}
	}

	[DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateWaitableTimerExW(nint attr, string name, uint flags, uint access);

	[DllImport("kernel32")]
	private static extern bool SetWaitableTimer(nint h, ref long due, int period, nint completion, nint arg, bool resume);

	[DllImport("kernel32")]
	private static extern uint WaitForSingleObject(nint h, uint ms);

	// Millisecond-accurate waits without raising the system-wide timer resolution (Windows 10 1803+).
	public sealed class PreciseTimer : IDisposable
	{
		// The timer's handle as a WaitHandle, for waiting on it together with a cancel event.
		private sealed class TimerWaitHandle : WaitHandle
		{
			public TimerWaitHandle(nint h)
			{
				SafeWaitHandle = new Microsoft.Win32.SafeHandles.SafeWaitHandle(h, ownsHandle: false);
			}
		}

		private nint h = CreateWaitableTimerExW(IntPtr.Zero, null, 2u, 0x1F0003u);

		private WaitHandle[] pair;

		public bool HighResolution => h != IntPtr.Zero;

		public void Sleep(int ms)
		{
			if (ms <= 0)
			{
				return;
			}
			long due = -10000L * ms;
			if (h != IntPtr.Zero && SetWaitableTimer(h, ref due, 0, IntPtr.Zero, IntPtr.Zero, resume: false))
			{
				WaitForSingleObject(h, uint.MaxValue);
			}
			else
			{
				Thread.Sleep(ms);
			}
		}

		// Like Sleep, but returns false as soon as cancel is set (the timer keeps its precision otherwise).
		public bool Sleep(int ms, WaitHandle cancel)
		{
			if (ms <= 0)
			{
				return !cancel.WaitOne(0);
			}
			long due = -10000L * ms;
			if (h != IntPtr.Zero && SetWaitableTimer(h, ref due, 0, IntPtr.Zero, IntPtr.Zero, resume: false))
			{
				if (pair == null || pair[1] != cancel)
				{
					pair = new WaitHandle[2]
					{
						new TimerWaitHandle(h),
						cancel
					};
				}
				return WaitHandle.WaitAny(pair) == 0;
			}
			return !cancel.WaitOne(ms);
		}

		public void Dispose()
		{
			if (h != IntPtr.Zero)
			{
				CloseHandle(h);
				h = IntPtr.Zero;
			}
		}
	}

	[DllImport("advapi32", SetLastError = true)]
	private static extern bool OpenProcessToken(nint process, uint access, out nint token);

	[DllImport("advapi32", SetLastError = true)]
	private static extern bool GetTokenInformation(nint token, int cls, out int info, int length, out int returned);

	private const uint PROCESS_QUERY_INFORMATION = 0x0400;

	private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

	private const uint TOKEN_QUERY = 0x0008;

	private const int TokenElevation = 20;

	private const int ERROR_ACCESS_DENIED = 5;

	// Which window is in front and which process owns it, without opening a handle: two cheap calls, so a
	// repeated foreground event for the same window costs nothing more.
	public static void ForegroundWindow(out nint hwnd, out uint pid)
	{
		pid = 0u;
		hwnd = IntPtr.Zero;
		try
		{
			hwnd = GetForegroundWindow();
			if (hwnd != IntPtr.Zero)
			{
				GetWindowThreadProcessId(hwnd, out pid);
			}
		}
		catch
		{
			hwnd = IntPtr.Zero;
			pid = 0u;
		}
	}

	// Full path of the program that owns the foreground window, or null (no window, or the process cannot be
	// queried). pid receives its process id (0 when unknown).
	public static string ForegroundExePath(out uint pid)
	{
		pid = 0u;
		try
		{
			nint w = GetForegroundWindow();
			if (w == IntPtr.Zero)
			{
				return null;
			}
			GetWindowThreadProcessId(w, out pid);
			nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, inh: false, pid);
			if (h == IntPtr.Zero)
			{
				return null;
			}
			try
			{
				StringBuilder sb = new StringBuilder(1024);
				uint n = 1024u;
				return QueryFullProcessImageName(h, 0u, sb, ref n) ? sb.ToString() : null;
			}
			finally
			{
				CloseHandle(h);
			}
		}
		catch
		{
			return null;
		}
	}

	public static string ForegroundExePath()
	{
		return ForegroundExePath(out _);
	}

	// True when this process runs elevated (as administrator).
	public static bool IsCurrentProcessElevated()
	{
		return TokenElevated(GetCurrentProcess()) == true;
	}

	// True when the process runs at a higher integrity level than a normal program (elevated), so input sent with
	// SendInput by a non-elevated program does not reach it (UIPI). null = could not tell.
	public static bool? IsProcessElevated(uint pid)
	{
		if (pid == 0)
		{
			return null;
		}
		nint h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, inh: false, pid);
		if (h == IntPtr.Zero)
		{
			return null;
		}
		try
		{
			bool? elevated = TokenElevated(h);
			if (elevated.HasValue)
			{
				return elevated;
			}
		}
		finally
		{
			CloseHandle(h);
		}
		// The token could not be read. Windows refuses PROCESS_QUERY_INFORMATION (but not the limited right) to a
		// process of higher integrity, which is exactly the case where our input would be blocked.
		nint full = OpenProcess(PROCESS_QUERY_INFORMATION, inh: false, pid);
		if (full != IntPtr.Zero)
		{
			CloseHandle(full);
			return false;
		}
		return Marshal.GetLastWin32Error() == ERROR_ACCESS_DENIED ? true : null;
	}

	private static bool? TokenElevated(nint process)
	{
		if (!OpenProcessToken(process, TOKEN_QUERY, out var token))
		{
			return null;
		}
		try
		{
			if (!GetTokenInformation(token, TokenElevation, out var elevated, 4, out _))
			{
				return null;
			}
			return elevated != 0;
		}
		finally
		{
			CloseHandle(token);
		}
	}

	public static string ForegroundExe()
	{
		try
		{
			nint w = GetForegroundWindow();
			if (w == IntPtr.Zero)
			{
				return null;
			}
			GetWindowThreadProcessId(w, out var pid);
			nint h = OpenProcess(4096u, inh: false, pid);
			if (h == IntPtr.Zero)
			{
				return null;
			}
			try
			{
				StringBuilder sb = new StringBuilder(1024);
				uint n = 1024u;
				return QueryFullProcessImageName(h, 0u, sb, ref n) ? Path.GetFileName(sb.ToString()) : null;
			}
			finally
			{
				CloseHandle(h);
			}
		}
		catch
		{
			return null;
		}
	}
}
