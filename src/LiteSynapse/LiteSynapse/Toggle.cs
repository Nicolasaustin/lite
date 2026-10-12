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
		// Reachable from the keyboard: a one-handed player may drive the whole window from the keyboard.
		base.TabStop = true;
		SetStyle(ControlStyles.Selectable, value: true);
	}

	// Space/Enter must reach OnKeyDown rather than being swallowed as dialog navigation.
	protected override bool IsInputKey(Keys keyData)
	{
		if (keyData == Keys.Space || keyData == Keys.Enter)
		{
			return true;
		}
		return base.IsInputKey(keyData);
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
		// Keyboard focus ring: a faint accent outline around the whole switch, only when it has focus.
		if (base.Focused && base.Enabled)
		{
			using GraphicsPath ring = Theme.Round(new RectangleF(0.5f, 1f, base.Width - 2, base.Height - 3), (float)(base.Height - 3) / 2f);
			using Pen rp = new Pen(Theme.Green, 1.4f);
			g.DrawPath(rp, ring);
		}
	}

	protected override void OnEnabledChanged(EventArgs e)
	{
		base.OnEnabledChanged(e);
		base.Cursor = base.Enabled ? Cursors.Hand : Cursors.Default;
		base.Invalidate();
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

	// Act on key-UP, not key-down: OnKeyDown repeats under Windows auto-repeat (~30/s on a held key), which would
	// machine-gun CheckedChanged - and for some switches that means repeated onboard HID writes, Task Scheduler
	// churn or engine reloads. Key-up fires once per physical press, matching a standard checkbox.
	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (base.Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
		{
			e.Handled = true;
		}
	}

	protected override void OnKeyUp(KeyEventArgs e)
	{
		base.OnKeyUp(e);
		if (base.Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
		{
			Checked = !Checked;
			e.Handled = true;
		}
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		if (base.Enabled)
		{
			if (base.CanFocus)
			{
				Focus();
			}
			Checked = !Checked;
		}
	}
}
