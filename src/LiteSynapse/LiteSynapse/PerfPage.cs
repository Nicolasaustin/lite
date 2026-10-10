using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace LiteSynapse;

// The Performance tab: everything LiteSynapse can do to the PC rather than to a profile.
//
// Five pages, each a list of things to tick and two buttons. The rule across all of them is the same: only
// what the user ticked is changed, and every row can go both ways, so nothing here is one-directional. The
// state badge on each row says what the PC is set to now, read fresh each time the page opens.
//
//   Razer        what Razer's own software left behind (a report plus the three elevated clean-up steps)
//   Device       whether Windows may power the mouse and keyboard down
//   Windows      the tweak catalog, grouped, each with its source and what it costs
//   Services     Windows services worth turning off on a gaming PC, and turning back on
//   Start-up     what starts with Windows, what holds a network connection, and a Defender scan
internal sealed class PerfPage
{
	// Pill order; MainForm shows these as the Performance tab's sub-tabs.
	public static readonly string[] Pills = new string[5] { "RAZER", "DEVICE", "WINDOWS", "SERVICES", "START-UP" };

	private readonly Service app;

	private readonly Form owner;

	private readonly Panel[] pages = new Panel[5];

	private readonly CheckList[] lists = new CheckList[5];

	private readonly Label[] notes = new Label[5];

	private readonly List<PillButton> buttons = new List<PillButton>();

	// Per page: the buttons that act on ticked rows, with the caption they were given. A destructive button
	// that says how many rows it will change is the difference between "I meant that" and "I did not".
	private readonly List<(int Page, PillButton Button, string Caption)> counted =
		new List<(int, PillButton, string)>();

