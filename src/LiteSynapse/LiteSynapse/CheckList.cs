using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LiteSynapse;

// A scrolling list of things the user can tick, used by every page that offers Windows changes.
//
// It is drawn by hand rather than built out of CheckBox and Label controls. The Windows settings page alone
// has thirty-odd rows, the services page over a hundred, and a control per row would mean several hundred
// window handles, each with its own paint and layout - on the one PC where this app must not cost anything.
// One control that paints only the rows you can see costs nothing when it is closed and very little when it
// is open.
//
// Rows come in two kinds: a header (a group title, not tickable) and an item. An item carries its current
// state, so the user can see what is already on before deciding what to change.
internal sealed class CheckList : Control
{
	internal sealed class Row
	{
		// What Apply gets back for this row.
		public string Id = "";

		// The program's file, when the row has one. Kept apart from Id because Id is the key a change is made
		// with (a Run value name, a service name, a tweak id) and a path is not interchangeable with it.
		public string Path = "";

		public string Title = "";

		// What it does, wrapped under the title.
		public string Desc = "";

		// What it gives up. Shown under Desc in a warmer colour when it is set.
		public string Cost = "";

		// on | off | part | "" (no badge). "part" is a setting another tool applied only halfway.
		public string State = "";

		// Small text on the right: where the setting comes from, or what it costs you.
		public string Badge = "";

		// safe | caution | risky. Colours the badge.
		public string Risk = Tweaks.Safe;

		public bool Checked;

		// False for a row that cannot be changed right now (shown greyed, with no checkbox).
		public bool Can = true;

		// A group title rather than a thing to tick.
		public bool Header;

		// Laid out by Measure().
		internal int Y;

		internal int H;
	}

	private const int Pad = 14;

	private const int BoxSize = 17;

	private const int Indent = 40;

	private readonly VScrollBar bar = new VScrollBar();

	private List<Row> items = new List<Row>();

	private int hover = -1;

	private int total;

	// Raised when the user ticks or unticks something.
	public event Action Changed;

	public CheckList()
	{
		DoubleBuffered = true;
		BackColor = Theme.Bg;
		// Focusable so the mouse wheel reaches it, but out of the tab order: it is a list, not a field.
		SetStyle(ControlStyles.Selectable, value: true);
		TabStop = false;
		bar.Dock = DockStyle.Right;
		bar.Width = 14;
		bar.SmallChange = 24;
		bar.LargeChange = 120;
		bar.ValueChanged += (object _1, EventArgs _2) => Invalidate();
		Controls.Add(bar);
		DarkScroll.Apply(bar);
	}

	public List<Row> Items
	{
		get
		{
			return items;
		}
		set
		{
			items = value ?? new List<Row>();
			// The row under the cursor is gone with the old list; keeping its index would highlight whatever
			// now sits at that position.
			hover = -1;
			bar.Value = 0;
			Relayout();
		}
	}

	// The rows the user has ticked.
	public IEnumerable<Row> Ticked => items.Where((Row r) => !r.Header && r.Can && r.Checked);

	public int TickedCount => Ticked.Count();

	// Forgets every tick, without losing the list.
	public void ClearTicks()
	{
		foreach (Row r in items)
		{
			r.Checked = false;
		}
		Invalidate();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		Relayout();
	}

	// Row heights depend on how the description wraps, so they are measured once per layout and kept.
	private void Relayout()
	{
		int width = Math.Max(120, Width - bar.Width - Pad * 2 - Indent);
		int y = 0;
		Font title = Theme.P(10.5f);
		Font small = Theme.P(9f);
		try
		{
			int lineHeight = 6 + (int)Tx.Measure("X", title).Height;
			foreach (Row r in items)
			{
				r.Y = y;
				if (r.Header)
				{
					r.H = 38 + (r.Desc.Length > 0 ? Lines(r.Desc, small, width + Indent) : 0);
				}
				else
				{
					int h = lineHeight;
					if (r.Desc.Length > 0)
					{
						h += Lines(r.Desc, small, width);
					}
					if (r.Cost.Length > 0)
					{
						h += Lines(r.Cost, small, width) + 2;
					}
					r.H = h + 12;
				}
				y += r.H;
			}
		}
		finally
		{
		}
		total = y;
		int view = Math.Max(1, Height);
		int maxScroll = Math.Max(0, total - view);
		bar.Visible = total > view;
		// A VScrollBar's highest reachable Value is Maximum - LargeChange + 1. Setting Maximum from the
		// LargeChange *getter* does not work: the getter returns Math.Min(whatLWeSet, Maximum - Minimum + 1),
		// so it is clamped by the Maximum we are about to replace. Right after construction, or after a layout
		// pass with an empty list, it reads 1 instead of 120 - and the list then stopped about two rows short
		// of the bottom until the window was resized.
		//
		// Setting both from the content instead has no such dependency: Maximum = total - 1 with
		// LargeChange = view gives a highest Value of exactly total - view, which is the last row.
		bar.Maximum = Math.Max(0, total - 1);
		bar.LargeChange = Math.Max(1, view);
		bar.SmallChange = 24;
		if (bar.Value > maxScroll)
		{
			bar.Value = maxScroll;
		}
		Invalidate();
	}

