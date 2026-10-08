using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class ModeSwitch : Control
{
	public ModeSwitch()
	{
		base.DoubleBuffered = true;
		base.Size = new Size(280, 40);
		base.BackColor = Theme.Bg;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0029: Unknown result type (might be due to invalid IL or missing references)
		//IL_002f: Expected Obj, but got Unknown
		//IL_0084: Unknown result type (might be due to invalid IL or missing references)
		//IL_008b: Expected Obj, but got Unknown
		//IL_011f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0126: Expected Obj, but got Unknown
		//IL_01c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_01ca: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		Pen pen = new Pen(Color.FromArgb(120, 120, 120), 1.2f);
		try
		{
			GraphicsPath p = Theme.Round(new RectangleF(0.5f, 4.5f, 38f, 30f), 15f);
			try
			{
				g.DrawPath(pen, p);
			}
			finally
			{
				((IDisposable)p)?.Dispose();
			}
			Pen ip = new Pen(Color.FromArgb(220, 220, 220), 1.4f);
			try
			{
				g.DrawRectangle(ip, 12, 14, 14, 11);
				g.DrawLine(ip, 17, 14, 17, 25);
			}
			finally
			{
				((IDisposable)ip)?.Dispose();
			}
			GraphicsPath p2 = Theme.Round(new RectangleF(50.5f, 1.5f, 218f, 36f), 18f);
			try
			{
				g.DrawPath(pen, p2);
			}
			finally
			{
				((IDisposable)p2)?.Dispose();
			}
			GraphicsPath p3 = Theme.Round(new RectangleF(54f, 5f, 76f, 29f), 14.5f);
			try
			{
				SolidBrush fb = new SolidBrush(Theme.Green);
				try
				{
					g.FillPath((Brush)(object)fb, p3);
				}
				finally
				{
					((IDisposable)fb)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)p3)?.Dispose();
			}
			Font f = Theme.F(10f, (FontStyle)1);
			try
			{
				Tx.Center(g, "Standard", f, Color.Black, new RectangleF(54f, 5f, 76f, 29f));
				Tx.Center(g, "Hypershift", f, Color.FromArgb(150, 150, 150), new RectangleF(130f, 5f, 100f, 29f));
				SolidBrush qb = new SolidBrush(Color.FromArgb(110, 110, 110));
				try
				{
					g.FillEllipse((Brush)(object)qb, 240, 11, 17, 17);
					Font qf = Theme.F(8f, (FontStyle)1);
					try
					{
						Tx.Center(g, "?", qf, Color.Black, new RectangleF(240f, 11f, 17f, 17f));
					}
					finally
					{
						((IDisposable)qf)?.Dispose();
					}
				}
				finally
				{
					((IDisposable)qb)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)f)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)pen)?.Dispose();
		}
	}
}
