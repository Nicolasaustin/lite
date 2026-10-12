using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LiteSynapse;

internal class PillTabs : Control
{
	public string[] Items = Array.Empty<string>();

	public int Selected;

	private int hover = -1;

	// The tab the keyboard ring is on, which may differ from Selected until Space/Enter commits it.
	private int focusIdx = -1;

	public event Action Changed;

	public PillTabs()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.Height = 34;
		base.BackColor = Theme.Bg;
		base.TabStop = true;
		SetStyle(ControlStyles.Selectable, value: true);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		switch (keyData)
		{
		case Keys.Left:
		case Keys.Right:
		case Keys.Home:
		case Keys.End:
		case Keys.Space:
		case Keys.Enter:
			return true;
		default:
			return base.IsInputKey(keyData);
		}
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		// SetPills resets Selected to 0 on every top-tab change, so re-sync the ring to it here.
		focusIdx = Selected;
		base.Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		base.Invalidate();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (!base.Enabled || Items.Length == 0)
		{
			return;
		}
		int fi = (focusIdx < 0) ? Selected : focusIdx;
		switch (e.KeyCode)
		{
		case Keys.Left:
			focusIdx = Math.Max(0, fi - 1);
			break;
		case Keys.Right:
			focusIdx = Math.Min(Items.Length - 1, fi + 1);
			break;
		case Keys.Home:
			focusIdx = 0;
			break;
		case Keys.End:
			focusIdx = Items.Length - 1;
			break;
		case Keys.Space:
		case Keys.Enter:
			if (focusIdx >= 0 && focusIdx != Selected)
			{
				Selected = focusIdx;
				Changed?.Invoke();
			}
			break;
		default:
			return;
		}
		base.Invalidate();
		e.Handled = true;
	}

	public int TotalWidth()
	{
		return (int)Widths().Sum();
	}

	// Measured once per set of captions, not on every mouse move: a 1000 Hz mouse over the bar sent a
	// thousand hit tests a second, each measuring every caption again.
	private string[] measuredFor;

	private float[] widths = Array.Empty<float>();

	private float[] Widths()
	{
		if (!ReferenceEquals(measuredFor, Items))
		{
			Font f = Theme.P(9.5f, (FontStyle)0);
			widths = Items.Select((string i) => Tx.Measure(i, f).Width + 38f).ToArray();
			measuredFor = Items;
		}
		return widths;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0082: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		float[] w = Widths();
		float x = 0f;
		Font f = Theme.P(9.5f, (FontStyle)0);
		try
		{
			for (int i = 0; i < Items.Length; i++)
			{
				RectangleF r = new RectangleF(x, 3f, w[i], base.Height - 6);
				if (i == Selected)
				{
					GraphicsPath p = Theme.Round(r, (float)(base.Height - 6) / 2f);
					try
					{
						SolidBrush fb = new SolidBrush(Theme.Green);
						try
						{
							g.FillPath((Brush)(object)fb, p);
						}
						finally
						{
							((IDisposable)fb)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)p)?.Dispose();
					}
				}
				Graphics g2 = g;
				string s = Items[i];
				Font f2 = f;
				Color c;
				if (i == Selected)
				{
					c = Color.Black;
				}
				else
				{
					c = ((i == hover) ? Color.White : Color.FromArgb(210, 210, 210));
				}
				Tx.Center(g2, s, f2, c, r);
				x += w[i];
			}
			PaintFocusRing(g);
		}
		finally
		{
		}
	}

	private int Hit(Point pt)
	{
		float[] w = Widths();
		float x = 0f;
		for (int i = 0; i < w.Length; i++)
		{
			if ((float)pt.X >= x && (float)pt.X <= x + w[i])
			{
				return i;
			}
			x += w[i];
		}
		return -1;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		int h = Hit(e.Location);
		if (h != hover)
		{
			hover = h;
			base.Invalidate();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hover = -1;
		base.Invalidate();
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		if (base.CanFocus)
		{
			Focus();
		}
		int h = Hit(e.Location);
		if (h >= 0 && h != Selected)
		{
			Selected = h;
			focusIdx = h;
			base.Invalidate();
			Changed?.Invoke();
		}
	}

	private void PaintFocusRing(Graphics g)
	{
		if (!base.Focused || !base.Enabled || focusIdx < 0 || focusIdx >= Items.Length)
		{
			return;
		}
		float[] w = Widths();
		float xr = 0f;
		for (int i = 0; i < focusIdx; i++)
		{
			xr += w[i];
		}
		Theme.FocusRing(g, new RectangleF(xr, 3f, w[focusIdx], base.Height - 6), (float)(base.Height - 6) / 2f);
	}
}
