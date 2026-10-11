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
		// Automatic switching on the desktop: Snap Tap/macros/remaps wait for a game (P1-9).
		EngineInputs e = ServicePlan.Engine(Cs2, Gamer, gameInFront: false, mouseOnboard: false, competitiveSafe: false, null, suspended: false);
		Assert.Equal(EngineInputs.Off, e);
		Assert.True(e.IsOff);
	}

	[Fact]
	public void AProfilePickedByHandRunsEverywhere()
	{
		// "Use whatever profile I click": its macros and Snap Tap work without a linked game in front.
		EngineInputs e = ServicePlan.Engine(Cs2, Gamer, gameInFront: false, mouseOnboard: true, competitiveSafe: false, null, suspended: false, pickedByHand: true);
		Assert.Same(Gamer, e.Keyboard);
		Assert.Null(e.Mouse);
		Assert.True(e.InGame);
		Assert.False(e.IsOff);
		// Sleep and lock still remove the hook.
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Cs2, Gamer, gameInFront: false, mouseOnboard: true, competitiveSafe: false, null, suspended: true, pickedByHand: true));
	}

	[Fact]
	public void CompetitiveSafeOnlyBlocksAnAntiCheatGameInFront()
	{
		// On the desktop a profile picked by hand is not blocked, even if it is named after an anti-cheat game.
		Assert.Same(Gamer, ServicePlan.Engine(Cs2, Gamer, gameInFront: false, mouseOnboard: true, competitiveSafe: true, null, suspended: false, pickedByHand: true).Keyboard);
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: true, competitiveSafe: true, "CS2", suspended: false, pickedByHand: true));
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
	public void CompetitiveSafeFollowsTheAntiCheatProgramWhateverItsProfile()
	{
		MouseProfile renamed = new MouseProfile { Name = "CS2 Premier" };
		// The CS2 profile was renamed: cs2.exe in front is still protected.
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(renamed, Gamer, gameInFront: true, mouseOnboard: true, competitiveSafe: true, "CS2 Premier", suspended: false, foregroundExe: "cs2.exe"));
		// cs2.exe is not linked at all and a keyboard profile was picked by hand.
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Peak, Gamer, gameInFront: false, mouseOnboard: true, competitiveSafe: true, null, suspended: false, pickedByHand: true, foregroundExe: "CS2.EXE"));
		Assert.True(ServicePlan.BlockedByCompetitiveSafe(competitiveSafe: true, gameInFront: false, null, "PEAK", "VALORANT-Win64-Shipping.exe"));
		// Competitive-safe off, or another program in front: nothing changes.
		Assert.Same(Gamer, ServicePlan.Engine(renamed, Gamer, gameInFront: true, mouseOnboard: true, competitiveSafe: false, "CS2 Premier", suspended: false, foregroundExe: "cs2.exe").Keyboard);
		Assert.Same(Gamer, ServicePlan.Engine(Peak, Gamer, gameInFront: false, mouseOnboard: true, competitiveSafe: true, null, suspended: false, pickedByHand: true, foregroundExe: "SomeIndieGame.exe").Keyboard);
		Assert.Same(Gamer, ServicePlan.Engine(Peak, Gamer, gameInFront: true, mouseOnboard: true, competitiveSafe: true, "PEAK", suspended: false, foregroundExe: "PEAK.exe").Keyboard);
	}

	[Fact]
	public void APickedProfileLeavesTypingInTheDesktopsOwnProgramsAlone()
	{
		// Picked by hand, a browser or a text editor in front: Snap Tap and arrow macros would put stray keys
		// into what the user types, so the keyboard part is off - the mouse's buttons keep the profile.
		foreach (string exe in new[] { "chrome.exe", "C:\\Windows\\explorer.exe", "Discord.exe", "NOTEPAD.EXE", "LiteSynapse.exe" })
		{
			EngineInputs e = ServicePlan.Engine(Peak, Gamer, gameInFront: false, mouseOnboard: false, competitiveSafe: false, null,
				suspended: false, pickedByHand: true, foregroundExe: exe);
			Assert.Null(e.Keyboard);
			Assert.Same(Peak, e.Mouse);
			Assert.False(e.LinkedGame);
		}
		// A linked game in front is a game whatever its name, and it is the only time the GC is held.
		EngineInputs game = ServicePlan.Engine(Peak, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: false, "PEAK",
			suspended: false, foregroundExe: "PEAK.exe");
		Assert.Same(Gamer, game.Keyboard);
		Assert.True(game.LinkedGame);
	}

	[Fact]
	public void RunMacrosAnywhereArmsOnTheDesktopAndInWorkApps()
	{
		// Off (default): on the desktop with nothing picked, the engine is off.
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Peak, Gamer, gameInFront: false, mouseOnboard: false, competitiveSafe: false, null,
			suspended: false, pickedByHand: false, foregroundExe: "EXCEL.EXE", macrosEverywhere: false));
		// On: the keyboard profile (macros, Snap Tap) is armed in a work app, even in auto mode with no game.
		EngineInputs work = ServicePlan.Engine(Peak, Gamer, gameInFront: false, mouseOnboard: false, competitiveSafe: false, null,
			suspended: false, pickedByHand: false, foregroundExe: "EXCEL.EXE", macrosEverywhere: true);
		Assert.Same(Gamer, work.Keyboard);
		Assert.False(work.LinkedGame);
	}

	[Fact]
	public void RunMacrosAnywhereStillLetsCompetitiveSafeBlockAForegroundGame()
	{
		// CS2 is the FRONT window and Competitive safe is on: blocked even with "run macros anywhere".
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: true, "CS2",
			suspended: false, foregroundExe: "cs2.exe", macrosEverywhere: true));
		// CS2 only in the BACKGROUND (a work app is front): not blocked, so the macros arm.
		EngineInputs bg = ServicePlan.Engine(Cs2, Gamer, gameInFront: false, mouseOnboard: false, competitiveSafe: true, null,
			suspended: false, foregroundExe: "WINWORD.EXE", macrosEverywhere: true);
		Assert.Same(Gamer, bg.Keyboard);
		// Competitive safe OFF is the full override: macros arm even with CS2 the front window.
		Assert.Same(Gamer, ServicePlan.Engine(Cs2, Gamer, gameInFront: true, mouseOnboard: false, competitiveSafe: false, "CS2",
			suspended: false, foregroundExe: "cs2.exe", macrosEverywhere: true).Keyboard);
		// Sleeping/locked still wins over everything.
		Assert.Equal(EngineInputs.Off, ServicePlan.Engine(Cs2, Gamer, gameInFront: false, mouseOnboard: false, competitiveSafe: false, null,
			suspended: true, foregroundExe: "WINWORD.EXE", macrosEverywhere: true));
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
		Assert.Equal(ServicePlan.RestoreDelayMs, ServicePlan.MouseDelay(wasGame: true, nowGame: false, debounce: true));
		Assert.Equal(0, ServicePlan.MouseDelay(wasGame: false, nowGame: true, debounce: true));
		Assert.Equal(0, ServicePlan.MouseDelay(wasGame: true, nowGame: true, debounce: true));
		Assert.Equal(0, ServicePlan.MouseDelay(wasGame: false, nowGame: false, debounce: true));
		// A choice made in the UI is applied right away.
		Assert.Equal(0, ServicePlan.MouseDelay(wasGame: true, nowGame: false, debounce: false));
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
