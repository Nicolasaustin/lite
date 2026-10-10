using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace LiteSynapse;

// The scrolling list every Performance page is made of. Each row says two things in one badge on the left -
// whether the thing is ON or OFF, and whether that is HELPING or HURTING performance - and has a switch on
// the right showing the side it is on. Flipping the switch marks a change, which the row then spells out
// ("will turn OFF when you press Apply - that helps performance"); nothing is changed until the page's Apply.
//
// It is drawn by hand rather than built out of controls. The services page alone has over a hundred rows, and
// a control per row would mean several hundred window handles, each with its own paint and layout - on the one
// PC where this app must not cost anything. One control that paints only the rows you can see costs nothing
// when it is closed and very little when it is open. Text is measured once per layout, not per paint.
internal sealed class SwitchList : Control
{
	internal sealed class Row
	{
		// "<kind>:<what>" - what Apply gets back for this row.
		public string Id = "";

		// The program's file, when the row has one (start-up entries), for the Defender scan.
		public string Path = "";

		// The thing itself, named without a direction: "Pointer acceleration", not "Pointer acceleration off".
		public string Title = "";

		// What it does, wrapped under the title.
		public string Desc = "";

		// What switching it to the helping side gives up. Shown in a warmer colour.
		public string Cost = "";

		// How the state was found when it was not simply read (remembered, waiting for a restart).
		public string Note = "";

		// The thing's state: on | off | part | other | unknown, or "" for a row that has none.
		public string State = "";

		// Which side helps performance.
		public bool HelpsWhenOn;

		// safe | caution | risky.
		public string Risk = Tweaks.Safe;

		// A short tag after the title ("Uses the network", "Worth a look").
		public string Badge = "";

		// The side the user switched it to and has not applied yet; null for no change.
		public bool? Want;

		// It has a switch. False for a row that is only reported.
		public bool Can = true;

		// A group title rather than a thing.
		public bool Header;

		// For a header: "3 hurting · 5 helping", filled in by the list.
		internal string Summary = "";

		public bool? Current => PerfModel.AsBool(State);

		public string Verdict => (State.Length == 0) ? "" : PerfModel.Verdict(State, HelpsWhenOn, Risk);

		public bool Pending => Can && PerfModel.IsChange(Current, Want);

		// Laid out by Relayout().
		internal int Y;

		internal int H;

		internal int TitleH;

		internal float TitleW;

		internal int DescH;

		internal int CostH;

		internal int NoteH;

		internal int PendH;
	}

	private const int Pad = 14;

	// The badge column on the left, wide enough for "PARTLY ON · HURTING".
	private const int BadgeW = 168;

	// The switch and its ON/OFF label on the right.
	private const int SwitchW = 40;

	private const int SwitchH = 22;

	private const int RightW = 96;

	private static readonly Color HurtColor = Color.FromArgb(240, 150, 60);

	private static readonly Color SafeColor = Color.FromArgb(120, 170, 230);

	private static readonly Color UnknownColor = Color.FromArgb(140, 140, 146);

	private readonly VScrollBar bar = new VScrollBar();

	private List<Row> items = new List<Row>();

	private int hover = -1;

	private int selected = -1;

	private int total;

	// Raised when a switch is flipped or the selection moves.
	public event Action Changed;

