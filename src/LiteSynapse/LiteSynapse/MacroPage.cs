using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace LiteSynapse;

// The MACRO page: pick a macro on the left, edit its steps on the right. NEW / DUPLICATE / RENAME / DELETE /
// IMPORT / EXPORT for the macro; RECORD / ADD KEY / ADD MOUSE / ADD DELAY / ADD TEXT / MOVE / REMOVE / SAVE /
// REVERT for its steps. All the decisions live in MacroEdit (tested); this draws and wires.
//
// Editing works on a copy, so REVERT goes back to what is stored and SAVE is a deliberate step. SAVE writes
// data/profiles.json and re-pushes the macros to the input engine, so a change takes effect without a restart.
internal sealed class MacroPage : Panel
{
	private readonly Service app;

	private readonly Form owner;

	private readonly ListBox lstMacros = new ListBox();

	private readonly ListBox lstSteps = new ListBox();

	private readonly Label lblName = new Label();

	private readonly Label lblInfo = new Label();

	private readonly Label lblBinds = new Label();

	private readonly PillButton btnSave;

	private readonly PillButton btnRevert;

	private readonly List<PillButton> stepButtons = new List<PillButton>();

	// The macro being edited (a copy of the stored one) and its name as stored. null when none is selected.
	private Macro editing;

	private string editingStored;

	private bool dirty;

	private bool loading;

	public MacroPage(Service app, Form owner)
	{
		this.app = app;
		this.owner = owner;
		base.Dock = (DockStyle)5;
		base.BackColor = Theme.Bg;
		base.Padding = new Padding(24);

		Card left = new Card { Title = "Macros" };
		((Control)left).Dock = (DockStyle)4; // Left
		((Control)left).Width = 320;
		((Control)left).Padding = new Padding(20, 70, 20, 20);

		StyleList(lstMacros, nameList: true);
		lstMacros.SelectedIndexChanged += (object _, EventArgs _) => { if (!loading) { SelectFromList(); } };

		FlowLayoutPanel leftButtons = ButtonRow();
		((Control)leftButtons).Dock = (DockStyle)2; // Bottom
		leftButtons.Controls.Add(MakeButton("NEW", NewMacro));
		leftButtons.Controls.Add(MakeButton("DUPLICATE", DuplicateMacro));
		leftButtons.Controls.Add(MakeButton("RENAME", RenameMacro));
		leftButtons.Controls.Add(MakeButton("DELETE", DeleteMacro));
		leftButtons.Controls.Add(MakeButton("IMPORT", ImportMacro));
		leftButtons.Controls.Add(MakeButton("EXPORT", ExportMacro));
		((Control)left).Controls.Add((Control)(object)lstMacros);
		((Control)left).Controls.Add((Control)(object)leftButtons);

		Card right = new Card { Title = "Steps" };
		((Control)right).Dock = (DockStyle)5; // Fill
		((Control)right).Padding = new Padding(24, 70, 24, 20);

		((Control)lblName).SetBounds(0, 0, 10, 10);
		lblName.AutoSize = true;
		lblName.ForeColor = Theme.Text;
		lblName.BackColor = Color.Transparent;
		lblName.Font = Theme.P(13f, (FontStyle)1);
		lblName.Dock = (DockStyle)1; // Top
		lblName.Height = 30;

		lblInfo.AutoSize = false;
		lblInfo.Dock = (DockStyle)1;
		lblInfo.Height = 24;
		lblInfo.ForeColor = Theme.Dim;
		lblInfo.BackColor = Color.Transparent;
		lblInfo.Font = Theme.P(9.5f, (FontStyle)0);

		lblBinds.AutoSize = false;
		lblBinds.Dock = (DockStyle)2; // Bottom
		lblBinds.Height = 44;
		lblBinds.ForeColor = Theme.Dim;
		lblBinds.BackColor = Color.Transparent;
		lblBinds.Font = Theme.P(9.5f, (FontStyle)0);

		StyleList(lstSteps, nameList: false);
		lstSteps.DoubleClick += (object _, EventArgs _) => ToggleStep();

		FlowLayoutPanel stepBtns = ButtonRow();
		((Control)stepBtns).Dock = (DockStyle)2; // Bottom
		stepButtons.Add(Add(stepBtns, "RECORD", RecordSteps));
		stepButtons.Add(Add(stepBtns, "ADD KEY", AddKey));
		stepButtons.Add(Add(stepBtns, "ADD MOUSE", AddMouse));
		stepButtons.Add(Add(stepBtns, "ADD DELAY", AddDelay));
		stepButtons.Add(Add(stepBtns, "ADD TEXT", AddText));
		stepButtons.Add(Add(stepBtns, "MOVE UP", () => Move(-1)));
		stepButtons.Add(Add(stepBtns, "MOVE DOWN", () => Move(1)));
		stepButtons.Add(Add(stepBtns, "REMOVE", RemoveStep));
		btnSave = Add(stepBtns, "SAVE", Save, primary: true);
		btnRevert = Add(stepBtns, "REVERT", Revert);

		((Control)right).Controls.Add((Control)(object)lstSteps);
		((Control)right).Controls.Add((Control)(object)lblBinds);
		((Control)right).Controls.Add((Control)(object)stepBtns);
		((Control)right).Controls.Add((Control)(object)lblInfo);
		((Control)right).Controls.Add((Control)(object)lblName);

		base.Controls.Add((Control)(object)right);
		base.Controls.Add((Control)new Panel { Dock = (DockStyle)4, Width = 20, BackColor = Theme.Bg });
		base.Controls.Add((Control)(object)left);

		DarkScroll.Apply((Control)(object)lstMacros);
		DarkScroll.Apply((Control)(object)lstSteps);
	}

