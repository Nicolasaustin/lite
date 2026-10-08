using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

internal static class Tx
{
	private const TextFormatFlags Base = (TextFormatFlags)285214752;

	public static SizeF Measure(string s, Font f)
	{
		return TextRenderer.MeasureText(s, f, new Size(int.MaxValue, int.MaxValue), (TextFormatFlags)285214752);
	}

	public static void Center(Graphics g, string s, Font f, Color c, RectangleF r)
	{
		TextRenderer.DrawText((IDeviceContext)(object)g, s, f, Rectangle.Round(r), c, (TextFormatFlags)285214757);
	}

	public static void Left(Graphics g, string s, Font f, Color c, RectangleF r)
	{
		TextRenderer.DrawText((IDeviceContext)(object)g, s, f, Rectangle.Round(r), c, (TextFormatFlags)285247524);
	}

	public static void Right(Graphics g, string s, Font f, Color c, RectangleF r)
	{
		TextRenderer.DrawText((IDeviceContext)(object)g, s, f, Rectangle.Round(r), c, (TextFormatFlags)285214758);
	}

	public static void Wrap(Graphics g, string s, Font f, Color c, RectangleF r)
	{
		TextRenderer.DrawText((IDeviceContext)(object)g, s, f, Rectangle.Round(r), c, (TextFormatFlags)268437520);
	}
}
