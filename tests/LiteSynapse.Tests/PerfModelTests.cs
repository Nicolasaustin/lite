using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// Every row on the Performance tab says whether the thing is ON or OFF and whether that is HELPING or HURTING.
// These pin the rules, because getting one backwards is exactly the confusion the layout exists to remove.
public class PerfModelTests
{
	[Theory]
	// "Pointer acceleration" helps when OFF: the setting applied ("on") means acceleration is off.
	[InlineData("on", false, "off")]
	[InlineData("off", false, "on")]
	// "Game Mode" helps when ON: applied means on.
	[InlineData("on", true, "on")]
	[InlineData("off", true, "off")]
	[InlineData("part", false, "part")]
	[InlineData("other", true, "other")]
	[InlineData("unknown", true, "unknown")]
	[InlineData(null, true, "unknown")]
	public void ASettingsStateBecomesTheStateOfTheThing(string tweakState, bool perfOn, string thing)
	{
		Assert.Equal(thing, PerfModel.ThingState(tweakState, perfOn));
	}

	[Theory]
	[InlineData("off", false, Tweaks.Safe, PerfModel.Helping)]
	[InlineData("on", false, Tweaks.Safe, PerfModel.Hurting)]
	[InlineData("on", true, Tweaks.Caution, PerfModel.Helping)]
	[InlineData("off", true, Tweaks.Caution, PerfModel.Hurting)]
	// A security feature left on is a safe default, not "hurting": turning it off is a trade-off, not a fix.
	[InlineData("on", false, Tweaks.Risky, PerfModel.SafeDefault)]
	[InlineData("off", false, Tweaks.Risky, PerfModel.Helping)]
	[InlineData("part", false, Tweaks.Safe, PerfModel.Hurting)]
	[InlineData("unknown", false, Tweaks.Safe, PerfModel.Unknown)]
	[InlineData("other", false, Tweaks.Safe, PerfModel.Unknown)]
	public void TheVerdictFollowsTheSideThatHelps(string state, bool helpsWhenOn, string risk, string verdict)
	{
		Assert.Equal(verdict, PerfModel.Verdict(state, helpsWhenOn, risk));
	}

	[Theory]
	[InlineData("disabled", "off")]
	[InlineData("absent", "off")]
	[InlineData("manual", "on")]
	[InlineData("auto", "on")]
	[InlineData("running", "on")]
	[InlineData("ready", "on")]
	[InlineData("on", "on")]
	[InlineData("off", "off")]
	[InlineData("", "unknown")]
	[InlineData(null, "unknown")]
	public void AServiceIsOnUnlessItIsDisabled(string scan, string state)
	{
		Assert.Equal(state, PerfModel.ServiceState(scan));
	}

	[Fact]
	public void ApplyingASettingPutsTheThingOnTheWantedSide()
	{
		// Want acceleration OFF (helps when off): apply the setting.
		Assert.True(PerfModel.TweakOn(wantThingOn: false, perfOn: false));
		// Want acceleration back ON: remove the setting.
		Assert.False(PerfModel.TweakOn(wantThingOn: true, perfOn: false));
		// Want Game Mode ON (helps when on): apply.
		Assert.True(PerfModel.TweakOn(true, true));
		Assert.False(PerfModel.TweakOn(false, true));
	}

	[Fact]
	public void AKnownSwitchFlipsAndFlipsBack()
	{
		bool? want = PerfModel.NextWant(current: true, want: null, helpsWhenOn: false);
		Assert.Equal(false, want);
		Assert.True(PerfModel.IsChange(true, want));
		want = PerfModel.NextWant(true, want, false);
		Assert.Null(want);
		Assert.False(PerfModel.IsChange(true, want));
	}

	[Fact]
	public void AnUnknownSwitchGoesToTheHelpingSideFirst()
	{
		bool? want = PerfModel.NextWant(current: null, want: null, helpsWhenOn: false);
		Assert.Equal(false, want);
		want = PerfModel.NextWant(null, want, false);
		Assert.Equal(true, want);
		want = PerfModel.NextWant(null, want, false);
		Assert.Null(want);
		// Any side is a change when the current one is not known.
		Assert.True(PerfModel.IsChange(null, true));
		Assert.True(PerfModel.IsChange(null, false));
		Assert.False(PerfModel.IsChange(null, null));
	}

	[Theory]
	[InlineData("ProcessLasso", "C:\\Program Files\\Process Lasso\\ProcessLasso.exe", true)]
	[InlineData("ProcessGovernor", "", true)]
	[InlineData("Bitsum something", "", true)]
	[InlineData("LiteSynapse", "C:\\x\\LiteSynapse.exe", true)]
	[InlineData("MSI Afterburner", "C:\\Program Files (x86)\\MSI Afterburner\\MSIAfterburner.exe", true)]
	[InlineData("RTSS", "C:\\Program Files (x86)\\RivaTuner Statistics Server\\RTSS.exe", true)]
	[InlineData("goxlr-launcher.exe - Shortcut", "C:\\Users\\admin\\AppData\\Roaming\\...\\goxlr-launcher.exe", true)]
	[InlineData("Steam", "C:\\Program Files (x86)\\Steam\\steam.exe", false)]
	[InlineData("Discord", "", false)]
	[InlineData(null, null, false)]
	public void ToolsThatHelpGamesAreHelpingWhenOn(string name, string path, bool helps)
	{
		Assert.Equal(helps, PerfModel.HelpsAtSignIn(name, path));
	}

	[Fact]
	public void LabelsSayItPlainly()
	{
		Assert.Equal("ON", PerfModel.StateLabel("on"));
		Assert.Equal("OFF", PerfModel.StateLabel("off"));
		Assert.Equal("HELPING", PerfModel.VerdictLabel(PerfModel.Helping));
		Assert.Equal("HURTING", PerfModel.VerdictLabel(PerfModel.Hurting));
		Assert.Equal("SAFE DEFAULT", PerfModel.VerdictLabel(PerfModel.SafeDefault));
		Assert.Equal("CAN'T TELL", PerfModel.VerdictLabel(PerfModel.Unknown));
	}

	[Fact]
	public void EveryWindowsSettingReadsAsHelpingOnceApplied()
	{
		// Whatever the setting, applying it must land the thing on its helping side - otherwise FIX ALL would
		// flip a row and it would still read HURTING afterwards.
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			string thing = PerfModel.ThingState("on", t.PerfOn);
			Assert.Equal(PerfModel.Helping, PerfModel.Verdict(thing, t.PerfOn, t.Risk));
			string back = PerfModel.ThingState("off", t.PerfOn);
			Assert.NotEqual(PerfModel.Helping, PerfModel.Verdict(back, t.PerfOn, t.Risk));
			// And the switch to that side applies it.
			Assert.True(PerfModel.TweakOn(PerfModel.AsBool(thing).Value, t.PerfOn), t.Id);
		}
	}
}
