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
		using (SolidBrush qb = new SolidBrush(Color.FromArgb(140, 140, 140)))
		{
			g.FillEllipse(qb, base.Width - 28, 10, 14, 14);
		}
		Tx.Center(g, "?", Theme.P(8f, (FontStyle)1), Color.Black, new RectangleF(base.Width - 28, 10f, 14f, 14f));
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