	// Which pages have been filled since the window opened, so switching back does not re-read the PC.
	private readonly bool[] loaded = new bool[5];

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
	}

	public Panel Page(int pill)
	{
		return pages[Math.Max(0, Math.Min(pages.Length - 1, pill))];
	}

	// Called when the page becomes visible. Reading the PC costs a moment (every service, a signature per
	// start-up program, a WMI query), so it happens once per window rather than on every tab click, and
	// "Check again" re-reads on demand.
	//
	// The first time a page is shown, whatever it needs is fetched in the background rather than leaving the
	// user looking at "not read yet" above a button they would press anyway. Opening the tab is the ask.
	public void Show(int pill)
	{
		int i = Math.Max(0, Math.Min(pages.Length - 1, pill));
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
			if (!app.Tuning.Checked)
			{
				Work(1, "Asking Windows about the devices...", () =>
				{
					app.RefreshTuning();
					return "Checked.";
				});
			}
			break;
		default:
			if (!app.Scan.Checked)
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

	private Panel Shell(int index, string title, string blurb, params (string Text, bool Primary, Action Click)[] actions)
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
			Height = blurb.Length > 0 ? 112 : 86,
			BackColor = Theme.Bg,
			Padding = new Padding(0)
		};
		Label heading = new Label
		{
			Text = title,
			AutoSize = true,
			ForeColor = Theme.Text,
			BackColor = Color.Transparent,
			Font = Theme.F(13f, FontStyle.Bold),
			Location = new Point(18, 14)
		};
		head.Controls.Add(heading);
		if (blurb.Length > 0)
		{
			head.Controls.Add(new Label
			{
				Text = blurb,
				ForeColor = Theme.Dim,
				BackColor = Color.Transparent,
				Font = Theme.F(9.5f),
				Location = new Point(18, 42),
				Size = new Size(820, 34)
			});
		}
		int x = 18;
		int y = blurb.Length > 0 ? 80 : 48;
		foreach ((string text, bool primary, Action click) in actions)
		{
			PillButton b = new PillButton
			{
				Primary = primary
			};
			b.Text = text;
			// Wide enough for the caption with a two-digit count spliced into it, so "TURN TICKED OFF"
			// becoming "TURN 12 TICKED OFF" does not clip.
			string widest = text.Replace("TICKED", "99 TICKED");
			b.Width = (int)Tx.Measure(widest, b.Font).Width + 40;
			b.Location = new Point(x, y);
			Action handler = click;
			int onPage = index;
			b.Click += (object _1, EventArgs _2) => Guarded(onPage, handler);
			head.Controls.Add(b);
			buttons.Add(b);
			if (text.IndexOf("TICKED", StringComparison.Ordinal) >= 0)
			{
				counted.Add((index, b, text));
			}
			x += b.Width + 10;
		}
		Label note = new Label
		{
			Text = "",
			ForeColor = Theme.Green,
			BackColor = Color.Transparent,
			Font = Theme.F(9.5f),
			Location = new Point(x + 6, y + 8),
			Size = new Size(Math.Max(120, 1100 - x), 24),
			AutoEllipsis = true
		};
		head.Controls.Add(note);
		notes[index] = note;

		CheckList list = new CheckList
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
	}

	// Puts the number of ticked rows on this page's apply buttons, so a destructive press is never a guess.
	private void ShowCounts(int index)
	{
		int n = lists[index]?.TickedCount ?? 0;
		foreach ((int page, PillButton button, string caption) in counted)
		{
			if (page != index || button.IsDisposed)
			{
				continue;
			}
			button.Text = (n > 0) ? caption.Replace("TICKED", n.ToString() + " TICKED") : caption;
			button.Invalidate();
		}
	}

	// Runs the slow part off the UI thread, with the buttons disabled while it does.
	private void Work(int index, string pending, Func<string> job)
	{
		if (busy)
		{
			return;
		}
		busy = true;
		foreach (PillButton b in buttons)
		{
			b.Enabled = false;
		}
		Say(index, pending);
		Task.Run(job).ContinueWith((Task<string> t) =>
		{
			if (owner == null || owner.IsDisposed || !owner.IsHandleCreated)
			{
				busy = false;
				return;
			}
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
			}));
		});
	}

	// ---- Razer ---------------------------------------------------------------------------------------

	private Panel BuildRazer()
	{
		return Shell(0, "Razer software left on this PC",
			"What Razer's own software installed and still runs. LiteSynapse talks to the devices directly, so " +
			"none of it is needed - but the filter drivers are left alone unless you say so, because removing " +
			"the wrong one takes the mouse with it.",
			("CHECK AGAIN", false, () => Work(0, "Checking...", () =>
			{
				app.RefreshRazer(deep: true);
				return "Checked.";
			})),
			("TURN OFF RAZER SERVICES", true, () => Work(0, "Waiting for Windows to ask permission...",
				() => app.RazerCleanup("disableServices"))),
			("TURN OFF GAME MANAGER", false, () => Work(0, "Waiting for Windows to ask permission...",
				() => app.RazerCleanup("disableGameManager"))),
			("FILTER DRIVERS", false, () => Work(0, "Working...", () => app.RazerCleanup("driverGuide"))));
	}

	private void FillRazer()
	{
		RazerLeftovers r = app.Razer;
		List<CheckList.Row> rows = new List<CheckList.Row>();
		if (!r.Checked)
		{
			rows.Add(Header("Not checked yet", "Press Check again."));
			lists[0].Items = rows;
			return;
		}
		rows.Add(Header("Synapse", ""));
		rows.Add(Info("Razer Synapse", r.SynapseInstalled ? "Installed on this PC." : "Not installed.",
			r.SynapseInstalled ? "on" : "off"));
		if (r.SynapseRunning)
		{
			rows.Add(Info("Synapse is running now",
				"While it runs it can change the lighting and switch the mouse's slots behind LiteSynapse. Close it from the tray.",
				"on", Tweaks.Risky, "Fights with LiteSynapse"));
		}
		rows.Add(Info("Razer Game Manager Service 3", GmsText(r.GmsService), r.GmsService));
		if (r.RzCommon)
		{
			rows.Add(Info("RzCommon folder", "Razer's shared files are still in ProgramData.", "on"));
		}

		rows.Add(Header("Services", !r.Deep
			? "Not read yet: press Check again for the services."
			: (r.Services.Count == 0
				? "None of Razer's services are set to start."
				: "Each of these starts with Windows. \"Turn off Razer services\" sets them all to disabled.")));
		foreach (RazerService s in r.Services)
		{
			rows.Add(Info(s.Label ?? s.Name,
				(s.Driver ? "A driver service. " : "") + (s.Network ? "Opens a network or localhost listener." : ""),
				s.State, s.Driver ? Tweaks.Caution : Tweaks.Safe, s.Network ? "Uses the network" : ""));
		}

		rows.Add(Header("Starts with Windows", (!r.Deep && r.RunOnce.Count == 0)
			? "The Run entries were read; press Check again for the RunOnce ones."
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
			? "Not read yet: press Check again for the scheduled tasks."
			: (r.Tasks.Count == 0 && r.UpgradeTask == "absent"
				? "No Razer scheduled tasks."
				: "Tasks Razer's installers left behind.")));
		if (r.UpgradeTask != "absent")
		{
			rows.Add(Info("Razer upgrade task", "Checks for and installs Synapse updates on a schedule.", r.UpgradeTask));
		}
		foreach (RazerTask t in r.Tasks)
		{
			rows.Add(Info(t.Path, "A scheduled task in a Razer folder.", t.State));
		}

		rows.Add(Header("Firewall and drivers", ""));
		rows.Add(Info("Firewall rules", !r.Deep
			? "Not read yet: press Check again for the firewall rules."
			: (r.FirewallRules == 0
				? "No firewall rules for Razer software."
				: $"{r.FirewallRules} rule(s) let Razer software through the firewall."),
			!r.Deep ? "" : (r.FirewallRules == 0 ? "off" : "on")));
		if (r.FilterDrivers.Count == 0)
		{
			rows.Add(Info("Filter drivers", "None of Razer's filter drivers are on the devices.", "off"));
		}
		else
		{
			foreach (string d in r.FilterDrivers)
			{
				rows.Add(Info(d,
					"A Razer filter driver sits between Windows and the device. LiteSynapse does not need it. " +
					"Removing it is done by a script you run yourself, because a mistake here leaves you without a mouse.",
					"on", Tweaks.Caution, "Remove by hand"));
			}
		}
		lists[0].Items = rows;
	}

	private static string GmsText(string state)
	{
		switch (state)
		{
		case "absent":
			return "Not installed.";
		case "disabled":
			return "Installed but disabled, which is what we want.";
		default:
			return "Installed and set to start. It watches which game is in front so Synapse can switch profiles - " +
				"LiteSynapse does that itself.";
		}
	}

	// ---- Device --------------------------------------------------------------------------------------

	private Panel BuildDevice()
	{
		return Shell(1, "Device latency",
			"Whether Windows is allowed to power the mouse and keyboard down between reports. A device that is " +
			"allowed to idle has to wake up before the first movement of a flick gets through, which is the one " +
			"kind of latency you feel rather than measure.",
			("CHECK AGAIN", false, () => Work(1, "Asking Windows about the devices...", () =>
			{
				app.RefreshTuning();
				return "Checked.";
			})),
			("KEEP TICKED AWAKE", true, () => Apply(1, keepAwake: true)),
			("LET WINDOWS MANAGE TICKED", false, () => Apply(1, keepAwake: false)));
	}

	private void Apply(int index, bool keepAwake)
	{
		List<CheckList.Row> ticked = lists[index].Ticked.ToList();
		if (ticked.Count == 0)
		{
			Say(index, "Tick the devices you want to change first.", problem: true);
			return;
		}
		List<string> instances = ticked.Where((CheckList.Row r) => r.Id.StartsWith("dev:", StringComparison.Ordinal))
			.Select((CheckList.Row r) => r.Id.Substring(4)).ToList();
		bool plan = ticked.Any((CheckList.Row r) => r.Id == "plan");
		Work(index, "Waiting for Windows to ask permission...",
			() => app.TuneDevicesAsync(instances, plan, keepAwake).GetAwaiter().GetResult());
	}

	private void FillDevice()
	{
		DeviceTuningState s = app.Tuning;
		List<CheckList.Row> rows = new List<CheckList.Row>();
		rows.Add(Header("Your devices", s.Devices.Count > 0
			? "This is the \"Allow the computer to turn off this device to save power\" checkbox on each device's Power Management tab."
			: (s.Readable
				? "No Razer device answered. Plug the mouse in and press Check again."
				: "Windows would not let LiteSynapse read the devices' power settings. Start LiteSynapse as administrator and check again.")));
		foreach (TunedDevice d in s.Devices)
		{
			rows.Add(new CheckList.Row
			{
				Id = "dev:" + d.Instance,
				Title = d.Name,
				Desc = d.PowerSaving
					? "Windows may currently power this device down to save power."
					: "Windows may not power this device down. This is what you want.",
				State = d.PowerSaving ? "on" : "off",
				Badge = d.PowerSaving ? "Worth changing" : "",
				Risk = d.PowerSaving ? Tweaks.Caution : Tweaks.Safe,
				Checked = d.PowerSaving,
				Can = true
			});
		}
		rows.Add(Header("Power plan", "The active plan's own USB setting, which applies to every USB port."));
		rows.Add(new CheckList.Row
		{
			Id = "plan",
			Title = "USB selective suspend",
			Desc = s.UsbSuspend switch
			{
				"on" => "The active power plan lets Windows suspend idle USB devices, which includes the mouse.",
				"off" => "Already off in the active power plan.",
				_ => "Could not read it from the active power plan."
			},
			State = s.UsbSuspend,
			Checked = s.UsbSuspend == "on",
			Can = s.UsbSuspend != "unknown"
		});
		rows.Add(Header("The rest of the input settings moved",
			"Pointer acceleration, pointer speed, the 125 Hz background raw-input cap and the mouse and keyboard " +
			"buffers are on the Windows page now, where each one has its own checkbox and can be turned back on."));
		lists[1].Items = rows;
	}

	// ---- Windows settings ----------------------------------------------------------------------------

	private Panel BuildWindows()
	{
		return Shell(2, "Windows settings",
			"Every setting here can be turned on and off on its own: tick what you want and press one of the two " +
			"buttons. Each row says where it comes from and what it costs - read that before ticking anything in " +
			"the last group.",
			("CHECK AGAIN", false, () => Work(2, "Reading the settings...", () =>
			{
				app.RunScanAsync().GetAwaiter().GetResult();
				return "Checked.";
			})),
			("TURN TICKED ON", true, () => ApplyScan(2, on: true)),
			("TURN TICKED OFF", false, () => ApplyScan(2, on: false)));
	}

	private void ApplyScan(int index, bool on)
	{
		List<CheckList.Row> ticked = lists[index].Ticked.ToList();
		if (ticked.Count == 0)
		{
			Say(index, "Tick what you want to change first.", problem: true);
			return;
		}
		// A risky row has to be ticked and then confirmed by name: these are the ones that give up security.
		//
		// Which direction is the dangerous one depends on what the row is. Turning a tweak ON is what applies
		// it, so that is the direction to ask about. For a service it is the other way round: turning
		// Defender, the firewall or Windows Update OFF is the change that costs something, and turning it back
		// on is the safe direction. Asking about "on" for everything meant the Services page's green
		// "TURN TICKED OFF" button disabled Defender with no confirmation at all, while "TURN TICKED BACK ON"
		// put up a warning about giving up security for restoring it.
		List<CheckList.Row> risky = ticked.Where((CheckList.Row r) => r.Risk == Tweaks.Risky && Destructive(r, on)).ToList();
		if (risky.Count > 0)
		{
			string list = string.Join("\n  ", risky.Select((CheckList.Row r) => "- " + r.Title));
			DialogResult answer = MessageBox.Show(owner,
				"These give up security or a part of Windows you may need:\n\n  " + list +
				"\n\nEach one's cost is written on its row. " + (on ? "Turn them on?" : "Turn them off?"),
				"LiteSynapse", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
			if (answer != DialogResult.Yes)
			{
				Say(index, "Nothing was changed.");
				return;
			}
		}
		// A row's id is "<kind>:<what>". Only the first colon is a separator: a file path has its own
		// ("file:C:\\Program Files\\x.exe"), so splitting on the last one would mangle it.
		List<(string Id, bool On)> choices = new List<(string, bool)>();
		List<string> notSwitchable = new List<string>();
		foreach (CheckList.Row r in ticked)
		{
			(string kind, string what) = Split(r.Id);
			// A start-up entry LiteSynapse cannot switch is still tickable, so Defender can be pointed at its
			// file. Pressing a turn-on/off button on one used to report "nothing was changed", which read as a
			// failure rather than as "this one has no switch".
			if (kind == "file")
			{
				notSwitchable.Add(r.Title);
				continue;
			}
			choices.Add((what, on));
		}
		if (choices.Count == 0)
		{
			Say(index, Cannot(notSwitchable), problem: true);
			return;
		}
		string trailer = (notSwitchable.Count > 0) ? " " + Cannot(notSwitchable) : "";
		Work(index, on ? "Turning them on..." : "Turning them off...",
			() => app.ScanApplyAsync(choices).GetAwaiter().GetResult() + trailer);
	}

	// Is this the direction that takes something away? Applying a tweak is "on"; disabling a service is "off".
	// A start-up entry is neither: turning one off costs nothing that cannot be undone from the same list.
	private static bool Destructive(CheckList.Row row, bool on)
	{
		switch (Split(row.Id).Kind)
		{
		case "svc":
			return !on;
		case "tweak":
			return on;
		default:
			return false;
		}
	}

	// "tweak:accel" -> ("tweak", "accel"); "file:C:\\x.exe" -> ("file", "C:\\x.exe"). Only the first colon
	// separates, because the rest of the id may be a Windows path.
	private static (string Kind, string What) Split(string id)
	{
		string s = id ?? "";
		int at = s.IndexOf(':');
		return (at < 0) ? ("", s) : (s.Substring(0, at), s.Substring(at + 1));
	}

	private static string Cannot(List<string> names)
	{
		if (names == null || names.Count == 0)
		{
			return "Nothing that can be switched was ticked.";
		}
		string which = (names.Count == 1) ? names[0] : string.Join(", ", names);
		return which + ": LiteSynapse cannot switch " + (names.Count == 1 ? "this one" : "these") +
			" from here (it starts for every account, or from the Startup folder). " +
			(names.Count == 1 ? "It can still be scanned with Defender." : "They can still be scanned with Defender.");
	}

	private void FillWindows()
	{
		ScanResult scan = app.Scan;
		List<CheckList.Row> rows = new List<CheckList.Row>();
		if (!scan.Checked)
		{
			rows.Add(Header("Not read yet", "Press Check again."));
			lists[2].Items = rows;
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
				rows.Add(new CheckList.Row
				{
					Id = "tweak:" + t.Id,
					Title = t.Title,
					Desc = t.What,
					Cost = Costly(t),
					State = t.State,
					Badge = Tweaks.SourceText(t.Source),
					Risk = t.Risk,
					Can = true
				});
			}
		}
		lists[2].Items = rows;
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
			"Services worth turning off on a gaming PC, from FR33THY's guide, each with what you give up and the " +
			"start type Windows ships so it can be put back. The input stack the mouse and keyboard run on, and " +
			"the services Windows will not boot without, are not on this list at all.",
			("CHECK AGAIN", false, () => Work(3, "Reading the services...", () =>
			{
				app.RunScanAsync().GetAwaiter().GetResult();
				return "Checked.";
			})),
			("TURN TICKED OFF", true, () => ApplyScan(3, on: false)),
			("TURN TICKED BACK ON", false, () => ApplyScan(3, on: true)));
	}

	private void FillServices()
	{
		ScanResult scan = app.Scan;
		List<CheckList.Row> rows = new List<CheckList.Row>();
		if (!scan.Checked)
		{
			rows.Add(Header("Not read yet", "Press Check again."));
			lists[3].Items = rows;
			return;
		}
		List<ScanItem> services = scan.Items.Where((ScanItem i) => i.Kind == "service").ToList();
		foreach ((string risk, string title, string blurb) in new[]
		{
			(Tweaks.Safe, "Safe to turn off", "Nothing you use on a gaming PC stops working."),
			(Tweaks.Caution, "Turns a feature off", "Each row says which feature. Read it before ticking it."),
			(Tweaks.Risky, "Affects security, updates or networking",
				"These are on the list because the guides include them, not because they are recommended. The row says what breaks.")
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
				rows.Add(new CheckList.Row
				{
					Id = "svc:" + i.Id,
					Title = i.Name,
					Desc = i.Costs,
					Cost = i.Lose,
					State = i.State,
					Badge = i.Network ? "Uses the network" : "",
					Risk = i.Risk,
					Can = i.CanDisable || i.CanEnable
				});
			}
		}
		if (rows.Count == 0)
		{
			rows.Add(Header("Nothing to show", "None of the catalog's services are installed on this PC."));
		}
		lists[3].Items = rows;
	}

	// ---- Start-up ------------------------------------------------------------------------------------

	private Panel BuildStartup()
	{
		return Shell(4, "What starts with Windows",
			"Everything in this account's and the machine's start-up lists, with where it lives, who signed it " +
			"and whether it is holding a network connection. LiteSynapse never says a file is malware - the rows " +
			"marked worth a look are facts, and Microsoft Defender gives the verdict.",
			("CHECK AGAIN", false, () => Work(4, "Reading the start-up list...", () =>
			{
				app.RunScanAsync().GetAwaiter().GetResult();
				return "Checked.";
			})),
			("TURN TICKED OFF", true, () => ApplyScan(4, on: false)),
			("TURN TICKED BACK ON", false, () => ApplyScan(4, on: true)),
			("SCAN WITH DEFENDER", false, DefenderScan));
	}

	private void DefenderScan()
	{
		List<CheckList.Row> ticked = lists[4].Ticked.ToList();
		// Every start-up row carries its program's file, switchable or not, so any of them can be scanned.
		List<string> paths = ticked.Select((CheckList.Row r) => r.Path)
			.Where((string p) => !string.IsNullOrWhiteSpace(p))
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.ToList();
		if (paths.Count == 0)
		{
			Say(4, "Tick a start-up program whose file you want Defender to scan.", problem: true);
			return;
		}
		// A custom scan can take minutes. Doing several in a row would look like the window had hung, so this
		// asks for one at a time rather than quietly scanning only the first.
		if (paths.Count > 1)
		{
			Say(4, $"Defender scans one file at a time, and each can take a few minutes. {paths.Count} are " +
				"ticked - untick all but one.", problem: true);
			return;
		}
		string path = paths[0];
		Work(4, "Asking Microsoft Defender to scan it...",
			() => app.DefenderScanAsync(path).GetAwaiter().GetResult());
	}

	private void FillStartup()
	{
		ScanResult scan = app.Scan;
		List<CheckList.Row> rows = new List<CheckList.Row>();
		if (!scan.Checked)
		{
			rows.Add(Header("Not read yet", "Press Check again."));
			lists[4].Items = rows;
			return;
		}
		rows.Add(Header("Microsoft Defender", "LiteSynapse is not an antivirus. It reports facts and leaves the verdict to Defender."));
		rows.Add(Info("Real-time protection", scan.Defender switch
		{
			"on" => "On. Tick a program below and press Scan ticked with Defender to have it checked.",
			"off" => "Off. Nothing is scanning files on this PC as they are opened.",
			_ => "Could not be read: policy or tamper protection is hiding it."
		}, scan.Defender, scan.Defender == "off" ? Tweaks.Risky : Tweaks.Safe,
			scan.Defender == "off" ? "No antivirus running" : ""));

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
			: "Turning one off is the same switch as Task Manager's Startup apps, and only for this account."));
		foreach (ScanItem i in rest)
		{
			rows.Add(StartupRow(i));
		}
		lists[4].Items = rows;
	}

	private static CheckList.Row StartupRow(ScanItem i)
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
		if (i.Scope == "HKLM")
		{
			facts.Add("Starts for every account, which LiteSynapse cannot change from here.");
		}
		if (i.Scope == "folder")
		{
			facts.Add("A shortcut in the Startup folder; delete the shortcut to stop it.");
		}
		return new CheckList.Row
		{
			// "start:" is a row with a switch; "file:" is one LiteSynapse can only report on. Either way Path
			// carries the program's file, so Defender can be pointed at it.
			Id = (i.CanDisable || i.CanEnable) ? ("start:" + i.Id) : ("file:" + i.Id),
			Path = i.Path,
			Title = i.Name,
			Desc = string.Join("  ", facts),
			Cost = ScanRules.FlagText(i.Flag),
			State = i.State,
			Badge = i.Flag.Length > 0 ? "Worth a look" : "",
			Risk = i.Flag.Length > 0 ? Tweaks.Caution : Tweaks.Safe,
			// Only this account's Run values have a switch; everything else is reported, and can still be scanned.
			Can = i.CanDisable || i.CanEnable || i.Path.Length > 0
		};
	}

	// ---- Shared row builders -------------------------------------------------------------------------

	private static CheckList.Row Header(string title, string blurb)
	{
		return new CheckList.Row
		{
			Header = true,
			Title = title,
			Desc = blurb ?? ""
		};
	}

	// A row that reports something without offering to change it.
	private static CheckList.Row Info(string title, string desc, string state, string risk = Tweaks.Safe, string badge = "")
	{
		return new CheckList.Row
		{
			Title = title,
			Desc = desc ?? "",
			State = state ?? "",
			Risk = risk,
			Badge = badge ?? "",
			Can = false
		};
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
