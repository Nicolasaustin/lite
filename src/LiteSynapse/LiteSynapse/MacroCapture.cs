using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LiteSynapse;

// The small dialogs the macro page uses to get input from the user: capture one key, pick a mouse button, or
// record a sequence of key and mouse events with their timing. All three draw in the app's dark style.
//
// Recording here is done with a focused window that reads WinForms key and mouse events: no global hook, so it
// only ever sees what the user does inside the recorder, never anything else on the PC.
internal static class MacroCapture
{
	// A key, as a dialog with a KeyCapture box. False when cancelled.
	public static bool AskKey(IWin32Window owner, out int vk, out bool ext)
	{
		vk = 0;
		ext = false;
		Form dlg = Dialog("Add a key", 380, 220);
		Label help = new Label
		{
			Text = "Click the box, then press the key to add. It is added as a press and a release.",
			AutoSize = false,
			Location = new Point(24, 54),
			Size = new Size(332, 40),
			ForeColor = Theme.Dim,
			BackColor = Color.Transparent,
			Font = Theme.P(9.5f, (FontStyle)0)
		};
		KeyCapture cap = new KeyCapture
		{
			Location = new Point(24, 100),
			Width = 332
		};
		PillButton ok = Button("ADD", 180, 160, primary: true);
		PillButton cancel = Button("CANCEL", 268, 160);
		((Control)ok).Enabled = false;
		cap.Captured += () => { ((Control)ok).Enabled = cap.Vk != 0; };
		int capturedVk = 0;
		bool capturedExt = false;
		((Control)ok).Click += (object _, EventArgs _) => { capturedVk = cap.Vk; capturedExt = cap.Ext; dlg.DialogResult = (DialogResult)1; };
		((Control)cancel).Click += (object _, EventArgs _) => { dlg.DialogResult = (DialogResult)2; };
		((Control)dlg).Controls.Add((Control)(object)help);
		((Control)dlg).Controls.Add((Control)(object)cap);
		((Control)dlg).Controls.Add((Control)(object)ok);
		((Control)dlg).Controls.Add((Control)(object)cancel);
		bool done = (int)dlg.ShowDialog(owner) == 1 && capturedVk != 0;
		dlg.Dispose();
		vk = capturedVk;
		ext = capturedExt;
		return done;
	}

	// A mouse button (1 left ... 5 forward), as a dialog of buttons. False when cancelled.
	public static bool AskMouse(IWin32Window owner, out int button)
	{
		button = 0;
		Form dlg = Dialog("Add a mouse button", 360, 300);
		int[] picked = new int[1];
		(int Button, string Text)[] choices = new (int, string)[5]
		{
			(MacroEdit.MouseLeft, "Left click"),
			(MacroEdit.MouseRight, "Right click"),
			(MacroEdit.MouseMiddle, "Middle click"),
			(MacroEdit.MouseBack, "Back (X1)"),
			(MacroEdit.MouseForward, "Forward (X2)")
		};
		int y = 54;
		foreach ((int b, string text) in choices)
		{
			PillButton pb = Button(text, 24, y);
			((Control)pb).Width = 312;
			int bb = b;
			((Control)pb).Click += (object _, EventArgs _) => { picked[0] = bb; dlg.DialogResult = (DialogResult)1; };
			((Control)dlg).Controls.Add((Control)(object)pb);
			y += 44;
		}
		bool done = (int)dlg.ShowDialog(owner) == 1 && picked[0] != 0;
		dlg.Dispose();
		button = picked[0];
		return done;
	}

