using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

internal class AssignPanel : Panel
{
	public Action<string, string> OnSave;

	private string id;

	private string title;

	private string defaultText;

	private bool editable;

	private int mode;

	private readonly KeyCapture cap;

	private readonly Label cancel;

	private readonly Label save;

	private static readonly string[] Icons = new string[6] { "↻", "⌨", "\ud83d\uddb1", "M", "▶", "⊘" };

	private static readonly string[] Names = new string[6] { "DEFAULT", "KEYBOARD FUNCTION", "MOUSE FUNCTION", "MACRO", "MULTIMEDIA", "DISABLE" };

	public AssignPanel()
	{
		//IL_0013: Unknown result type (might be due to invalid IL or missing references)
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_0023: Unknown result type (might be due to invalid IL or missing references)
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0047: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Expected Obj, but got Unknown
		//IL_0070: Unknown result type (might be due to invalid IL or missing references)
		//IL_0075: Unknown result type (might be due to invalid IL or missing references)
		//IL_0080: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Unknown result type (might be due to invalid IL or missing references)
		//IL_0093: Unknown result type (might be due to invalid IL or missing references)
		//IL_009e: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bf: Expected Obj, but got Unknown
		KeyCapture keyCapture = new KeyCapture();
		((Control)keyCapture).Visible = false;
		cap = keyCapture;
		cancel = new Label
		{
			Text = "CANCEL",
			TextAlign = (ContentAlignment)32,
			ForeColor = Color.White,
			BackColor = Color.FromArgb(120, 120, 120),
			Cursor = Cursors.Hand,
			Font = Theme.F(9f, (FontStyle)1),
			UseCompatibleTextRendering = false
		};
		save = new Label
		{
			Text = "SAVE",
			TextAlign = (ContentAlignment)32,
			ForeColor = Color.White,
			BackColor = Theme.GreenDark,
			Cursor = Cursors.Hand,
			Font = Theme.F(9f, (FontStyle)1)
		};
		base.DoubleBuffered = true;
		base.BackColor = Color.FromArgb(24, 24, 24);
		base.Size = new Size(300, 300);
		base.Visible = false;
		base.Controls.Add((Control)(object)cap);
		base.Controls.Add((Control)(object)cancel);
		base.Controls.Add((Control)(object)save);
		((Control)cancel).Click += (object? _, EventArgs _) =>
		{
			base.Hide();
		};
		((Control)save).Click += (object? _, EventArgs _) =>
		{
			if (!editable)
			{
				base.Hide();
			}
			else
			{
				if (mode == 0)
				{
					OnSave?.Invoke(id, "");
				}
				else if (mode == 1 && cap.KeyName != null)
				{
					OnSave?.Invoke(id, cap.KeyName);
				}
				else if (mode == 5)
				{
					OnSave?.Invoke(id, "DISABLED");
				}
				else if (mode == 1)
				{
					return;
				}
				base.Hide();
			}
		};
	}

	public void Open(string id, string title, string current, string def, bool editable)
	{
		this.id = id;
		this.title = title;
		defaultText = def;
		this.editable = editable;
		mode = ((current != null) ? ((!(current == "DISABLED")) ? 1 : 5) : 0);
		KeyCapture keyCapture = cap;
		bool flag = ((current == null || current == "DISABLED") ? true : false);
		keyCapture.KeyName = (flag ? null : current);
		base.Visible = true;
		base.BringToFront();
		Relayout();
		if (mode == 1)
		{
			((Control)cap).Focus();
		}
	}

