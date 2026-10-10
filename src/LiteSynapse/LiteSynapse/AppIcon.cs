using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace LiteSynapse;

// The app's logo: the round "RzR optimizer" badge, as PNG frames embedded from src/LiteSynapse/icon (built by
// tools/icon/make-icon.py; app.ico holds the same frames for the exe, the tray and the installer). Small sizes are
// a simplified drawing of the logo, 128 and 256 the original artwork.
internal static class AppIcon
{
	public static readonly int[] Sizes = new int[9] { 16, 20, 24, 32, 40, 48, 64, 128, 256 };

	// The logo at s x s pixels: the embedded frame of that size, or the next larger one scaled down.
	public static Bitmap Make(int s)
	{
		int pick = Sizes.FirstOrDefault((int x) => x >= s);
		if (pick == 0)
		{
			pick = Sizes[^1];
		}
		using Stream stream = typeof(AppIcon).Assembly.GetManifestResourceStream($"LiteSynapse.icon-{pick}.png");
		if (stream == null)
		{
			return Draw(s);
		}
		using Bitmap frame = new Bitmap(stream);
		Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.Clear(Color.Transparent);
		g.InterpolationMode = InterpolationMode.HighQualityBicubic;
		g.PixelOffsetMode = PixelOffsetMode.HighQuality;
		g.CompositingQuality = CompositingQuality.HighQuality;
		g.DrawImage(frame, 0, 0, s, s);
		return bmp;
	}

	private static Icon cached;

	[System.Runtime.InteropServices.DllImport("user32.dll")]
	private static extern bool DestroyIcon(nint h);

	// One icon for the process. Icon.FromHandle does not own the handle, so the old version leaked an HICON
	// (and its two bitmaps) every time a window was built; this one makes an owning copy and frees the
	// original. Forms never dispose Form.Icon, so sharing it is safe.
	public static Icon Icon(int size = 32)
	{
		if (cached != null)
		{
			return cached;
		}
		using Bitmap b = Make(size);
		nint h = b.GetHicon();
		try
		{
			using Icon temp = System.Drawing.Icon.FromHandle(h);
			cached = (Icon)temp.Clone();
		}
		finally
		{
			DestroyIcon(h);
		}
		return cached;
	}

	// Writes app.ico from the embedded frames (PNG-compressed, Windows Vista and later).
	public static void WriteIco(string path)
	{
		byte[][] frames = Sizes.Select((int sz) =>
		{
			using Bitmap b = Make(sz);
			using MemoryStream ms = new MemoryStream();
			b.Save(ms, ImageFormat.Png);
			return ms.ToArray();
		}).ToArray();
		using BinaryWriter w = new BinaryWriter(File.Create(path));
		w.Write((short)0);
		w.Write((short)1);
		w.Write((short)Sizes.Length);
		int offset = 6 + 16 * Sizes.Length;
		for (int i = 0; i < Sizes.Length; i++)
		{
			w.Write((byte)((Sizes[i] < 256) ? Sizes[i] : 0));
			w.Write((byte)((Sizes[i] < 256) ? Sizes[i] : 0));
			w.Write((byte)0);
			w.Write((byte)0);
			w.Write((short)1);
			w.Write((short)32);
			w.Write(frames[i].Length);
			w.Write(offset);
			offset += frames[i].Length;
		}
		foreach (byte[] f in frames)
		{
			w.Write(f);
		}
	}

	// Fallback if the frames are missing from the build: the logo's look (dark disc, green ring, check mark).
	private static Bitmap Draw(int s)
	{
		Bitmap bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
		using Graphics g = Graphics.FromImage(bmp);
		g.SmoothingMode = SmoothingMode.AntiAlias;
		g.Clear(Color.Transparent);
		Color green = Color.FromArgb(0x70, 0xE0, 0x60);
		using (SolidBrush disc = new SolidBrush(Color.FromArgb(0x21, 0x2A, 0x2E)))
		{
			g.FillEllipse(disc, 0.5f, 0.5f, s - 1f, s - 1f);
		}
		float u = s / 100f;
		using Pen ring = new Pen(green, Math.Max(1f, 6f * u));
		g.DrawEllipse(ring, 12f * u, 12f * u, 76f * u, 76f * u);
		using Pen check = new Pen(green, Math.Max(1.2f, 8f * u))
		{
			StartCap = LineCap.Round,
			EndCap = LineCap.Round,
			LineJoin = LineJoin.Round
		};
		g.DrawLines(check, new PointF[3]
		{
			new PointF(33f * u, 51f * u),
			new PointF(45f * u, 63f * u),
			new PointF(68f * u, 39f * u)
		});
		return bmp;
	}
}
