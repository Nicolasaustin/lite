using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class Card : Panel
{
	public string Title = "";

	public bool Muted;

	private bool hover;

	public Card()
	{
		base.DoubleBuffered = true;
		base.BackColor = Theme.Card;
	}

	// Only the clickable cards (the home tiles, which set Cursor=Hand) join the tab order; a plain card stays
	// inert. Keyed off the same Hand-cursor signal the card already uses for its hover edge.
	protected override void OnCursorChanged(EventArgs e)
	{
		base.OnCursorChanged(e);
		bool hand = base.Cursor == Cursors.Hand;
		base.TabStop = hand;
		SetStyle(ControlStyles.Selectable, hand);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		if (keyData == Keys.Space || keyData == Keys.Enter)
		{
			return true;
		}
		return base.IsInputKey(keyData);
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (base.Cursor == Cursors.Hand && base.Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
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
		if (base.Cursor == Cursors.Hand && base.CanFocus)
		{
			Focus();
		}
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		Theme.Quality(g);
		// Rounded, with a hairline edge: the corners show whatever the card sits on.
		Color behind = base.Parent?.BackColor ?? Theme.Bg;
		g.Clear((behind.A == 255) ? behind : Theme.Bg);
		using (GraphicsPath path = Theme.Round(new RectangleF(0.5f, 0.5f, base.Width - 1.5f, base.Height - 1.5f), Theme.Radius))
		{
			using (SolidBrush fill = new SolidBrush(base.BackColor))
			{
				g.FillPath(fill, path);
			}
			// A card that opens something (the home page ones) lights its edge under the pointer.
			using (Pen edge = new Pen((hover && base.Cursor == Cursors.Hand) ? Theme.GreenDim : Theme.Edge, 1f))
			{
				g.DrawPath(edge, path);
			}
		}
		Tx.Left(g, Title.ToUpperInvariant(), Theme.P(11f, (FontStyle)0), Muted ? Theme.GreenDim : Theme.Green, new RectangleF(40f, 34f, base.Width - 90, 24f));
		if (base.Focused && base.Enabled && base.Cursor == Cursors.Hand)
		{
			Theme.FocusRing(g, new RectangleF(0.5f, 0.5f, base.Width - 1.5f, base.Height - 1.5f), Theme.Radius);
		}
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		base.OnMouseEnter(e);
		SetHover(true);
	}

	// Leaving the card for one of its own labels is not leaving the card.
	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		SetHover(base.ClientRectangle.Contains(base.PointToClient(Cursor.Position)));
	}

	// And leaving one of its labels for outside the card is leaving it, which the card itself never hears about.
	protected override void OnControlAdded(ControlEventArgs e)
	{
		base.OnControlAdded(e);
		e.Control.MouseLeave += (object? _, EventArgs _) =>
		{
			SetHover(base.ClientRectangle.Contains(base.PointToClient(Cursor.Position)));
		};
	}

	private void SetHover(bool on)
	{
		if (hover != on && base.Cursor == Cursors.Hand)
		{
			hover = on;
			base.Invalidate();
		}
	}

	// A docked card changes size with the window, and its rounded edge has to be redrawn where it now is.
	protected override void OnResize(EventArgs eventargs)
	{
		base.OnResize(eventargs);
		base.Invalidate();
	}
}