	// ---- Loading --------------------------------------------------------------------------------------------

	// Rebuilds the macro list from the store, keeping the selection where it can. Called on every show and after
	// an import/reload.
	public void Reload()
	{
		if (dirty && editing != null)
		{
			// A half-finished edit survives a tab switch; a different macro's reload does not throw it away.
			RefreshBindings();
			return;
		}
		string want = (string)lstMacros.SelectedItem ?? editingStored;
		loading = true;
		try
		{
			lstMacros.Items.Clear();
			foreach (Macro m in app.Store.Macros)
			{
				lstMacros.Items.Add((object)m.Name);
			}
		}
		finally
		{
			loading = false;
		}
		if (lstMacros.Items.Count == 0)
		{
			editing = null;
			editingStored = null;
			ShowEditing();
			return;
		}
		int idx = (want != null) ? lstMacros.Items.IndexOf((object)want) : -1;
		lstMacros.SelectedIndex = (idx >= 0) ? idx : 0;
		SelectFromList();
	}

	private void SelectFromList()
	{
		string name = (string)lstMacros.SelectedItem;
		if (name == null)
		{
			return;
		}
		if (dirty && editing != null && !string.Equals(name, editingStored, StringComparison.Ordinal))
		{
			if (!ConfirmDiscard())
			{
				// Put the selection back on the macro being edited.
				loading = true;
				try { lstMacros.SelectedItem = (object)editingStored; } finally { loading = false; }
				return;
			}
		}
		Macro stored = app.Store.Macros.FirstOrDefault((Macro m) => m.Name == name);
		if (stored == null)
		{
			return;
		}
		editingStored = stored.Name;
		editing = MacroEdit.Copy(stored);
		dirty = false;
		ShowEditing();
	}

	private void ShowEditing()
	{
		loading = true;
		try
		{
			lstSteps.Items.Clear();
			if (editing == null)
			{
				lblName.Text = "No macro selected";
				lblInfo.Text = app.Store.Macros.Count == 0 ? "Press NEW to make your first macro." : "Pick a macro on the left.";
				lblBinds.Text = "";
				foreach (PillButton b in stepButtons)
				{
					((Control)b).Enabled = ((Control)b).Text == "RECORD" ? false : ((Control)b).Text == "SAVE" || ((Control)b).Text == "REVERT" ? false : false;
				}
				return;
			}
			lblName.Text = editing.Name + (dirty ? "  *" : "");
			lblInfo.Text = editing.Steps.Count + " step" + (editing.Steps.Count == 1 ? "" : "s") + "   ·   runs in " +
				MacroEdit.DurationText(MacroEdit.DurationMs(editing.Steps));
			foreach (MacroStep s in editing.Steps)
			{
				lstSteps.Items.Add((object)s);
			}
			foreach (PillButton b in stepButtons)
			{
				((Control)b).Enabled = true;
			}
			((Control)btnSave).Enabled = dirty;
			((Control)btnRevert).Enabled = dirty;
			RefreshBindings();
		}
		finally
		{
			loading = false;
		}
	}

