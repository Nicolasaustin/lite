using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiteSynapse;

// The Performance tab: everything LiteSynapse can do to the PC rather than to a profile.
//
// Every row on every page reads the same way: a badge that says whether the thing is ON or OFF and whether
// that is HELPING or HURTING performance, and a switch showing the side it is on. Flip the switch to the side
// you want - the row then says what will happen and whether that helps - and press APPLY. FIX ALL HURTING
// flips every row that is costing performance (never a security trade-off) so the whole page can be fixed in
// two clicks. Nothing changes until APPLY, and every row can go back the other way the same way.
//
//   Razer        what Razer's own software left behind (reported; three clean-up buttons)
//   Device       whether Windows may power the mouse and keyboard down
//   Windows      the settings catalog, grouped
//   Services     Windows services worth turning off on a gaming PC
//   Start-up     what starts with Windows, and a Defender scan of any of it
internal sealed class PerfPage
{
	// Pill order; MainForm shows these as the Performance tab's sub-tabs.
	public static readonly string[] Pills = new string[5] { "RAZER", "DEVICE", "WINDOWS", "SERVICES", "START-UP" };

	private const string Legend = "Each row says whether it is ON or OFF, and whether that is HELPING or HURTING performance. " +
		"Flip a switch to the side you want, then press APPLY. FIX ALL HURTING flips everything that is costing you.";

	private readonly Service app;

	private readonly Form owner;

	private readonly Panel[] pages = new Panel[5];

	private readonly SwitchList[] lists = new SwitchList[5];

	private readonly Label[] notes = new Label[5];

	private readonly Label[] totals = new Label[5];

	private readonly List<PillButton> buttons = new List<PillButton>();

	// Buttons whose caption carries a count: (page, button, caption with {0}, caption with nothing to count,
	// which count). A button that says how many rows it will change is the difference between "I meant that"
	// and "I did not".
	private readonly List<(int Page, PillButton Button, string Some, string None, bool Pending)> counted =
		new List<(int, PillButton, string, string, bool)>();

	// Which pages have been filled since the window opened, so switching back does not re-read the PC.
	private readonly bool[] loaded = new bool[5];

	// Each page's header layout, re-run when its result line changes length.
	private readonly Action[] layouts = new Action[5];

	// The page on screen, so a job started on another one still refreshes it when it finishes.
	private int current;

	private bool busy;

	public PerfPage(Service app, Form owner)
	{
		this.app = app;
		this.owner = owner;
		pages[0] = BuildRazer();
		pages[1] = BuildDevice();
		pages[2] = BuildWindows();
		pages[3] = BuildServices();
		pages[4] = BuildStartup();
		// A job started by the window before this one (it closed for a game while an apply ran) is still
		// running: wait for it rather than offer a second one, and show what it said when it ends.
		Task<string> running = app.PerfJob;
		if (running != null && !running.IsCompleted)
		{
			Attach(Math.Max(0, Math.Min(pages.Length - 1, app.PerfJobPage)), "Still applying what was started before...", running);
		}
	}

	public Panel Page(int pill)
	{
		return pages[Math.Max(0, Math.Min(pages.Length - 1, pill))];
	}

	// Called when the page becomes visible. Reading the PC costs a moment (every service, a signature per
	// start-up program, a PowerShell start), so it happens once per window rather than on every tab click,
	// and CHECK AGAIN re-reads on demand.
	//
	// The first time a page is shown, whatever it needs is fetched in the background rather than leaving the
	// user looking at "not read yet" above a button they would press anyway. Opening the tab is the ask.
	public void Show(int pill)
	{
		int i = Math.Max(0, Math.Min(pages.Length - 1, pill));
		current = i;
		if (loaded[i])
		{
			return;
		}
		loaded[i] = true;
		Refresh(i);
		// The tray's "Performance" opens the window straight onto this tab, which means Goto runs before the
		// form is shown and its handle does not exist yet. Work() needs the handle to post its result back, so
		// in that case the fetch waits for the window to appear - otherwise it would be dropped and the page
		// would sit there with its buttons disabled.
		if (!owner.IsHandleCreated)
		{
			int page = i;
			void WhenShown(object sender, EventArgs e)
			{
				owner.Shown -= WhenShown;
				Fetch(page);
			}
			owner.Shown += WhenShown;
			return;
		}
		Fetch(i);
	}

	// Reads whatever the page needs, the first time it is opened.
	private void Fetch(int i)
	{
		switch (i)
		{
		case 0:
			// A shallow check ran at start-up; the services, tasks and firewall rules need the full one.
			if (!app.Razer.Deep)
			{
				Work(0, "Checking what Razer left behind...", () =>
				{
					app.RefreshRazer(deep: true);
					return "Checked.";
				});
			}
			break;
		case 1:
			if (!app.Tuning.Checked || app.TuningStale)
			{
				Work(1, "Asking Windows about the devices...", () =>
				{
					app.RefreshTuning();
					return "Checked.";
				});
			}
			break;
		default:
			if (!app.Scan.Checked || app.ScanStale)
			{
				Work(i, "Reading the settings...", () =>
				{
					app.RunScanAsync().GetAwaiter().GetResult();
					return "Checked.";
				});
			}
			break;
		}
	}

	// ---- Layout helpers -------------------------------------------------------------------------------

