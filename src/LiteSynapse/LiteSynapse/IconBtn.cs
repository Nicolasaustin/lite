using System;
using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

internal class IconBtn : Control
{
	public string Glyph = "";

	public bool Sd;

	public Color? SdDot;

	private bool hover;

	// One tooltip per button, disposed with it. A new ToolTip per set left a tooltip window (and the row it
	// pointed at) alive until the main window closed, and the games list rebuilds its rows on every keystroke
	// in the search box.
	private ToolTip tip;

	public string Tip
	{
		set
		{
			tip ??= new ToolTip();
			tip.SetToolTip(this, value);
		}
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing)
		{
			tip?.Dispose();
			tip = null;
		}
		base.Dispose(disposing);
	}

	public IconBtn()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.Size = new Size(34, 34);
		base.BackColor = Color.FromArgb(24, 24, 24);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected Obj, but got Unknown
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		if (hover)
		{
			SolidBrush b = new SolidBrush(Color.FromArgb(48, 48, 48));
			try
			{
				g.FillEllipse((Brush)(object)b, 1, 1, base.Width - 3, base.Height - 3);
			}
			finally
			{
				((IDisposable)b)?.Dispose();
			}
		}
		if (Sd)
		{
			Icons.SdCard(g, (float)base.Width / 2f - 8f, (float)base.Height / 2f - 9f, 1f, hover ? Color.White : Color.FromArgb(215, 215, 215), SdDot);
			return;
		}
		Tx.Center(g, Glyph, Theme.P("Segoe UI Symbol", 11f), hover ? Theme.Green : Color.FromArgb(190, 190, 190), new RectangleF(0f, 0f, base.Width, base.Height));
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
}
