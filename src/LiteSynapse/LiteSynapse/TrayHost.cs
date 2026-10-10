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

	private const uint WM_WTSSESSION_CHANGE = 0x02B1u;

	private const int PBT_APMSUSPEND = 4;

	private const int PBT_APMRESUMESUSPEND = 7;

	private const int PBT_APMRESUMEAUTOMATIC = 18;

	private const int WTS_SESSION_LOCK = 7;

	private const int WTS_SESSION_UNLOCK = 8;

	private const int NOTIFY_FOR_THIS_SESSION = 0;

	// Tray menu commands.
	private const int MenuOpen = 1;

	private const int MenuSettings = 2;

	private const int MenuStartup = 3;

	private const int MenuExit = 4;

	private const int MenuPerf = 5;

	private static readonly nint SettleTimer = 1;

	// Retries the session (lock/unlock) registration, which fails while the session service is not up yet.
	private static readonly nint SessionTimer = 2;

	private readonly Service svc;

	private nint hwnd;

	// WM_APP + 3: another thread (the window's Exit) asks the tray loop to end; Program then shuts the service down,
	// which puts the mouse back on its own slot.
	private const uint MsgExit = 32771u;

	private static nint current;

	// Ends the app as the tray menu's Exit does. False when no tray loop is running.
	// Second start of LiteSynapse.exe: asks the running instance to show its window (it may be hidden in the tray).
	public static void ShowRunningInstance()
	{
		uint msg = W32.RegisterWindowMessage(ShowWindowMessage);
		if (msg != 0)
		{
			// The running instance may bring its window to the front (this process got the user's click).
			AllowSetForegroundWindow(-1);
			W32.PostMessage(new IntPtr(0xFFFF), msg, IntPtr.Zero, IntPtr.Zero);
		}
	}

	public static bool RequestExit()
	{
		nint h = current;
		return h != IntPtr.Zero && W32.PostMessage(h, MsgExit, IntPtr.Zero, IntPtr.Zero);
	}

	private nint icon;

	private nint devNotify;

	private nint fgHook;

	private bool sessionRegistered;

	private int sessionAttempts;

	private uint taskbarCreated;

	// Posted (broadcast) by a second start of LiteSynapse.exe: show this instance's window instead.
	public const string ShowWindowMessage = "LiteSynapse.ShowWindow";

	private uint showWindowMsg;

	private const int WM_CLOSE = 16;

	private const uint MSGFLT_ALLOW = 1u;

	[DllImport("user32", SetLastError = true)]
	private static extern bool ChangeWindowMessageFilterEx(nint hwnd, uint msg, uint action, nint changeInfo);

	[DllImport("user32")]
	private static extern bool AllowSetForegroundWindow(int processId);

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

	[DllImport("wtsapi32.dll", SetLastError = true)]
	private static extern bool WTSRegisterSessionNotification(nint hwnd, int flags);

	[DllImport("wtsapi32.dll")]
	private static extern bool WTSUnRegisterSessionNotification(nint hwnd);

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
		current = hwnd;
		taskbarCreated = W32.RegisterWindowMessage("TaskbarCreated");
		showWindowMsg = W32.RegisterWindowMessage(ShowWindowMessage);
		// Run as administrator, Windows blocks these from Explorer and from a normal second start unless allowed.
		ChangeWindowMessageFilterEx(hwnd, taskbarCreated, MSGFLT_ALLOW, IntPtr.Zero);
		ChangeWindowMessageFilterEx(hwnd, showWindowMsg, MSGFLT_ALLOW, IntPtr.Zero);
		nint[] small = new nint[1];
		ExtractIconEx(Environment.ProcessPath, 0, null, small, 1u);
		icon = small[0];
		AddIcon();
		devNotify = DeviceEvents.Register(hwnd);
		RegisterSession();
		fgProc = (nint _, uint _, nint _, int _, int _, uint _, uint _) =>
		{
			svc.Check();
		};
		fgHook = Native.SetWinEventHook(3u, 3u, IntPtr.Zero, fgProc, 0u, 0u, 0u);
		svc.Changed += OnServiceChanged;
		svc.Start();
		W32.RunLoop();
		current = IntPtr.Zero;
		svc.Changed -= OnServiceChanged;
		if (fgHook != IntPtr.Zero)
		{
			Native.UnhookWinEvent(fgHook);
		}
		DeviceEvents.Unregister(devNotify);
		if (sessionRegistered)
		{
			WTSUnRegisterSessionNotification(hwnd);
		}
		RemoveIcon();
		if (icon != IntPtr.Zero)
		{
			DestroyIcon(icon);
		}
		W32.DestroyWindow(hwnd);
	}

	// Lock/unlock notifications (WM_WTSSESSION_CHANGE) for the service.
	private void RegisterSession()
	{
		sessionAttempts++;
		if (WTSRegisterSessionNotification(hwnd, NOTIFY_FOR_THIS_SESSION))
		{
			sessionRegistered = true;
			return;
		}
		int err = Marshal.GetLastWin32Error();
		if (sessionAttempts <= 10)
		{
			W32.SetTimer(hwnd, SessionTimer, 15000u, IntPtr.Zero);
		}
		else
		{
			Cfg.Log($"session lock notifications unavailable (error {err})");
		}
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
		case MsgExit:
			Cfg.Log("exit requested from the window");
			W32.PostQuitMessage(0);
			return IntPtr.Zero;
		default:
			if (m == taskbarCreated && m != 0)
			{
				AddIcon();
				return IntPtr.Zero;
			}
			if (m == showWindowMsg && m != 0)
			{
				Cfg.Log("LiteSynapse was started again: showing the window");
				UiHost.ShowWindow(svc);
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
				OnPower((int)w);
				return null;
			case WM_WTSSESSION_CHANGE:
				OnSession((int)w);
				return null;
			case 275u:
				if (w == SettleTimer)
				{
					W32.KillTimer(hwnd, SettleTimer);
					svc.EnsureDevices();
					return IntPtr.Zero;
				}
				if (w == SessionTimer)
				{
					W32.KillTimer(hwnd, SessionTimer);
					if (!sessionRegistered)
					{
						RegisterSession();
					}
					return IntPtr.Zero;
				}
				break;
			}
			if (m == WM_ENDSESSION && w != IntPtr.Zero)
			{
				// Sign-out, or Restart Manager / Setup closing apps (the session goes on): leave the message loop so
				// the tray icon goes and the process really ends.
				svc.Shutdown();
				W32.PostQuitMessage(0);
				return IntPtr.Zero;
			}
			if (m == WM_CLOSE)
			{
				// taskkill without /F (Setup's second step): the same as Exit.
				Cfg.Log("close requested");
				W32.PostQuitMessage(0);
				return IntPtr.Zero;
			}
			return null;
		}
	}

	// WM_POWERBROADCAST: sleep and wake. Both resume events arrive on a wake; the service is told once.
	private void OnPower(int evt)
	{
		switch (evt)
		{
		case PBT_APMSUSPEND:
			Safe("suspend", svc.OnSuspend);
			break;
		case PBT_APMRESUMEAUTOMATIC:
			Safe("resume", svc.OnResume);
			Settle();
			break;
		case PBT_APMRESUMESUSPEND:
			Settle();
			break;
		}
	}

	// WM_WTSSESSION_CHANGE: the workstation was locked or unlocked.
	private void OnSession(int evt)
	{
		switch (evt)
		{
		case WTS_SESSION_LOCK:
			Safe("session lock", svc.OnSessionLock);
			break;
		case WTS_SESSION_UNLOCK:
			Safe("session unlock", svc.OnSessionUnlock);
			break;
		}
	}

	private static void Safe(string what, Action action)
	{
		try
		{
			action();
		}
		catch (Exception ex)
		{
			Cfg.Log(what + ": " + ex.Message);
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

	private string shownTip;

	private void ModifyTip()
	{
		// Changed fires for everything the window shows (a lighting slider, a DPI change); the tooltip only has
		// the profile name in it, so Windows is left alone unless that text really changed.
		string tip = svc.TrayText ?? "";
		if (tip == shownTip)
		{
			return;
		}
		shownTip = tip;
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
		AppendMenu(menu, 0u, MenuOpen, "LiteSynapse");
		AppendMenu(menu, 2048u, UIntPtr.Zero, null);
		AppendMenu(menu, 0u, MenuSettings, "Settings");
		AppendMenu(menu, 0u, MenuPerf, "Performance");
		AppendMenu(menu, (uint)(0 | (startupOn ? 8 : 0)), MenuStartup, "Start with Windows");
		AppendMenu(menu, 2048u, UIntPtr.Zero, null);
		AppendMenu(menu, 0u, MenuExit, "Exit");
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
		case MenuOpen:
			UiHost.ShowWindow(svc);
			break;
		case MenuSettings:
			UiHost.ShowWindow(svc, 5);
			break;
		case MenuPerf:
			UiHost.ShowWindow(svc, MainForm.PagePerf);
			break;
		case MenuStartup:
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
		case MenuExit:
			W32.PostQuitMessage(0);
			break;
		}
	}
}