	private static int Lines(string text, Font f, int width)
	{
		return TextRenderer.MeasureText(text, f, new Size(Math.Max(40, width), int.MaxValue),
			TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
	}

	private int Scroll => bar.Visible ? bar.Value : 0;

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(Theme.Bg);
		int view = Height;
		int off = Scroll;
		int width = Width - (bar.Visible ? bar.Width : 0);
		Font title = Theme.P(10.5f);
		Font small = Theme.P(9f);
		Font head = Theme.P(9f, FontStyle.Bold);
		Font pill = Theme.P(8f, FontStyle.Bold);
		try
		{
			for (int i = 0; i < items.Count; i++)
			{
				Row r = items[i];
				int y = r.Y - off;
				// Only what is on screen is drawn: a hundred rows off-screen cost one comparison each.
				if (y + r.H < 0)
				{
					continue;
				}
				if (y > view)
				{
					break;
				}
				if (r.Header)
				{
					PaintHeader(g, r, y, width, head, small);
					continue;
				}
				PaintItem(g, r, y, width, i == hover, title, small, pill);
			}
		}
		finally
		{
		}
	}

	private void PaintHeader(Graphics g, Row r, int y, int width, Font head, Font small)
	{
		Tx.Left(g, r.Title.ToUpperInvariant(), head, Theme.Green, new RectangleF(Pad, y + 12f, width - Pad * 2, 18f));
		if (r.Desc.Length > 0)
		{
			Tx.Wrap(g, r.Desc, small, Theme.Dim, new RectangleF(Pad, y + 32f, width - Pad * 2, r.H - 36));
		}
		using Pen p = new Pen(Color.FromArgb(52, 52, 52));
		g.DrawLine(p, Pad, y + r.H - 4, width - Pad, y + r.H - 4);
	}

	private void PaintItem(Graphics g, Row r, int y, int width, bool isHover, Font title, Font small, Font pill)
	{
		if (isHover && r.Can)
		{
			using GraphicsPath hp = Theme.Round(new RectangleF(4f, y + 1f, width - 8, r.H - 2), 8f);
			using SolidBrush hb = new SolidBrush(Color.FromArgb(40, 40, 44));
			g.FillPath(hb, hp);
		}
		// Checkbox: rounded, and a filled accent square with a tick when checked, as Windows 11 draws one.
		int bx = Pad + 4;
		int by = y + 8;
		if (r.Can)
		{
			using GraphicsPath box = Theme.Round(new RectangleF(bx, by, BoxSize, BoxSize), 4f);
			if (r.Checked)
			{
				using (SolidBrush fill = new SolidBrush(Theme.Green))
				{
					g.FillPath(fill, box);
				}
				using Pen tick = new Pen(Color.Black, 2f);
				tick.StartCap = LineCap.Round;
				tick.EndCap = LineCap.Round;
				tick.LineJoin = LineJoin.Round;
				g.DrawLines(tick, new PointF[3]
				{
					new PointF(bx + 4.5f, by + 9f),
					new PointF(bx + 7.5f, by + 12f),
					new PointF(bx + 12.5f, by + 5.5f)
				});
			}
			else
			{
				using Pen border = new Pen(Color.FromArgb(120, 120, 126), 1.2f);
				g.DrawPath(border, box);
			}
		}
		int tx = Pad + Indent - 10;
		int right = width - Pad;
		// The state badge sits to the right of the title, so "already on" is readable at a glance.
		float titleWidth = Tx.Measure(r.Title, title).Width;
		Tx.Left(g, r.Title, title, r.Can ? Theme.Text : Theme.Dim, new RectangleF(tx, y + 5f, Math.Max(40f, right - tx - 140), 20f));
		float badgeX = tx + Math.Min(titleWidth + 10f, Math.Max(40f, right - tx - 150f));
		if (r.State.Length > 0)
		{
			PaintPill(g, StateText(r.State), StateColor(r.State), pill, badgeX, y + 7f);
		}
		if (r.Badge.Length > 0)
		{
			Tx.Right(g, r.Badge, small, RiskColor(r.Risk), new RectangleF(right - 190, y + 6f, 190f, 18f));
		}
		int ty = y + 5 + (int)Tx.Measure("X", title).Height;
		int descWidth = right - tx;
		if (r.Desc.Length > 0)
		{
			int h = Lines(r.Desc, small, descWidth);
			Tx.Wrap(g, r.Desc, small, Theme.Dim, new RectangleF(tx, ty, descWidth, h));
			ty += h;
		}
		if (r.Cost.Length > 0)
		{
			int h2 = Lines(r.Cost, small, descWidth);
			Tx.Wrap(g, r.Cost, small, CostColor(r.Risk), new RectangleF(tx, ty + 2, descWidth, h2));
		}
	}