	private void RefreshBindings()
	{
		if (editing == null)
		{
			lblBinds.Text = "";
			return;
		}
		List<string> binds = MacroEdit.Bindings(app.Store, editingStored);
		lblBinds.Text = binds.Count == 0
			? "Not bound to a key yet. Bind it on the BlackWidow Lite page (Customize ▸ Macro keys), or in Settings."
			: "Runs on: " + string.Join(", ", binds) + ".";
	}

	// ---- Macro-level actions --------------------------------------------------------------------------------

	private void NewMacro()
	{
		if (dirty && !ConfirmDiscard())
		{
			return;
		}
		string name = MacroEdit.UniqueName(app.Store.Macros.Select((Macro m) => m.Name), "New Macro");
		app.Store.Macros.Add(new Macro { Name = name });
		Persist();
		Reload();
		lstMacros.SelectedItem = (object)name;
	}

	private void DuplicateMacro()
	{
		if (editing == null)
		{
			return;
		}
		Macro source = app.Store.Macros.FirstOrDefault((Macro m) => m.Name == editingStored);
		if (source == null)
		{
			return;
		}
		string name = MacroEdit.UniqueName(app.Store.Macros.Select((Macro m) => m.Name), source.Name + " copy");
		app.Store.Macros.Add(MacroEdit.Copy(source, name));
		Persist();
		dirty = false;
		Reload();
		lstMacros.SelectedItem = (object)name;
	}

	private void RenameMacro()
	{
		if (editing == null)
		{
			return;
		}
		string wanted = Ui.Prompt((IWin32Window)(object)owner, "Rename macro", "New name", editingStored);
		if (wanted == null)
		{
			return;
		}
		wanted = wanted.Trim();
		string problem = MacroEdit.NameProblem(app.Store.Macros.Select((Macro m) => m.Name), wanted, allowed: editingStored);
		if (problem != null)
		{
			MessageBox.Show((IWin32Window)(object)owner, problem, "LiteSynapse");
			return;
		}
		List<string> moved = MacroEdit.Rename(app.Store, editingStored, wanted);
		editingStored = wanted;
		if (editing != null)
		{
			editing.Name = wanted;
		}
		Persist();
		Reload();
		lstMacros.SelectedItem = (object)wanted;
		if (moved.Count > 0)
		{
			lblInfo.Text = "Renamed. The key binds followed it: " + string.Join(", ", moved) + ".";
		}
	}

	private void DeleteMacro()
	{
		if (editing == null)
		{
			return;
		}
		List<string> binds = MacroEdit.Bindings(app.Store, editingStored);
		string extra = binds.Count > 0 ? "\n\nIt is bound to: " + string.Join(", ", binds) + ". Those key binds will be cleared." : "";
		if ((int)MessageBox.Show((IWin32Window)(object)owner, "Delete \"" + editingStored + "\"?" + extra, "LiteSynapse", (MessageBoxButtons)4) != 6)
		{
			return;
		}
		MacroEdit.Delete(app.Store, editingStored);
		editing = null;
		editingStored = null;
		dirty = false;
		Persist();
		Reload();
	}

