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
		//IL_006c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Expected Obj, but got Unknown
		//IL_00c3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ca: Expected Obj, but got Unknown
		Theme.Quality(e.Graphics);
		e.Graphics.Clear(base.BackColor);
		GraphicsPath path = Theme.Round(new RectangleF(1f, 2f, base.Width - 3, base.Height - 5), (float)(base.Height - 5) / 2f);
		try
		{
			SolidBrush fill = new SolidBrush(on ? Theme.Green : Color.FromArgb(70, 70, 70));
			try
			{
				e.Graphics.FillPath((Brush)(object)fill, path);
				float d = base.Height - 9;
				float x = (on ? ((float)base.Width - d - 5f) : 4f);
				SolidBrush knob = new SolidBrush(on ? Color.Black : Color.FromArgb(25, 25, 25));
				try
				{
					e.Graphics.FillEllipse((Brush)(object)knob, x, 4f, d, d);
				}
				finally
				{
					((IDisposable)knob)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)fill)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)path)?.Dispose();
		}
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		if (base.Enabled)
		{
			Checked = !Checked;
		}
	}
}
