using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime;
using System.Threading;
using System.Windows.Forms;

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

	// Only for the memory release after the last window closed.
	private static Service svcForMemory;

	private static MainForm main;

	private static TrayFlyout flyout;

	private static Form calibration;

	private static long flyoutClosedAt;

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
				// An action that opened nothing (a tray click that only dismissed the flyout) must not leave this
				// thread idling with no window.
				if (Application.OpenForms.Count == 0)
				{
					AfterClose();
				}
			}
		}));
	}

	private static void Loop()
	{
		if (!inited)
		{
			ApplicationConfiguration.Initialize();
			inited = true;
		}
		// Per thread (this thread is started again after the last window closed): an error in a window is
		// logged instead of showing the WinForms error dialog.
		try
		{
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
		}
		catch (InvalidOperationException)
		{
		}
		Application.ThreadException += OnThreadException;
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

	private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
	{
		Cfg.Log("UI error: " + e.Exception);
	}

	private static void AfterClose()
	{
		bool exit = false;
		lock (gate)
		{
			// Can be reached twice for one close (the closed form's handler and the action that closed it):
			// the second time the thread is already on its way out.
			if (alive && Application.OpenForms.Count == 0 && pending == 0)
			{
				alive = false;
				exit = true;
			}
		}
		if (exit)
		{
			ReleaseMemory();
			Application.ExitThread();
		}
	}

	// Set by the window just before it closes itself because a linked game came to the front.
	internal static volatile bool ClosingForGame;

	// The main window closed since the last clean-up. The flyout and the calibration window allocate next to
	// nothing, and the flyout closes precisely when the user clicks back into the game - a forced, blocking
	// collection then stopped every thread, the keyboard hook included, on the first keystrokes of the round.
	private static volatile bool mainClosed;

	// The window is gone: hand the memory back instead of sitting on it for the rest of the session.
	//
	// Skipped while the keyboard hook is live, because a compacting collection pauses every thread. And
	// skipped when a game is in front: the window closes itself the moment a linked game comes forward, so
	// this used to run a full blocking, compacting collection at exactly the instant the game started
	// rendering - a burst of CPU and page faults during the one second where a hitch is most noticeable. The
	// memory is not lost by waiting; it goes back the next time the window closes on the desktop.
	private static void ReleaseMemory()
	{
		bool forGame = ClosingForGame;
		ClosingForGame = false;
		bool afterMain = mainClosed;
		mainClosed = false;
		try
		{
			if (!afterMain || forGame || svcForMemory?.EngineBusy == true || svcForMemory?.InGame == true)
			{
				return;
			}
			// The service may not have seen the switch yet: look at what is in front right now.
			string front = Native.ForegroundExePath(out uint pid);
			if (front != null && pid != (uint)Environment.ProcessId
				&& svcForMemory?.FindGame(System.IO.Path.GetFileName(front), front) != null)
			{
				return;
			}
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
		}
		catch
		{
		}
	}

	// Closes every window before the process exits (tray Exit), so each closes the normal way - the main
	// window saves its size and state - instead of being cut off with the thread. Waits up to timeoutMs.
	public static void CloseAll(int timeoutMs)
	{
		Control inv;
		lock (gate)
		{
			if (!alive)
			{
				return;
			}
			inv = invoker;
		}
		if (inv == null || inv.IsDisposed)
		{
			return;
		}
		using ManualResetEventSlim done = new ManualResetEventSlim();
		try
		{
			inv.BeginInvoke((Action)(() =>
			{
				try
				{
					foreach (Form f in Application.OpenForms.Cast<Form>().ToArray())
					{
						f.Close();
					}
				}
				finally
				{
					done.Set();
				}
			}));
			done.Wait(timeoutMs);
		}
		catch (InvalidOperationException)
		{
		}
	}

	private static void BeginAfterClose()
	{
		if (invoker != null && !invoker.IsDisposed)
		{
			invoker.BeginInvoke((Action)AfterClose);
		}
	}

	// Opens (or brings back) the main window. page/tab are the window's page and pill numbers (see
	// MainForm.Goto); mem opens the on-board memory panel with it.
	public static void ShowWindow(Service svc, int page = -1, int tab = 0, bool mem = false)
	{
		svcForMemory = svc;
		Post(() =>
		{
			if (main == null || main.IsDisposed)
			{
				main = new MainForm(svc);
				main.FormClosed += (object _1, FormClosedEventArgs _2) =>
				{
					main = null;
					mainClosed = true;
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
		});
	}

	public static void ToggleFlyout(Service svc, int x, int y)
	{
		svcForMemory = svc;
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
		svcForMemory = svc;
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
			// Owned by the main window, so when that closes itself for a game this goes with it, instead of
			// staying on top of the game with Snap Tap off and in-game presses taken as answers.
			if (main != null && !main.IsDisposed)
			{
				form.Owner = main;
			}
			Ui.Dialog(form);
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
				// Saved only when the run was finished, and on top of what was learned before: closing the window
				// to look, or half way, used to replace the saved calibration with a partial or empty one, and
				// every button not re-learned lost its remap.
				if (step >= names.Length)
				{
					Dictionary<string, BaseKey> merged = new Dictionary<string, BaseKey>();
					foreach (KeyValuePair<string, BaseKey> kv in svc.Base ?? new Dictionary<string, BaseKey>())
					{
						// An old entry whose key a newly learned button now sends would make two buttons collide.
						if (kv.Value != null && !learned.Values.Any((BaseKey b) => b.Sc == kv.Value.Sc && b.Ext == kv.Value.Ext))
						{
							merged[kv.Key] = kv.Value;
						}
					}
					foreach (KeyValuePair<string, BaseKey> kv in learned)
					{
						merged[kv.Key] = kv.Value;
					}
					svc.SetCalibration(merged);
				}
				else
				{
					Cfg.Log($"calibration closed after {step} of {names.Length} buttons: the saved calibration is unchanged");
				}
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
