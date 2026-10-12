using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LiteSynapse;

internal class DarkCombo : Control
{
	private sealed class OutsideClick : IMessageFilter
	{
		private readonly Func<nint, bool> inside;

		private readonly Action<nint> outsideClick;

		public OutsideClick(Func<nint, bool> inside, Action<nint> outsideClick)
		{
			this.inside = inside;
			this.outsideClick = outsideClick;
		}

		public bool PreFilterMessage(ref Message m)
		{
			bool flag;
			switch (m.Msg)
			{
			case 161:
			case 164:
			case 167:
			case 513:
			case 516:
			case 519:
				flag = true;
				break;
			default:
				flag = false;
				break;
			}
			if (flag && !inside(m.HWnd))
			{
				outsideClick(m.HWnd);
			}
			return false;
		}
	}

	public readonly List<object> Items = new List<object>();

	private int sel = -1;

	private bool open;

	public bool Inline;

	private ToolStripDropDown drop;

	private ListBox popupList;

	private Panel inlineHost;

	private OutsideClick outside;

	private long closedAt;

	private bool closedOnSelf;

	public int SelectedIndex
	{
		get
		{
			return sel;
		}
		set
		{
			if (sel != value)
			{
				sel = value;
				base.Invalidate();
				SelectedIndexChanged?.Invoke();
			}
			else
			{
				base.Invalidate();
			}
		}
	}

	public object SelectedItem
	{
		get
		{
			if (sel < 0 || sel >= Items.Count)
			{
				return null;
			}
			return Items[sel];
		}
		set
		{
			int i = Items.IndexOf(value);
			if (i >= 0 || value == null)
			{
				SelectedIndex = i;
			}
			else
			{
				// Not in the list: show nothing selected rather than keep whatever index the old list had,
				// which displayed a different profile as the active one. No event: nothing was chosen.
				sel = -1;
				base.Invalidate();
			}
		}
	}

	public bool IsOpen => open;

	internal ListBox TestList => popupList;

	public event Action SelectedIndexChanged;

	public event Action PopupOpened;

	public event Action PopupClosed;

	public DarkCombo()
	{
		base.DoubleBuffered = true;
		base.Cursor = Cursors.Hand;
		base.Height = 30;
		base.Font = Theme.F(10f, (FontStyle)1);
		base.BackColor = Theme.Bg;
		base.TabStop = true;
		SetStyle(ControlStyles.Selectable, value: true);
	}

	protected override bool IsInputKey(Keys keyData)
	{
		switch (keyData)
		{
		case Keys.Down:
		case Keys.Up:
		case Keys.Enter:
		case Keys.Space:
			return true;
		default:
			return base.IsInputKey(keyData);
		}
	}

	protected override void OnKeyDown(KeyEventArgs e)
	{
		base.OnKeyDown(e);
		// Open the list from the keyboard; once open, the inline list takes focus and handles arrows/Enter/Esc.
		if (!open && base.Enabled && (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up || e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space))
		{
			ShowPopup();
			e.Handled = true;
		}
	}

	protected override void OnGotFocus(EventArgs e)
	{
		base.OnGotFocus(e);
		base.Invalidate();
	}

	protected override void OnLostFocus(EventArgs e)
	{
		base.OnLostFocus(e);
		base.Invalidate();
	}

	protected override void OnPaint(PaintEventArgs e)
	{
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0059: Expected Obj, but got Unknown
		//IL_008a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0091: Expected Obj, but got Unknown
		//IL_01d8: Unknown result type (might be due to invalid IL or missing references)
		//IL_01df: Expected Obj, but got Unknown
		Graphics g = e.Graphics;
		Theme.Quality(g);
		g.Clear(base.BackColor);
		// Rounded field, like every other control on the page now.
		using (GraphicsPath r = Theme.Round(new RectangleF(0.5f, 0.5f, base.Width - 1.5f, base.Height - 1.5f), 6f))
		{
			using (SolidBrush b = new SolidBrush(base.Enabled ? Color.FromArgb(40, 40, 44) : Color.FromArgb(34, 34, 36)))
			{
				g.FillPath(b, r);
			}
			using Pen p = new Pen((open || (base.Focused && base.Enabled)) ? Theme.Green : Color.FromArgb(78, 78, 84));
			g.DrawPath(p, r);
		}
		Tx.Left(g, SelectedItem?.ToString() ?? "", base.Font, base.Enabled ? Theme.Text : Theme.Dim, new RectangleF(10f, 0f, base.Width - 34, base.Height));
		float ax = base.Width - 18;
		float ay = (float)base.Height / 2f;
		// A thin chevron rather than a filled triangle.
		float dy = open ? -2.5f : 2.5f;
		using Pen chev = new Pen(Color.FromArgb(200, 200, 200), 1.6f);
		chev.StartCap = LineCap.Round;
		chev.EndCap = LineCap.Round;
		chev.LineJoin = LineJoin.Round;
		g.DrawLines(chev, new PointF[3]
		{
			new PointF(ax - 4.5f, ay - dy),
			new PointF(ax, ay + dy),
			new PointF(ax + 4.5f, ay - dy)
		});
	}

