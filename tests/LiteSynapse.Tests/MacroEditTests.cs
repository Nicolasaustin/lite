using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// The macro editor's logic (MacroEdit): step words, text and recordings as steps, unique names, the key binds a
// macro has, rename/delete carrying the binds, and the export/import round trip.
public class MacroEditTests
{
	[Fact]
	public void StepsReadInPlainWords()
	{
		Assert.Equal("Press W", MacroEdit.Describe(MacroEdit.KeyStep('W', ext: false, down: true)));
		Assert.Equal("Release W", MacroEdit.Describe(MacroEdit.KeyStep('W', ext: false, down: false)));
		Assert.Equal("Press Left click", MacroEdit.Describe(MacroEdit.MouseStep(MacroEdit.MouseLeft, down: true)));
		Assert.Equal("Release Forward (X2)", MacroEdit.Describe(MacroEdit.MouseStep(MacroEdit.MouseForward, down: false)));
		Assert.Equal("Wait 50 ms", MacroEdit.Describe(MacroEdit.WaitStep(50)));
		Assert.Equal("Numpad Enter", MacroEdit.KeyLabel(13, ext: true));
		Assert.Equal("Enter", MacroEdit.KeyLabel(13, ext: false));
	}

	[Fact]
	public void DurationIsEveryWait()
	{
		List<MacroStep> steps = new List<MacroStep>
		{
			MacroEdit.KeyStep('A', false, true, 30),
			MacroEdit.KeyStep('A', false, false),
			MacroEdit.WaitStep(20)
		};
		Assert.Equal(50, MacroEdit.DurationMs(steps));
		Assert.Equal("0.050 s", MacroEdit.DurationText(50));
	}

	[Fact]
	public void ATapIsAPressThenARelease()
	{
		List<MacroStep> tap = MacroEdit.Tap(MacroEdit.KeyStep(' ', false, true), gapMs: 0);
		Assert.Equal(2, tap.Count);
		Assert.True(tap[0].Down);
		Assert.False(tap[1].Down);
		Assert.Equal(32, tap[0].Vk);
		Assert.Equal(32, tap[1].Vk);
	}

	[Fact]
	public void TextBecomesKeyStepsWithShiftForCapitals()
	{
		List<MacroStep> steps = MacroEdit.TextSteps("Hi!", 0);
		// H: shift down, H down, H up, shift up; i: i down, i up; !: shift down, 1 down, 1 up, shift up.
		Assert.Equal(160, steps[0].Vk);   // left shift down
		Assert.True(steps[0].Down);
		Assert.Equal('H', steps[1].Vk);
		Assert.Equal('I', steps[4].Vk);   // 'i' -> vk of 'I'
		Assert.False(steps[1].Ext);
		// The last character's release is the last step and carries no trailing delay.
		Assert.False(steps[steps.Count - 1].Down);
		Assert.Equal(0, steps[steps.Count - 1].DelayMs);
		// A character the US layout does not have is skipped, not guessed.
		Assert.Empty(MacroEdit.TextSteps("é", 0));
	}

	[Fact]
	public void RecordingKeepsOrAbandonsTiming()
	{
		List<MacroEdit.Recorded> rec = new List<MacroEdit.Recorded>
		{
			new MacroEdit.Recorded('W', false, 0, true, 0),
			new MacroEdit.Recorded('W', false, 0, false, 40),
			new MacroEdit.Recorded(0, false, MacroEdit.MouseLeft, true, 100)
		};
		List<MacroStep> kept = MacroEdit.FromRecording(rec, MacroEdit.DelaysRecorded, 0);
		Assert.Equal(40, kept[0].DelayMs);   // gap to the next event
		Assert.Equal(60, kept[1].DelayMs);
		Assert.Equal(0, kept[2].DelayMs);    // last step: no trailing wait
		Assert.Equal(MacroEdit.MouseLeft, kept[2].Mouse);
		List<MacroStep> none = MacroEdit.FromRecording(rec, MacroEdit.DelaysNone, 0);
		Assert.All(none, (MacroStep s) => Assert.Equal(0, s.DelayMs));
		List<MacroStep> fixedGap = MacroEdit.FromRecording(rec, MacroEdit.DelaysFixed, 15);
		Assert.Equal(15, fixedGap[0].DelayMs);
		Assert.Equal(15, fixedGap[1].DelayMs);
		Assert.Equal(0, fixedGap[2].DelayMs);
	}

