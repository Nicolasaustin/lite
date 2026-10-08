using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace LiteSynapse;

// Windows calls behind the HTML window's commands.
internal static class WebNative
{
	private const uint GW_OWNER = 4u;

	private const int DWMWA_CLOAKED = 14;

	private delegate bool EnumWindowsProc(nint hwnd, nint lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool EnumWindows(EnumWindowsProc callback, nint lParam);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool IsWindowVisible(nint hwnd);

	[DllImport("user32.dll")]
	private static extern nint GetWindow(nint hwnd, uint cmd);

	[DllImport("user32.dll", EntryPoint = "GetWindowTextLengthW")]
	private static extern int GetWindowTextLength(nint hwnd);

	[DllImport("user32.dll")]
	private static extern uint GetWindowThreadProcessId(nint hwnd, out uint pid);

	[DllImport("dwmapi.dll")]
	private static extern int DwmGetWindowAttribute(nint hwnd, int attribute, out int value, int size);

	// Ids of the processes that own a visible, titled top-level window (what the taskbar and Alt+Tab list).
	// One pass over the windows instead of one per process (Process.MainWindowHandle).
	public static HashSet<int> ProcessesWithWindows()
	{
		HashSet<int> pids = new HashSet<int>();
		EnumWindowsProc callback = (nint hwnd, nint _) =>
		{
			if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && GetWindowTextLength(hwnd) > 0 && !IsCloaked(hwnd))
			{
				GetWindowThreadProcessId(hwnd, out uint pid);
				if (pid != 0)
				{
					pids.Add((int)pid);
				}
			}
			return true;
		};
		EnumWindows(callback, IntPtr.Zero);
		GC.KeepAlive(callback);
		return pids;
	}

	// Hidden by the window manager although "visible" (suspended Store apps, windows on other virtual desktops).
	private static bool IsCloaked(nint hwnd)
	{
		try
		{
			return DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0;
		}
		catch
		{
			return false;
		}
	}
}
