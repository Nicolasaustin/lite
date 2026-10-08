using System;
using System.Runtime.InteropServices;

namespace LiteSynapse;

internal sealed class TrayHost
{
	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct NOTIFYICONDATA
	{
		public int cbSize;

		public nint hWnd;

		public uint uID;

		public uint uFlags;

		public uint uCallbackMessage;

		public nint hIcon;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string szTip;

		public uint dwState;

		public uint dwStateMask;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
		public string szInfo;

		public uint uVersion;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string szInfoTitle;

		public uint dwInfoFlags;

		public Guid guidItem;

		public nint hBalloonIcon;
	}

	private const uint NIM_ADD = 0u;

	private const uint NIM_MODIFY = 1u;

	private const uint NIM_DELETE = 2u;

	private const uint NIF_MESSAGE = 1u;

	private const uint NIF_ICON = 2u;

	private const uint NIF_TIP = 4u;

	private const uint MF_STRING = 0u;

	private const uint MF_CHECKED = 8u;

	private const uint MF_SEPARATOR = 2048u;

	private const uint TPM_RIGHTBUTTON = 2u;

	private const uint TPM_RIGHTALIGN = 8u;

	private const uint TPM_BOTTOMALIGN = 32u;

	private const uint TPM_RETURNCMD = 256u;

	private const uint CALLBACK = 32769u;

	private const uint REFRESH_TIP = 32770u;

	private const int WM_ENDSESSION = 22;

	private const int PBT_APMRESUMESUSPEND = 7;

	private const int PBT_APMRESUMEAUTOMATIC = 18;

	private static readonly nint SettleTimer = 1;

	private readonly Service svc;

	private nint hwnd;

	private nint icon;

	private nint devNotify;

	private nint fgHook;

	private uint taskbarCreated;

	private W32.WndProc keepAlive;

	private Native.WinEventProc fgProc;

	[DllImport("shell32", CharSet = CharSet.Unicode)]
	private static extern bool Shell_NotifyIcon(uint msg, ref NOTIFYICONDATA d);

	[DllImport("shell32", CharSet = CharSet.Unicode)]
	private static extern uint ExtractIconEx(string file, int index, nint[] large, nint[] small, uint n);

	[DllImport("user32")]
	private static extern bool DestroyIcon(nint h);

	[DllImport("user32")]
	private static extern nint CreatePopupMenu();

	[DllImport("user32", CharSet = CharSet.Unicode)]
	private static extern bool AppendMenu(nint m, uint flags, nuint id, string text);

	[DllImport("user32")]
	private static extern int TrackPopupMenu(nint m, uint flags, int x, int y, int reserved, nint hwnd, nint rc);

	[DllImport("user32")]
	private static extern bool DestroyMenu(nint m);

	[DllImport("user32")]
	private static extern bool SetMenuDefaultItem(nint m, uint item, uint byPos);

	[DllImport("uxtheme.dll", EntryPoint = "#135")]
	private static extern int SetPreferredAppMode(int mode);

	[DllImport("uxtheme.dll", EntryPoint = "#136")]
	private static extern void FlushMenuThemes();

	public TrayHost(Service svc)
	{
		this.svc = svc;
	}

	public void Run()
	{
		PrepareDarkMenus();
		(nint, W32.WndProc) tuple = W32.CreateWindow(messageOnly: false, OnMessage);
		hwnd = tuple.Item1;
		keepAlive = tuple.Item2;
		taskbarCreated = W32.RegisterWindowMessage("TaskbarCreated");
		nint[] small = new nint[1];
		ExtractIconEx(Environment.ProcessPath, 0, null, small, 1u);
		icon = small[0];
		AddIcon();
		devNotify = DeviceEvents.Register(hwnd);
		fgProc = (nint _, uint _, nint _, int _, int _, uint _, uint _) =>
		{
			svc.Check();
		};
		fgHook = Native.SetWinEventHook(3u, 3u, IntPtr.Zero, fgProc, 0u, 0u, 0u);
		svc.Changed += OnServiceChanged;
		svc.Start();
		W32.RunLoop();
		svc.Changed -= OnServiceChanged;
		if (fgHook != IntPtr.Zero)
		{
			Native.UnhookWinEvent(fgHook);
		}
		DeviceEvents.Unregister(devNotify);
		RemoveIcon();
		if (icon != IntPtr.Zero)
		{
			DestroyIcon(icon);
		}
		W32.DestroyWindow(hwnd);
	}

