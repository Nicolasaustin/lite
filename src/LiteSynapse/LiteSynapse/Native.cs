using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

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

	[DllImport("user32")]
	public static extern nint SetWindowsHookEx(int id, HookProc p, nint m, uint t);

	[DllImport("user32")]
	public static extern bool UnhookWindowsHookEx(nint h);

	[DllImport("user32")]
	public static extern nint CallNextHookEx(nint h, int n, nint w, nint l);

	[DllImport("kernel32")]
	public static extern nint GetModuleHandle(string n);

	[DllImport("user32")]
	public static extern uint MapVirtualKey(uint code, uint type);

	[DllImport("user32", SetLastError = true)]
	public static extern uint SendInput(uint n, INPUT[] i, int size);

	[DllImport("user32")]
	public static extern bool RegisterRawInputDevices(RID[] d, uint n, uint sz);

	[DllImport("user32")]
	public static extern uint GetRawInputData(nint h, uint cmd, byte[] d, ref uint sz, uint hs);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	public static extern uint GetRawInputDeviceInfo(nint dev, uint cmd, StringBuilder d, ref uint sz);

	[DllImport("user32")]
	public static extern nint GetForegroundWindow();

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

	public static void TrimMemory()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
	}

	[DllImport("user32")]
	public static extern uint GetWindowThreadProcessId(nint h, out uint pid);

	[DllImport("kernel32", SetLastError = true)]
	public static extern nint OpenProcess(uint acc, bool inh, uint pid);

	[DllImport("kernel32")]
	public static extern bool CloseHandle(nint h);

	[DllImport("kernel32", CharSet = CharSet.Unicode)]
	public static extern bool QueryFullProcessImageName(nint h, uint flags, StringBuilder b, ref uint size);

	public static void SendKey(int vk, bool ext, bool down)
	{
		uint sc = MapVirtualKey((uint)vk, 0u);
		uint flags = (uint)(8 | ((!down) ? 2 : 0)) | (ext ? 1u : 0u);
		INPUT i = new INPUT
		{
			type = 1u
		};
		i.u.ki = new KEYBDINPUT
		{
			vk = 0,
			sc = (ushort)sc,
			flags = flags,
			extra = MAGIC
		};
		SendInput(1u, new INPUT[1] { i }, Marshal.SizeOf<INPUT>());
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