	private void ImportMacro()
	{
		OpenFileDialog dlg = new OpenFileDialog
		{
			Filter = "Macros (*.synapse4;*.xml;*.json)|*.synapse4;*.xml;*.json|Razer Synapse 4 (*.synapse4)|*.synapse4|Razer Synapse 3 macro (*.xml)|*.xml|LiteSynapse macro (*.json)|*.json|All files (*.*)|*.*",
			Multiselect = true
		};
		try
		{
			if ((int)((CommonDialog)dlg).ShowDialog((IWin32Window)(object)owner) != 1)
			{
				return;
			}
			List<string> added = new List<string>();
			List<string> failed = new List<string>();
			foreach (string file in ((FileDialog)dlg).FileNames)
			{
				try
				{
					// A Synapse 4 file can hold several macros; .xml / .json hold one.
					foreach (Macro m in Importer.ImportMacrosFromFile(file))
					{
						m.Name = MacroEdit.UniqueName(app.Store.Macros.Select((Macro x) => x.Name), m.Name);
						app.Store.Macros.Add(m);
						added.Add(m.Name);
					}
				}
				catch (Exception ex)
				{
					failed.Add(System.IO.Path.GetFileName(file) + " (" + ex.Message + ")");
				}
			}
			if (added.Count > 0)
			{
				Persist();
				dirty = false;
				Reload();
				lstMacros.SelectedItem = (object)added[added.Count - 1];
			}
			if (failed.Count > 0)
			{
				MessageBox.Show((IWin32Window)(object)owner, "Could not import:\n  " + string.Join("\n  ", failed), "LiteSynapse");
			}
			else if (added.Count > 0)
			{
				lblInfo.Text = "Imported: " + string.Join(", ", added) + ".";
			}
		}
		finally
		{
			((IDisposable)dlg)?.Dispose();
		}
	}

	private void ExportMacro()
	{
		if (editing == null)
		{
			return;
		}
		SaveFileDialog dlg = new SaveFileDialog
		{
			Filter = "LiteSynapse macro (*.json)|*.json",
			FileName = SafeFileName(editingStored) + ".json"
		};
		try
		{
			if ((int)((CommonDialog)dlg).ShowDialog((IWin32Window)(object)owner) != 1)
			{
				return;
			}
			Macro source = app.Store.Macros.FirstOrDefault((Macro m) => m.Name == editingStored) ?? editing;
			System.IO.File.WriteAllText(((FileDialog)dlg).FileName, MacroEdit.ToJson(source));
			lblInfo.Text = "Exported to " + ((FileDialog)dlg).FileName;
		}
		catch (Exception ex)
		{
			MessageBox.Show((IWin32Window)(object)owner, "Export failed: " + ex.Message, "LiteSynapse");
		}
		finally
		{
			((IDisposable)dlg)?.Dispose();
		}
	}

	// ---- Step-level actions ---------------------------------------------------------------------------------

	private void AddKey()
	{
		if (editing == null)
		{
			return;
		}
		if (MacroCapture.AskKey(owner, out int vk, out bool ext))
		{
			InsertSteps(MacroEdit.Tap(MacroEdit.KeyStep(vk, ext, down: true), 0));
		}
	}

	private void AddMouse()
	{
		if (editing == null)
		{
			return;
		}
		if (MacroCapture.AskMouse(owner, out int button))
		{
			InsertSteps(MacroEdit.Tap(MacroEdit.MouseStep(button, down: true), 0));
		}
	}

	private void AddDelay()
	{
		if (editing == null)
		{
			return;
		}
		string text = Ui.Prompt((IWin32Window)(object)owner, "Add a wait", "Milliseconds", "50");
		if (text == null)
		{
			return;
		}
		if (!int.TryParse(text.Trim(), out int ms) || ms < 0)
		{
			MessageBox.Show((IWin32Window)(object)owner, "Enter a whole number of milliseconds (0 or more).", "LiteSynapse");
			return;
		}
		InsertSteps(new List<MacroStep> { MacroEdit.WaitStep(ms) });
	}

	private void AddText()
	{
		if (editing == null)
		{
			return;
		}
		string text = Ui.Prompt((IWin32Window)(object)owner, "Type text", "Text to type", "");
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		List<MacroStep> steps = MacroEdit.TextSteps(text, 0);
		if (steps.Count == 0)
		{
			MessageBox.Show((IWin32Window)(object)owner, "None of those characters are on a standard keyboard.", "LiteSynapse");
			return;
		}
		InsertSteps(steps);
	}

