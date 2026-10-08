using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class PillButton : Control
{
	public bool Selected;

	public bool Primary;

	private bool hover;

	public PillButton()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.Height = 34;
		base.Font = Theme.F(9.5f, (FontStyle)1);
		base.BackColor = Theme.Bg;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008a: Expected Obj, but got Unknown
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b5: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		bool lit = Selected || Primary;
		GraphicsPath path = Theme.Round(new RectangleF(1f, 1f, base.Width - 3, base.Height - 3), (float)(base.Height - 3) / 2f);
		try
		{
			Color color;
			if (Selected)
			{
				color = Theme.Green;
			}
			else
			{
				color = (hover ? Theme.Panel2 : Theme.Bg);
			}
			SolidBrush fill = new SolidBrush(color);
			try
			{
				g.FillPath((Brush)(object)fill, path);
				Pen pen = new Pen((lit || hover) ? Theme.Green : Theme.Line, 1.5f);
				try
				{
					g.DrawPath(pen, path);
					Graphics g2 = g;
					string text = base.Text;
					Font font = base.Font;
					Color c;
					if (Selected)
					{
						c = Color.Black;
					}
					else
					{
						c = (base.Enabled ? Theme.Text : Theme.Dim);
					}
					Tx.Center(g2, text, font, c, new RectangleF(0f, 0f, base.Width, base.Height));
				}
				finally
				{
					((IDisposable)pen)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)fill)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)path)?.Dispose();
		}
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		hover = true;
		base.Invalidate();
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		hover = false;
		base.Invalidate();
	}

	protected override void OnTextChanged(EventArgs e)
	{
		base.Invalidate();
	}
}