	// Records key and mouse events in a focused window. Returns the events (empty/null when cancelled) and how the
	// caller should treat the timing (delays + fixedMs), chosen with the radio buttons.
	public static List<MacroEdit.Recorded> Record(IWin32Window owner, out string delays, out int fixedMs)
	{
		delays = MacroEdit.DelaysRecorded;
		fixedMs = 20;
		List<MacroEdit.Recorded> events = new List<MacroEdit.Recorded>();
		Stopwatch clock = new Stopwatch();

		Form dlg = Dialog("Record a macro", 460, 440);
		dlg.KeyPreview = true;

		Panel area = new Panel
		{
			Location = new Point(24, 54),
			Size = new Size(412, 150),
			BackColor = Color.FromArgb(18, 18, 18),
			BorderStyle = (BorderStyle)1
		};
		Label areaText = new Label
		{
			Dock = (DockStyle)5,
			Text = "Click here, then press keys and mouse buttons.\nThey are recorded in order.",
			TextAlign = (ContentAlignment)32,
			ForeColor = Theme.Dim,
			Font = Theme.P(10f, (FontStyle)0),
			BackColor = Color.Transparent
		};
		Label count = new Label
		{
			Location = new Point(24, 210),
			Size = new Size(412, 24),
			Text = "0 events",
			ForeColor = Theme.Green,
			Font = Theme.P(10f, (FontStyle)1),
			BackColor = Color.Transparent
		};
		((Control)area).Controls.Add((Control)(object)areaText);

		void Start()
		{
			if (!clock.IsRunning)
			{
				clock.Start();
			}
		}
		void AddKey(int vk, bool down)
		{
			Start();
			// Map the generic Shift/Ctrl/Alt to a specific side the way the hook does.
			int mapped = vk switch { 16 => 160, 17 => 162, 18 => 164, _ => vk };
			events.Add(new MacroEdit.Recorded(mapped, Keys2.IsExt(mapped), 0, down, clock.ElapsedMilliseconds));
			count.Text = events.Count + " events";
		}
		void AddMouse(MouseButtons b, bool down)
		{
			int button = b switch
			{
				MouseButtons.Left => MacroEdit.MouseLeft,
				MouseButtons.Right => MacroEdit.MouseRight,
				MouseButtons.Middle => MacroEdit.MouseMiddle,
				MouseButtons.XButton1 => MacroEdit.MouseBack,
				MouseButtons.XButton2 => MacroEdit.MouseForward,
				_ => 0
			};
			if (button == 0)
			{
				return;
			}
			Start();
			events.Add(new MacroEdit.Recorded(0, false, button, down, clock.ElapsedMilliseconds));
			count.Text = events.Count + " events";
		}

		dlg.KeyDown += (object _, KeyEventArgs e) => { AddKey((int)e.KeyCode, down: true); e.Handled = true; e.SuppressKeyPress = true; };
		dlg.KeyUp += (object _, KeyEventArgs e) => { AddKey((int)e.KeyCode, down: false); e.Handled = true; };
		area.MouseDown += (object _, MouseEventArgs e) => { ((Control)area).Focus(); AddMouse(e.Button, down: true); };
		area.MouseUp += (object _, MouseEventArgs e) => AddMouse(e.Button, down: false);

		// Timing choice.
		Label tLbl = new Label { Text = "Timing:", Location = new Point(24, 244), AutoSize = true, ForeColor = Theme.Text, Font = Theme.P(10f, (FontStyle)1), BackColor = Color.Transparent };
		RadioButton rRec = Radio("Keep the real timing", 24, 272, true);
		RadioButton rNone = Radio("No waits (all at once)", 24, 300, false);
		RadioButton rFix = Radio("Fixed 20 ms between steps", 24, 328, false);
		((Control)dlg).Controls.Add((Control)(object)tLbl);
		((Control)dlg).Controls.Add((Control)(object)rRec);
		((Control)dlg).Controls.Add((Control)(object)rNone);
		((Control)dlg).Controls.Add((Control)(object)rFix);

		// Laid out without overlap inside the ~436px usable width: CLEAR left, then DONE and CANCEL right-aligned.
		PillButton clear = Button("CLEAR", 24, 368);
		PillButton cancel = Button("CANCEL", 0, 368);
		PillButton done = Button("DONE", 0, 368, primary: true);
		((Control)cancel).Left = 436 - ((Control)cancel).Width;
		((Control)done).Left = ((Control)cancel).Left - ((Control)done).Width - 8;
		((Control)clear).Click += (object _, EventArgs _) => { events.Clear(); clock.Reset(); count.Text = "0 events"; ((Control)area).Focus(); };
		((Control)done).Click += (object _, EventArgs _) => { dlg.DialogResult = (DialogResult)1; };
		((Control)cancel).Click += (object _, EventArgs _) => { dlg.DialogResult = (DialogResult)2; };

		((Control)dlg).Controls.Add((Control)(object)area);
		((Control)dlg).Controls.Add((Control)(object)count);
		((Control)dlg).Controls.Add((Control)(object)clear);
		((Control)dlg).Controls.Add((Control)(object)done);
		((Control)dlg).Controls.Add((Control)(object)cancel);
		dlg.Shown += (object _, EventArgs _) => ((Control)area).Focus();

		bool ok = (int)dlg.ShowDialog(owner) == 1;
		delays = rNone.Checked ? MacroEdit.DelaysNone : rFix.Checked ? MacroEdit.DelaysFixed : MacroEdit.DelaysRecorded;
		fixedMs = 20;
		dlg.Dispose();
		return ok ? events : null;
	}

	// ---- Shared dark dialog pieces --------------------------------------------------------------------------

	private static Form Dialog(string title, int w, int h)
	{
		Form f = new Form
		{
			Text = title,
			Width = w,
			Height = h,
			StartPosition = (FormStartPosition)4,
			BackColor = Theme.Bg,
			ForeColor = Theme.Text,
			FormBorderStyle = (FormBorderStyle)3,
			MaximizeBox = false,
			MinimizeBox = false,
			ShowInTaskbar = false,
			Font = Theme.F(10f, (FontStyle)0)
		};
		Ui.DarkTitleBar(f);
		return f;
	}

	private static PillButton Button(string text, int x, int y, bool primary = false)
	{
		PillButton b = new PillButton { Primary = primary };
		((Control)b).Text = text;
		((Control)b).Location = new Point(x, y);
		((Control)b).Width = Math.Max(76, TextRenderer.MeasureText(text, Theme.F(9.5f, (FontStyle)1)).Width + 28);
		((Control)b).Height = 34;
		return b;
	}

	private static RadioButton Radio(string text, int x, int y, bool on)
	{
		return new RadioButton
		{
			Text = text,
			Location = new Point(x, y),
			AutoSize = true,
			Checked = on,
			ForeColor = Theme.Text,
			BackColor = Color.Transparent,
			Font = Theme.P(9.5f, (FontStyle)0)
		};
	}
}