	private void RecordSteps()
	{
		if (editing == null)
		{
			return;
		}
		List<MacroEdit.Recorded> rec = MacroCapture.Record(owner, out string delays, out int fixedMs);
		if (rec == null || rec.Count == 0)
		{
			return;
		}
		List<MacroStep> steps = MacroEdit.FromRecording(rec, delays, fixedMs);
		// Yes = add to the end, No = replace what is there, Cancel = keep the recording out.
		DialogResult answer = MessageBox.Show((IWin32Window)(object)owner,
			"Recorded " + steps.Count + " step(s).\n\nYes = add them to the end of \"" + editing.Name + "\".\nNo = replace what is there.",
			"LiteSynapse", (MessageBoxButtons)3);
		if (answer == DialogResult.Cancel)
		{
			return;
		}
		if (answer == DialogResult.No)
		{
			editing.Steps.Clear();
		}
		editing.Steps.AddRange(steps);
		MarkDirty();
	}

	private void InsertSteps(List<MacroStep> steps)
	{
		int at = (lstSteps.SelectedIndex >= 0) ? lstSteps.SelectedIndex + 1 : editing.Steps.Count;
		editing.Steps.InsertRange(at, steps);
		MarkDirty();
		if (at + steps.Count - 1 < lstSteps.Items.Count)
		{
			lstSteps.SelectedIndex = at + steps.Count - 1;
		}
	}

	private void RemoveStep()
	{
		int i = lstSteps.SelectedIndex;
		if (editing == null || i < 0 || i >= editing.Steps.Count)
		{
			return;
		}
		editing.Steps.RemoveAt(i);
		MarkDirty();
		if (lstSteps.Items.Count > 0)
		{
			lstSteps.SelectedIndex = Math.Min(i, lstSteps.Items.Count - 1);
		}
	}

	private void Move(int by)
	{
		int i = lstSteps.SelectedIndex;
		if (editing == null || i < 0)
		{
			return;
		}
		int j = i + by;
		if (j < 0 || j >= editing.Steps.Count)
		{
			return;
		}
		(editing.Steps[i], editing.Steps[j]) = (editing.Steps[j], editing.Steps[i]);
		MarkDirty();
		lstSteps.SelectedIndex = j;
	}

	// Double-click a key or mouse step to flip it between press and release, or a wait to change its time.
	private void ToggleStep()
	{
		int i = lstSteps.SelectedIndex;
		if (editing == null || i < 0 || i >= editing.Steps.Count)
		{
			return;
		}
		MacroStep s = editing.Steps[i];
		if (s.IsEvent)
		{
			s.Down = !s.Down;
			MarkDirty();
		}
		else
		{
			string text = Ui.Prompt((IWin32Window)(object)owner, "Change the wait", "Milliseconds", s.DelayMs.ToString());
			if (text != null && int.TryParse(text.Trim(), out int ms) && ms >= 0)
			{
				s.DelayMs = ms;
				MarkDirty();
			}
		}
	}

	private void Save()
	{
		if (editing == null || !dirty)
		{
			return;
		}
		Macro stored = app.Store.Macros.FirstOrDefault((Macro m) => m.Name == editingStored);
		if (stored == null)
		{
			app.Store.Macros.Add(MacroEdit.Copy(editing));
		}
		else
		{
			stored.Steps = MacroEdit.Copy(editing).Steps;
		}
		dirty = false;
		Persist();
		ShowEditing();
		lblInfo.Text = "Saved. The macro is live now.";
	}

	private void Revert()
	{
		if (editing == null)
		{
			return;
		}
		Macro stored = app.Store.Macros.FirstOrDefault((Macro m) => m.Name == editingStored);
		editing = (stored != null) ? MacroEdit.Copy(stored) : null;
		dirty = false;
		ShowEditing();
	}

	// ---- Plumbing -------------------------------------------------------------------------------------------

	private void MarkDirty()
	{
		dirty = true;
		ShowEditing();
	}

	// Writes data/profiles.json and re-pushes the macros to the input engine, so a saved change runs at once.
	private void Persist()
	{
		Cfg.Save(Cfg.Profiles, app.Store);
		app.ApplyNames(app.ActiveMouse, app.ActiveKeyboard, app.InGame);
	}

