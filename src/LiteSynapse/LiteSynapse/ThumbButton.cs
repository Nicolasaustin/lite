using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class ThumbButton : Control
{
	public bool Selected;

	public bool Side;

	public ThumbButton()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.Size = new Size(50, 50);
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

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (base.Enabled && (e.KeyCode == Keys.Space || e.KeyCode == Keys.Enter))
		{
			e.Handled = true;
		}
	}

	// Fire on key-up, one view-switch per press.
	protected override void OnKeyUp(KeyEventArgs e)
	{
		base.OnKeyUp(e);
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
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Expected Obj, but got Unknown
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_0068: Expected Obj, but got Unknown
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0112: Expected Obj, but got Unknown
		//IL_00a6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		Pen pen = new Pen(Selected ? Theme.Green : Color.FromArgb(95, 95, 95), 2f);
		try
		{
			g.DrawRectangle(pen, 1, 1, base.Width - 3, base.Height - 3);
			// Inset ring (the Selected state already paints the outer border), drawn before the non-Side branch's
			// early return so it shows in both views.
			if (base.Focused && base.Enabled)
			{
				using Pen rp = new Pen(Theme.Green, 1.4f);
				g.DrawRectangle(rp, 4, 4, base.Width - 9, base.Height - 9);
			}
			SolidBrush body = new SolidBrush(Color.FromArgb(70, 70, 70));
			try
			{
				if (!Side)
				{
					GraphicsPath p = Theme.Round(new RectangleF(15f, 8f, 20f, 34f), 9f);
					try
					{
						g.FillPath((Brush)(object)body, p);
						Pen gb = new Pen(Theme.Green, 1.5f);
						try
						{
							g.DrawLine(gb, 25, 13, 25, 22);
							return;
						}
						finally
						{
							((IDisposable)gb)?.Dispose();
						}
					}
					finally
					{
						((IDisposable)p)?.Dispose();
					}
				}
				GraphicsPath p2 = Theme.Round(new RectangleF(17f, 7f, 16f, 36f), 7f);
				try
				{
					g.FillPath((Brush)(object)body, p2);
					SolidBrush gb2 = new SolidBrush(Theme.Green);
					try
					{
						for (int r = 0; r < 4; r++)
						{
							g.FillRectangle((Brush)(object)gb2, 20, 15 + r * 6, 10, 3);
						}
					}
					finally
					{
						((IDisposable)gb2)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)p2)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)body)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)pen)?.Dispose();
		}
	}
}
