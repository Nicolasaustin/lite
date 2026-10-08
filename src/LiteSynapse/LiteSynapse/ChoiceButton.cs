using System;
using System.Drawing;
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
		//IL_0042: Unknown result type (might be due to invalid IL or missing references)
		//IL_0048: Expected Obj, but got Unknown
		//IL_007b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0081: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		g.Clear(base.BackColor);
		SolidBrush b = new SolidBrush(Color.FromArgb(hover ? 55 : 45, hover ? 55 : 45, hover ? 55 : 45));
		try
		{
			g.FillRectangle((Brush)(object)b, 0, 0, base.Width - 1, base.Height - 1);
			Pen p = new Pen(Selected ? Theme.Green : Color.FromArgb(90, 90, 90));
			try
			{
				g.DrawRectangle(p, 0, 0, base.Width - 1, base.Height - 1);
				Tx.Center(g, base.Text, base.Font, Theme.Text, new RectangleF(0f, 0f, base.Width, base.Height));
			}
			finally
			{
				((IDisposable)p)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)b)?.Dispose();
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