	private void Relayout()
	{
		((Control)cap).Visible = editable && mode == 1;
		((Control)cap).SetBounds(58, 100, base.Width - 58 - 18, 44);
		int y = ((mode == 1) ? 164 : 160);
		((Control)cancel).SetBounds(58, y, 96, 30);
		((Control)save).SetBounds(164, y, 96, 30);
		Label val = cancel;
		bool visible = (((Control)save).Visible = true);
		((Control)val).Visible = visible;
		base.Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0018: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Expected Obj, but got Unknown
		//IL_004e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0055: Expected Obj, but got Unknown
		//IL_00ca: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d1: Expected Obj, but got Unknown
		//IL_0126: Unknown result type (might be due to invalid IL or missing references)
		//IL_012d: Expected Obj, but got Unknown
		//IL_015b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0162: Expected Obj, but got Unknown
		//IL_01b7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01be: Expected Obj, but got Unknown
		//IL_031d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0324: Expected Obj, but got Unknown
		//IL_0339: Unknown result type (might be due to invalid IL or missing references)
		//IL_0340: Expected Obj, but got Unknown
		//IL_0208: Unknown result type (might be due to invalid IL or missing references)
		//IL_020f: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		Pen p = new Pen(Color.FromArgb(90, 90, 90));
		try
		{
			g.DrawRectangle(p, 0, 0, base.Width - 1, base.Height - 1);
		}
		finally
		{
			((IDisposable)p)?.Dispose();
		}
		SolidBrush hb = new SolidBrush(Color.FromArgb(30, 30, 30));
		try
		{
			g.FillRectangle((Brush)(object)hb, 1, 1, base.Width - 2, 40);
		}
		finally
		{
			((IDisposable)hb)?.Dispose();
		}
		Font tf = Theme.F(10f, (FontStyle)0);
		try
		{
			Tx.Center(g, title, tf, Color.Silver, new RectangleF(0f, 0f, base.Width, 42f));
		}
		finally
		{
			((IDisposable)tf)?.Dispose();
		}
		Pen xp = new Pen(Color.Silver, 1.6f);
		try
		{
			g.DrawLine(xp, base.Width - 28, 14, base.Width - 16, 26);
			g.DrawLine(xp, base.Width - 16, 14, base.Width - 28, 26);
		}
		finally
		{
			((IDisposable)xp)?.Dispose();
		}
		Pen line = new Pen(Color.FromArgb(70, 70, 70));
		try
		{
			g.DrawLine(line, 1, 42, base.Width - 2, 42);
		}
		finally
		{
			((IDisposable)line)?.Dispose();
		}
		SolidBrush strip = new SolidBrush(Color.FromArgb(34, 34, 34));
		try
		{
			g.FillRectangle((Brush)(object)strip, 1, 43, 42, base.Height - 44);
		}
		finally
		{
			((IDisposable)strip)?.Dispose();
		}
		for (int i = 0; i < 6; i++)
		{
			Rectangle r = new Rectangle(1, 46 + i * 46, 42, 44);
			if (i == mode)
			{
				SolidBrush sb = new SolidBrush(Color.FromArgb(14, 14, 14));
				try
				{
					g.FillRectangle((Brush)(object)sb, r);
				}
				finally
				{
					((IDisposable)sb)?.Dispose();
				}
			}
			Font f = new Font(i switch
			{
				3 => "Segoe UI", 
				2 => "Segoe UI Emoji", 
				_ => "Segoe UI Symbol", 
			}, (float)((i == 3) ? 11 : 13), (i == 3) ? FontStyle.Bold : FontStyle.Regular);
			try
			{
				Tx.Center(g, Icons[i], f, (i == mode) ? Theme.Green : Color.FromArgb(150, 150, 150), r);
			}
			finally
			{
				((IDisposable)f)?.Dispose();
			}
		}
		Font hf = Theme.F(10.5f, (FontStyle)0);
		try
		{
			Tx.Left(g, Names[mode], hf, Theme.Green, new RectangleF(58f, 52f, base.Width - 70, 30f));
			Font df = Theme.F(10f, (FontStyle)0);
			try
			{
				if (!editable)
				{
					Tx.Wrap(g, "This button is handled by the mouse itself, so LiteSynapse can't change it.", df, Color.Silver, new RectangleF(58f, 96f, base.Width - 78, 60f));
					return;
				}
				if (mode == 0)
				{
					Rectangle r2 = new Rectangle(58, 100, base.Width - 76, 44);
					SolidBrush bb = new SolidBrush(Color.FromArgb(18, 18, 18));
					try
					{
						g.FillRectangle((Brush)(object)bb, r2);
						Pen bp = new Pen(Color.FromArgb(110, 110, 110));
						try
						{
							g.DrawRectangle(bp, r2);
							Font vf = Theme.F(11f, (FontStyle)0);
							try
							{
								Tx.Center(g, defaultText, vf, Theme.Text, r2);
								return;
							}
							finally
							{
								((IDisposable)vf)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)bp)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)bb)?.Dispose();
					}
				}
				if (mode == 5)
				{
					Tx.Wrap(g, "This button will do nothing in this profile.", df, Color.Silver, new RectangleF(58f, 96f, base.Width - 78, 60f));
				}
				else if (mode != 1)
				{
					Tx.Wrap(g, "Not supported by LiteSynapse yet.", df, Color.Silver, new RectangleF(58f, 96f, base.Width - 78, 60f));
				}
			}
			finally
			{
				((IDisposable)df)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)hf)?.Dispose();
		}
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		if (e.Y < 42 && e.X > base.Width - 40)
		{
			base.Hide();
		}
		else
		{
			if (!editable || e.X >= 44 || e.Y < 46)
			{
				return;
			}
			int i = (e.Y - 46) / 46;
			if (i >= 0 && i < 6)
			{
				mode = i;
				Relayout();
				if (mode == 1)
				{
					((Control)cap).Focus();
				}
			}
		}
	}
}
