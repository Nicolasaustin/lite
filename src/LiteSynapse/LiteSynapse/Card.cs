using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

internal class Card : Panel
{
	public string Title = "";

	public bool Muted;

	public Card()
	{
		base.DoubleBuffered = true;
		base.BackColor = Theme.Card;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		Font f = Theme.F(11f, (FontStyle)0);
		try
		{
			Font q = Theme.F(8f, (FontStyle)1);
			try
			{
				Tx.Left(g, Title.ToUpperInvariant(), f, Muted ? Theme.GreenDim : Theme.Green, new RectangleF(40f, 34f, base.Width - 90, 24f));
				SolidBrush qb = new SolidBrush(Color.FromArgb(140, 140, 140));
				try
				{
					g.FillEllipse((Brush)(object)qb, base.Width - 28, 10, 14, 14);
					Tx.Center(g, "?", q, Color.Black, new RectangleF(base.Width - 28, 10f, 14f, 14f));
				}
				finally
				{
					((IDisposable)qb)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)q)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)f)?.Dispose();
		}
	}
}
