using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LiteSynapse;

// The main window: ui/index.html (an embedded resource) in WebView2, talking to the app through Bridge.
//
// Performance rules (gaming first):
//  - WebView2 exists only while this window is open. Closing it disposes the control, and the msedgewebview2
//    processes exit with it. It is never created in --tray mode or at startup.
//  - Minimized: the WebView is hidden and suspended, and no state is pushed until it is shown again.
//  - When a linked game comes to the front, the window closes (UiPrefs.CloseOnGame, on by default).
// Everything here runs on UiHost's UI thread; other threads only call RequestStatePush and SendFromAnyThread.
internal sealed class WebWindow : Form
{
	public static readonly Color Background = Color.FromArgb(0x0D, 0x0E, 0x11);

	private const string PageResource = "LiteSynapse.ui.index.html";

	// Used only when the page is too large for NavigateToString (2 MB): it is then served from this address.
	private const string PageHost = "https://litesynapse.invalid/";

	private const int NavigateToStringLimit = 2_000_000;

	private const string BackgroundVariable = "WEBVIEW2_DEFAULT_BACKGROUND_COLOR";

	private const uint EVENT_SYSTEM_FOREGROUND = 3u;

	private const uint WINEVENT_SKIPOWNPROCESS = 2u;

	private const int StatePushDelayMs = 100;

	private readonly Service svc;

	private readonly UiPrefs prefs;

	private readonly Bridge bridge;

	private readonly WebView2 web;

	private readonly System.Windows.Forms.Timer pushTimer;

	private readonly Action serviceChanged;

	private readonly List<string> held = new List<string>();

	private readonly float scale;

	private CoreWebView2Environment env;

	private CoreWebView2 core;

	private string page;

	// The page has loaded, so its message listener is there.
	private bool pageReady;

	// Navigate() started loading the page: the next NavigationStarting is ours, whatever URI it reports.
	private bool loadingOwnPage;

	// Minimized: the WebView is hidden and suspended; messages wait in 'held'.
	private volatile bool hidden;

	// A state push was skipped (hidden or not loaded yet); one is sent when that ends.
	private volatile bool stateDirty;

	private int pushQueued;

	private string pendingNavigate;

	private nint foregroundHook;

	private Native.WinEventProc foregroundProc;

	private int modalDepth;

	private int renderFailures;

	private bool loggedUnresponsive;

	private bool closing;

	private FormWindowState shownState = FormWindowState.Normal;

	// WebView2 could not start (runtime missing, ...). The window closes itself after raising it.
	public event Action<Exception> InitFailed;

	public WebWindow(Service svc)
	{
		this.svc = svc;
		prefs = UiPrefs.Load();
		bridge = new Bridge(svc, prefs, this);
		serviceChanged = RequestStatePush;
		scale = Math.Max(1f, DeviceDpi / 96f);
		Text = "LiteSynapse";
		Icon = AppIcon.Icon();
		BackColor = Background;
		ForeColor = Color.White;
		StartPosition = FormStartPosition.CenterScreen;
		MinimumSize = Scaled(UiPrefs.MinWidth, UiPrefs.MinHeight);
		Size = FitToScreen(Scaled(prefs.Width, prefs.Height));
		if (prefs.Maximized)
		{
			WindowState = FormWindowState.Maximized;
			shownState = FormWindowState.Maximized;
		}
		web = new WebView2
		{
			Dock = DockStyle.Fill,
			BackColor = Background,
			DefaultBackgroundColor = Background,
			AllowExternalDrop = false
		};
		Controls.Add(web);
		pushTimer = new System.Windows.Forms.Timer
		{
			Interval = StatePushDelayMs
		};
		pushTimer.Tick += OnPushTimer;
	}

	private Size Scaled(int width, int height)
	{
		return new Size((int)Math.Round(width * scale), (int)Math.Round(height * scale));
	}

	private Size FitToScreen(Size size)
	{
		Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
		return new Size(Math.Min(size.Width, area.Width), Math.Min(size.Height, area.Height));
	}

	protected override void OnHandleCreated(EventArgs e)
	{
		base.OnHandleCreated(e);
		Ui.DarkTitleBar(this);
	}

	protected override async void OnShown(EventArgs e)
	{
		base.OnShown(e);
		svc.Changed += serviceChanged;
		HookForeground();
		await InitWebViewAsync();
	}

	// ---- WebView2 setup ------------------------------------------------------------------------------------

