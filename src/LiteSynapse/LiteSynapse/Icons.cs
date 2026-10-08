using System;
using System.Drawing;

namespace LiteSynapse;

internal static class Icons
{
	public static void SdCard(Graphics g, float x, float y, float scale, Color body, Color? dot = null)
	{
		//IL_0073: Unknown result type (might be due to invalid IL or missing references)
		//IL_007a: Expected Obj, but got Unknown
		//IL_00ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b4: Expected Obj, but got Unknown
		//IL_0110: Unknown result type (might be due to invalid IL or missing references)
		//IL_0117: Expected Obj, but got Unknown
		//IL_0176: Unknown result type (might be due to invalid IL or missing references)
		//IL_017d: Expected Obj, but got Unknown
		//IL_01ad: Unknown result type (might be due to invalid IL or missing references)
		//IL_01b4: Expected Obj, but got Unknown
		float w = 16f * scale;
		float h = 21f * scale;
		float c = 5f * scale;
		PointF[] pts = new PointF[5]
		{
			new PointF(x, y + c),
			new PointF(x + c, y),
			new PointF(x + w, y),
			new PointF(x + w, y + h),
			new PointF(x, y + h)
		};
		SolidBrush b = new SolidBrush(body);
		try
		{
			g.FillPolygon((Brush)(object)b, pts);
		}
		finally
		{
			((IDisposable)b)?.Dispose();
		}
		Pen pin = new Pen(Color.FromArgb(70, 70, 70), Math.Max(1f, 1.6f * scale));
		try
		{
			for (int i = 0; i < 4; i++)
			{
				float px = x + (3.2f + (float)i * 3.2f) * scale;
				g.DrawLine(pin, px, y + 2.5f * scale, px, y + 8f * scale);
			}
		}
		finally
		{
			((IDisposable)pin)?.Dispose();
		}
		SolidBrush lab = new SolidBrush(Color.FromArgb(70, 70, 70));
		try
		{
			g.FillRectangle((Brush)(object)lab, x + 3f * scale, y + 12f * scale, 10f * scale, 5.5f * scale);
		}
		finally
		{
			((IDisposable)lab)?.Dispose();
		}
		if (!dot.HasValue)
		{
			return;
		}
		Color d = dot.GetValueOrDefault();
		float r = 4.2f * scale;
		SolidBrush ring = new SolidBrush(Color.FromArgb(24, 24, 24));
		try
		{
			g.FillEllipse((Brush)(object)ring, x + w - r * 1.05f, y - r * 1.3f, r * 2.4f, r * 2.4f);
			SolidBrush db = new SolidBrush(d);
			try
			{
				g.FillEllipse((Brush)(object)db, x + w - r * 0.65f, y - r * 0.9f, r * 1.6f, r * 1.6f);
			}
			finally
			{
				((IDisposable)db)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)ring)?.Dispose();
		}
	}
}
