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
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		bool lit = Selected || Primary;
		using GraphicsPath path = Theme.Round(new RectangleF(1f, 1f, base.Width - 3, base.Height - 3), (float)(base.Height - 3) / 2f);
		// A filled surface with a hairline edge rather than an empty outline: reads as a button at a glance.
		// Selected is the accent fill; Primary and hover light the edge in the accent.
		Color fill = Selected ? Theme.Green : ((hover && base.Enabled) ? Theme.Hover : Theme.Raised);
		using (SolidBrush b = new SolidBrush(fill))
		{
			g.FillPath(b, path);
		}
		if (!Selected)
		{
			using Pen pen = new Pen((lit || (hover && base.Enabled)) ? Theme.Green : Theme.Edge, 1f);
			g.DrawPath(pen, path);
		}
		Color c = Selected ? Color.Black : (base.Enabled ? Theme.Text : Theme.Dim);
		Tx.Center(g, base.Text, base.Font, c, new RectangleF(0f, 0f, base.Width, base.Height));
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		base.Invalidate();
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
