using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace LiteSynapse;

// Runs every window on one STA thread that exists only while a window is open (the tray itself has no UI
// thread). Post() starts the thread on demand; it ends when the last window closes.
internal static class UiHost
{
	private static readonly object gate = new object();

	private static readonly ManualResetEventSlim ready = new ManualResetEventSlim();

	private static bool alive;

	private static bool inited;

	private static int pending;

	private static Control invoker;

	private static WebWindow web;

	private static MainForm main;

	private static TrayFlyout flyout;

	private static Form calibration;

	private static long flyoutClosedAt;

	// null = not checked yet; false = the WebView2 runtime is missing or failed this session (classic window).
	private static bool? webAvailable;

	private static void Post(Action action)
	{
		lock (gate)
		{
			pending++;
			if (!alive)
			{
				alive = true;
				ready.Reset();
				Thread thread = new Thread(Loop)
				{
					IsBackground = true,
					Name = "LiteSynapse window"
				};
				thread.SetApartmentState(ApartmentState.STA);
				thread.Start();
			}
		}
		ready.Wait();
		Control inv;
		lock (gate)
		{
			inv = invoker;
		}
		inv.BeginInvoke((Action)(() =>
		{
			try
			{
				action();
			}
			catch (Exception ex)
			{
				Cfg.Log("ui: " + ex);
			}
			finally
			{
				Interlocked.Decrement(ref pending);
			}
		}));
	}

	private static void Loop()
	{
		if (!inited)
		{
			ApplicationConfiguration.Initialize();
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += (object _, ThreadExceptionEventArgs e) =>
			{
				Cfg.Log("UI error: " + e.Exception);
			};
			inited = true;
		}
		Control inv = new Control();
		inv.CreateControl();
		_ = inv.Handle;
		lock (gate)
		{
			invoker = inv;
		}
		ready.Set();
		Application.Run();
		lock (gate)
		{
			if (invoker == inv)
			{
				invoker = null;
			}
		}
		inv.Dispose();
		// No forced garbage collection here: it blocked for a moment right as the user clicked into a game.
	}

	private static void AfterClose()
	{
		bool exit = false;
		lock (gate)
		{
			if (Application.OpenForms.Count == 0 && pending == 0)
			{
				alive = false;
				exit = true;
			}
		}
		if (exit)
		{
			Application.ExitThread();
		}
	}

	private static void BeginAfterClose()
	{
		if (invoker != null && !invoker.IsDisposed)
		{
			invoker.BeginInvoke((Action)AfterClose);
		}
	}

	// Opens (or brings back) the main window: the HTML window, or the classic one when WebView2 cannot run.
	// page/tab/mem are the classic window's page numbers (see PageName); the HTML window gets them as a
	// { type: "navigate" } message.
	public static void ShowWindow(Service svc, int page = -1, int tab = 0, bool mem = false)
	{
		Post(() =>
		{
			if (!WebAvailable())
			{
				OpenClassic(svc, page, tab, mem);
				return;
			}
			if (web == null || web.IsDisposed)
			{
				WebWindow w = new WebWindow(svc);
				w.InitFailed += (Exception _) =>
				{
					webAvailable = false;
					OpenClassic(svc, page, tab, mem);
				};
				w.FormClosed += (object _1, FormClosedEventArgs _2) =>
				{
					if (web == w)
					{
						web = null;
					}
					BeginAfterClose();
				};
				web = w;
			}
			if (mem && page < 0)
			{
				page = 3;
			}
			web.NavigateTo(PageName(page), TabName(page, tab, mem));
			web.ShowAndActivate();
		});
	}

	// The old WinForms window (kept as a fallback and for "Classic window").
	public static void ShowClassicWindow(Service svc, int page = -1, int tab = 0, bool mem = false)
	{
		Post(() => OpenClassic(svc, page, tab, mem));
	}

	private static void OpenClassic(Service svc, int page, int tab, bool mem)
	{
		if (main == null || main.IsDisposed)
		{
			main = new MainForm(svc);
			main.FormClosed += (object _1, FormClosedEventArgs _2) =>
			{
				main = null;
				BeginAfterClose();
			};
		}
		if (page >= 0)
		{
			main.Goto(page, tab);
		}
		if (mem)
		{
			main.OpenMemory();
		}
		main.Show();
		if (main.WindowState == FormWindowState.Minimized)
		{
			main.WindowState = FormWindowState.Normal;
		}
		main.Activate();
	}

	// Checked once per session: a missing runtime (or WebView2Loader.dll) means the classic window.
	private static bool WebAvailable()
	{
		if (webAvailable.HasValue)
		{
			return webAvailable.Value;
		}
		try
		{
			// A single-file exe extracts WebView2Loader.dll to its own folder. Loading it through .NET's native
			// library probing (which knows that folder) first lets the WebView2 library find it by name.
			NativeLibrary.TryLoad("WebView2Loader.dll", typeof(CoreWebView2Environment).Assembly, null, out _);
			string version = CoreWebView2Environment.GetAvailableBrowserVersionString();
			webAvailable = !string.IsNullOrEmpty(version);
			Cfg.Log("WebView2 runtime " + (version ?? "not found"));
		}
		catch (Exception ex)
		{
			webAvailable = false;
			Cfg.Log("WebView2 is not available, using the classic window: " + ex.GetType().Name + ": " + ex.Message);
		}
		return webAvailable.Value;
	}

