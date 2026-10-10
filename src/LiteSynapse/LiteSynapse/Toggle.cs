using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class Toggle : Control
{
	private bool on;

	public bool Checked
	{
		get
		{
			return on;
		}
		set
		{
			if (on != value)
			{
				on = value;
				base.Invalidate();
				CheckedChanged?.Invoke();
			}
		}
	}

	public event Action CheckedChanged;

	public Toggle()
	{
		base.DoubleBuffered = true;
		base.Size = new Size(40, 22);
		base.Cursor = Cursors.Hand;
		base.BackColor = Theme.Bg;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		// Windows 11 style: off is an outlined track with a grey knob, on is the accent with a dark knob.
		using GraphicsPath path = Theme.Round(new RectangleF(1f, 2f, base.Width - 3, base.Height - 5), (float)(base.Height - 5) / 2f);
		// A switch that cannot be used must not look like one that can: muted, whatever its position.
		Color onColor = base.Enabled ? Theme.Green : Theme.GreenDark;
		using (SolidBrush fill = new SolidBrush(on ? onColor : Theme.Raised))
		{
			g.FillPath(fill, path);
		}
		if (!on)
		{
			using Pen edge = new Pen(Color.FromArgb(110, 110, 116), 1f);
			g.DrawPath(edge, path);
		}
		float d = base.Height - 11;
		float x = on ? ((float)base.Width - d - 6f) : 5f;
		using SolidBrush knob = new SolidBrush(on ? Color.Black : (base.Enabled ? Color.FromArgb(200, 200, 205) : Color.FromArgb(110, 110, 116)));
		g.FillEllipse(knob, x, 5f, d, d);
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		base.Cursor = base.Enabled ? Cursors.Hand : Cursors.Default;
		base.Invalidate();
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		if (base.Enabled)
		{
			Checked = !Checked;
		}
	}
}