	protected override void OnMouseClick(MouseEventArgs e)
	{
		if (base.CanFocus)
		{
			Focus();
		}
		if (closedOnSelf && Environment.TickCount64 - closedAt < 600)
		{
			closedOnSelf = false;
			return;
		}
		closedOnSelf = false;
		ShowPopup();
	}

	private ListBox BuildList(int rows, Action<int> pick)
	{
		//IL_0015: Unknown result type (might be due to invalid IL or missing references)
		//IL_001a: Unknown result type (might be due to invalid IL or missing references)
		//IL_0021: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_003e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0057: Unknown result type (might be due to invalid IL or missing references)
		//IL_0063: Unknown result type (might be due to invalid IL or missing references)
		//IL_006f: Unknown result type (might be due to invalid IL or missing references)
		//IL_007b: Expected Obj, but got Unknown
		//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
		//IL_0105: Expected Obj, but got Unknown
		//IL_0112: Unknown result type (might be due to invalid IL or missing references)
		//IL_011c: Expected Obj, but got Unknown
		//IL_0129: Unknown result type (might be due to invalid IL or missing references)
		//IL_0133: Expected Obj, but got Unknown
		//IL_0140: Unknown result type (might be due to invalid IL or missing references)
		//IL_014a: Expected Obj, but got Unknown
		ListBox list = new ListBox
		{
			BorderStyle = (BorderStyle)0,
			BackColor = Color.Black,
			ForeColor = Theme.Text,
			DrawMode = (DrawMode)1,
			ItemHeight = 25,
			Font = Theme.P(10f, (FontStyle)0),
			Width = base.Width,
			Height = rows * 25 + 2,
			IntegralHeight = false
		};
		foreach (object it in Items)
		{
			list.Items.Add(it);
		}
		list.TopIndex = Math.Max(0, Math.Min(sel - 3, Items.Count - rows));
		int hot = -1;
		list.DrawItem += (object? s, DrawItemEventArgs d) =>
		{
			//IL_0014: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_001c: Invalid comparison between Unknown and I4
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Expected Obj, but got Unknown
			if (d.Index < 0)
			{
				return;
			}
			SolidBrush val = new SolidBrush(((hot >= 0) ? (d.Index == hot) : ((d.State & DrawItemState.Selected) != 0)) ? Color.FromArgb(38, 38, 38) : Color.Black);
			try
			{
				d.Graphics.FillRectangle((Brush)(object)val, d.Bounds);
				Tx.Left(d.Graphics, list.Items[d.Index].ToString(), ((Control)list).Font, (d.Index == sel) ? Theme.Green : Theme.Text, new RectangleF(d.Bounds.X + 6, d.Bounds.Y, d.Bounds.Width - 8, d.Bounds.Height));
			}
			finally
			{
				((IDisposable)val)?.Dispose();
			}
		};
		((Control)list).MouseMove += (object? s, MouseEventArgs m) =>
		{
			int num = list.IndexFromPoint(m.Location);
			if (num != hot)
			{
				hot = num;
				((Control)list).Invalidate();
			}
		};
		((Control)list).KeyDown += (object? s, KeyEventArgs k) =>
		{
			if (hot >= 0)
			{
				hot = -1;
				((Control)list).Invalidate();
			}
		};
		list.MouseClick += (object? s, MouseEventArgs m) =>
		{
			int num = list.IndexFromPoint(m.Location);
			if (num >= 0)
			{
				pick(num);
			}
		};
		return list;
	}

	public void ShowPopup()
	{
		//IL_005a: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0066: Unknown result type (might be due to invalid IL or missing references)
		//IL_0067: Unknown result type (might be due to invalid IL or missing references)
		//IL_0071: Unknown result type (might be due to invalid IL or missing references)
		//IL_0072: Unknown result type (might be due to invalid IL or missing references)
		//IL_007c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0088: Expected Obj, but got Unknown
		//IL_0089: Unknown result type (might be due to invalid IL or missing references)
		//IL_008e: Unknown result type (might be due to invalid IL or missing references)
		//IL_008f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0099: Unknown result type (might be due to invalid IL or missing references)
		//IL_009a: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ab: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b8: Expected Obj, but got Unknown
		//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d7: Expected Obj, but got Unknown
		if (Items.Count == 0 || !base.Enabled || open)
		{
			return;
		}
		// Always use the inline list when hosted in a form: a ListBox in a ToolStripDropDown does not reliably
		// take keyboard focus, so the inline path (proven by the tray) is what makes the list arrow-navigable.
		if (base.FindForm() != null)
		{
			ShowInline();
			return;
		}
		ListBox list = BuildList(Math.Min(Items.Count, 9), PickAndClose);
		ToolStripDropDown dd = new ToolStripDropDown
		{
			AutoClose = true,
			Padding = Padding.Empty,
			Margin = Padding.Empty,
			BackColor = Color.Black
		};
		ToolStripControlHost host = new ToolStripControlHost((Control)(object)list)
		{
			Margin = Padding.Empty,
			Padding = Padding.Empty,
			AutoSize = false,
			Size = ((Control)list).Size
		};
		((ToolStrip)dd).Items.Add((ToolStripItem)(object)host);
		dd.Closed += (object? s, ToolStripDropDownClosedEventArgs e) =>
		{
			open = false;
			drop = null;
			popupList = null;
			// A click on this field closed it: the click's mouse-up must not open it again.
			if (e.CloseReason == ToolStripDropDownCloseReason.AppClicked && base.ClientRectangle.Contains(base.PointToClient(Cursor.Position)))
			{
				closedOnSelf = true;
				closedAt = Environment.TickCount64;
			}
			base.Invalidate();
			PopupClosed?.Invoke();
			// The popup window, its host and the list go with it, once the click that closed it is handled.
			if (base.IsHandleCreated)
			{
				base.BeginInvoke((Action)dd.Dispose);
			}
		};
		drop = dd;
		popupList = list;
		open = true;
		base.Invalidate();
		PopupOpened?.Invoke();
		dd.Show((Control)(object)this, 0, base.Height);
	}