	// Classic page numbers: 0 Synapse, 1 Macro, 2 Linked Games, 3 Naga, 4 BlackWidow Lite, 5 Settings.
	internal static string PageName(int page)
	{
		return page switch
		{
			0 => "home",
			1 => "macros",
			2 => "games",
			3 => "mouse",
			4 => "keyboard",
			5 => "settings",
			_ => null,
		};
	}

	// Classic device tabs: Naga 0 customize, 1 performance, 2 lighting, 3 calibration; keyboard the first three.
	// mem = the mouse's on-board memory.
	internal static string TabName(int page, int tab, bool mem)
	{
		if (mem)
		{
			return "onboard";
		}
		string[] tabs = page switch
		{
			3 => new string[4] { "customize", "performance", "lighting", "calibration" },
			4 => new string[3] { "customize", "performance", "lighting" },
			_ => Array.Empty<string>(),
		};
		return (tab >= 0 && tab < tabs.Length) ? tabs[tab] : null;
	}

	public static void ToggleFlyout(Service svc, int x, int y)
	{
		Post(() =>
		{
			if (flyout != null && !flyout.IsDisposed)
			{
				flyout.Close();
			}
			else if (Environment.TickCount64 - flyoutClosedAt >= 400)
			{
				flyout = new TrayFlyout(svc, new Point(x, y));
				flyout.FormClosed += (object _1, FormClosedEventArgs _2) =>
				{
					flyout = null;
					flyoutClosedAt = Environment.TickCount64;
					BeginAfterClose();
				};
				flyout.Show();
			}
		});
	}

	// Teaches LiteSynapse which key each Naga button sends. closed (optional) runs on the UI thread when the
	// calibration window closes and the result is saved.
	public static void Calibrate(Service svc, Action closed = null)
	{
		Post(() =>
		{
			if (calibration != null && !calibration.IsDisposed)
			{
				calibration.Activate();
				return;
			}
			Dictionary<string, BaseKey> learned = new Dictionary<string, BaseKey>();
			string[] names = InputEngine.ButtonIds;
			string[] hints = (from i in Enumerable.Range(1, 12)
				select $"side button {i} (grid numbered as in Synapse: 1-6 left column, 7-12 right)").Concat(new string[5] { "scroll wheel click", "M1: the UPPER of the two small buttons behind the wheel", "M2: the LOWER of the two small buttons behind the wheel", "scroll wheel tilt LEFT", "scroll wheel tilt RIGHT" }).ToArray();
			int step = 0;
			Form form = new Form
			{
				Text = "Calibrate Naga",
				Width = 480,
				Height = 230,
				StartPosition = FormStartPosition.CenterScreen,
				TopMost = true,
				BackColor = Theme.Bg,
				ForeColor = Theme.Text,
				Icon = AppIcon.Icon()
			};
			Label label = new Label
			{
				Dock = DockStyle.Top,
				Height = 80,
				Font = new Font("Segoe UI", 11f),
				Padding = new Padding(12),
				ForeColor = Theme.Text
			};
			Label note = new Label
			{
				Dock = DockStyle.Top,
				Height = 44,
				Padding = new Padding(12, 0, 12, 0),
				ForeColor = Theme.Green
			};
			NoFocusButton skip = new NoFocusButton();
			skip.Text = "Skip this button";
			skip.Dock = DockStyle.Bottom;
			skip.Height = 36;
			form.Controls.AddRange(new Control[3] { skip, note, label });
			skip.Click += (object _, EventArgs _) =>
			{
				Next();
			};
			svc.Engine.Calibrating = (int sc, bool ext) =>
			{
				form.BeginInvoke((Action)(() =>
				{
					if (step >= names.Length)
					{
						return;
					}
					KeyValuePair<string, BaseKey> used = learned.FirstOrDefault((KeyValuePair<string, BaseKey> kv) => kv.Value.Sc == sc && kv.Value.Ext == ext);
					if (used.Key != null)
					{
						note.Text = "That key was already used by " + used.Key + ". Two buttons sending the same key can't be told apart - press a different button, or Skip.";
						return;
					}
					note.Text = "Last press sent: " + Program.KeyName(sc, ext);
					learned[names[step]] = new BaseKey
					{
						Sc = sc,
						Ext = ext
					};
					Next();
				}));
			};
			ShowStep();
			form.FormClosed += (object _1, FormClosedEventArgs _2) =>
			{
				svc.Engine.Calibrating = null;
				svc.SetCalibration(learned);
				calibration = null;
				try
				{
					closed?.Invoke();
				}
				catch (Exception ex)
				{
					Cfg.Log("calibration closed: " + ex.Message);
				}
				BeginAfterClose();
			};
			calibration = form;
			form.Show();

			void Next()
			{
				step++;
				if (step >= names.Length)
				{
					form.Close();
				}
				else
				{
					ShowStep();
				}
			}

			void ShowStep()
			{
				label.Text = $"Press: {hints[step]}\n({step + 1} of {names.Length})";
			}
		});
	}
}
