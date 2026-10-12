using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

// Hypershift (a hold-to-shift second layer) is not supported yet, so this is a plain static note, not a control
// that looks interactive. If onboard Hypershift is added later, this becomes a real Standard/Hypershift switch.
internal class ModeSwitch : Control
{
	public ModeSwitch()
	{
		base.DoubleBuffered = true;
		base.Size = new Size(320, 40);
		base.BackColor = Theme.Bg;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		// A small layer glyph, then a dim note - nothing here is clickable.
		using (Pen ip = new Pen(Color.FromArgb(150, 150, 150), 1.4f))
		{
			g.DrawRectangle(ip, 2, 13, 14, 11);
			g.DrawLine(ip, 7, 13, 7, 24);
		}
		Tx.Left(g, "Standard layer  ·  Hypershift not supported yet", Theme.P(10f, (FontStyle)0),
			Color.FromArgb(150, 150, 150), new RectangleF(26f, 10f, base.Width - 28, 20f));
	}
}