	private void ShowInline()
	{
		//IL_00a3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ad: Expected Obj, but got Unknown
		//IL_00ae: Unknown result type (might be due to invalid IL or missing references)
		//IL_00b3: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
		//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
		//IL_00ee: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fa: Expected Obj, but got Unknown
		Form form = base.FindForm();
		Point at = ((Control)form).PointToClient(base.Parent.PointToScreen(new Point(base.Left, base.Bottom)));
		int rows = Math.Max(1, Math.Min(Math.Min(Items.Count, 9), (form.ClientSize.Height - at.Y - 8) / 25));
		ListBox list = BuildList(rows, (int i) =>
		{
			CloseInline(onSelf: false);
			SelectedIndex = i;
		});
		((Control)list).Dock = (DockStyle)5;
		((Control)list).KeyDown += (object? s, KeyEventArgs k) =>
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0008: Invalid comparison between Unknown and I4
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0026: Invalid comparison between Unknown and I4
			if ((int)k.KeyCode == 27)
			{
				CloseInline(onSelf: false);
				k.Handled = true;
			}
			else if ((int)k.KeyCode == 13 && ((ListControl)list).SelectedIndex >= 0)
			{
				int selectedIndex = ((ListControl)list).SelectedIndex;
				CloseInline(onSelf: false);
				SelectedIndex = selectedIndex;
				k.Handled = true;
			}
		};
		inlineHost = new Panel
		{
			BackColor = Color.FromArgb(110, 110, 110),
			Padding = new Padding(1),
			Size = new Size(base.Width, ((Control)list).Height + 2),
			Location = at
		};
		((Control)inlineHost).Controls.Add((Control)(object)list);
		popupList = list;
		open = true;
		base.Invalidate();
		PopupOpened?.Invoke();
		((Control)form).Controls.Add((Control)(object)inlineHost);
		((Control)inlineHost).BringToFront();
		DarkScroll.Apply((Control)(object)list);
		((ListControl)list).SelectedIndex = sel;
		((Control)list).Focus();
		outside = new OutsideClick((nint h) => h == (nint)((Control)inlineHost).Handle || h == (nint)((Control)list).Handle, (nint h) =>
		{
			CloseInline(h == (nint)base.Handle);
		});
		Application.AddMessageFilter((IMessageFilter)(object)outside);
	}

	private void CloseInline(bool onSelf)
	{
		if (open && inlineHost != null)
		{
			Application.RemoveMessageFilter((IMessageFilter)(object)outside);
			outside = null;
			Panel host = inlineHost;
			inlineHost = null;
			popupList = null;
			((Control)host).Visible = false;
			Control parent = ((Control)host).Parent;
			if (parent != null)
			{
				parent.Controls.Remove((Control)(object)host);
			}
			try
			{
				((Control)host).BeginInvoke((Action)((Component)(object)host).Dispose);
			}
			catch
			{
				((Component)(object)host).Dispose();
			}
			open = false;
			base.Invalidate();
			closedAt = Environment.TickCount64;
			closedOnSelf = onSelf;
			PopupClosed?.Invoke();
		}
	}

	private void PickAndClose(int i)
	{
		ToolStripDropDown val = drop;
		if (val != null)
		{
			val.Close();
		}
		SelectedIndex = i;
	}

	protected override void Dispose(bool disposing)
	{
		if (disposing && outside != null)
		{
			Application.RemoveMessageFilter((IMessageFilter)(object)outside);
			outside = null;
		}
		base.Dispose(disposing);
	}

	internal void TestPick(int i)
	{
		if (!open)
		{
			SelectedIndex = i;
		}
		else if (inlineHost != null)
		{
			CloseInline(onSelf: false);
			SelectedIndex = i;
		}
		else
		{
			PickAndClose(i);
		}
	}
}