	private readonly struct Action2
	{
		public readonly string Some;

		public readonly string None;

		public readonly bool Primary;

		public readonly Action Click;

		// Some: the caption with a count ("APPLY {0} CHANGES"); None: without one. counts: "pending" for the
		// rows waiting to be applied, "fixable" for the rows FIX ALL would flip, null for no count.
		public readonly string Counts;

		public Action2(string some, string none, bool primary, string counts, Action click)
		{
			Some = some;
			None = none;
			Primary = primary;
			Counts = counts;
			Click = click;
		}
	}

	private static Action2 Plain(string text, bool primary, Action click)
	{
		return new Action2(text, text, primary, null, click);
	}

	private Panel Shell(int index, string title, string blurb, bool switches, params Action2[] actions)
	{
		Panel page = new Panel
		{
			Dock = DockStyle.Fill,
			BackColor = Theme.Bg,
			Padding = new Padding(0)
		};
		Panel head = new Panel
		{
			Dock = DockStyle.Top,
			Height = 150,
			BackColor = Theme.Bg,
			Padding = new Padding(0)
		};
		head.Controls.Add(new Label
		{
			Text = title,
			AutoSize = true,
			ForeColor = Theme.Text,
			BackColor = Color.Transparent,
			Font = Theme.F(13f, FontStyle.Bold),
			Location = new Point(18, 12)
		});
		// How many rows are hurting and helping, next to the title: the page's answer at a glance.
		Label count = new Label
		{
			Text = "",
			AutoSize = true,
			ForeColor = Theme.Dim,
			BackColor = Color.Transparent,
			Font = Theme.F(10f, FontStyle.Bold),
			Location = new Point(18, 40)
		};
		head.Controls.Add(count);
		totals[index] = count;
		Label text = new Label
		{
			Text = blurb + (switches ? "\n" + Legend : ""),
			AutoSize = false,
			ForeColor = Theme.Dim,
			BackColor = Color.Transparent,
			Font = Theme.F(9f),
			Location = new Point(18, 64)
		};
		head.Controls.Add(text);
		List<PillButton> row = new List<PillButton>();
		foreach (Action2 a in actions)
		{
			PillButton b = new PillButton
			{
				Primary = a.Primary
			};
			b.Text = a.None;
			// Wide enough for the caption with a three-digit count in it, so it never clips.
			string widest = a.Some.Contains("{0}") ? string.Format(a.Some, 999) : a.Some;
			b.Width = (int)Math.Max(Tx.Measure(widest, b.Font).Width, Tx.Measure(a.None, b.Font).Width) + 40;
			Action handler = a.Click;
			int onPage = index;
			b.Click += (object _1, EventArgs _2) => Guarded(onPage, handler);
			head.Controls.Add(b);
			buttons.Add(b);
			row.Add(b);
			if (a.Counts != null)
			{
				counted.Add((index, b, a.Some, a.None, a.Counts == "pending"));
			}
		}
		Label note = new Label
		{
			Text = "",
			ForeColor = Theme.Green,
			BackColor = Color.Transparent,
			Font = Theme.F(9.5f),
			AutoSize = false
		};
		head.Controls.Add(note);
		notes[index] = note;
		// Laid out to the width the page actually has: the text wraps, the buttons wrap onto a second line
		// rather than running off the edge, and the result line sits under them at full width.
		bool laying = false;
		void Layout()
		{
			if (laying || head.Width <= 0)
			{
				return;
			}
			laying = true;
			try
			{
				int room = Math.Max(300, head.Width - 36);
				// Measured here rather than left to AutoSize, whose new height is not known until a layout pass
				// that may not have run yet.
				int textH = TextRenderer.MeasureText(text.Text, text.Font, new Size(room, int.MaxValue),
					TextFormatFlags.WordBreak).Height;
				text.SetBounds(18, 64, room, textH + 4);
				int x = 18;
				int y = text.Bottom + 10;
				foreach (PillButton b in row)
				{
					if (x > 18 && x + b.Width > 18 + room)
					{
						x = 18;
						y += b.Height + 8;
					}
					b.Location = new Point(x, y);
					x += b.Width + 10;
				}
				int bottom = (row.Count > 0) ? (y + row[0].Height) : y;
				// The result line wraps: "Not changed: ..." and "Restart the PC" are exactly the parts that
				// say whether a change went through, and a one-line ellipsis cut them off.
				int noteH = (note.Text.Length == 0) ? 22 : Math.Min(4 * (note.Font.Height + 2),
					TextRenderer.MeasureText(note.Text, note.Font, new Size(room, int.MaxValue), TextFormatFlags.WordBreak).Height + 4);
				note.SetBounds(18, bottom + 6, room, Math.Max(22, noteH));
				head.Height = note.Bottom + 8;
			}
			finally
			{
				laying = false;
			}
		}
		head.Resize += (object _1, EventArgs _2) => Layout();
		layouts[index] = Layout;
		Layout();

		SwitchList list = new SwitchList
		{
			Dock = DockStyle.Fill,
			BackColor = Theme.Bg
		};
		int listPage = index;
		list.Changed += () => ShowCounts(listPage);
		lists[index] = list;
		page.Controls.Add(list);
		page.Controls.Add(head);
		return page;
	}

	// Every button goes through here: one job at a time, and a failure reaches the user instead of the log.
	private void Guarded(int index, Action work)
	{
		if (busy)
		{
			return;
		}
		try
		{
			work();
		}
		catch (Exception ex)
		{
			Cfg.Log("performance: " + ex);
			Say(index, "Something went wrong: " + ex.Message, problem: true);
		}
	}