	private async Task InitWebViewAsync()
	{
		try
		{
			page = LoadPage();
			// Paints the dark background from the first frame (setting the property alone can flash white).
			// Read when the control is created; removed again below so programs started later do not inherit it.
			Environment.SetEnvironmentVariable(BackgroundVariable, "FF0D0E11");
			string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LiteSynapse", "WebView2");
			CoreWebView2EnvironmentOptions options = new CoreWebView2EnvironmentOptions
			{
				// The page is local: no tracking prevention lists to load.
				EnableTrackingPrevention = false
			};
			env = await CoreWebView2Environment.CreateAsync(null, dataDir, options);
			if (closing)
			{
				return;
			}
			await web.EnsureCoreWebView2Async(env);
			Environment.SetEnvironmentVariable(BackgroundVariable, null);
			if (closing)
			{
				return;
			}
			core = web.CoreWebView2;
			Configure(core);
			Navigate();
			if (WindowState == FormWindowState.Minimized)
			{
				// Minimized while WebView2 was starting.
				HideWebView();
			}
		}
		catch (Exception ex)
		{
			Environment.SetEnvironmentVariable(BackgroundVariable, null);
			if (closing)
			{
				return;
			}
			Cfg.Log("window: WebView2 could not start, using the classic window: " + ex.GetType().Name + ": " + ex.Message);
			try
			{
				InitFailed?.Invoke(ex);
			}
			finally
			{
				Close();
			}
		}
	}

	private static string LoadPage()
	{
		using Stream s = typeof(WebWindow).Assembly.GetManifestResourceStream(PageResource)
			?? throw new InvalidOperationException("the window page (" + PageResource + ") is not built into this exe");
		using StreamReader r = new StreamReader(s, Encoding.UTF8);
		return r.ReadToEnd();
	}

	private void Configure(CoreWebView2 c)
	{
		bool devTools = Environment.GetEnvironmentVariable("LITESYNAPSE_DEVTOOLS") == "1";
		CoreWebView2Settings s = c.Settings;
		s.AreDevToolsEnabled = devTools;
		s.AreDefaultContextMenusEnabled = devTools;
		s.AreBrowserAcceleratorKeysEnabled = devTools;
		s.IsZoomControlEnabled = false;
		s.IsStatusBarEnabled = false;
		s.AreHostObjectsAllowed = false;
		s.IsWebMessageEnabled = true;
		// Settings newer runtimes have; an older runtime throws for them, which is harmless.
		Try(() => s.IsPinchZoomEnabled = false);
		Try(() => s.IsSwipeNavigationEnabled = false);
		Try(() => s.IsPasswordAutosaveEnabled = false);
		Try(() => s.IsGeneralAutofillEnabled = false);
		Try(() => s.IsReputationCheckingRequired = false);
		Try(() => c.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Dark);
		Try(() => c.Profile.IsPasswordAutosaveEnabled = false);
		Try(() => c.Profile.IsGeneralAutofillEnabled = false);
		Try(() => c.DownloadStarting += (_, e) => e.Cancel = true);
		c.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
		c.WebMessageReceived += OnWebMessage;
		c.NavigationStarting += OnNavigationStarting;
		c.NavigationCompleted += OnNavigationCompleted;
		c.NewWindowRequested += OnNewWindowRequested;
		c.ProcessFailed += OnProcessFailed;
	}

	private static void Try(Action set)
	{
		try
		{
			set();
		}
		catch
		{
		}
	}

	private void Navigate()
	{
		pageReady = false;
		loadingOwnPage = true;
		if (Encoding.UTF8.GetByteCount(page) <= NavigateToStringLimit)
		{
			core.NavigateToString(page);
			return;
		}
		core.AddWebResourceRequestedFilter(PageHost + "*", CoreWebView2WebResourceContext.All);
		core.WebResourceRequested -= ServePage;
		core.WebResourceRequested += ServePage;
		core.Navigate(PageHost + "index.html");
	}

	private void ServePage(object sender, CoreWebView2WebResourceRequestedEventArgs e)
	{
		if (string.Equals(e.Request.Uri, PageHost + "index.html", StringComparison.OrdinalIgnoreCase))
		{
			e.Response = env.CreateWebResourceResponse(new MemoryStream(Encoding.UTF8.GetBytes(page)), 200, "OK", "Content-Type: text/html; charset=utf-8");
		}
		else
		{
			e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
		}
	}

	private static bool IsOwnPage(string uri)
	{
		return uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith("about:", StringComparison.OrdinalIgnoreCase) || uri.StartsWith(PageHost, StringComparison.OrdinalIgnoreCase);
	}

	// The window only ever shows its own page; web links open in the default browser.
	private void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
	{
		string uri = e.Uri ?? "";
		if (loadingOwnPage)
		{
			loadingOwnPage = false;
			return;
		}
		if (IsOwnPage(uri))
		{
			return;
		}
		e.Cancel = true;
		OpenInBrowser(uri);
	}

	private void OnNewWindowRequested(object sender, CoreWebView2NewWindowRequestedEventArgs e)
	{
		e.Handled = true;
		OpenInBrowser(e.Uri);
	}

