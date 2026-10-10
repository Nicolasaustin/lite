using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LiteSynapse;

internal class TopTabs : Control
{
	public (string text, string glyph)[] Items = Array.Empty<(string, string)>();

	public int Selected;

	public List<int> Order = new List<int>();

	private int hover = -1;

	private int downIdx = -1;

	private int downX;

	private bool dragging;

	public event Action Changed;

	public event Action OrderChanged;

	public TopTabs()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.BackColor = Theme.Top;
	}

	private void Normalize()
	{
		List<int> o = Order.Where((int num) => num >= 0 && num < Items.Length).Distinct().ToList();
		for (int i = 0; i < Items.Length; i++)
		{
			if (!o.Contains(i))
			{
				o.Add(i);
			}
		}
		Order = o;
	}

	// Each tab is its glyph, its caption and some padding. With seven tabs that can be wider than the window
	// at the minimum size, or at a display scale above 100% where the text grows and the window does not -
	// and this row has no wrap and no scroll, so an overflowing tab was simply clipped off the right edge.
	// When the total does not fit, the padding is squeezed down to a floor before anything is cut.
	private float[] Widths()
	{
		Font f = Theme.F(9.5f, (FontStyle)0);
		try
		{
			float[] text = Items.Select(((string text, string glyph) i) =>
				Tx.Measure(i.text.ToUpperInvariant(), f).Width).ToArray();
			const float glyph = 46f;
			const float padding = 18f;
			const float minPadding = 4f;
			float[] w = text.Select((float t) => glyph + t + padding).ToArray();
			float available = base.Width - 8f - 4f * Math.Max(0, Items.Length - 1);
			float total = w.Sum();
			if (available <= 0f || total <= available)
			{
				return w;
			}
			// Take the overflow out of the padding, evenly, no further than the floor.
			float over = total - available;
			float give = Math.Min(over / Math.Max(1, Items.Length), padding - minPadding);
			return text.Select((float t) => glyph + t + padding - give).ToArray();
		}
		finally
		{
			if (f != null)
			{
				((IDisposable)f).Dispose();
			}
		}
	}

	private float[] Lefts(float[] w)
	{
		float[] x = new float[Items.Length];
		float cur = 4f;
		foreach (int i in Order)
		{
			x[i] = cur;
			cur += w[i] + 4f;
		}
		return x;
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0143: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Expected Obj, but got Unknown
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d4: Expected Obj, but got Unknown
		//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ef: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		Normalize();
		float[] w = Widths();
		float[] xs = Lefts(w);
		Font f = Theme.F(9.5f, (FontStyle)0);
		try
		{
			Font gf = Theme.F(8.5f, (FontStyle)1);
			try
			{
				foreach (int i in Order)
				{
					float x = xs[i];
					RectangleF r = new RectangleF(x, 3f, w[i], base.Height - 4);
					bool lifted = dragging && i == downIdx;
					if ((i == Selected) | lifted)
					{
						GraphicsPath p = Theme.Round(r, 3f);
						try
						{
							SolidBrush fb = new SolidBrush(lifted ? Color.FromArgb(40, 40, 40) : Theme.Deep);
							try
							{
								g.FillPath((Brush)(object)fb, p);
								Pen pen = new Pen(Theme.Green, 1.2f);
								try
								{
									g.DrawPath(pen, p);
								}
								finally
								{
									((IDisposable)pen)?.Dispose();
								}
							}
							finally
							{
								((IDisposable)fb)?.Dispose();
							}
						}
						finally
						{
							((IDisposable)p)?.Dispose();
						}
					}
					float cx = x + 24f;
					float cy = (float)base.Height / 2f + 1f;
					SolidBrush gb = new SolidBrush(Theme.Green);
					try
					{
						g.FillEllipse((Brush)(object)gb, cx - 11f, cy - 11f, 22f, 22f);
					}
					finally
					{
						((IDisposable)gb)?.Dispose();
					}
					Tx.Center(g, Items[i].glyph, gf, Color.Black, new RectangleF(cx - 11f, cy - 11f, 22f, 22f));
					Tx.Left(g, Items[i].text.ToUpperInvariant(), f, (i == Selected || i == hover) ? Color.White : Color.FromArgb(205, 205, 205), new RectangleF(x + 42f, 0f, w[i] - 44f, base.Height));
				}
			}
			finally
			{
				((IDisposable)gf)?.Dispose();
			}
		}
		finally
		{
			((IDisposable)f)?.Dispose();
		}
	}

	private int Hit(Point pt)
	{
		Normalize();
		float[] w = Widths();
		float[] xs = Lefts(w);
		for (int i = 0; i < Items.Length; i++)
		{
			if ((float)pt.X >= xs[i] && (float)pt.X <= xs[i] + w[i])
			{
				return i;
			}
		}
		return -1;
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		//IL_0001: Unknown result type (might be due to invalid IL or missing references)
		//IL_000b: Invalid comparison between Unknown and I4
		if ((int)e.Button == 1048576)
		{
			downIdx = Hit(e.Location);
			downX = e.X;
			dragging = false;
			if (downIdx >= 0)
			{
				base.Capture = true;
			}
		}
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		//IL_000d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0017: Unknown result type (might be due to invalid IL or missing references)
		if (downIdx >= 0 && (e.Button & MouseButtons.Left) != 0)
		{
			if (!dragging && Math.Abs(e.X - downX) > 8)
			{
				dragging = true;
				base.Cursor = Cursors.SizeAll;
			}
			if (dragging)
			{
				Normalize();
				float[] w = Widths();
				float[] xs = Lefts(w);
				int cur = Order.IndexOf(downIdx);
				int target = cur;
				for (int k = 0; k < Order.Count; k++)
				{
					int i = Order[k];
					if ((float)e.X >= xs[i] && (float)e.X <= xs[i] + w[i])
					{
						target = k;
						break;
					}
				}
				if (e.X < 4)
				{
					target = 0;
				}
				else
				{
					float num = e.X;
					List<int> order = Order;
					float num2 = xs[order[order.Count - 1]];
					List<int> order2 = Order;
					if (num > num2 + w[order2[order2.Count - 1]])
					{
						target = Order.Count - 1;
					}
				}
				if (target != cur)
				{
					Order.RemoveAt(cur);
					Order.Insert(target, downIdx);
				}
				base.Invalidate();
				return;
			}
		}
		int h = Hit(e.Location);
		if (h != hover)
		{
			hover = h;
			base.Invalidate();
		}
	}

	protected override void OnMouseUp(MouseEventArgs e)
	{
		bool wasDrag = dragging;
		int idx = downIdx;
		downIdx = -1;
		dragging = false;
		base.Capture = false;
		base.Cursor = Cursors.Hand;
		if (idx >= 0)
		{
			if (wasDrag)
			{
				base.Invalidate();
				OrderChanged?.Invoke();
			}
			else if (Hit(e.Location) == idx && idx != Selected)
			{
				Selected = idx;
				base.Invalidate();
				Changed?.Invoke();
			}
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		if (!dragging)
		{
			hover = -1;
			base.Invalidate();
		}
	}
}