	private bool ConfirmDiscard()
	{
		return (int)MessageBox.Show((IWin32Window)(object)owner,
			"\"" + (editing?.Name ?? "This macro") + "\" has unsaved changes. Discard them?",
			"LiteSynapse", (MessageBoxButtons)4) == 6;
	}

	private static string SafeFileName(string name)
	{
		foreach (char c in System.IO.Path.GetInvalidFileNameChars())
		{
			name = (name ?? "macro").Replace(c, '_');
		}
		return string.IsNullOrWhiteSpace(name) ? "macro" : name;
	}

	// ---- Controls -------------------------------------------------------------------------------------------

	private void StyleList(ListBox list, bool nameList)
	{
		list.Dock = nameList ? (DockStyle)5 : (DockStyle)5;
		list.BackColor = Theme.Card;
		list.ForeColor = Theme.Text;
		list.BorderStyle = (BorderStyle)0;
		list.IntegralHeight = false;
		list.DrawMode = (DrawMode)1; // OwnerDrawFixed
		list.ItemHeight = 30;
		list.Font = Theme.F(11f, (FontStyle)0);
		list.DrawItem += (object _, DrawItemEventArgs d) =>
		{
			if (d.Index < 0)
			{
				return;
			}
			bool sel = (d.State & DrawItemState.Selected) != 0;
			using (SolidBrush bg = new SolidBrush(sel ? Color.FromArgb(40, 40, 40) : Theme.Card))
			{
				d.Graphics.FillRectangle((Brush)(object)bg, d.Bounds);
			}
			if (sel)
			{
				using SolidBrush bar = new SolidBrush(Theme.Green);
				d.Graphics.FillRectangle((Brush)(object)bar, d.Bounds.X, d.Bounds.Y, 3, d.Bounds.Height);
			}
			object item = list.Items[d.Index];
			if (item is MacroStep step)
			{
				Color tagColor = !step.IsEvent ? Theme.Dim : step.Down ? Theme.Green : Color.FromArgb(210, 150, 90);
				Tx.Left(d.Graphics, MacroEdit.Tag(step), Theme.P(9f, (FontStyle)1), tagColor,
					new RectangleF(d.Bounds.X + 12, d.Bounds.Y, 86, d.Bounds.Height));
				Tx.Left(d.Graphics, MacroEdit.What(step), Theme.P(11f, (FontStyle)0), Theme.Text,
					new RectangleF(d.Bounds.X + 104, d.Bounds.Y, d.Bounds.Width - 110, d.Bounds.Height));
			}
			else
			{
				Tx.Left(d.Graphics, item?.ToString() ?? "", Theme.P(11f, (FontStyle)0), sel ? Theme.Green : Theme.Text,
					new RectangleF(d.Bounds.X + 12, d.Bounds.Y, d.Bounds.Width - 14, d.Bounds.Height));
			}
		};
	}

	private static FlowLayoutPanel ButtonRow()
	{
		return new FlowLayoutPanel
		{
			Height = 80,
			BackColor = Color.Transparent,
			FlowDirection = (FlowDirection)0,
			WrapContents = true,
			Padding = new Padding(0, 10, 0, 0)
		};
	}

	private PillButton Add(FlowLayoutPanel row, string text, Action click, bool primary = false)
	{
		PillButton b = MakeButton(text, click, primary);
		row.Controls.Add((Control)(object)b);
		return b;
	}

	private PillButton MakeButton(string text, Action click, bool primary = false)
	{
		PillButton b = new PillButton { Primary = primary };
		((Control)b).Text = text;
		((Control)b).Width = Math.Max(92, TextRenderer.MeasureText(text, Theme.F(9.5f, (FontStyle)1)).Width + 30);
		((Control)b).Height = 34;
		((Control)b).Margin = new Padding(0, 0, 8, 8);
		((Control)b).Click += (object _, EventArgs _) =>
		{
			try
			{
				click();
			}
			catch (Exception ex)
			{
				Cfg.Log("macro page: " + ex);
				MessageBox.Show((IWin32Window)(object)owner, "Something went wrong: " + ex.Message, "LiteSynapse");
			}
		};
		return b;
	}
}
