using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace LiteSynapse;

internal static class W32
{
	public delegate nint WndProc(nint h, uint msg, nint w, nint l);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct WNDCLASSEX
	{
		public uint cbSize;

		public uint style;

		public nint lpfnWndProc;

		public int cbClsExtra;

		public int cbWndExtra;

		public nint hInstance;

		public nint hIcon;

		public nint hCursor;

		public nint hbrBackground;

		public string lpszMenuName;

		public string lpszClassName;

		public nint hIconSm;
	}

	public struct MSG
	{
		public nint hwnd;

		public uint message;

		public nint wParam;

		public nint lParam;

		public uint time;

		public int x;

		public int y;
	}

	public struct POINT
	{
		public int x;

		public int y;
	}

	public const uint WM_NULL = 0u;

	public const uint WM_TIMER = 275u;

	public const uint WM_COMMAND = 273u;

	public const uint WM_INPUT = 255u;

	public const uint WM_DEVICECHANGE = 537u;

	public const uint WM_POWERBROADCAST = 536u;

	public const uint WM_LBUTTONUP = 514u;

	public const uint WM_RBUTTONUP = 517u;

	public const uint WM_APP = 32768u;

	private static int classCounter;

	[DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern ushort RegisterClassEx(ref WNDCLASSEX c);

	[DllImport("user32", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern nint CreateWindowEx(int ex, string cls, string name, int style, int x, int y, int w, int h, nint parent, nint menu, nint inst, nint param);

	[DllImport("user32")]
	public static extern nint DefWindowProc(nint h, uint m, nint w, nint l);

	[DllImport("user32")]
	public static extern bool DestroyWindow(nint h);

	[DllImport("user32")]
	public static extern int GetMessage(out MSG m, nint h, uint min, uint max);

	[DllImport("user32")]
	private static extern bool TranslateMessage(ref MSG m);

	[DllImport("user32")]
	private static extern nint DispatchMessage(ref MSG m);

	[DllImport("user32")]
	public static extern bool PostMessage(nint h, uint m, nint w, nint l);

	[DllImport("user32")]
	public static extern void PostQuitMessage(int code);

	[DllImport("user32")]
	public static extern nint SetTimer(nint h, nint id, uint ms, nint proc);

	[DllImport("user32")]
	public static extern bool KillTimer(nint h, nint id);

	[DllImport("user32", CharSet = CharSet.Unicode)]
	public static extern uint RegisterWindowMessage(string s);

	[DllImport("user32")]
	public static extern bool GetCursorPos(out POINT p);

	[DllImport("user32")]
	public static extern bool SetForegroundWindow(nint h);

	[DllImport("kernel32")]
	private static extern nint GetModuleHandle(string n);

	public static void RunLoop()
	{
		MSG m;
		while (GetMessage(out m, IntPtr.Zero, 0u, 0u) > 0)
		{
			TranslateMessage(ref m);
			DispatchMessage(ref m);
		}
	}

	public static (nint hwnd, WndProc keepAlive) CreateWindow(bool messageOnly, Func<uint, nint, nint, nint?> handler)
	{
		WndProc proc = (nint h, uint m, nint w, nint l) =>
		{
			try
			{
				nint? num2 = handler(m, w, l);
				if (num2.HasValue)
				{
					return num2.Value;
				}
			}
			catch (Exception ex)
			{
				Cfg.Log("window: " + ex.Message);
			}
			return DefWindowProc(h, m, w, l);
		};
		string cls = "LiteSynapseWnd" + Interlocked.Increment(ref classCounter);
		WNDCLASSEX wc = new WNDCLASSEX
		{
			cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
			lpfnWndProc = Marshal.GetFunctionPointerForDelegate(proc),
			hInstance = GetModuleHandle(null),
			lpszClassName = cls
		};
		if (RegisterClassEx(ref wc) == 0)
		{
			throw new InvalidOperationException("RegisterClassEx failed " + Marshal.GetLastWin32Error());
		}
		nint num = CreateWindowEx(0, cls, "LiteSynapse", 0, 0, 0, 0, 0, messageOnly ? new IntPtr(-3) : IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
		if (num == IntPtr.Zero)
		{
			throw new InvalidOperationException("CreateWindowEx failed " + Marshal.GetLastWin32Error());
		}
		return (hwnd: num, keepAlive: proc);
	}
}
