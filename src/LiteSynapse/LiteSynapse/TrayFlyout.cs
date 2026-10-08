using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LiteSynapse;

internal sealed class TrayFlyout : Form
{
	private const int W = 360;

	private const int H = 428;

	private readonly Service svc;

	private readonly DarkCombo cbMouse;

	private readonly DarkCombo cbKb;

	private readonly Toggle auto;

	private readonly IconBtn gear;

	private string status;

	private int devices;

	private bool loading;

	private bool popupOpen;

	private readonly long shownAt;

	private Bitmap logo;

	internal DarkCombo MouseCombo => cbMouse;

	internal DarkCombo KeyboardCombo => cbKb;

	protected override CreateParams CreateParams
	{
		get
		{
			CreateParams createParams = base.CreateParams;
			createParams.ClassStyle |= 0x20000;
			createParams.ExStyle |= 0x80;
			return createParams;
		}
	}

	internal void TestDeactivate()
	{
		base.OnDeactivate(EventArgs.Empty);
	}

	public TrayFlyout(Service svc, Point cursor)
	{
		//IL_0398: Unknown result type (might be due to invalid IL or missing references)
		//IL_03a2: Expected Obj, but got Unknown
		DarkCombo darkCombo = new DarkCombo();
		((Control)darkCombo).Location = new Point(78, 116);
		((Control)darkCombo).Width = 252;
		((Control)darkCombo).BackColor = Theme.Top;
		darkCombo.Inline = true;
		cbMouse = darkCombo;
		DarkCombo darkCombo2 = new DarkCombo();
		((Control)darkCombo2).Location = new Point(78, 192);
		((Control)darkCombo2).Width = 252;
		((Control)darkCombo2).BackColor = Theme.Top;
		darkCombo2.Inline = true;
		cbKb = darkCombo2;
		Toggle toggle = new Toggle();
		((Control)toggle).Location = new Point(300, 306);
		((Control)toggle).BackColor = Color.FromArgb(30, 30, 30);
		auto = toggle;
		IconBtn iconBtn = new IconBtn
		{
			Glyph = "⚙",
			Tip = "Settings"
		};
		((Control)iconBtn).Location = new Point(304, 20);
		((Control)iconBtn).BackColor = Theme.Top;
		gear = iconBtn;
		status = "";
		shownAt = Environment.TickCount64;
		TrayFlyout trayFlyout = this;
		this.svc = svc;
		base.FormBorderStyle = (FormBorderStyle)0;
		base.ShowInTaskbar = false;
		base.TopMost = true;
		base.StartPosition = (FormStartPosition)0;
		base.BackColor = Theme.Top;
		base.ClientSize = new Size(360, 428);
		base.DoubleBuffered = true;
		base.Font = Theme.F(10f, (FontStyle)0);
		Rectangle area = Screen.FromPoint(cursor).WorkingArea;
		int x = Math.Clamp(cursor.X - 180, area.Left + 8, area.Right - 360 - 8);
		int y = ((cursor.Y > area.Top + area.Height / 2) ? (area.Bottom - 428 - 8) : (area.Top + 8));
		base.Location = new Point(x, y);
		logo = AppIcon.Make(40);
		base.Controls.AddRange(new Control[4] { cbMouse, cbKb, auto, gear });
		cbMouse.SelectedIndexChanged += () =>
		{
			if (!trayFlyout.loading && trayFlyout.cbMouse.SelectedItem is string text)
			{
				Cfg.Log("flyout: mouse profile picked: " + text);
				svc.SelectMouse(text);
			}
		};
		cbKb.SelectedIndexChanged += () =>
		{
			if (!trayFlyout.loading && trayFlyout.cbKb.SelectedItem is string text)
			{
				Cfg.Log("flyout: keyboard profile picked: " + text);
				svc.SelectKeyboard(text);
			}
		};
		DarkCombo[] array = new DarkCombo[2] { cbMouse, cbKb };
		foreach (DarkCombo obj in array)
		{
			obj.PopupOpened += () =>
			{
				trayFlyout.popupOpen = true;
			};
			obj.PopupClosed += () =>
			{
				trayFlyout.popupOpen = false;
				if (((Control)trayFlyout).IsHandleCreated && !((Control)trayFlyout).IsDisposed)
				{
					((Control)trayFlyout).BeginInvoke((Action)trayFlyout.Reload);
				}
			};
		}
		auto.CheckedChanged += () =>
		{
			if (!trayFlyout.loading)
			{
				svc.SetAuto(trayFlyout.auto.Checked);
			}
		};
		((Control)gear).Click += (object? _, EventArgs _) =>
		{
			UiHost.ShowWindow(svc, 5);
			((Form)trayFlyout).Close();
		};
		base.Deactivate += (object? _, EventArgs _) =>
		{
			if (Environment.TickCount64 - trayFlyout.shownAt > 200 && ((Control)trayFlyout).IsHandleCreated && !((Control)trayFlyout).IsDisposed)
			{
				((Control)trayFlyout).BeginInvoke((Action)((Form)trayFlyout).Close);
			}
		};
		Action changed = () =>
		{
			if (((Control)trayFlyout).IsHandleCreated && !((Control)trayFlyout).IsDisposed)
			{
				((Control)trayFlyout).BeginInvoke((Action)trayFlyout.Reload);
			}
		};
		svc.Changed += changed;
		base.FormClosed += (object? _, FormClosedEventArgs _) =>
		{
			svc.Changed -= changed;
			((Image)trayFlyout.logo).Dispose();
		};
		Reload();
	}