	private static void OpenInBrowser(string uri)
	{
		if (Uri.TryCreate(uri, UriKind.Absolute, out Uri u) && (u.Scheme == Uri.UriSchemeHttps || u.Scheme == Uri.UriSchemeHttp))
		{
			try
			{
				Process.Start(new ProcessStartInfo(u.AbsoluteUri)
				{
					UseShellExecute = true
				})?.Dispose();
			}
			catch (Exception ex)
			{
				Cfg.Log("window: could not open " + u.AbsoluteUri + ": " + ex.Message);
			}
		}
	}

	private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
	{
		if (!e.IsSuccess)
		{
			Cfg.Log("window: the page did not load: " + e.WebErrorStatus);
			return;
		}
		PageReady();
	}

	private void PageReady()
	{
		if (pageReady)
		{
			return;
		}
		pageReady = true;
		if (pendingNavigate != null)
		{
			string json = pendingNavigate;
			pendingNavigate = null;
			Send(json);
		}
		if (stateDirty)
		{
			PushState();
		}
	}

	private void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
	{
		string json;
		try
		{
			json = e.WebMessageAsJson;
		}
		catch (Exception ex)
		{
			Cfg.Log("window: unreadable message: " + ex.Message);
			return;
		}
		PageReady();
		// Not inside the WebView2 event: commands may open dialogs (a nested message loop).
		Post(() => bridge.Handle(json));
	}

	private void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
	{
		CoreWebView2ProcessFailedKind kind = e.ProcessFailedKind;
		if (kind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
		{
			// Raised every few seconds while it lasts: log it once.
			if (!loggedUnresponsive)
			{
				loggedUnresponsive = true;
				Cfg.Log("window: the page stopped responding");
			}
			return;
		}
		Cfg.Log("window: WebView2 " + kind + " (exit code " + SafeExitCode(e) + ")");
		switch (kind)
		{
		case CoreWebView2ProcessFailedKind.BrowserProcessExited:
			// The control cannot be used any more; the window opens fresh next time.
			Post(Close);
			break;
		case CoreWebView2ProcessFailedKind.RenderProcessExited:
		case CoreWebView2ProcessFailedKind.FrameRenderProcessExited:
			if (++renderFailures <= 3)
			{
				Post(() =>
				{
					if (core != null && !closing)
					{
						Navigate();
					}
				});
			}
			else
			{
				Post(Close);
			}
			break;
		}
	}

	private static string SafeExitCode(CoreWebView2ProcessFailedEventArgs e)
	{
		try
		{
			return e.ExitCode.ToString();
		}
		catch
		{
			return "?";
		}
	}

	// ---- Messages to the page --------------------------------------------------------------------------------

	// UI thread. Dropped when the page is gone; held while the window is minimized.
	public void Send(string json)
	{
		if (core == null || closing)
		{
			return;
		}
		if (hidden)
		{
			held.Add(json);
			return;
		}
		try
		{
			core.PostWebMessageAsJson(json);
		}
		catch (Exception ex)
		{
			Cfg.Log("window: message not delivered: " + ex.Message);
		}
	}

	public void SendFromAnyThread(string json)
	{
		Post(() => Send(json));
	}

	// Any thread. Pushes the state at most every StatePushDelayMs; nothing is built while the window is hidden.
	public void RequestStatePush()
	{
		if (hidden || closing)
		{
			stateDirty = true;
			return;
		}
		if (Interlocked.Exchange(ref pushQueued, 1) == 0)
		{
			Post(() =>
			{
				if (!closing)
				{
					pushTimer.Start();
				}
			});
		}
	}

	private void OnPushTimer(object sender, EventArgs e)
	{
		pushTimer.Stop();
		Volatile.Write(ref pushQueued, 0);
		PushState();
	}

	private void PushState()
	{
		if (core == null || closing || hidden || !pageReady)
		{
			stateDirty = true;
			return;
		}
		stateDirty = false;
		try
		{
			Send(WebProtocol.State(bridge.State()));
		}
		catch (Exception ex)
		{
			Cfg.Log("window: state push failed: " + ex.Message);
		}
	}

	// Shows the page the tray, a command line or the classic window asked for, once it has loaded.
	public void NavigateTo(string pageName, string tab)
	{
		if (pageName == null)
		{
			return;
		}
		string json = WebProtocol.Navigate(pageName, tab);
		if (core != null && pageReady)
		{
			Send(json);
		}
		else
		{
			pendingNavigate = json;
		}
	}

	private void Post(Action action)
	{
		if (closing || IsDisposed || !IsHandleCreated)
		{
			return;
		}
		try
		{
			BeginInvoke(action);
		}
		catch (InvalidOperationException)
		{
			// The window is closing.
		}
	}

	// Native dialogs (file pickers) owned by this window. While one is open the window does not close for a game.
	public DialogResult ShowModal(CommonDialog dialog)
	{
		modalDepth++;
		try
		{
			return dialog.ShowDialog(this);
		}
		finally
		{
			modalDepth--;
		}
	}

	// ---- Window state ----------------------------------------------------------------------------------------

	public void ShowAndActivate()
	{
		if (!Visible)
		{
			Show();
		}
		if (WindowState == FormWindowState.Minimized)
		{
			WindowState = shownState;
		}
		Activate();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		if (WindowState == FormWindowState.Minimized)
		{
			HideWebView();
		}
		else
		{
			shownState = WindowState;
			if (hidden)
			{
				ShowWebView();
			}
		}
	}

	// Minimized: hide and suspend the page (timers and animations stop, the renderer's memory can be reused).
	private async void HideWebView()
	{
		if (hidden || core == null || closing)
		{
			return;
		}
		hidden = true;
		web.Visible = false;
		try
		{
			await core.TrySuspendAsync();
			if (!hidden && core != null)
			{
				// Shown again while the suspend was on its way.
				core.Resume();
			}
		}
		catch (Exception ex)
		{
			Cfg.Log("window: suspend: " + ex.Message);
		}
	}

	private void ShowWebView()
	{
		hidden = false;
		if (core == null || closing)
		{
			return;
		}
		web.Visible = true;
		try
		{
			core.Resume();
		}
		catch (Exception ex)
		{
			Cfg.Log("window: resume: " + ex.Message);
		}
		List<string> waiting = new List<string>(held);
		held.Clear();
		foreach (string json in waiting)
		{
			Send(json);
		}
		if (stateDirty)
		{
			PushState();
		}
	}

	// ---- Close when a game comes to the front ----------------------------------------------------------------

	private void HookForeground()
	{
		foregroundProc = OnForegroundChanged;
		foregroundHook = Native.SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, foregroundProc, 0u, 0u, WINEVENT_SKIPOWNPROCESS);
	}

