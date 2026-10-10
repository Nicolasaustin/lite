using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class ChoiceButton : Control
{
	public bool Selected;

	private bool hover;

	public ChoiceButton()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.Size = new Size(60, 26);
		base.BackColor = Theme.Card;
		base.Font = Theme.F(9.5f, (FontStyle)0);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		// Rounded; the chosen one is tinted with the accent as well as outlined in it.
		using GraphicsPath path = Theme.Round(new RectangleF(0.5f, 0.5f, base.Width - 1.5f, base.Height - 1.5f), 6f);
		Color fill = Selected ? Color.FromArgb(30, 58, 26) : (hover ? Theme.Hover : Theme.Raised);
		using (SolidBrush b = new SolidBrush(fill))
		{
			g.FillPath(b, path);
		}
		using (Pen p = new Pen(Selected ? Theme.Green : Theme.Edge))
		{
			g.DrawPath(p, path);
		}
		Tx.Center(g, base.Text, base.Font, Selected ? Color.White : Theme.Text, new RectangleF(0f, 0f, base.Width, base.Height));
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
