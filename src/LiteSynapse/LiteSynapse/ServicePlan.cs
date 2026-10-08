using System;
using System.Collections.Generic;

namespace LiteSynapse;

// What the keyboard hook (InputEngine.SetProfile) is given.
internal readonly record struct EngineInputs(MouseProfile Mouse, KbProfile Keyboard, bool InGame)
{
	public static readonly EngineInputs Off = new EngineInputs(null, null, InGame: false);

	// True when the engine needs neither its hook nor macros.
	public bool IsOff => !InGame || (Mouse == null && Keyboard == null);
}

// Decisions the Service makes on every foreground change, kept free of Windows calls so they can be tested.
internal static class ServicePlan
{
	// How long the game has to stay out of the foreground before the mouse goes back to the desktop slot.
	public const int RestoreDelayMs = 3000;

	// Programs that are LiteSynapse itself or part of the Windows shell: never "the game that is running".
	private static readonly HashSet<string> ShellHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"explorer.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe", "SearchHost.exe", "SearchApp.exe",
		"SearchUI.exe", "LockApp.exe", "TextInputHost.exe", "ApplicationFrameHost.exe", "ShellHost.exe", "sihost.exe",
		"ctfmon.exe", "dwm.exe", "LogonUI.exe", "consent.exe", "CredentialUIBroker.exe", "PickerHost.exe",
		"OpenWith.exe", "rundll32.exe", "SystemSettings.exe", "Taskmgr.exe", "msedgewebview2.exe", "WidgetBoard.exe",
		"Widgets.exe", "SnippingTool.exe", "ScreenClippingHost.exe", "SecHealthUI.exe", "LiteSynapse.exe"
	};

	public static bool IsShellOrSelf(string exe, string selfExe)
	{
		if (string.IsNullOrEmpty(exe))
		{
			return true;
		}
		if (!string.IsNullOrEmpty(selfExe) && string.Equals(exe, selfExe, StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		return ShellHosts.Contains(exe);
	}

	// Keyboard profile for a game rule: the rule's own, else the "games" default, else none.
	// exists says whether a keyboard profile of that name is there.
	public static string GameKeyboard(GameRule rule, string gameKeyboardDefault, Func<string, bool> exists)
	{
		if (rule == null)
		{
			return "";
		}
		if (!string.IsNullOrEmpty(rule.KeyboardProfile))
		{
			return rule.KeyboardProfile;
		}
		if (!string.IsNullOrEmpty(gameKeyboardDefault) && (exists == null || exists(gameKeyboardDefault)))
		{
			return gameKeyboardDefault;
		}
		return "";
	}

	// True when Competitive-safe applies: it is on, a linked game is in front, and that game (or the profile the
	// user picked for it) is flagged as anti-cheat protected.
	public static bool BlockedByCompetitiveSafe(bool competitiveSafe, bool gameInFront, string gameProfile, string activeMouse)
	{
		if (!competitiveSafe || !gameInFront)
		{
			return false;
		}
		return AntiCheat.IsProtected(gameProfile) || AntiCheat.IsProtected(activeMouse);
	}

	// What the keyboard hook gets.
	//  - gameInFront: a linked game is in front (Snap Tap, macros and remaps only work then; a profile picked by
	//    hand changes the devices, not this);
	//  - mouseOnboard: the mouse runs the profile from its own memory, so no software remap is needed;
	//  - suspended: sleeping or locked: nothing at all, the hook is removed.
	public static EngineInputs Engine(MouseProfile mp, KbProfile kp, bool gameInFront, bool mouseOnboard, bool competitiveSafe, string gameProfile, bool suspended)
	{
		if (suspended || !gameInFront)
		{
			return EngineInputs.Off;
		}
		if (BlockedByCompetitiveSafe(competitiveSafe, gameInFront, gameProfile, mp?.Name))
		{
			return EngineInputs.Off;
		}
		return new EngineInputs(mouseOnboard ? null : mp, kp, InGame: true);
	}

	// Delay before the mouse follows a new target: going from a game (live) back to the desktop waits, so a quick
	// alt-tab does not switch slots twice; everything else is immediate.
	public static int MouseDelay(bool wasLive, bool nowLive, bool debounce)
	{
		if (debounce && wasLive && !nowLive)
		{
			return RestoreDelayMs;
		}
		return 0;
	}
}
