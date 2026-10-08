using System.Collections.Generic;
using Xunit;

namespace LiteSynapse.Tests;

public class ServicePlanTests
{
	private static readonly MouseProfile Cs2 = new MouseProfile { Name = "CS2" };

	private static readonly MouseProfile Peak = new MouseProfile { Name = "PEAK" };

	private static readonly KbProfile Gamer = new KbProfile { Name = "gamer - S3", SnapTap = true };

	[Fact]
	public void NothingRunsWithoutALinkedGameInFront()
	{
		// A profile picked by hand changes the devices, but Snap Tap/macros/remaps wait for a game (P1-9).
		EngineInputs e = ServicePlan.Engine(Cs2, Gamer, gameInFront: false, mouseOnboard: false, competitiveSafe: false, null, suspended: false);
		Assert.Equal(EngineInputs.Off, e);
		Assert.True(e.IsOff);
	}

	[Fact]
	public void InAGameTheKeyboardProfileAndSoftwareRemapsApply()
	{
		EngineInputs e = ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: false, "CS2", suspended: false);
		Assert.Same(Cs2, e.Mouse);
		Assert.Same(Gamer, e.Keyboard);
		Assert.True(e.InGame);
	}

	[Fact]
	public void TheMouseRunningTheProfileOnboardNeedsNoSoftwareRemap()
	{
		EngineInputs e = ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: true, competitiveSafe: false, "CS2", suspended: false);
		Assert.Null(e.Mouse);
		Assert.Same(Gamer, e.Keyboard);
	}

	[Fact]
	public void CompetitiveSafeTurnsEverythingOffInAntiCheatGames()
	{
		EngineInputs e = ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: true, "CS2", suspended: false);
		Assert.Equal(EngineInputs.Off, e);
		// Also when the user picked an anti-cheat profile by hand for another game.
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: true, "PEAK", suspended: false));
	}

	[Fact]
	public void CompetitiveSafeLeavesOtherGamesAlone()
	{
		EngineInputs e = ServicePlan.Engine(Peak, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: true, "PEAK", suspended: false);
		Assert.Same(Gamer, e.Keyboard);
		Assert.True(e.InGame);
	}

	[Fact]
	public void CompetitiveSafeOffKeepsTheUsersCs2Setup()
	{
		// Default: existing users keep Snap Tap and macros in CS2 until they turn the switch on.
		Assert.False(new AppSettings().CompetitiveSafe);
		EngineInputs e = ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: true, competitiveSafe: false, "CS2", suspended: false);
		Assert.Same(Gamer, e.Keyboard);
	}

	[Fact]
	public void SleepOrLockRemovesTheHook()
	{
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: false, "CS2", suspended: true));
	}

	[Fact]
	public void BlockedOnlyWhenOnAndAGameIsInFront()
	{
		Assert.True(ServicePlan.BlockedByCompetitiveSafe(competitiveSafe: true, gameInFront: true, "Valorant", null));
		Assert.False(ServicePlan.BlockedByCompetitiveSafe(competitiveSafe: false, gameInFront: true, "Valorant", "Valorant"));
		Assert.False(ServicePlan.BlockedByCompetitiveSafe(competitiveSafe: true, gameInFront: false, "Valorant", "Valorant"));
		Assert.False(ServicePlan.BlockedByCompetitiveSafe(competitiveSafe: true, gameInFront: true, "PEAK", "PEAK"));
	}

	[Fact]
	public void GameKeyboardUsesTheRuleThenTheGamesDefault()
	{
		HashSet<string> kbs = new HashSet<string> { "gamer - S3", "typing" };
		GameRule own = new GameRule { Profile = "CS2", KeyboardProfile = "typing" };
		GameRule none = new GameRule { Profile = "PEAK", KeyboardProfile = "" };
		Assert.Equal("typing", ServicePlan.GameKeyboard(own, "gamer - S3", kbs.Contains));
		Assert.Equal("gamer - S3", ServicePlan.GameKeyboard(none, "gamer - S3", kbs.Contains));
		Assert.Equal("", ServicePlan.GameKeyboard(none, "", kbs.Contains));
		Assert.Equal("", ServicePlan.GameKeyboard(none, "deleted profile", kbs.Contains));
		Assert.Equal("", ServicePlan.GameKeyboard(null, "gamer - S3", kbs.Contains));
	}

	[Fact]
	public void OnlyLeavingAGameWaitsBeforeTheMouseFollows()
	{
		Assert.Equal(ServicePlan.RestoreDelayMs, ServicePlan.MouseDelay(wasLive: true, nowLive: false, debounce: true));
		Assert.Equal(0, ServicePlan.MouseDelay(wasLive: false, nowLive: true, debounce: true));
		Assert.Equal(0, ServicePlan.MouseDelay(wasLive: true, nowLive: true, debounce: true));
		Assert.Equal(0, ServicePlan.MouseDelay(wasLive: false, nowLive: false, debounce: true));
		// A choice made in the UI is applied right away.
		Assert.Equal(0, ServicePlan.MouseDelay(wasLive: true, nowLive: false, debounce: false));
		Assert.InRange(ServicePlan.RestoreDelayMs, 2500, 3500);
	}

	[Theory]
	[InlineData("explorer.exe", true)]
	[InlineData("EXPLORER.EXE", true)]
	[InlineData("ShellExperienceHost.exe", true)]
	[InlineData("SearchHost.exe", true)]
	[InlineData("LiteSynapse.exe", true)]
	[InlineData("MyLiteSynapse.exe", true)]
	[InlineData("cs2.exe", false)]
	[InlineData("FireProject.exe", false)]
	[InlineData("", true)]
	[InlineData(null, true)]
	public void ShellHostsAndLiteSynapseAreNeverTheRunningGame(string exe, bool expected)
	{
		Assert.Equal(expected, ServicePlan.IsShellOrSelf(exe, "MyLiteSynapse.exe"));
	}

	[Fact]
	public void AntiCheatProfilesMatchTheContract()
	{
		string[] expected = new string[16]
		{
			"APEXLEGENDS", "BF", "Call of Duty", "CS2", "fortnite", "h1z1", "Halo", "MarvelRivals", "new world", "PUBG",
			"rogue", "rust", "splitgate", "SIEGE", "Tarky", "Valorant"
		};
		foreach (string p in expected)
		{
			Assert.True(AntiCheat.IsProtected(p), p);
		}
		Assert.False(AntiCheat.IsProtected("PEAK"));
		Assert.False(AntiCheat.IsProtected("Valorant (1)"));
	}
}