	private void UnhookForeground()
	{
		if (foregroundHook != IntPtr.Zero)
		{
			Native.UnhookWinEvent(foregroundHook);
			foregroundHook = IntPtr.Zero;
		}
	}

	// Another program came to the front (this process's windows are skipped by the hook). If it is a linked
	// game, the window closes so nothing renders while playing.
	private void OnForegroundChanged(nint hook, uint evt, nint hwnd, int idObject, int idChild, uint thread, uint time)
	{
		try
		{
			if (closing || !prefs.CloseOnGame || modalDepth > 0)
			{
				return;
			}
			string path = Native.ForegroundExePath(out uint pid);
			if (path == null || pid == (uint)Environment.ProcessId)
			{
				return;
			}
			string exe = Path.GetFileName(path);
			GameRule rule = GameRule.Find(svc.Games, exe, path);
			if (rule == null)
			{
				return;
			}
			Cfg.Log($"window closed: {exe} (\"{rule.Profile}\") came to the front");
			Post(Close);
		}
		catch (Exception ex)
		{
			Cfg.Log("window: foreground check: " + ex.Message);
		}
	}

	// ---- Closing ---------------------------------------------------------------------------------------------

	protected override void OnFormClosing(FormClosingEventArgs e)
	{
		base.OnFormClosing(e);
		if (e.Cancel)
		{
			return;
		}
		Rectangle bounds = (WindowState == FormWindowState.Normal) ? Bounds : RestoreBounds;
		if (bounds.Width > 0 && bounds.Height > 0)
		{
			prefs.Width = (int)Math.Round(bounds.Width / scale);
			prefs.Height = (int)Math.Round(bounds.Height / scale);
		}
		prefs.Maximized = WindowState == FormWindowState.Maximized || (WindowState == FormWindowState.Minimized && shownState == FormWindowState.Maximized);
		prefs.Normalize();
		prefs.Save();
	}

	// Disposes WebView2 now, so its processes exit as soon as the window is gone.
	protected override void OnFormClosed(FormClosedEventArgs e)
	{
		closing = true;
		svc.Changed -= serviceChanged;
		UnhookForeground();
		pushTimer.Stop();
		held.Clear();
		CoreWebView2 c = core;
		core = null;
		if (c != null)
		{
			try
			{
				c.WebMessageReceived -= OnWebMessage;
				c.NavigationStarting -= OnNavigationStarting;
				c.NavigationCompleted -= OnNavigationCompleted;
				c.NewWindowRequested -= OnNewWindowRequested;
				c.ProcessFailed -= OnProcessFailed;
				c.WebResourceRequested -= ServePage;
			}
			catch
			{
			}
		}
		try
		{
			web.Dispose();
		}
		catch (Exception ex)
		{
			Cfg.Log("window: closing WebView2: " + ex.Message);
		}
		env = null;
		base.OnFormClosed(e);
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			pushTimer.Dispose();
		}
		base.Dispose(disposing);
	}
}