	private void Say(int index, string text, bool problem = false)
	{
		int i = Math.Max(0, Math.Min(notes.Length - 1, index));
		Label note = notes[i];
		if (note == null || note.IsDisposed)
		{
			return;
		}
		note.ForeColor = problem ? Color.FromArgb(235, 120, 120) : Theme.Green;
		note.Text = text ?? "";
		layouts[i]?.Invoke();
	}

	// The counts on this page's buttons and next to its title.
	private void ShowCounts(int index)
	{
		SwitchList list = lists[index];
		if (list == null)
		{
			return;
		}
		int pending = list.PendingCount;
		int fixable = list.Fixable.Count();
		foreach ((int page, PillButton button, string some, string none, bool isPending) in counted)
		{
			if (page != index || button.IsDisposed)
			{
				continue;
			}
			int n = isPending ? pending : fixable;
			button.Text = (n > 0) ? string.Format(some, n) : none;
			button.Invalidate();
		}
		Label total = totals[index];
		if (total != null && !total.IsDisposed)
		{
			int hurt = list.Items.Count((SwitchList.Row r) => !r.Header && r.Verdict == PerfModel.Hurting);
			int help = list.Items.Count((SwitchList.Row r) => !r.Header && r.Verdict == PerfModel.Helping);
			total.ForeColor = (hurt > 0) ? Color.FromArgb(240, 150, 60) : Theme.Green;
			int unknown = list.Items.Count((SwitchList.Row r) => !r.Header && r.Verdict == PerfModel.Unknown);
			string tail = (unknown > 0) ? $"  ·  {unknown} can't tell" : "";
			total.Text = (hurt + help + unknown == 0) ? "" : ((hurt > 0)
				? $"{hurt} hurting performance  ·  {help} helping" + tail
				: $"Nothing here is hurting performance  ·  {help} helping" + tail);
		}
	}

	// Runs the slow part off the UI thread, with the buttons disabled while it does.
	private void Work(int index, string pending, Func<string> job)
	{
		if (busy)
		{
			return;
		}
		// Below normal priority: these reads (every service, signatures, a WMI query) must not compete with a
		// game that is starting while they finish.
		Task<string> task = Task.Run(() =>
		{
			System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.BelowNormal;
			try
			{
				return job();
			}
			finally
			{
				System.Threading.Thread.CurrentThread.Priority = System.Threading.ThreadPriority.Normal;
			}
		});
		app.PerfJob = task;
		app.PerfJobPage = index;
		Attach(index, pending, task);
	}

	// Disables the buttons until the job ends, then refreshes and says what it said.
	private void Attach(int index, string pending, Task<string> task)
	{
		busy = true;
		foreach (PillButton b in buttons)
		{
			b.Enabled = false;
		}
		Say(index, pending);
		task.ContinueWith((Task<string> t) =>
		{
			if (owner == null || owner.IsDisposed || !owner.IsHandleCreated)
			{
				busy = false;
				return;
			}
			try
			{
				owner.BeginInvoke((Action)(() =>
				{
					busy = false;
					foreach (PillButton b in buttons)
					{
						b.Enabled = true;
					}
					string message = t.IsFaulted
						? ("Could not finish: " + (t.Exception?.GetBaseException().Message ?? "unknown error"))
						: t.Result;
					// The Windows, Services and Start-up pages all read the same scan, so a change made on one
					// leaves the others showing the state from before it. Mark them for a re-fill and redraw the
					// one in front now.
					Invalidate();
					loaded[index] = true;
					Refresh(index);
					Say(index, message, t.IsFaulted);
					// The page on screen may be another one, opened while this ran: its own read was dropped
					// while busy, so fill it now and start that read.
					if (current != index)
					{
						loaded[current] = true;
						Refresh(current);
						Fetch(current);
					}
				}));
			}
			catch (InvalidOperationException)
			{
				// The window closed between the check and the post.
				busy = false;
			}
		});
	}

	// The two buttons every switch page has.
	private Action2 FixAll(int index)
	{
		return new Action2("FIX ALL HURTING ({0})", "FIX ALL HURTING", false, "fixable", () =>
		{
			SwitchList list = lists[index];
			int n = list.FixAll();
			List<SwitchList.Row> rows = list.Items.Where((SwitchList.Row r) => !r.Header).ToList();
			int left = rows.Count((SwitchList.Row r) => r.Verdict == PerfModel.Hurting && !r.Pending);
			int unknown = rows.Count((SwitchList.Row r) => r.Verdict == PerfModel.Unknown && r.Can && !r.Pending);
			int pending = list.PendingCount;
			string rest = (left > 0)
				? $" {left} more hurting row(s) are marked 'use with care' or cannot be switched here - read them and switch them yourself."
				: "";
			if (unknown > 0)
			{
				rest += $" {unknown} row(s) can't be read" + ((index == 1 || index == 2) ? " - press CHECK AS ADMIN." : ".");
			}
			if (n > 0)
			{
				Say(index, $"{n} switch(es) flipped to the helping side. Check them, then press APPLY." + rest);
			}
			else if (pending > 0)
			{
				Say(index, $"{pending} change(s) are waiting - press APPLY." + rest);
			}
			else if (left > 0 || unknown > 0)
			{
				Say(index, "Nothing safe to flip for you." + rest);
			}
			else
			{
				Say(index, "Nothing on this page is hurting performance.");
			}
		});
	}