	public SwitchList()
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
			selected = -1;
			bar.Value = 0;
			Relayout();
		}
	}

	// Rows switched to a side they are not on yet.
	public IEnumerable<Row> PendingRows => items.Where((Row r) => !r.Header && r.Pending);

	public int PendingCount => PendingRows.Count();

	// Rows on the hurting side that "fix all" may switch: never a security trade-off (those read "safe
	// default", not "hurting") and never one that cannot be switched.
	public IEnumerable<Row> Fixable => items.Where((Row r) => !r.Header && r.Can && r.Verdict == PerfModel.Hurting
		&& r.Risk != Tweaks.Risky && !r.Pending);

	public Row SelectedRow => (selected >= 0 && selected < items.Count) ? items[selected] : null;

	// Switches every hurting row to its helping side. Returns how many it switched.
	public int FixAll()
	{
		List<Row> rows = Fixable.ToList();
		foreach (Row r in rows)
		{
			r.Want = r.HelpsWhenOn;
		}
		if (rows.Count > 0)
		{
			Relayout();
			Raise();
		}
		return rows.Count;
	}

	// Forgets every switch that was flipped, without losing the list.
	public void ClearPending()
	{
		foreach (Row r in items)
		{
			r.Want = null;
		}
		Relayout();
		Raise();
	}

	protected override void OnResize(EventArgs e)
	{
		base.OnResize(e);
		Relayout();
	}

	private int TextWidth => Math.Max(120, Width - (bar.Visible ? bar.Width : 0) - Pad * 2 - BadgeW - 12);

	// Row heights depend on how the text wraps, so they are measured here and kept: painting and scrolling
	// never measure anything.
	private void Relayout()
	{
		int width = TextWidth;
		Font title = Theme.P(10.5f);
		Font small = Theme.P(9f);
		int titleLine = (int)Tx.Measure("X", title).Height;
		// Header counts first, so a header's summary is current when it is drawn.
		Row head = null;
		int hurt = 0;
		int help = 0;
		foreach (Row r in items)
		{
			if (r.Header)
			{
				Summarise(head, hurt, help);
				head = r;
				hurt = 0;
				help = 0;
				continue;
			}
			if (r.Verdict == PerfModel.Hurting)
			{
				hurt++;
			}
			else if (r.Verdict == PerfModel.Helping)
			{
				help++;
			}
		}
		Summarise(head, hurt, help);
		int y = 0;
		foreach (Row r in items)
		{
			r.Y = y;
			if (r.Header)
			{
				r.DescH = (r.Desc.Length > 0) ? Lines(r.Desc, small, width + BadgeW + 12) : 0;
				r.H = 40 + r.DescH;
			}
			else
			{
				Measure(r, width, titleLine, small);
			}
			y += r.H;
		}
		total = y;
		int view = Math.Max(1, Height);
		int maxScroll = Math.Max(0, total - view);
		bar.Visible = total > view;
		// A VScrollBar's highest reachable Value is Maximum - LargeChange + 1. Setting both from the content
		// (Maximum = total - 1, LargeChange = view) gives exactly total - view, the last row; reading the
		// LargeChange getter instead returned a value clamped by the old Maximum.
		bar.Maximum = Math.Max(0, total - 1);
		bar.LargeChange = Math.Max(1, view);
		bar.SmallChange = 24;
		if (bar.Value > maxScroll)
		{
			bar.Value = maxScroll;
		}
		Invalidate();
	}

	private static void Summarise(Row head, int hurt, int help)
	{
		if (head == null)
		{
			return;
		}
		List<string> parts = new List<string>();
		if (hurt > 0)
		{
			parts.Add(hurt + " hurting");
		}
		if (help > 0)
		{
			parts.Add(help + " helping");
		}
		head.Summary = string.Join("  ·  ", parts);
	}

	private void Measure(Row r, int width, int titleLine, Font small)
	{
		r.TitleH = titleLine;
		r.TitleW = Tx.Measure(r.Title, Theme.P(10.5f)).Width;
		r.DescH = (r.Desc.Length > 0) ? Lines(r.Desc, small, width) : 0;
		r.CostH = (r.Cost.Length > 0) ? Lines(r.Cost, small, width) + 2 : 0;
		r.NoteH = (r.Note.Length > 0) ? Lines(r.Note, small, width) + 2 : 0;
		r.PendH = r.Pending ? Lines(PendingText(r), small, width) + 4 : 0;
		r.H = Math.Max(SwitchH + 20, 12 + r.TitleH + r.DescH + r.CostH + r.NoteH + r.PendH + 12);
	}

	private static int Lines(string text, Font f, int width)
	{
		return TextRenderer.MeasureText(text, f, new Size(Math.Max(40, width), int.MaxValue),
			TextFormatFlags.WordBreak | TextFormatFlags.NoPadding).Height;
	}

	private int Scroll => bar.Visible ? bar.Value : 0;

	// "Will turn OFF when you press Apply - that helps performance."
	internal static string PendingText(Row r)
	{
		if (!r.Want.HasValue)
		{
			return "";
		}
		string side = r.Want.Value ? "ON" : "OFF";
		string verdict = PerfModel.Verdict(r.Want.Value ? "on" : "off", r.HelpsWhenOn, r.Risk);
		string effect = verdict switch
		{
			PerfModel.Helping => "that helps performance.",
			PerfModel.Hurting => "that costs performance.",
			_ => "that puts the safe default back."
		};
		return "Will turn " + side + " when you press Apply - " + effect;
	}

	private static Color VerdictColor(string verdict)
	{
		return verdict switch
		{
			PerfModel.Helping => Theme.Green,
			PerfModel.Hurting => HurtColor,
			PerfModel.SafeDefault => SafeColor,
			_ => UnknownColor
		};
	}

	private static string BadgeText(Row r)
	{
		if (r.State == "unknown" || r.State == "other")
		{
			return (r.State == "other") ? "CUSTOM VALUE" : "CAN'T TELL";
		}
		return PerfModel.StateLabel(r.State) + "  ·  " + PerfModel.VerdictLabel(r.Verdict);
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(Theme.Bg);
		int view = Height;
		int off = Scroll;
		int width = Width - (bar.Visible ? bar.Width : 0);
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
				PaintHeader(g, r, y, width);
			}
			else
			{
				PaintItem(g, r, y, width, i == hover, i == selected);
			}
		}
	}

	private void PaintHeader(Graphics g, Row r, int y, int width)
	{
		Font head = Theme.P(9f, FontStyle.Bold);
		Font small = Theme.P(9f);
		Tx.Left(g, r.Title.ToUpperInvariant(), head, Theme.Green, new RectangleF(Pad, y + 14f, width - Pad * 2 - 220, 18f));
		if (r.Summary.Length > 0)
		{
			Tx.Right(g, r.Summary, small, Theme.Dim, new RectangleF(width - Pad - 220, y + 14f, 220f, 18f));
		}
		if (r.Desc.Length > 0)
		{
			Tx.Wrap(g, r.Desc, small, Theme.Dim, new RectangleF(Pad, y + 34f, width - Pad * 2, r.DescH));
		}
		using Pen p = new Pen(Color.FromArgb(52, 52, 56));
		g.DrawLine(p, Pad, y + r.H - 4, width - Pad, y + r.H - 4);
	}

	private void PaintItem(Graphics g, Row r, int y, int width, bool isHover, bool isSelected)
	{
		RectangleF box = new RectangleF(4f, y + 2f, width - 8, r.H - 4);
		if (isHover || r.Pending)
		{
			using GraphicsPath hp = Theme.Round(box, 8f);
			if (isHover)
			{
				using SolidBrush hb = new SolidBrush(Color.FromArgb(38, 38, 42));
				g.FillPath(hb, hp);
			}
			if (r.Pending)
			{
				// Outlined in the colour of where it is going, so a change that costs something stands out.
				string going = PerfModel.Verdict(r.Want.Value ? "on" : "off", r.HelpsWhenOn, r.Risk);
				using Pen pp = new Pen(VerdictColor(going), 1.2f);
				g.DrawPath(pp, hp);
			}
		}
		if (isSelected)
		{
			using GraphicsPath sel = Theme.Round(new RectangleF(5f, y + 10f, 3f, r.H - 20), 1.5f);
			using SolidBrush sb = new SolidBrush(Theme.Dim);
			g.FillPath(sb, sel);
		}
		Font title = Theme.P(10.5f);
		Font small = Theme.P(9f);
		Font pill = Theme.P(8f, FontStyle.Bold);
		// The badge: ON/OFF and helping/hurting, in one place, in one colour.
		if (r.State.Length > 0)
		{
			PaintBadge(g, BadgeText(r), VerdictColor(r.Verdict), pill, Pad, y + 12f, BadgeW);
		}
		int tx = Pad + BadgeW + 12;
		int textRight = width - Pad - (r.Can ? RightW : 0);
		Tx.Left(g, r.Title, title, Theme.Text, new RectangleF(tx, y + 10f, Math.Max(40f, textRight - tx), r.TitleH + 2));
		if (r.Badge.Length > 0)
		{
			float bx = tx + Math.Min(r.TitleW + 10f, Math.Max(40f, textRight - tx - 140f));
			PaintTag(g, r.Badge, RiskColor(r.Risk), pill, bx, y + 12f);
		}
		if (r.Can)
		{
			PaintSwitch(g, r, width - Pad - SwitchW, y + 10);
		}
		int ty = y + 10 + r.TitleH;
		int descWidth = width - Pad - tx;
		if (r.DescH > 0)
		{
			Tx.Wrap(g, r.Desc, small, Theme.Dim, new RectangleF(tx, ty, descWidth, r.DescH));
			ty += r.DescH;
		}
		if (r.CostH > 0)
		{
			Tx.Wrap(g, r.Cost, small, CostColor(r.Risk), new RectangleF(tx, ty + 2, descWidth, r.CostH));
			ty += r.CostH;
		}
		if (r.NoteH > 0)
		{
			Tx.Wrap(g, r.Note, small, SafeColor, new RectangleF(tx, ty + 2, descWidth, r.NoteH));
			ty += r.NoteH;
		}
		if (r.PendH > 0)
		{
			string going = PerfModel.Verdict(r.Want.Value ? "on" : "off", r.HelpsWhenOn, r.Risk);
			Tx.Wrap(g, PendingText(r), Theme.P(9f, FontStyle.Bold), VerdictColor(going), new RectangleF(tx, ty + 4, descWidth, r.PendH));
		}
	}

	// The switch shows the side the row will be on: the pending one if it was flipped, else the current one.
	// A state that cannot be read and has not been flipped shows the knob in the middle.
	private static void PaintSwitch(Graphics g, Row r, int x, int y)
	{
		bool? side = r.Want ?? r.Current;
		Font label = Theme.P(8.5f, FontStyle.Bold);
		string text = side.HasValue ? (side.Value ? "ON" : "OFF") : "?";
		Color tint = side.HasValue
			? VerdictColor(PerfModel.Verdict(side.Value ? "on" : "off", r.HelpsWhenOn, r.Risk))
			: UnknownColor;
		Tx.Right(g, text, label, tint, new RectangleF(x - 52, y, 46f, SwitchH));
		using GraphicsPath track = Theme.Round(new RectangleF(x + 1, y + 2, SwitchW - 3, SwitchH - 5), (SwitchH - 5) / 2f);
		bool on = side == true;
		using (SolidBrush fill = new SolidBrush(on ? tint : Theme.Raised))
		{
			g.FillPath(fill, track);
		}
		if (!on)
		{
			using Pen edge = new Pen(side.HasValue ? tint : Color.FromArgb(110, 110, 116), 1f);
			g.DrawPath(edge, track);
		}
		float d = SwitchH - 11;
		float kx = !side.HasValue ? (x + (SwitchW - d) / 2f) : (on ? (x + SwitchW - d - 6f) : (x + 5f));
		using SolidBrush knob = new SolidBrush(on ? Color.Black : Color.FromArgb(200, 200, 205));
		g.FillEllipse(knob, kx, y + 5f, d, d);
	}

	private static void PaintBadge(Graphics g, string text, Color colour, Font f, float x, float y, float maxWidth)
	{
		float w = Math.Min(maxWidth, Tx.Measure(text, f).Width + 18f);
		using (GraphicsPath path = Theme.Round(new RectangleF(x, y, w, 18f), 9f))
		{
			using SolidBrush b = new SolidBrush(Color.FromArgb(46, colour.R, colour.G, colour.B));
			g.FillPath(b, path);
			using Pen p = new Pen(colour, 1f);
			g.DrawPath(p, path);
		}
		Tx.Center(g, text, f, colour, new RectangleF(x, y, w, 18f));
	}

	private static void PaintTag(Graphics g, string text, Color colour, Font f, float x, float y)
	{
		float w = Tx.Measure(text, f).Width + 12f;
		using (GraphicsPath path = Theme.Round(new RectangleF(x, y + 1, w, 16f), 8f))
		{
			using Pen p = new Pen(Color.FromArgb(150, colour.R, colour.G, colour.B), 1f);
			g.DrawPath(p, path);
		}
		Tx.Center(g, text, f, colour, new RectangleF(x, y + 1, w, 16f));
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

	// A click anywhere on a row flips its switch (a big target beats a small one) and selects it; a row
	// without a switch is only selected, so its file can still be scanned.
	protected override void OnMouseDown(MouseEventArgs e)
	{
		base.OnMouseDown(e);
		if (!Focused && CanFocus)
		{
			Focus();
		}
		if (e.Button != MouseButtons.Left)
		{
			return;
		}
		int at = RowAt(e.Y);
		if (at < 0 || items[at].Header)
		{
			return;
		}
		Row r = items[at];
		selected = at;
		if (r.Can)
		{
			r.Want = PerfModel.NextWant(r.Current, r.Want, r.HelpsWhenOn);
			if (!r.Pending)
			{
				r.Want = null;
			}
			Relayout();
		}
		else
		{
			Invalidate();
		}
		Raise();
	}

	private void Raise()
	{
		try
		{
			Changed?.Invoke();
		}
		catch (Exception ex)
		{
			Cfg.Log("switch list: " + ex.Message);
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