	protected override void OnShown(EventArgs e)
	{
		base.OnShown(e);
		base.Activate();
	}

	private void Reload()
	{
		if (popupOpen)
		{
			return;
		}
		loading = true;
		try
		{
			cbMouse.Items.Clear();
			cbMouse.Items.AddRange(svc.Store.Mouse.Select((MouseProfile p) => p.Name).Cast<object>().ToList());
			cbMouse.SelectedItem = svc.ActiveMouse;
			cbKb.Items.Clear();
			cbKb.Items.Add("(none)");
			cbKb.Items.AddRange(svc.Store.Keyboard.Select((KbProfile p) => p.Name).Cast<object>().ToList());
			cbKb.SelectedItem = ((svc.ActiveKeyboard.Length == 0) ? "(none)" : svc.ActiveKeyboard);
			auto.Checked = svc.Auto;
			devices = ((svc.Mouse != null) ? 1 : 0) + ((svc.Kb != null) ? 1 : 0);
			ref string reference = ref status;
			string text;
			if (svc.Mouse == null)
			{
				text = "Naga not found";
			}
			else if (svc.LiveStatus.Length > 0)
			{
				text = svc.LiveStatus;
			}
			else if (svc.InGame)
			{
				text = (svc.Auto ? "Game profile active" : "Profile active (picked by you)");
			}
			else
			{
				text = "Starting profile - no game running";
			}
			reference = text;
		}
		finally
		{
			loading = false;
		}
		base.Invalidate();
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		base.OnMouseClick(e);
		if (e.Y >= 372)
		{
			UiHost.ShowWindow(svc);
			base.Close();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_00db: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e1: Expected Obj, but got Unknown
		//IL_0141: Unknown result type (might be due to invalid IL or missing references)
		//IL_0148: Expected Obj, but got Unknown
		//IL_0242: Unknown result type (might be due to invalid IL or missing references)
		//IL_0249: Expected Obj, but got Unknown
		//IL_029f: Unknown result type (might be due to invalid IL or missing references)
		//IL_02a6: Expected Obj, but got Unknown
		//IL_02ec: Unknown result type (might be due to invalid IL or missing references)
		//IL_02f3: Expected Obj, but got Unknown
		//IL_0403: Unknown result type (might be due to invalid IL or missing references)
		//IL_040a: Expected Obj, but got Unknown
		//IL_0438: Unknown result type (might be due to invalid IL or missing references)
		//IL_043f: Expected Obj, but got Unknown
		//IL_0519: Unknown result type (might be due to invalid IL or missing references)
		//IL_0520: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(Theme.Top);
		g.DrawImage((Image)(object)logo, 20, 16, 40, 40);
		Font f = Theme.F(14f, (FontStyle)1);
		try
		{
			Tx.Left(g, "LiteSynapse", f, Color.White, new RectangleF(72f, 16f, 220f, 24f));
		}
		finally
		{
			((IDisposable)f)?.Dispose();
		}
		Font f2 = Theme.F(8.5f, (FontStyle)0);
		try
		{
			Graphics g2 = g;
			string s;
			if (devices == 2)
			{
				s = "Naga and BlackWidow connected";
			}
			else
			{
				s = ((devices == 1) ? "1 device connected" : "No devices found");
			}
			Tx.Left(g2, s, f2, Theme.Dim, new RectangleF(72f, 40f, 240f, 18f));
		}
		finally
		{
			((IDisposable)f2)?.Dispose();
		}
		Pen p = new Pen(Color.FromArgb(40, 40, 40));
		try
		{
			g.DrawLine(p, 0, 72, 360, 72);
		}
		finally
		{
			((IDisposable)p)?.Dispose();
		}
		Font f3 = Theme.F(8f, (FontStyle)1);
		try
		{
			Tx.Center(g, "PROFILES", f3, Theme.Dim, new RectangleF(0f, 80f, 360f, 22f));
			Pen p2 = new Pen(Color.FromArgb(50, 50, 50));
			try
			{
				g.DrawLine(p2, 20, 91, 128, 91);
				g.DrawLine(p2, 232, 91, 340, 91);
			}
			finally
			{
				((IDisposable)p2)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)f3)?.Dispose();
		}
		DrawMouseIcon(g, 26f, 112f);
		Font f4 = Theme.F(7.5f, (FontStyle)0);
		try
		{
			Tx.Left(g, "RAZER NAGA LEFT-HANDED EDITION", f4, Theme.Dim, new RectangleF(78f, 150f, 260f, 16f));
		}
		finally
		{
			((IDisposable)f4)?.Dispose();
		}
		DrawKeyboardIcon(g, 24f, 194f);
		Font f5 = Theme.F(7.5f, (FontStyle)0);
		try
		{
			Tx.Left(g, "RAZER BLACKWIDOW LITE", f5, Theme.Dim, new RectangleF(78f, 226f, 260f, 16f));
		}
		finally
		{
			((IDisposable)f5)?.Dispose();
		}
		Pen p3 = new Pen(Color.FromArgb(40, 40, 40));
		try
		{
			g.DrawLine(p3, 0, 258, 360, 258);
		}
		finally
		{
			((IDisposable)p3)?.Dispose();
		}
		GraphicsPath gp = Theme.Round(new RectangleF(16f, 270f, 328f, 90f), 6f);
		try
		{
			SolidBrush b = new SolidBrush(Color.FromArgb(30, 30, 30));
			try
			{
				g.FillPath((Brush)(object)b, gp);
			}
			finally
			{
				((IDisposable)b)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)gp)?.Dispose();
		}
		SolidBrush b2 = new SolidBrush((svc.Mouse == null) ? Color.FromArgb(230, 120, 80) : Theme.Green);
		try
		{
			g.FillEllipse((Brush)(object)b2, 32, 288, 9, 9);
		}
		finally
		{
			((IDisposable)b2)?.Dispose();
		}
		Font f6 = Theme.F(9.5f, (FontStyle)0);
		try
		{
			Tx.Left(g, status, f6, Theme.Text, new RectangleF(50f, 281f, 280f, 26f));
		}
		finally
		{
			((IDisposable)f6)?.Dispose();
		}
		Font f7 = Theme.F(9.5f, (FontStyle)0);
		try
		{
			Tx.Left(g, "Switch profiles by game automatically", f7, Theme.Text, new RectangleF(32f, 304f, 250f, 28f));
		}
		finally
		{
			((IDisposable)f7)?.Dispose();
		}
		Font f8 = Theme.F(8f, (FontStyle)0);
		try
		{
			Tx.Left(g, svc.Auto ? "Follows the program in front" : "Manual: the profile you picked stays", f8, Theme.Dim, new RectangleF(32f, 330f, 296f, 18f));
		}
		finally
		{
			((IDisposable)f8)?.Dispose();
		}
		SolidBrush b3 = new SolidBrush(Color.FromArgb(8, 8, 8));
		try
		{
			g.FillRectangle((Brush)(object)b3, 0, 372, 360, 56);
		}
		finally
		{
			((IDisposable)b3)?.Dispose();
		}
		Pen p4 = new Pen(Color.FromArgb(40, 40, 40));
		try
		{
			g.DrawLine(p4, 0, 372, 360, 372);
		}
		finally
		{
			((IDisposable)p4)?.Dispose();
		}
		g.DrawImage((Image)(object)logo, 102, 382, 36, 36);
		Font f9 = Theme.F(10.5f, (FontStyle)1);
		try
		{
			Tx.Left(g, "LITESYNAPSE", f9, Color.White, new RectangleF(146f, 382f, 140f, 36f));
		}
		finally
		{
			((IDisposable)f9)?.Dispose();
		}
		Font f10 = Theme.F(8f, (FontStyle)0);
		try
		{
			Tx.Right(g, "v1.0", f10, Color.FromArgb(110, 110, 110), new RectangleF(300f, 406f, 52f, 16f));
		}
		finally
		{
			((IDisposable)f10)?.Dispose();
		}
		Pen p5 = new Pen(Color.FromArgb(70, 70, 70));
		try
		{
			g.DrawRectangle(p5, 0, 0, 359, 427);
		}
		finally
		{
			((IDisposable)p5)?.Dispose();
		}
	}

	private static void DrawMouseIcon(Graphics g, float x, float y)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected Obj, but got Unknown
		Pen p = new Pen(Color.FromArgb(215, 215, 215), 1.6f);
		try
		{
			GraphicsPath path = Theme.Round(new RectangleF(x + 6f, y, 18f, 30f), 9f);
			try
			{
				g.DrawPath(p, path);
			}
			finally
			{
				((IDisposable)path)?.Dispose();
			}
			g.DrawLine(p, x + 15f, y, x + 15f, y + 11f);
			g.DrawLine(p, x + 6f, y + 11f, x + 24f, y + 11f);
		}
		finally
		{
			((IDisposable)p)?.Dispose();
		}
	}

	private static void DrawKeyboardIcon(Graphics g, float x, float y)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001f: Expected Obj, but got Unknown
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0039: Expected Obj, but got Unknown
		Pen p = new Pen(Color.FromArgb(215, 215, 215), 1.6f);
		try
		{
			SolidBrush b = new SolidBrush(Color.FromArgb(215, 215, 215));
			try
			{
				GraphicsPath path = Theme.Round(new RectangleF(x, y, 34f, 17f), 3f);
				try
				{
					g.DrawPath(p, path);
				}
				finally
				{
					((IDisposable)path)?.Dispose();
				}
				for (int r = 0; r < 2; r++)
				{
					for (int c = 0; c < 6; c++)
					{
						g.FillRectangle((Brush)(object)b, x + 4f + (float)(c * 5), y + 4f + (float)(r * 5), 3f, 3f);
					}
				}
			}
			finally
			{
				((IDisposable)b)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)p)?.Dispose();
		}
	}
}
