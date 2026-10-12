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
		base.TabStop = true;
		SetStyle(ControlStyles.Selectable, value: true);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		if (keyData == Keys.Space || keyData == Keys.Enter)
		{
			return true;
		}
		return base.IsInputKey(keyData);
	}

	// No internal state: re-raise the same Click the external handlers are wired to, with sender = this.
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (base.Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
		{
			OnClick(EventArgs.Empty);
			e.Handled = true;
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

	protected override void OnMouseClick(MouseEventArgs e)
	{
		base.OnMouseClick(e);
		if (base.Enabled && base.CanFocus)
		{
			Focus();
		}
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
		if (base.Focused && base.Enabled)
		{
			Theme.FocusRing(g, new RectangleF(0.5f, 0.5f, base.Width - 2, base.Height - 2), (float)(base.Height - 2) / 2f);
		}
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
