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

	public event Action Changed;

	public PillTabs()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.Height = 34;
		base.BackColor = Theme.Bg;
	}

	public int TotalWidth()
	{
		return (int)Widths().Sum();
	}

	private float[] Widths()
	{
		Font f = Theme.F(9.5f, (FontStyle)0);
		try
		{
			return Items.Select((string i) => Tx.Measure(i, f).Width + 38f).ToArray();
		}
		finally
		{
			if (f != null)
			{
				((IDisposable)f).Dispose();
			}
		}
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
		Font f = Theme.F(9.5f, (FontStyle)0);
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
		}
		finally
		{
			((IDisposable)f)?.Dispose();
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
		int h = Hit(e.Location);
		if (h >= 0 && h != Selected)
		{
			Selected = h;
			base.Invalidate();
			Changed?.Invoke();
		}
	}
}