	[Fact]
	public void RecordingCutsAThinkingPause()
	{
		List<MacroEdit.Recorded> rec = new List<MacroEdit.Recorded>
		{
			new MacroEdit.Recorded('A', false, 0, true, 0),
			new MacroEdit.Recorded('A', false, 0, false, 60000)
		};
		List<MacroStep> steps = MacroEdit.FromRecording(rec, MacroEdit.DelaysRecorded, 0);
		Assert.Equal(MacroEdit.MaxRecordedDelayMs, steps[0].DelayMs);
	}

	[Fact]
	public void UniqueNameAddsANumber()
	{
		string[] have = new[] { "Jumpthrow", "Jumpthrow (2)" };
		Assert.Equal("W Jumpthrow", MacroEdit.UniqueName(have, "W Jumpthrow"));
		Assert.Equal("Jumpthrow (3)", MacroEdit.UniqueName(have, "Jumpthrow"));
		Assert.Equal("Macro", MacroEdit.UniqueName(new string[0], ""));
	}

	[Fact]
	public void NameProblemsAreCaught()
	{
		string[] have = new[] { "Jumpthrow" };
		Assert.NotNull(MacroEdit.NameProblem(have, ""));
		Assert.NotNull(MacroEdit.NameProblem(have, "bad/name"));
		Assert.NotNull(MacroEdit.NameProblem(have, "jumpthrow"));          // case-insensitive clash
		Assert.Null(MacroEdit.NameProblem(have, "Jumpthrow", allowed: "Jumpthrow")); // renaming to itself
		Assert.Null(MacroEdit.NameProblem(have, "New One"));
	}

	private static ProfileStore StoreWithBinding()
	{
		ProfileStore store = new ProfileStore();
		store.Macros.Add(new Macro { Name = "Jumpthrow" });
		KbProfile kb = new KbProfile { Name = "gamer - S3" };
		kb.MacroKeys["KEY_UP_ARROW"] = "Jumpthrow";
		store.Keyboard.Add(kb);
		return store;
	}

	[Fact]
	public void BindingsAreListedByProfile()
	{
		List<string> binds = MacroEdit.Bindings(StoreWithBinding(), "Jumpthrow");
		Assert.Single(binds);
		Assert.Contains("UP ARROW", binds[0]);
		Assert.Contains("gamer - S3", binds[0]);
	}

	[Fact]
	public void RenameCarriesTheBinds()
	{
		ProfileStore store = StoreWithBinding();
		List<string> moved = MacroEdit.Rename(store, "Jumpthrow", "JT");
		Assert.Single(moved);
		Assert.Equal("JT", store.Macros[0].Name);
		Assert.Equal("JT", store.Keyboard[0].MacroKeys["KEY_UP_ARROW"]);
	}

	[Fact]
	public void DeleteRemovesTheMacroAndItsBinds()
	{
		ProfileStore store = StoreWithBinding();
		List<string> removed = MacroEdit.Delete(store, "Jumpthrow");
		Assert.Single(removed);
		Assert.Empty(store.Macros);
		Assert.False(store.Keyboard[0].MacroKeys.ContainsKey("KEY_UP_ARROW"));
	}

	[Fact]
	public void ExportAndImportRoundTrip()
	{
		Macro m = new Macro { Name = "W Jumpthrow" };
		m.Steps.Add(MacroEdit.KeyStep('W', false, true, 10));
		m.Steps.Add(MacroEdit.MouseStep(MacroEdit.MouseLeft, false, 5));
		m.Steps.Add(MacroEdit.WaitStep(20));
		string json = MacroEdit.ToJson(m);
		Macro back = MacroEdit.FromJson(json);
		Assert.Equal("W Jumpthrow", back.Name);
		Assert.Equal(3, back.Steps.Count);
		Assert.Equal('W', back.Steps[0].Vk);
		Assert.Equal(MacroEdit.MouseLeft, back.Steps[1].Mouse);
		Assert.Equal(20, back.Steps[2].DelayMs);
	}

	[Fact]
	public void ImportRejectsRubbish()
	{
		Assert.Throws<InvalidDataException>(() => MacroEdit.FromJson("not json"));
		Assert.Throws<InvalidDataException>(() => MacroEdit.FromJson("{\"Steps\":[]}"));            // no name
		Assert.Throws<InvalidDataException>(() => MacroEdit.FromJson("{\"Name\":\"x\",\"Steps\":[{\"Mouse\":9}]}")); // bad step
	}

	[Fact]
	public void CopyIsIndependent()
	{
		Macro m = new Macro { Name = "A" };
		m.Steps.Add(MacroEdit.KeyStep('A', false, true));
		Macro copy = MacroEdit.Copy(m, "B");
		copy.Steps[0].Vk = 'Z';
		Assert.Equal('A', m.Steps[0].Vk);
		Assert.Equal("B", copy.Name);
	}
}