	private static void PaintPill(Graphics g, string text, Color colour, Font f, float x, float y)
	{
		float w = Tx.Measure(text, f).Width + 14f;
		using (GraphicsPath path = Theme.Round(new RectangleF(x, y, w, 16f), 8f))
		{
			using SolidBrush b = new SolidBrush(Color.FromArgb(40, colour.R, colour.G, colour.B));
			g.FillPath(b, path);
			using Pen p = new Pen(colour, 1f);
			g.DrawPath(p, path);
		}
		Tx.Center(g, text, f, colour, new RectangleF(x, y, w, 16f));
	}

	private static string StateText(string state)
	{
		switch (state)
		{
		case "on":
			return "ON";
		case "off":
			return "OFF";
		case "part":
			return "HALF SET";
		case "other":
			return "SET BY SOMETHING ELSE";
		case "auto":
			return "AUTOMATIC";
		case "manual":
			return "MANUAL";
		case "disabled":
			return "OFF";
		case "running":
			return "RUNNING";
		default:
			return state.ToUpperInvariant();
		}
	}

	private static Color StateColor(string state)
	{
		switch (state)
		{
		case "on":
		case "auto":
		case "running":
			return Theme.Green;
		case "part":
		case "other":
			return Color.FromArgb(235, 180, 60);
		default:
			return Color.FromArgb(140, 140, 140);
		}
	}

	private static Color RiskColor(string risk)
	{
		switch (risk)
		{
		case Tweaks.Risky:
			return Color.FromArgb(235, 90, 90);
		case Tweaks.Caution:
			return Color.FromArgb(235, 180, 60);
		default:
			return Theme.Dim;
		}
	}

	private static Color CostColor(string risk)
	{
		return (risk == Tweaks.Risky) ? Color.FromArgb(220, 120, 120) : Color.FromArgb(175, 160, 120);
	}

	// ---- Input ----------------------------------------------------------------------------------------

	private int RowAt(int y)
	{
		int at = y + Scroll;
		for (int i = 0; i < items.Count; i++)
		{
			if (at >= items[i].Y && at < items[i].Y + items[i].H)
			{
				return i;
			}
		}
		return -1;
	}

	protected override void OnMouseMove(MouseEventArgs e)
	{
		base.OnMouseMove(e);
		int at = RowAt(e.Y);
		if (at >= 0 && items[at].Header)
		{
			at = -1;
		}
		if (at != hover)
		{
			hover = at;
			Cursor = (at >= 0 && items[at].Can) ? Cursors.Hand : Cursors.Default;
			Invalidate();
		}
	}

	protected override void OnMouseEnter(EventArgs e)
	{
		base.OnMouseEnter(e);
		// Windows delivers WM_MOUSEWHEEL to the focused window, and WinForms bubbles an unhandled wheel
		// message up the parent chain rather than down to the control under the pointer. Without this the
		// wheel scrolls nothing after the first tab switch, because ShowPage clears the host's controls and
		// takes the focused one with them.
		if (!Focused && CanFocus)
		{
			Focus();
		}
	}

	protected override void OnMouseLeave(EventArgs e)
	{
		base.OnMouseLeave(e);
		if (hover != -1)
		{
			hover = -1;
			Invalidate();
		}
	}

	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		// The wheel only reaches the control that has focus, and nothing else on these pages wants it.
		if (!Focused && CanFocus)
		{
			Focus();
		}
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		int at = RowAt(e.Y);
		if (at < 0 || items[at].Header || !items[at].Can)
		{
			return;
		}
		items[at].Checked = !items[at].Checked;
		Invalidate();
		try
		{
			Changed?.Invoke();
		}
		catch (Exception ex)
		{
			Cfg.Log("checklist: " + ex.Message);
		}
	}

	protected override void OnMouseWheel(MouseEventArgs e)
	{
		base.OnMouseWheel(e);
		if (!bar.Visible)
		{
			return;
		}
		int step = -(e.Delta / 120) * 60;
		// The same reachable maximum the scrollbar itself enforces, worked out from the content rather than
		// from the clamped LargeChange getter.
		int maxScroll = Math.Max(0, total - Math.Max(1, Height));
		bar.Value = Math.Max(bar.Minimum, Math.Min(maxScroll, bar.Value + step));
	}
}