	private Action2 ApplyButton(int index)
	{
		return new Action2("APPLY {0} CHANGE(S)", "APPLY", true, "pending", () => ApplyPending(index));
	}

	private Action2 Undo(int index)
	{
		return Plain("CLEAR", false, () =>
		{
			lists[index].ClearPending();
			Say(index, "Nothing is waiting to be applied.");
		});
	}

	// ---- Applying -------------------------------------------------------------------------------------

	private void ApplyPending(int index)
	{
		List<SwitchList.Row> rows = lists[index].PendingRows.ToList();
		if (rows.Count == 0)
		{
			Say(index, "Flip a switch first: nothing is waiting to be applied.", problem: true);
			return;
		}
		// Moving a security feature to its helping side gives up security; ask by name. (Moving one back to
		// its safe default never needs asking.)
		List<SwitchList.Row> risky = rows.Where((SwitchList.Row r) => r.Risk == Tweaks.Risky && r.Want == r.HelpsWhenOn).ToList();
		if (risky.Count > 0)
		{
			string names = string.Join("\n  ", risky.Select((SwitchList.Row r) => "- " + r.Title));
			DialogResult answer = MessageBox.Show(owner,
				"These give up security or a part of Windows you may need:\n\n  " + names +
				"\n\nEach one's cost is written on its row. Change them anyway?",
				"LiteSynapse", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
			if (answer != DialogResult.Yes)
			{
				Say(index, "Nothing was changed.");
				return;
			}
		}
		if (index == 1)
		{
			ApplyDevices(rows);
			return;
		}
		// A row's id is "<kind>:<what>"; the kind travels with it so Apply never has to guess which of three
		// namespaces a bare id belongs to (a start-up entry's id is a Run value name any program can choose).
		List<(string Kind, string Id, bool On)> choices = new List<(string, string, bool)>();
		foreach (SwitchList.Row r in rows)
		{
			(string kind, string what) = Split(r.Id);
			bool want = r.Want.Value;
			switch (kind)
			{
			case "tweak":
				// A setting is applied or not; the switch is the thing's side, which is the same or the
				// opposite depending on the setting - and that never depends on the PC, unlike which side helps.
				choices.Add((kind, what, PerfModel.TweakOn(want, r.AppliedOn)));
				break;
			case "svc":
			case "start":
				choices.Add((kind, what, want));
				break;
			}
		}
		if (choices.Count == 0)
		{
			Say(index, "Nothing that can be switched from here was changed.", problem: true);
			return;
		}
		Work(index, "Applying " + choices.Count + " change(s)...",
			() => app.ScanApplyAsync(choices).GetAwaiter().GetResult());
	}

	// The Device page's switches are "power saving": OFF keeps the device awake.
	private void ApplyDevices(List<SwitchList.Row> rows)
	{
		List<string> awake = new List<string>();
		List<string> allow = new List<string>();
		bool? plan = null;
		foreach (SwitchList.Row r in rows)
		{
			if (r.Id == "plan")
			{
				plan = r.Want.Value;
			}
			else if (r.Id.StartsWith("dev:", StringComparison.Ordinal))
			{
				(r.Want.Value ? allow : awake).Add(r.Id.Substring(4));
			}
		}
		Work(1, "Waiting for Windows to ask permission...",
			() => app.TuneDevicesAsync(awake, allow, plan).GetAwaiter().GetResult());
	}

	// "tweak:accel" -> ("tweak", "accel"); "file:C:\\x.exe" -> ("file", "C:\\x.exe"). Only the first colon
	// separates, because the rest of the id may be a Windows path.
	private static (string Kind, string What) Split(string id)
	{
		string s = id ?? "";
		int at = s.IndexOf(':');
		return (at < 0) ? ("", s) : (s.Substring(0, at), s.Substring(at + 1));
	}

	// ---- Razer ---------------------------------------------------------------------------------------

	private Panel BuildRazer()
	{
		return Shell(0, "Razer software left on this PC",
			"What Razer's own software installed and still runs. LiteSynapse talks to the devices directly, so none of it " +
			"is needed. Anything ON here is HURTING: it runs alongside your game. The filter drivers are left alone " +
			"unless you say so, because removing the wrong one takes the mouse with it.",
			false,
			Plain("CHECK AGAIN", false, () => Work(0, "Checking...", () =>
			{
				app.RefreshRazer(deep: true);
				return "Checked.";
			})),
			Plain("TURN OFF RAZER SERVICES", true, () => Work(0, "Waiting for Windows to ask permission...",
				() => app.RazerCleanup("disableServices"))),
			Plain("TURN OFF GAME MANAGER", false, () => Work(0, "Waiting for Windows to ask permission...",
				() => app.RazerCleanup("disableGameManager"))),
			Plain("FILTER DRIVERS", false, () => Work(0, "Working...", () => app.RazerCleanup("driverGuide"))));
	}

	private void FillRazer()
	{
		RazerLeftovers r = app.Razer;
		List<SwitchList.Row> rows = new List<SwitchList.Row>();
		if (!r.Checked)
		{
			rows.Add(Header("Not checked yet", "Press CHECK AGAIN."));
			SetItems(0, rows);
			return;
		}
		rows.Add(Header("Synapse", ""));
		rows.Add(Info("Razer Synapse installed", r.SynapseInstalled ? "Installed on this PC." : "Not installed.",
			r.SynapseInstalled ? "on" : "off"));
		if (r.SynapseRunning)
		{
			rows.Add(Info("Razer Synapse running",
				"While it runs it can change the lighting and switch the mouse's slots behind LiteSynapse. Close it from the tray.",
				"on", Tweaks.Caution, "Fights with LiteSynapse"));
		}
		rows.Add(Info("Razer Game Manager Service 3", GmsText(r.GmsService), PerfModel.ServiceState(r.GmsService)));
		if (r.RzCommon)
		{
			rows.Add(Info("RzCommon folder", "Razer's shared files are still in ProgramData.", "on"));
		}

		rows.Add(Header("Services", !r.Deep
			? "Not read yet: press CHECK AGAIN for the services."
			: (r.Services.Count == 0
				? "None of Razer's services are set to start."
				: "Each of these starts with Windows. TURN OFF RAZER SERVICES sets them all to disabled.")));
		foreach (RazerService s in r.Services)
		{
			rows.Add(Info(s.Label ?? s.Name,
				(s.Driver ? "A driver service. " : "") + (s.Network ? "Opens a network or localhost listener." : ""),
				PerfModel.ServiceState(s.State), s.Driver ? Tweaks.Caution : Tweaks.Safe, s.Network ? "Uses the network" : ""));
		}

		rows.Add(Header("Starts with Windows", (!r.Deep && r.RunOnce.Count == 0)
			? "The Run entries were read; press CHECK AGAIN for the RunOnce ones."
			: (r.Autostart.Count == 0 && r.RunOnce.Count == 0
				? "Nothing of Razer's starts with Windows."
				: "Razer entries in this account's start-up list.")));
		foreach (string a in r.Autostart)
		{
			rows.Add(Info(a, "A Run entry that starts Razer software.", "on"));
		}
		foreach (string a in r.RunOnce)
		{
			rows.Add(Info(a, "A RunOnce entry: it runs at the next sign-in and then removes itself.", "on"));
		}

		// A shallow check does not read the task cache or the firewall at all, so "none" would be a claim it
		// has not earned. Say which it is.
		rows.Add(Header("Scheduled tasks", !r.Deep
			? "Not read yet: press CHECK AGAIN for the scheduled tasks."
			: (r.Tasks.Count == 0 && r.UpgradeTask == "absent"
				? "No Razer scheduled tasks."
				: "Tasks Razer's installers left behind.")));
		if (r.UpgradeTask != "absent")
		{
			rows.Add(Info("Razer upgrade task", "Checks for and installs Synapse updates on a schedule.", PerfModel.ServiceState(r.UpgradeTask)));
		}
		foreach (RazerTask t in r.Tasks)
		{
			// Whether a task is enabled is not in the task cache, so this does not claim to know. The
			// clean-up disables it either way, which costs nothing if it was off already.
			rows.Add(Info(t.Path, "A scheduled task in a Razer folder. Whether Windows has it enabled is not readable " +
				"from here; TURN OFF RAZER SERVICES disables it either way.", "unknown"));
		}

		rows.Add(Header("Firewall and drivers", ""));
		rows.Add(Info("Firewall rules for Razer software", !r.Deep
			? "Not read yet: press CHECK AGAIN for the firewall rules."
			: (r.FirewallRules == 0
				? "No firewall rules for Razer software."
				: $"{r.FirewallRules} rule(s) let Razer software through the firewall."),
			!r.Deep ? "unknown" : (r.FirewallRules == 0 ? "off" : "on")));
		if (r.FilterDrivers.Count == 0)
		{
			rows.Add(Info("Razer filter drivers", "None of Razer's filter drivers are on the devices.", "off"));
		}
		else
		{
			foreach (string d in r.FilterDrivers)
			{
				rows.Add(Info(d,
					"A Razer filter driver sits between Windows and the device. LiteSynapse does not need it. Removing it is " +
					"done by a script you run yourself (FILTER DRIVERS), because a mistake here leaves you without a mouse.",
					"on", Tweaks.Caution, "Remove by hand"));
			}
		}
		SetItems(0, rows);
	}

	private static string GmsText(string state)
	{
		switch (state)
		{
		case "absent":
			return "Not installed.";
		case "disabled":
			return "Installed but disabled.";
		default:
			return "Installed and set to start. It watches which game is in front so Synapse can switch profiles - " +
				"LiteSynapse does that itself.";
		}
	}

	// ---- Device --------------------------------------------------------------------------------------

	private Panel BuildDevice()
	{
		return Shell(1, "Device latency",
			"Whether Windows may power the mouse and keyboard down between reports. A device that is allowed to idle has " +
			"to wake up before the first movement of a flick gets through - the one kind of latency you feel rather than measure.",
			true,
			Plain("CHECK AGAIN", false, () => Work(1, "Asking Windows about the devices...", () =>
			{
				app.RefreshTuning();
				return DeviceTuning.Summary(app.Tuning);
			})),
			Plain("CHECK AS ADMIN", false, () => Work(1, "Waiting for Windows to ask permission...",
				() => app.CheckDevicesAsAdminAsync().GetAwaiter().GetResult())),
			FixAll(1),
			ApplyButton(1),
			Undo(1));
	}

	private void FillDevice()
	{
		DeviceTuningState s = app.Tuning;
		List<SwitchList.Row> rows = new List<SwitchList.Row>();
		if (!s.Checked)
		{
			rows.Add(Header("Not read yet", "Press CHECK AGAIN."));
			SetItems(1, rows);
			return;
		}
		string where = s.AdminAtUtc.HasValue
			? $" Windows only tells an administrator, so these are as last read with administrator rights ({s.AdminAtUtc.Value.ToLocalTime():g})."
			: "";
		rows.Add(Header("Your devices", s.Devices.Count > 0
			? "\"Allow the computer to turn off this device to save power\" on each device's Power Management tab." + where
			: (s.Readable
				? "No Razer device answered. Plug the mouse in and press CHECK AGAIN."
				: "Windows would not list the devices. Press CHECK AS ADMIN.")));
		foreach (TunedDevice d in s.Devices)
		{
			rows.Add(new SwitchList.Row
			{
				Id = "dev:" + d.Instance,
				Title = "Power saving: " + d.Name,
				Desc = "ON lets Windows power the device down when it thinks it is idle; OFF keeps it awake.",
				Note = d.Known ? "" : "Windows will not say how this one is set without administrator rights. Switching it " +
					"runs as administrator and reads it back, or press CHECK AS ADMIN.",
				State = d.Known ? (d.PowerSaving ? "on" : "off") : "unknown",
				HelpsWhenOn = false,
				Risk = Tweaks.Safe,
				Can = true
			});
		}
		rows.Add(Header("Power plan", "The active plan's own USB setting, which applies to every USB port."));
		rows.Add(new SwitchList.Row
		{
			Id = "plan",
			Title = "USB selective suspend",
			Desc = s.UsbSuspend switch
			{
				"on" => "The active power plan lets Windows suspend idle USB devices, which includes the mouse.",
				"off" => "The active power plan never suspends idle USB devices.",
				_ => "Could not read it from the active power plan."
			},
			State = s.UsbSuspend,
			HelpsWhenOn = false,
			// Switching it does not need to know where it is now: powercfg sets the active plan either way.
			Can = true
		});
		rows.Add(Header("The rest of the input settings are on the Windows page",
			"Pointer acceleration, pointer speed, the background raw-input cap and the mouse and keyboard buffers."));
		SetItems(1, rows);
	}

	// ---- Windows settings ----------------------------------------------------------------------------

	private Panel BuildWindows()
	{
		return Shell(2, "Windows settings",
			"From the FR33THY, Chris Titus and other trusted guides. Each row says where it comes from and what it costs. " +
			"The last group trades security for speed, so FIX ALL HURTING never touches it.",
			true,
			Plain("CHECK AGAIN", false, () => Work(2, "Reading the settings...", () =>
			{
				app.RunScanAsync().GetAwaiter().GetResult();
				return "Checked.";
			})),
			Plain("CHECK AS ADMIN", false, () => Work(2, "Waiting for Windows to ask permission...",
				() => app.CheckSettingsAsAdminAsync().GetAwaiter().GetResult())),
			FixAll(2),
			ApplyButton(2),
			Undo(2));
	}

	private void FillWindows()
	{
		ScanResult scan = app.Scan;
		List<SwitchList.Row> rows = new List<SwitchList.Row>();
		if (!scan.Checked)
		{
			rows.Add(Header("Not read yet", "Press CHECK AGAIN."));
			SetItems(2, rows);
			return;
		}
		foreach ((string gid, string gtitle, string gblurb) in Tweaks.Groups)
		{
			List<TweakRow> group = scan.Tweaks.Where((TweakRow t) => t.Group == gid).ToList();
			if (group.Count == 0)
			{
				continue;
			}
			rows.Add(Header(gtitle, gblurb));
			foreach (TweakRow t in group)
			{
				string thing = PerfModel.ThingState(t.State, t.AppliedOn);
				bool helping = PerfModel.Verdict(thing, t.PerfOn, t.Risk) == PerfModel.Helping;
				rows.Add(new SwitchList.Row
				{
					Id = "tweak:" + t.Id,
					Title = (t.Thing.Length > 0) ? t.Thing : t.Title,
					Desc = t.What + " Source: " + Tweaks.SourceText(t.Source) + ".",
					Cost = Costly(t),
					Note = (t.OneWay && helping)
						? "Already right, so there is no switch: this one has no other value worth setting from here."
						: HowKnown(t),
					State = thing,
					HelpsWhenOn = t.PerfOn,
					AppliedOn = t.AppliedOn,
					Risk = t.Risk,
					// A one-way setting has nothing to switch back to once it is applied.
					Can = !(t.OneWay && helping)
				});
			}
		}
		SetItems(2, rows);
	}

	// Why the state on a row is what it is, when it is not simply read off the PC.
	private static string HowKnown(TweakRow t)
	{
		if (t.Restart)
		{
			return "Set by LiteSynapse this session - takes effect after a restart.";
		}
		if (t.Remembered)
		{
			return "Windows does not report this one without administrator rights, so this is what LiteSynapse last set. " +
				"CHECK AS ADMIN reads it.";
		}
		if (t.FromAdmin.HasValue)
		{
			return $"Read with administrator rights ({t.FromAdmin.Value.ToLocalTime():g}).";
		}
		if (t.State == "other")
		{
			return "Set to a value of its own by something else (another tweak tool, or a policy). Switching it puts ours or Windows' back.";
		}
		if (t.State == "unknown")
		{
			return "Windows only shows this one to an administrator. Press CHECK AS ADMIN to read it, or switch it to set it.";
		}
		if (t.State == "part")
		{
			return "Partly set: some of what it covers is not (a network adapter added since, or another tool set only part). " +
				"Switching it ON sets all of it.";
		}
		return "";
	}

	private static string Costly(TweakRow t)
	{
		List<string> parts = new List<string>();
		if (t.Cost.Length > 0)
		{
			parts.Add(t.Cost);
		}
		if (t.NeedsReboot && t.Cost.IndexOf("restart", StringComparison.OrdinalIgnoreCase) < 0
			&& t.Cost.IndexOf("sign-in", StringComparison.OrdinalIgnoreCase) < 0)
		{
			parts.Add("Needs a restart.");
		}
		return string.Join(" ", parts);
	}

	// ---- Services ------------------------------------------------------------------------------------

	private Panel BuildServices()
	{
		return Shell(3, "Windows services",
			"Services worth turning off on a gaming PC, from FR33THY's guide, each with what you give up. The input stack " +
			"the mouse and keyboard run on, and the services Windows will not boot without, are not on this list at all.",
			true,
			Plain("CHECK AGAIN", false, () => Work(3, "Reading the services...", () =>
			{
				app.RunScanAsync().GetAwaiter().GetResult();
				return "Checked.";
			})),
			FixAll(3),
			ApplyButton(3),
			Undo(3));
	}

	private void FillServices()
	{
		ScanResult scan = app.Scan;
		List<SwitchList.Row> rows = new List<SwitchList.Row>();
		if (!scan.Checked)
		{
			rows.Add(Header("Not read yet", "Press CHECK AGAIN."));
			SetItems(3, rows);
			return;
		}
		List<ScanItem> services = scan.Items.Where((ScanItem i) => i.Kind == "service").ToList();
		foreach ((string risk, string title, string blurb) in new[]
		{
			(Tweaks.Safe, "Safe to turn off", "Nothing you use on a gaming PC stops working."),
			(Tweaks.Caution, "Turns a feature off", "Each row says which feature. Read it before switching it."),
			(Tweaks.Risky, "Security, updates or networking",
				"On the list because the guides include them. ON is the safe default here, and FIX ALL HURTING leaves them alone.")
		})
		{
			List<ScanItem> group = services.Where((ScanItem i) => i.Risk == risk)
				.OrderBy((ScanItem i) => i.Name, StringComparer.OrdinalIgnoreCase).ToList();
			if (group.Count == 0)
			{
				continue;
			}
			rows.Add(Header(title, blurb));
			foreach (ScanItem i in group)
			{
				bool can = i.CanDisable || i.CanEnable;
				rows.Add(new SwitchList.Row
				{
					Id = "svc:" + i.Id,
					Title = i.Name,
					Desc = i.Costs,
					Cost = "Turning it off: " + i.Lose,
					Note = ServiceNote(i, can),
					State = PerfModel.ServiceState(i.State),
					HelpsWhenOn = i.KeepOn,
					Badge = i.Network ? "Uses the network" : (i.KeepOn ? "Keep on" : ""),
					Risk = i.Risk,
					Can = can
				});
			}
		}
		if (rows.Count == 0)
		{
			rows.Add(Header("Nothing to show", "None of the catalog's services are installed on this PC."));
		}
		SetItems(3, rows);
	}

	private static string ServiceNote(ScanItem i, bool can)
	{
		if (i.State == "disabled" && i.Running)
		{
			return "Disabled, but still running until the next restart.";
		}
		if (i.State == "manual" && !i.Running)
		{
			return "Not running now: it starts only when something asks for it.";
		}
		if (!can)
		{
			return (i.State == "disabled")
				? "Windows ships this one disabled too, so there is nothing to turn back on."
				: "Reported only: LiteSynapse does not switch this one.";
		}
		return "";
	}

	// ---- Start-up ------------------------------------------------------------------------------------

	private Panel BuildStartup()
	{
		return Shell(4, "What starts with Windows",
			"Everything in this account's and the machine's start-up lists. ON means it starts at sign-in and runs in the " +
			"background; tools that help a gaming PC (Process Lasso, LiteSynapse) are marked HELPING when ON. Click a row " +
			"and press SCAN WITH DEFENDER to have its file checked - LiteSynapse never calls anything malware.",
			true,
			Plain("CHECK AGAIN", false, () => Work(4, "Reading the start-up list...", () =>
			{
				app.RunScanAsync().GetAwaiter().GetResult();
				return "Checked.";
			})),
			FixAll(4),
			ApplyButton(4),
			Undo(4),
			Plain("SCAN WITH DEFENDER", false, DefenderScan));
	}

	private void DefenderScan()
	{
		SwitchList.Row row = lists[4].SelectedRow;
		string path = row?.Path;
		if (string.IsNullOrWhiteSpace(path))
		{
			Say(4, "Click a start-up program first, then press SCAN WITH DEFENDER.", problem: true);
			return;
		}
		Work(4, "Asking Microsoft Defender to scan " + row.Title + "...",
			() => app.DefenderScanAsync(path).GetAwaiter().GetResult());
	}

	private void FillStartup()
	{
		ScanResult scan = app.Scan;
		List<SwitchList.Row> rows = new List<SwitchList.Row>();
		if (!scan.Checked)
		{
			rows.Add(Header("Not read yet", "Press CHECK AGAIN."));
			SetItems(4, rows);
			return;
		}
		rows.Add(Header("Microsoft Defender", "LiteSynapse is not an antivirus. It reports facts and leaves the verdict to Defender."));
		rows.Add(Info("Defender real-time protection", scan.Defender switch
		{
			"on" => "Scanning files as they are opened.",
			"off" => "Nothing is scanning files on this PC as they are opened.",
			_ => "Could not be read: policy or tamper protection is hiding it."
		}, scan.Defender, Tweaks.Risky, scan.Defender == "off" ? "No antivirus running" : ""));

		List<ScanItem> startup = scan.Items.Where((ScanItem i) => i.Kind == "startup").ToList();
		List<ScanItem> flagged = startup.Where((ScanItem i) => i.Flag.Length > 0).ToList();
		List<ScanItem> rest = startup.Where((ScanItem i) => i.Flag.Length == 0).ToList();
		if (flagged.Count > 0)
		{
			rows.Add(Header("Worth a look", "Not an accusation: these are the facts that make something worth checking."));
			foreach (ScanItem i in flagged)
			{
				rows.Add(StartupRow(i));
			}
		}
		rows.Add(Header("Starts with Windows", rest.Count == 0
			? "Nothing else starts with Windows."
			: "Switching one is the same switch as Task Manager's Startup apps, and only for this account."));
		foreach (ScanItem i in rest)
		{
			rows.Add(StartupRow(i));
		}
		SetItems(4, rows);
	}

	private static SwitchList.Row StartupRow(ScanItem i)
	{
		List<string> facts = new List<string>();
		if (i.Path.Length > 0)
		{
			facts.Add(i.Path);
		}
		facts.Add(i.Publisher.Length > 0 ? "Signed by " + i.Publisher + "." : "Carries no signature.");
		if (i.Network)
		{
			facts.Add("Holding a network connection right now.");
		}
		bool can = i.CanDisable || i.CanEnable;
		string note = "";
		if (i.Scope == "HKLM")
		{
			note = "Starts for every account, which LiteSynapse cannot switch from here.";
		}
		else if (i.Scope == "folder")
		{
			note = "A shortcut in the Startup folder; delete the shortcut to stop it.";
		}
		bool helps = PerfModel.HelpsAtSignIn(i.Name, i.Path);
		bool security = PerfModel.IsSecurity(i.Name, i.Path);
		return new SwitchList.Row
		{
			// "start:" is a row with a switch; "file:" is one LiteSynapse can only report on. Either way Path
			// carries the program's file, so Defender can be pointed at it.
			Id = can ? ("start:" + i.Id) : ("file:" + i.Id),
			Path = i.Path,
			Title = i.Name,
			Desc = string.Join("  ", facts),
			Cost = ScanRules.FlagText(i.Flag),
			Note = note,
			State = PerfModel.ServiceState(i.State),
			HelpsWhenOn = helps,
			Badge = i.Flag.Length > 0 ? "Worth a look" : (helps ? "Helps games" : ""),
			Risk = security ? Tweaks.Risky : (i.Flag.Length > 0 ? Tweaks.Caution : Tweaks.Safe),
			Can = can
		};
	}

	// ---- Shared row builders -------------------------------------------------------------------------

	private static SwitchList.Row Header(string title, string blurb)
	{
		return new SwitchList.Row
		{
			Header = true,
			Title = title,
			Desc = blurb ?? ""
		};
	}

	// A row that reports something without offering to change it. Leftovers and background things help
	// when OFF, which is every report row this tab has.
	private static SwitchList.Row Info(string title, string desc, string state, string risk = Tweaks.Safe, string badge = "")
	{
		return new SwitchList.Row
		{
			Title = title,
			Desc = desc ?? "",
			State = string.IsNullOrEmpty(state) ? "unknown" : state,
			HelpsWhenOn = false,
			Risk = risk,
			Badge = badge ?? "",
			Can = false
		};
	}

	// Puts new rows on a page, keeping any switch the user flipped and has not applied yet - a re-read caused
	// by a change on another page must not throw away what they were in the middle of choosing. A flip that
	// the PC now already matches is dropped, since there is nothing left to do for it.
	private void SetItems(int index, List<SwitchList.Row> rows)
	{
		SwitchList list = lists[index];
		Dictionary<string, bool> kept = new Dictionary<string, bool>(StringComparer.Ordinal);
		foreach (SwitchList.Row old in list.Items)
		{
			if (!old.Header && old.Pending && old.Id.Length > 0)
			{
				kept[old.Id] = old.Want.Value;
			}
		}
		foreach (SwitchList.Row r in rows)
		{
			if (!r.Header && r.Can && kept.TryGetValue(r.Id, out bool want))
			{
				r.Want = want;
				if (!r.Pending)
				{
					r.Want = null;
				}
			}
		}
		list.Refill(rows);
	}

	private void Refresh(int index)
	{
		switch (index)
		{
		case 0:
			FillRazer();
			break;
		case 1:
			FillDevice();
			break;
		case 2:
			FillWindows();
			break;
		case 3:
			FillServices();
			break;
		default:
			FillStartup();
			break;
		}
		ShowCounts(index);
	}

	// The service told us something changed: re-read whichever page is showing next time it is opened.
	public void Invalidate()
	{
		for (int i = 0; i < loaded.Length; i++)
		{
			loaded[i] = false;
		}
	}
}
