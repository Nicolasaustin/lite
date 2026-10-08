using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class UiHost
{
	[Serializable]
	[CompilerGenerated]
	private sealed class _003C_003Ec
	{
		public static readonly _003C_003Ec _003C_003E9 = new _003C_003Ec();

		public static ThreadExceptionEventHandler _003C_003E9__11_0;

		public static FormClosedEventHandler _003C_003E9__13_1;

		public static FormClosedEventHandler _003C_003E9__15_1;

		public static Func<int, string> _003C_003E9__16_1;

		internal void _003CLoop_003Eb__11_0(object _, ThreadExceptionEventArgs e)
		{
			Cfg.Log("UI error: " + e.Exception);
		}

		internal void _003CShowWindow_003Eb__13_1(object? _1, FormClosedEventArgs _2)
		{
			main = null;
			BeginAfterClose();
		}

		internal void _003CToggleFlyout_003Eb__15_1(object? _1, FormClosedEventArgs _2)
		{
			flyout = null;
			flyoutClosedAt = Environment.TickCount64;
			BeginAfterClose();
		}

		internal string _003CCalibrate_003Eb__16_1(int i)
		{
			return $"side button {i} (grid numbered as in Synapse: 1-6 left column, 7-12 right)";
		}
	}

	private static readonly object gate = new object();

	private static readonly ManualResetEventSlim ready = new ManualResetEventSlim();

	private static bool alive;

	private static bool inited;

	private static int pending;

	private static Control invoker;

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
				Thread thread = new Thread(Loop);
				thread.IsBackground = true;
				thread.Name = "LiteSynapse window";
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
		//IL_003c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0042: Expected Obj, but got Unknown
		if (!inited)
		{
			ApplicationConfiguration.Initialize();
			Application.SetUnhandledExceptionMode((UnhandledExceptionMode)2);
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
		((Component)(object)inv).Dispose();
		Native.TrimMemory();
	}

	private static void AfterClose()
	{
		bool exit = false;
		lock (gate)
		{
			if (((ReadOnlyCollectionBase)(object)Application.OpenForms).Count == 0 && pending == 0)
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

	public static void ShowWindow(Service svc, int page = -1, int tab = 0, bool mem = false)
	{
		Post(() =>
		{
			//IL_003c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0041: Unknown result type (might be due to invalid IL or missing references)
			//IL_0047: Expected Obj, but got Unknown
			if (main == null || ((Control)main).IsDisposed)
			{
				main = new MainForm(svc);
				MainForm mainForm = main;
				FormClosedEventHandler val = _003C_003Ec._003C_003E9__13_1;
				if (val == null)
				{
					FormClosedEventHandler val2 = (object? _1, FormClosedEventArgs _2) =>
					{
						main = null;
						BeginAfterClose();
					};
					_003C_003Ec._003C_003E9__13_1 = val2;
					val = val2;
				}
				((Form)mainForm).FormClosed += val;
			}
			if (page >= 0)
			{
				main.Goto(page, tab);
			}
			if (mem)
			{
				main.OpenMemory();
			}
			((Control)main).Show();
			((Form)main).WindowState = (FormWindowState)0;
			((Form)main).Activate();
		});
	}

	private static void BeginAfterClose()
	{
		if (invoker != null && !invoker.IsDisposed)
		{
			invoker.BeginInvoke((Action)AfterClose);
		}
	}

	public static void ToggleFlyout(Service svc, int x, int y)
	{
		Post(() =>
		{
			//IL_006c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Expected Obj, but got Unknown
			if (flyout != null && !((Control)flyout).IsDisposed)
			{
				((Form)flyout).Close();
			}
			else if (Environment.TickCount64 - flyoutClosedAt >= 400)
			{
				flyout = new TrayFlyout(svc, new Point(x, y));
				TrayFlyout trayFlyout = flyout;
				FormClosedEventHandler val = _003C_003Ec._003C_003E9__15_1;
				if (val == null)
				{
					FormClosedEventHandler val2 = (object? _1, FormClosedEventArgs _2) =>
					{
						flyout = null;
						flyoutClosedAt = Environment.TickCount64;
						BeginAfterClose();
					};
					_003C_003Ec._003C_003E9__15_1 = val2;
					val = val2;
				}
				((Form)trayFlyout).FormClosed += val;
				((Control)flyout).Show();
			}
		});
	}

	public static void Calibrate(Service svc)
	{
		Post(() =>
		{
			//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_010f: Expected Obj, but got Unknown
			//IL_0110: Unknown result type (might be due to invalid IL or missing references)
			//IL_0115: Unknown result type (might be due to invalid IL or missing references)
			//IL_011c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0124: Unknown result type (might be due to invalid IL or missing references)
			//IL_012f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0139: Expected Obj, but got Unknown
			//IL_0139: Unknown result type (might be due to invalid IL or missing references)
			//IL_013c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0146: Unknown result type (might be due to invalid IL or missing references)
			//IL_0156: Expected Obj, but got Unknown
			//IL_0157: Unknown result type (might be due to invalid IL or missing references)
			//IL_015c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0163: Unknown result type (might be due to invalid IL or missing references)
			//IL_016b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0172: Unknown result type (might be due to invalid IL or missing references)
			//IL_017c: Unknown result type (might be due to invalid IL or missing references)
			//IL_018c: Expected Obj, but got Unknown
			//IL_0219: Unknown result type (might be due to invalid IL or missing references)
			//IL_0223: Expected Obj, but got Unknown
			string[] names;
			string[] hints;
			int step;
			Form form;
			Label label;
			if (calibration != null && !((Control)calibration).IsDisposed)
			{
				calibration.Activate();
			}
			else
			{
				Dictionary<string, BaseKey> learned = new Dictionary<string, BaseKey>();
				names = InputEngine.ButtonIds;
				hints = (from i in Enumerable.Range(1, 12)
					select $"side button {i} (grid numbered as in Synapse: 1-6 left column, 7-12 right)").Concat(new string[5] { "scroll wheel click", "M1: the UPPER of the two small buttons behind the wheel", "M2: the LOWER of the two small buttons behind the wheel", "scroll wheel tilt LEFT", "scroll wheel tilt RIGHT" }).ToArray();
				step = 0;
				form = new Form
				{
					Text = "Calibrate Naga",
					Width = 480,
					Height = 230,
					StartPosition = (FormStartPosition)1,
					TopMost = true,
					BackColor = Theme.Bg,
					ForeColor = Theme.Text,
					Icon = AppIcon.Icon()
				};
				label = new Label
				{
					Dock = (DockStyle)1,
					Height = 80,
					Font = new Font("Segoe UI", 11f),
					Padding = new Padding(12),
					ForeColor = Theme.Text
				};
				Label note = new Label
				{
					Dock = (DockStyle)1,
					Height = 44,
					Padding = new Padding(12, 0, 12, 0),
					ForeColor = Theme.Green
				};
				NoFocusButton noFocusButton = new NoFocusButton();
				((Control)noFocusButton).Text = "Skip this button";
				((Control)noFocusButton).Dock = (DockStyle)2;
				((Control)noFocusButton).Height = 36;
				NoFocusButton noFocusButton2 = noFocusButton;
				((Control)form).Controls.AddRange(new Control[3]
				{
					(Control)noFocusButton2,
					(Control)note,
					(Control)label
				});
				((Control)noFocusButton2).Click += (object? _, EventArgs _) =>
				{
					Next();
				};
				svc.Engine.Calibrating = (int sc, bool ext) =>
				{
					((Control)form).BeginInvoke((Action)(() =>
					{
						if (step < names.Length)
						{
							KeyValuePair<string, BaseKey> keyValuePair = learned.FirstOrDefault((KeyValuePair<string, BaseKey> kv) => kv.Value.Sc == sc && kv.Value.Ext == ext);
							if (keyValuePair.Key != null)
							{
								((Control)note).Text = "That key was already used by " + keyValuePair.Key + ". Two buttons sending the same key can't be told apart - press a different button, or Skip.";
							}
							else
							{
								((Control)note).Text = "Last press sent: " + Program.KeyName(sc, ext);
								learned[names[step]] = new BaseKey
								{
									Sc = sc,
									Ext = ext
								};
								Next();
							}
						}
					}));
				};
				ShowStep();
				form.FormClosed += (object? _1, FormClosedEventArgs _2) =>
				{
					svc.Engine.Calibrating = null;
					svc.SetCalibration(learned);
					calibration = null;
					BeginAfterClose();
				};
				calibration = form;
				((Control)form).Show();
			}
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
				((Control)label).Text = $"Press: {hints[step]}\n({step + 1} of {names.Length})";
			}
		});
	}
}
