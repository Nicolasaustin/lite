using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace LiteSynapse;

internal static class AppIcon
{
	public static Bitmap Make(int s)
	{
		//IL_0007: Unknown result type (might be due to invalid IL or missing references)
		//IL_000d: Expected Obj, but got Unknown
		//IL_002b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0032: Expected Obj, but got Unknown
		//IL_00e3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Unknown result type (might be due to invalid IL or missing references)
		//IL_00f8: Expected Obj, but got Unknown
		Bitmap bmp = new Bitmap(s, s, (PixelFormat)2498570);
		Graphics g = Graphics.FromImage((Image)(object)bmp);
		try
		{
			g.SmoothingMode = (SmoothingMode)4;
			g.Clear(Color.Transparent);
			SolidBrush b = new SolidBrush(Theme.Green);
			try
			{
				g.FillEllipse((Brush)(object)b, 0.5f, 0.5f, (float)(s - 1), (float)(s - 1));
			}
			finally
			{
				((IDisposable)b)?.Dispose();
			}
			float c = (float)s / 2f;
			float u = (float)s / 100f;
			PointF center = new PointF(c, c + 2f * u);
			PointF[] nodes = new PointF[3]
			{
				new PointF(c - 20f * u, c - 17f * u),
				new PointF(c + 20f * u, c - 17f * u),
				new PointF(c, c + 27f * u)
			};
			Pen pen = new Pen(Color.Black, 5.5f * u)
			{
				StartCap = (LineCap)2,
				EndCap = (LineCap)2
			};
			try
			{
				PointF[] array = nodes;
				foreach (PointF n in array)
				{
					g.DrawLine(pen, center, n);
				}
				g.FillEllipse(Brushes.Black, center.X - 10f * u, center.Y - 10f * u, 20f * u, 20f * u);
				array = nodes;
				for (int i = 0; i < array.Length; i++)
				{
					PointF n2 = array[i];
					g.FillEllipse(Brushes.Black, n2.X - 7f * u, n2.Y - 7f * u, 14f * u, 14f * u);
				}
				return bmp;
			}
			finally
			{
				((IDisposable)pen)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)g)?.Dispose();
		}
	}

	public static Icon Icon(int size = 32)
	{
		Bitmap b = Make(size);
		try
		{
			return System.Drawing.Icon.FromHandle(b.GetHicon());
		}
		finally
		{
			((IDisposable)b)?.Dispose();
		}
	}

	public static void WriteIco(string path)
	{
		int[] sizes = new int[6] { 16, 24, 32, 48, 64, 256 };
		byte[][] frames = sizes.Select((int sz) =>
		{
			Bitmap val = Make(sz);
			try
			{
				using MemoryStream memoryStream = new MemoryStream();
				((Image)val).Save((Stream)memoryStream, ImageFormat.Png);
				return memoryStream.ToArray();
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		}).ToArray();
		using BinaryWriter w = new BinaryWriter(File.Create(path));
		w.Write((short)0);
		w.Write((short)1);
		w.Write((short)sizes.Length);
		int offset = 6 + 16 * sizes.Length;
		for (int i = 0; i < sizes.Length; i++)
		{
			w.Write((byte)((sizes[i] < 256) ? ((uint)sizes[i]) : 0u));
			w.Write((byte)((sizes[i] < 256) ? ((uint)sizes[i]) : 0u));
			w.Write((byte)0);
			w.Write((byte)0);
			w.Write((short)1);
			w.Write((short)32);
			w.Write(frames[i].Length);
			w.Write(offset);
			offset += frames[i].Length;
		}
		byte[][] array = frames;
		foreach (byte[] f in array)
		{
			w.Write(f);
		}
	}
}