	private void OnServiceChanged()
	{
		if (hwnd != IntPtr.Zero)
		{
			W32.PostMessage(hwnd, 32770u, IntPtr.Zero, IntPtr.Zero);
		}
	}

	private nint? OnMessage(uint m, nint w, nint l)
	{
		switch (m)
		{
		case 32769u:
			switch ((uint)((int)l & 0xFFFF))
			{
			case 514u:
			{
				W32.GetCursorPos(out var p);
				UiHost.ToggleFlyout(svc, p.x, p.y);
				break;
			}
			case 517u:
				ShowMenu();
				break;
			}
			return IntPtr.Zero;
		case 32770u:
			ModifyTip();
			return IntPtr.Zero;
		default:
			if (m == taskbarCreated && m != 0)
			{
				AddIcon();
				return IntPtr.Zero;
			}
			switch (m)
			{
			case 537u:
				if (DeviceEvents.IsRazerChange(w, l))
				{
					Settle();
				}
				return null;
			case 536u:
				if ((int)w == 7 || (int)w == 18)
				{
					Settle();
				}
				return null;
			case 275u:
				if (w == SettleTimer)
				{
					W32.KillTimer(hwnd, SettleTimer);
					svc.EnsureDevices();
					return IntPtr.Zero;
				}
				break;
			}
			if (m == 22 && w != IntPtr.Zero)
			{
				svc.Shutdown();
				return null;
			}
			return null;
		}
	}

	private void Settle()
	{
		W32.SetTimer(hwnd, SettleTimer, 1500u, IntPtr.Zero);
	}

	private NOTIFYICONDATA Data(uint flags)
	{
		return new NOTIFYICONDATA
		{
			cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
			hWnd = hwnd,
			uID = 1u,
			uFlags = flags,
			uCallbackMessage = 32769u,
			hIcon = icon,
			szTip = svc.TrayText,
			szInfo = "",
			szInfoTitle = ""
		};
	}

	private void AddIcon()
	{
		NOTIFYICONDATA d = Data(7u);
		Shell_NotifyIcon(0u, ref d);
	}

	private void ModifyTip()
	{
		NOTIFYICONDATA d = Data(4u);
		Shell_NotifyIcon(1u, ref d);
	}

	private void RemoveIcon()
	{
		NOTIFYICONDATA d = Data(0u);
		Shell_NotifyIcon(2u, ref d);
	}

	internal static void PrepareDarkMenus()
	{
		try
		{
			SetPreferredAppMode(2);
			FlushMenuThemes();
		}
		catch
		{
		}
	}

	internal static int TrackMenu(nint owner, bool startupOn, int x, int y)
	{
		nint menu = CreatePopupMenu();
		AppendMenu(menu, 0u, 1u, "LiteSynapse");
		AppendMenu(menu, 2048u, UIntPtr.Zero, null);
		AppendMenu(menu, 0u, 2u, "Settings");
		AppendMenu(menu, (uint)(0 | (startupOn ? 8 : 0)), 3u, "Start with Windows");
		AppendMenu(menu, 2048u, UIntPtr.Zero, null);
		AppendMenu(menu, 0u, 4u, "Exit");
		SetMenuDefaultItem(menu, 1u, 0u);
		W32.SetForegroundWindow(owner);
		int result = TrackPopupMenu(menu, 298u, x, y, 0, owner, IntPtr.Zero);
		W32.PostMessage(owner, 0u, IntPtr.Zero, IntPtr.Zero);
		DestroyMenu(menu);
		return result;
	}

	private void ShowMenu()
	{
		W32.GetCursorPos(out var p);
		switch (TrackMenu(hwnd, Service.IsStartup(), p.x, p.y))
		{
		case 1:
			UiHost.ShowWindow(svc);
			break;
		case 2:
			UiHost.ShowWindow(svc, 5);
			break;
		case 3:
			try
			{
				Service.SetStartup(!Service.IsStartup());
				break;
			}
			catch (Exception ex)
			{
				Cfg.Log("startup: " + ex.Message);
				break;
			}
		case 4:
			W32.PostQuitMessage(0);
			break;
		}
	}
}
