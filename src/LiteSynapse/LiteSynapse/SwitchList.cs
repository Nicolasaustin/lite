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

		// For a Windows setting: the side applying it puts the thing on (Tweaks.Tweak.PerfOn), which is what
		// Apply needs. Usually HelpsWhenOn too, except where this PC's hardware turns it round.
		public bool AppliedOn;

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

		internal float BadgeTextW;

		internal float TagW;

		internal int DescH;

		internal int CostH;

		internal int NoteH;

		internal int PendH;
	}

	private const int Pad = 14;

	// The badge column on the left: at least this wide, and wider when the badge text needs it (a larger
	// display scale makes the text wider but not this number).
	private const int MinBadgeW = 168;

	private int badgeW = MinBadgeW;

	// The width rows were last measured at, so a height-only resize does not measure them again.
	private int measuredWidth = -1;

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
		bar.ValueChanged += (object _1, EventArgs _2) =>
		{
			// The rows moved under a pointer that did not: the highlight has to follow what is under it now.
			UpdateHover(PointToClient(Cursor.Position));
			Invalidate();
		};
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
			measuredWidth = -1;
			Relayout();
		}
	}

	// New rows for the same page (a re-read after APPLY or CHECK AGAIN): the list stays where it was and the
	// selected row stays selected, so the user can see the row they just changed instead of the top.
	public void Refill(List<Row> rows)
	{
		int keep = bar.Value;
		string selectedId = SelectedRow?.Id;
		bool placeholder = !items.Any((Row r) => !r.Header);
		items = rows ?? new List<Row>();
		hover = -1;
		selected = string.IsNullOrEmpty(selectedId) ? -1 : items.FindIndex((Row r) => !r.Header && r.Id == selectedId);
		measuredWidth = -1;
		Relayout();
		if (!placeholder)
		{
			bar.Value = Math.Max(bar.Minimum, Math.Min(keep, Math.Max(0, total - Math.Max(1, Height))));
		}
	}

	// Rows switched to a side they are not on yet.
	public IEnumerable<Row> PendingRows => items.Where((Row r) => !r.Header && r.Pending);

	public int PendingCount => PendingRows.Count();

	// Rows on the hurting side that "fix all" may switch: only the ones marked safe. A "use with care" row
	// turns off a feature someone may use (Bluetooth, controllers, the keyboard's media keys) and a security
	// trade-off is never a fix, so both are left for the user to switch by hand, after reading the row.
	public IEnumerable<Row> Fixable => items.Where((Row r) => !r.Header && r.Can && r.Verdict == PerfModel.Hurting
		&& r.Risk == Tweaks.Safe && !r.Pending);

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
		if (Width <= 0 || Height <= 0)
		{
			return;
		}
		// Row heights depend only on the width: a height-only resize just moves the scroll range.
		if (TextWidth == measuredWidth)
		{
			UpdateScroll();
			Invalidate();
			return;
		}
		Relayout();
	}

	// The scroll bar's width is always kept free, shown or not: measuring at one width and painting at another
	// (the bar appearing after the rows were measured) cut the last line off wrapped descriptions.
	private int ListWidth => Math.Max(0, Width - bar.Width);

	private int TextWidth => Math.Max(120, ListWidth - Pad * 2 - badgeW - 12);

	// Row heights depend on how the text wraps, so they are measured here and kept: painting and scrolling
	// never measure anything.
	private void Relayout()
	{
		Font pillFont = Theme.P(8f, FontStyle.Bold);
		badgeW = Math.Max(MinBadgeW, (int)Tx.Measure("PARTLY ON  ·  SAFE DEFAULT", pillFont).Width + 20);
		int width = TextWidth;
		measuredWidth = width;
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
				r.DescH = (r.Desc.Length > 0) ? Lines(r.Desc, small, width + badgeW + 12) : 0;
				r.H = 40 + r.DescH;
			}
			else
			{
				Measure(r, width, titleLine, small);
			}
			y += r.H;
		}
		total = y;
		UpdateScroll();
		Invalidate();
	}

	private void UpdateScroll()
	{
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
	}

	// One row's height changed (its pending line came or went): measure that row only and move the ones below
	// it. A click must not re-measure a hundred and fifty rows of wrapped text.
	private void Reflow(int at)
	{
		if (at < 0 || at >= items.Count)
		{
			Relayout();
			return;
		}
		int before = items[at].H;
		Measure(items[at], TextWidth, (int)Tx.Measure("X", Theme.P(10.5f)).Height, Theme.P(9f));
		int delta = items[at].H - before;
		if (delta != 0)
		{
			for (int i = at + 1; i < items.Count; i++)
			{
				items[i].Y += delta;
			}
			total += delta;
			UpdateScroll();
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
		// Measured in the bold it is painted in.
		r.PendH = r.Pending ? Lines(PendingText(r), Theme.P(9f, FontStyle.Bold), width) + 4 : 0;
		Font pill = Theme.P(8f, FontStyle.Bold);
		r.BadgeTextW = (r.State.Length > 0) ? Tx.Measure(BadgeText(r), pill).Width : 0f;
		r.TagW = (r.Badge.Length > 0) ? Tx.Measure(r.Badge, pill).Width : 0f;
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
		int width = ListWidth;
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
			PaintBadge(g, BadgeText(r), r.BadgeTextW, VerdictColor(r.Verdict), pill, Pad, y + 12f, badgeW);
		}
		int tx = Pad + badgeW + 12;
		int textRight = width - Pad - (r.Can ? RightW : 0);
		Tx.Left(g, r.Title, title, Theme.Text, new RectangleF(tx, y + 10f, Math.Max(40f, textRight - tx), r.TitleH + 2));
		if (r.Badge.Length > 0)
		{
			float bx = tx + Math.Min(r.TitleW + 10f, Math.Max(40f, textRight - tx - 140f));
			PaintTag(g, r.Badge, r.TagW, RiskColor(r.Risk), pill, bx, y + 12f);
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

	private static void PaintBadge(Graphics g, string text, float textW, Color colour, Font f, float x, float y, float maxWidth)
	{
		float w = Math.Min(maxWidth, textW + 18f);
		using (GraphicsPath path = Theme.Round(new RectangleF(x, y, w, 18f), 9f))
		{
			using SolidBrush b = new SolidBrush(Color.FromArgb(46, colour.R, colour.G, colour.B));
			g.FillPath(b, path);
			using Pen p = new Pen(colour, 1f);
			g.DrawPath(p, path);
		}
		Tx.Center(g, text, f, colour, new RectangleF(x, y, w, 18f));
	}

	private static void PaintTag(Graphics g, string text, float textW, Color colour, Font f, float x, float y)
	{
		float w = textW + 12f;
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
		if (UpdateHover(e.Location))
		{
			Invalidate();
		}
	}

	// The row under a point, and the hand cursor over a switch. True when the highlighted row changed.
	private bool UpdateHover(Point p)
	{
		int at = ClientRectangle.Contains(p) ? RowAt(p.Y) : -1;
		if (at >= 0 && items[at].Header)
		{
			at = -1;
		}
		Cursor = (at >= 0 && items[at].Can && OnSwitch(p.X)) ? Cursors.Hand : Cursors.Default;
		if (at == hover)
		{
			return false;
		}
		hover = at;
		return true;
	}

	// The switch, its ON/OFF label and the badge are what flip a row; the rest of the row only selects it,
	// so picking a start-up program to scan does not also queue turning it off.
	private bool OnSwitch(int x)
	{
		return x >= ListWidth - Pad - RightW - 8 || (x >= Pad && x < Pad + badgeW);
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

	// A click on a row's switch (or its badge) flips it; a click anywhere else on the row selects it, for
	// SCAN WITH DEFENDER, without changing anything.
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
		if (r.Can && OnSwitch(e.X))
		{
			r.Want = PerfModel.NextWant(r.Current, r.Want, r.HelpsWhenOn);
			if (!r.Pending)
			{
				r.Want = null;
			}
			Reflow(at);
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
		// Scaled rather than divided: a touchpad or a free-spinning wheel sends deltas under 120, which
		// integer division turned into no scroll at all.
		int step = -e.Delta * 60 / 120;
		// The same reachable maximum the scrollbar itself enforces, worked out from the content rather than
		// from the clamped LargeChange getter.
		int maxScroll = Math.Max(0, total - Math.Max(1, Height));
		bar.Value = Math.Max(bar.Minimum, Math.Min(maxScroll, bar.Value + step));
	}
}
