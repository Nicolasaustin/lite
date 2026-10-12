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

	// The tab the keyboard ring is on (an Items index), which may differ from Selected until Space/Enter commits.
	private int focusIdx = -1;

	public event Action Changed;

	public event Action OrderChanged;

	public TopTabs()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.BackColor = Theme.Top;
		base.TabStop = true;
		SetStyle(ControlStyles.Selectable, value: true);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		switch (keyData)
		{
		case Keys.Left:
		case Keys.Right:
		case Keys.Home:
		case Keys.End:
		case Keys.Space:
		case Keys.Enter:
			return true;
		default:
			return base.IsInputKey(keyData);
		}
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		// Several call sites set Selected directly (bypassing Changed); re-syncing here keeps the ring correct.
		focusIdx = Selected;
		base.Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		base.Invalidate();
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		if (!base.Enabled)
		{
			return;
		}
		Normalize();
		if (Order.Count == 0)
		{
			return;
		}
		int fi = (focusIdx >= 0) ? focusIdx : Selected;
		// Left/Right walk the VISUAL order (Order), not the raw Items index, so the ring moves the way it looks.
		int pos = Math.Max(0, Order.IndexOf(fi));
		switch (e.KeyCode)
		{
		case Keys.Left:
			focusIdx = Order[Math.Max(0, pos - 1)];
			break;
		case Keys.Right:
			focusIdx = Order[Math.Min(Order.Count - 1, pos + 1)];
			break;
		case Keys.Home:
			focusIdx = Order[0];
			break;
		case Keys.End:
			focusIdx = Order[Order.Count - 1];
			break;
		case Keys.Space:
		case Keys.Enter:
			if (focusIdx >= 0 && focusIdx != Selected)
			{
				Selected = focusIdx;
				Changed?.Invoke();
			}
			break;
		default:
			return;
		}
		base.Invalidate();
		e.Handled = true;
	}

	// The order list last checked, so a hit test or a paint does not rebuild it with LINQ every time.
	private List<int> normalized;

	private void Normalize()
	{
		if (ReferenceEquals(normalized, Order) && Order.Count == Items.Length)
		{
			return;
		}
		List<int> o = (Order ?? new List<int>()).Where((int num) => num >= 0 && num < Items.Length).Distinct().ToList();
		for (int i = 0; i < Items.Length; i++)
		{
			if (!o.Contains(i))
			{
				o.Add(i);
			}
		}
		Order = o;
		normalized = o;
	}

	// Each tab is its glyph, its caption and some padding. With seven tabs that can be wider than the window
	// at the minimum size, or at a display scale above 100% where the text grows and the window does not -
	// and this row has no wrap and no scroll, so an overflowing tab was simply clipped off the right edge.
	// When the total does not fit, the padding is squeezed down to a floor before anything is cut.
	// Caption widths are measured once per set of tabs and the layout once per width, not on every mouse move
	// (a 1000 Hz mouse over the bar meant thousands of text measurements a second).
	private (string text, string glyph)[] measuredFor;

	private float[] textWidths = Array.Empty<float>();

	private float[] layout;

	private float layoutWidth = -1f;

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		layout = null;
		base.Invalidate();
	}

	private float[] Widths()
	{
		if (!ReferenceEquals(measuredFor, Items))
		{
			Font tf = Theme.P(9.5f, (FontStyle)0);
			textWidths = Items.Select(((string text, string glyph) i) => Tx.Measure(i.text.ToUpperInvariant(), tf).Width).ToArray();
			measuredFor = Items;
			layout = null;
		}
		if (layout != null && layoutWidth == base.Width)
		{
			return layout;
		}
		layout = Compute();
		layoutWidth = base.Width;
		return layout;
	}

	private float[] Compute()
	{
		Font f = Theme.P(9.5f, (FontStyle)0);
		try
		{
			float[] text = textWidths;
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
		Font f = Theme.P(9.5f, (FontStyle)0);
		try
		{
			Font gf = Theme.P(8.5f, (FontStyle)1);
			try
			{
				foreach (int i in Order)
				{
					float x = xs[i];
					RectangleF r = new RectangleF(x, 3f, w[i], base.Height - 4);
					bool lifted = dragging && i == downIdx;
					bool sel = i == Selected;
					// The selected tab is a soft raised surface with a short accent bar along its bottom, the way
					// Windows 11 marks the current page; the tab under the pointer gets the surface alone.
					if (sel || lifted || i == hover)
					{
						using GraphicsPath p = Theme.Round(r, 6f);
						using SolidBrush fb = new SolidBrush(lifted ? Color.FromArgb(40, 40, 40) : (sel ? Theme.Deep : Theme.Hover));
						g.FillPath(fb, p);
					}
					if (sel && !lifted)
					{
						float bw = Math.Min(28f, w[i] - 16f);
						using GraphicsPath bar = Theme.Round(new RectangleF(x + (w[i] - bw) / 2f, base.Height - 5f, bw, 3f), 1.5f);
						using SolidBrush gb2 = new SolidBrush(Theme.Green);
						g.FillPath(gb2, bar);
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
				if (base.Focused && base.Enabled)
				{
					int fi = (focusIdx >= 0) ? focusIdx : Selected;
					if (fi >= 0 && fi < Items.Length)
					{
						Theme.FocusRing(g, new RectangleF(xs[fi], 3f, w[fi], base.Height - 4), 6f);
					}
				}
			}
			finally
			{
			}
		}
		finally
		{
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
				focusIdx = downIdx;
				if (base.CanFocus)
				{
					Focus();
				}
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
					base.Invalidate();
				}
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
				focusIdx = idx;
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
