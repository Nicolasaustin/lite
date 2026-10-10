using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class Theme
{
	public static readonly Color Top = Color.FromArgb(13, 13, 13);

	public static readonly Color Bg = Color.FromArgb(32, 32, 32);

	public static readonly Color Panel2 = Color.FromArgb(45, 45, 45);

	public static readonly Color Deep = Color.FromArgb(22, 22, 22);

	public static readonly Color Card = Color.FromArgb(22, 22, 22);

	public static readonly Color Line = Color.FromArgb(70, 70, 70);

	// Hairline edge around cards and the window: just enough to separate a surface from the one behind it.
	public static readonly Color Edge = Color.FromArgb(50, 50, 54);

	// Resting fill of a button: one step up from the surface it sits on.
	public static readonly Color Raised = Color.FromArgb(38, 38, 42);

	// Quiet fill for a control under the pointer.
	public static readonly Color Hover = Color.FromArgb(50, 50, 55);

	// Corner radius of a card.
	public const float Radius = 10f;

	public static readonly Color Text = Color.FromArgb(240, 240, 240);

	public static readonly Color Dim = Color.FromArgb(150, 150, 150);

	public static readonly Color Green = Color.FromArgb(68, 214, 44);

	public static readonly Color GreenDim = Color.FromArgb(46, 112, 36);

	public static readonly Color GreenDark = Color.FromArgb(34, 107, 22);

	public const string Face = "Segoe UI";

	private static TextureBrush dots;

	// A new font the caller owns: for a control's Font property, set once when the control is built.
	public static Font F(float size, FontStyle st = (FontStyle)0)
	{
		return new Font("Segoe UI", size, st, (GraphicsUnit)3);
	}

	// Fonts for Paint handlers. Every handler used to make its own Font each time it painted (per list row, per
	// tab, per tray card) and one of them never gave it back, so every repaint was a GDI allocation and some were
	// a GDI leak. These are made once per size and style and kept for the life of the process: never dispose one.
	// Per thread, because the window and the tray paint on different threads and a GDI+ object is not meant to
	// be used by two at once.
	[ThreadStatic]
	private static Dictionary<(string, float, FontStyle), Font> painted;

	public static Font P(float size, FontStyle st = (FontStyle)0)
	{
		return P("Segoe UI", size, st);
	}

	// The same for a symbol or emoji face.
	public static Font P(string face, float size, FontStyle st = (FontStyle)0)
	{
		Dictionary<(string, float, FontStyle), Font> cache = painted ??= new Dictionary<(string, float, FontStyle), Font>();
		if (!cache.TryGetValue((face, size, st), out Font f))
		{
			f = new Font(face, size, st, (GraphicsUnit)3);
			cache[(face, size, st)] = f;
		}
		return f;
	}

	public static void Quality(Graphics g)
	{
		g.SmoothingMode = (SmoothingMode)4;
		g.PixelOffsetMode = (PixelOffsetMode)2;
	}

	public static GraphicsPath Round(RectangleF r, float rad)
	{
		//IL_0000: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Unknown result type (might be due to invalid IL or missing references)
		//IL_007d: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a6: Expected Obj, but got Unknown
		GraphicsPath val = new GraphicsPath();
		float d = Math.Max(1f, rad * 2f);
		val.AddArc(r.X, r.Y, d, d, 180f, 90f);
		val.AddArc(r.Right - d, r.Y, d, d, 270f, 90f);
		val.AddArc(r.Right - d, r.Bottom - d, d, d, 0f, 90f);
		val.AddArc(r.X, r.Bottom - d, d, d, 90f, 90f);
		val.CloseFigure();
		return val;
	}

	public static void DotGrid(Graphics g, Rectangle r)
	{
		//IL_000b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0011: Expected Obj, but got Unknown
		//IL_002e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0034: Expected Obj, but got Unknown
		//IL_0058: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Expected Obj, but got Unknown
		if (dots == null)
		{
			Bitmap bmp = new Bitmap(24, 24);
			Graphics gg = Graphics.FromImage((Image)(object)bmp);
			try
			{
				gg.Clear(Bg);
				SolidBrush b = new SolidBrush(Color.FromArgb(58, 58, 58));
				try
				{
					gg.FillRectangle((Brush)(object)b, 11, 11, 2, 2);
				}
				finally
				{
					((IDisposable)b)?.Dispose();
				}
			}
			finally
			{
				((IDisposable)gg)?.Dispose();
			}
			dots = new TextureBrush((Image)(object)bmp);
		}
		g.FillRectangle((Brush)(object)dots, r);
	}

	public static void StyleGrid(DataGridView g)
	{
		g.EnableHeadersVisualStyles = false;
		g.BorderStyle = (BorderStyle)0;
		g.CellBorderStyle = (DataGridViewCellBorderStyle)8;
		g.BackgroundColor = Bg;
		g.GridColor = Line;
		g.RowHeadersVisible = false;
		g.DefaultCellStyle.BackColor = Bg;
		g.DefaultCellStyle.ForeColor = Text;
		g.DefaultCellStyle.SelectionBackColor = GreenDark;
		g.DefaultCellStyle.SelectionForeColor = Color.White;
		g.DefaultCellStyle.Font = F(10f, (FontStyle)0);
		g.RowTemplate.Height = 32;
		g.ColumnHeadersDefaultCellStyle.BackColor = Top;
		g.ColumnHeadersDefaultCellStyle.ForeColor = Dim;
		g.ColumnHeadersDefaultCellStyle.Font = F(9f, (FontStyle)1);
		g.ColumnHeadersDefaultCellStyle.SelectionBackColor = Top;
		g.ColumnHeadersHeight = 34;
		g.ColumnHeadersBorderStyle = (DataGridViewHeaderBorderStyle)4;
	}
}
