using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class GreenSlider : Control
{
	public int Min;

	public int Max = 100;

	public int Step = 1;

	public bool Bubble;

	public bool Labels;

	private int val;

	private bool drag;

	public int Value
	{
		get
		{
			return val;
		}
		set
		{
			value = Math.Clamp(value, Min, Max);
			if (val != value)
			{
				val = value;
				base.Invalidate();
			}
		}
	}

	public event Action Committed;

	public event Action Changing;

	public GreenSlider()
	{
		base.DoubleBuffered = true;
		base.Height = 36;
		base.Cursor = Cursors.Hand;
		base.BackColor = Theme.Card;
		base.TabStop = true;
		SetStyle(ControlStyles.Selectable, value: true);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		switch (keyData)
		{
		case Keys.Left:
		case Keys.Right:
		case Keys.Up:
		case Keys.Down:
		case Keys.Home:
		case Keys.End:
		case Keys.PageUp:
		case Keys.PageDown:
			return true;
		default:
			return base.IsInputKey(keyData);
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (!base.Enabled)
		{
			return;
		}
		int big = Math.Max(Step, (Max - Min) / 10);
		bool handled = true;
		switch (e.KeyCode)
		{
		case Keys.Left:
		case Keys.Down:
			Value -= Step;
			break;
		case Keys.Right:
		case Keys.Up:
			Value += Step;
			break;
		case Keys.PageDown:
			Value -= big;
			break;
		case Keys.PageUp:
			Value += big;
			break;
		case Keys.Home:
			Value = Min;
			break;
		case Keys.End:
			Value = Max;
			break;
		default:
			handled = false;
			break;
		}
		if (handled)
		{
			Changing?.Invoke();
			e.Handled = true;
		}
	}

	// Mirrors the drag-release commit, and covers auto-repeat (one commit when the keys are let go).
	protected override void OnKeyUp(KeyEventArgs e)
	{
		base.OnKeyUp(e);
		if (base.Enabled)
		{
			Committed?.Invoke();
		}
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		base.Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		base.Invalidate();
	}

	private float X(int v)
	{
		return 10f + (float)((base.Width - 20) * (v - Min)) / (float)(Max - Min);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0086: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0092: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Expected Obj, but got Unknown
		//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00cd: Expected Obj, but got Unknown
		//IL_00e9: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f0: Expected Obj, but got Unknown
		//IL_01f7: Unknown result type (might be due to invalid IL or missing references)
		//IL_01fe: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		// Drawn first: OnPaint returns early when !Bubble, so a ring added after that would never show.
		if (base.Focused && base.Enabled)
		{
			Theme.FocusRing(g, new RectangleF(0.5f, 0.5f, base.Width - 1.5f, base.Height - 1.5f), 6f);
		}
		float y = ((Bubble || Labels) ? ((float)base.Height / 2f - 2f) : ((float)base.Height / 2f));
		Color on = (base.Enabled ? Theme.Green : Theme.GreenDim);
		Pen track = new Pen(base.Enabled ? Color.FromArgb(70, 70, 70) : Color.FromArgb(48, 48, 48), 4f)
		{
			StartCap = (LineCap)2,
			EndCap = (LineCap)2
		};
		try
		{
			g.DrawLine(track, 10f, y, (float)(base.Width - 10), y);
			Pen fill = new Pen(on, 4f)
			{
				StartCap = (LineCap)2,
				EndCap = (LineCap)2
			};
			try
			{
				g.DrawLine(fill, 10f, y, X(val), y);
				SolidBrush b = new SolidBrush(on);
				try
				{
					g.FillEllipse((Brush)(object)b, X(val) - 7f, y - 7f, 14f, 14f);
					Font small = Theme.P(9f, (FontStyle)0);
					try
					{
						if (Labels)
						{
							Tx.Left(g, Min.ToString(), small, Theme.Text, new RectangleF(0f, base.Height - 16, 60f, 16f));
							Tx.Right(g, Max.ToString(), small, Theme.Text, new RectangleF(base.Width - 60, base.Height - 16, 60f, 16f));
						}
						if (!Bubble)
						{
							return;
						}
						RectangleF r = new RectangleF(Math.Clamp(X(val) - 17f, 0f, base.Width - 34), 0f, 34f, 18f);
						GraphicsPath p = Theme.Round(r, 3f);
						try
						{
							SolidBrush bb = new SolidBrush(on);
							try
							{
								g.FillPath((Brush)(object)bb, p);
								Tx.Center(g, val.ToString(), small, Color.Black, r);
							}
							finally
							{
								((IDisposable)bb)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)p)?.Dispose();
						}
					}
					finally
					{
					}
				}
				finally
				{
					((IDisposable)b)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)fill)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)track)?.Dispose();
		}
	}

	private void Set(int px)
	{
		double t = Math.Clamp(((double)px - 10.0) / (double)(base.Width - 20), 0.0, 1.0);
		int v = Min + (int)Math.Round(t * (double)(Max - Min) / (double)Step) * Step;
		if (v != val)
		{
			Value = v;
			Changing?.Invoke();
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		if (base.Enabled)
		{
			if (base.CanFocus)
			{
				Focus();
			}
			drag = true;
			base.Capture = true;
			Set(e.X);
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		if (drag)
		{
			Set(e.X);
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		if (drag)
		{
			drag = false;
			base.Capture = false;
			Committed?.Invoke();
		}
	}
}
