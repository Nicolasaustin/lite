using System;
using System.Collections.Generic;

namespace LiteSynapse;

// What the keyboard hook (InputEngine.SetProfile) is given.
// LinkedGame: a linked game is actually in front, rather than a profile picked by hand (keeps the low-latency GC
//   mode to a real game, so a tray-resident "run macros anywhere" session does not hold it for days).
// SnapTapAllowed: Snap Tap may run in this context. Arrow macros run wherever the keyboard profile is armed, but
//   Snap Tap reorders A/D and would corrupt typing, so it runs only in a linked game, a hand-picked context, or
//   when the user opts into "Run Snap Tap everywhere too".
internal readonly record struct EngineInputs(MouseProfile Mouse, KbProfile Keyboard, bool InGame, bool LinkedGame = false, bool SnapTapAllowed = false)
{
	public static readonly EngineInputs Off = new EngineInputs(null, null, InGame: false);

	// True when the engine needs neither its hook nor macros.
	public bool IsOff => !InGame || (Mouse == null && Keyboard == null);
}

// Decisions the Service makes on every foreground change, kept free of Windows calls so they can be tested.
internal static class ServicePlan
{
	// How long the game has to stay out of the foreground before the mouse goes back to the desktop profile.
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

	// True when Competitive-safe applies: it is on and an anti-cheat game is in front. That is the program itself
	// (cs2.exe, ... whatever profile it is linked to, or none), or a linked game whose profile (or the profile the
	// user picked for it) is flagged as anti-cheat protected.
	public static bool BlockedByCompetitiveSafe(bool competitiveSafe, bool gameInFront, string gameProfile, string activeMouse, string foregroundExe = null)
	{
		if (!competitiveSafe)
		{
			return false;
		}
		if (AntiCheat.IsProtectedExe(foregroundExe))
		{
			return true;
		}
		if (!gameInFront)
		{
			return false;
		}
		return AntiCheat.IsProtected(gameProfile) || AntiCheat.IsProtected(activeMouse);
	}

	// What the keyboard hook gets.
	//  - gameInFront: a linked game is in front;
	//  - pickedByHand: the user picked the profiles by hand (automatic switching off) and LiteSynapse's own window
	//    is not in front. Snap Tap, macros and remaps work only in a linked game or with a profile picked by hand;
	//  - mouseOnboard: the mouse runs the profile from its own memory, so no software remap is needed;
	//  - suspended: sleeping or locked: nothing at all, the hook is removed;
	//  - foregroundExe: the program in front (for Competitive-safe).
	public static EngineInputs Engine(MouseProfile mp, KbProfile kp, bool gameInFront, bool mouseOnboard, bool competitiveSafe, string gameProfile, bool suspended, bool pickedByHand = false, string foregroundExe = null, bool macrosEverywhere = false, bool snapEverywhere = false)
	{
		// Normally the hook is armed only in a linked game or with profiles picked by hand. "Run macros anywhere"
		// keeps it armed on the desktop and in every other app too (the caller passes it as false while LiteSynapse's
		// own window is in front, so the macro editor's own arrow keys never fire a macro).
		if (suspended || !(gameInFront || pickedByHand || macrosEverywhere))
		{
			return EngineInputs.Off;
		}
		// Competitive safe still blocks when an anti-cheat game is the FOREGROUND window - that is when the input
		// would reach it. With the game only in the background, the foreground is another app, so this does not block.
		if (BlockedByCompetitiveSafe(competitiveSafe, gameInFront, gameProfile, mp?.Name, foregroundExe))
		{
			return EngineInputs.Off;
		}
		// A profile picked by hand runs its keyboard features (Snap Tap, arrow macros) wherever the user plays
		// - but not while typing in the desktop's own programs, where Snap Tap turns "ad" into stray letters and
		// an arrow macro fires into a text field. "Run macros anywhere" is the user opting into exactly that, so it
		// keeps the keyboard profile on those apps too. The mouse's buttons keep the profile everywhere regardless.
		bool suppressDesktopKb = !gameInFront && !macrosEverywhere && pickedByHand && IsDesktopApp(foregroundExe);
		KbProfile keyboard = suppressDesktopKb ? null : kp;
		// Arrow macros arm wherever the keyboard profile is armed. Snap Tap is separate: it reorders A/D and would
		// mangle typing, so it runs only in a real linked game, a hand-picked context (where desktop apps have the
		// keyboard suppressed above anyway), or when the user opts into "Run Snap Tap everywhere too".
		bool snapTapAllowed = gameInFront || pickedByHand || snapEverywhere;
		return new EngineInputs(mouseOnboard ? null : mp, keyboard, InGame: true, LinkedGame: gameInFront, SnapTapAllowed: snapTapAllowed);
	}

	// Programs people type in, which are never the game: the shell, browsers, chat, editors, terminals.
	private static readonly HashSet<string> DesktopApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"explorer.exe", "LiteSynapse.exe", "SearchHost.exe", "SearchApp.exe", "StartMenuExperienceHost.exe",
		"ShellExperienceHost.exe", "TextInputHost.exe", "ApplicationFrameHost.exe",
		"chrome.exe", "msedge.exe", "firefox.exe", "opera.exe", "opera_gx.exe", "brave.exe", "vivaldi.exe",
		"Discord.exe", "Teams.exe", "ms-teams.exe", "slack.exe", "WhatsApp.exe", "Telegram.exe",
		"notepad.exe", "Notepad++.exe", "Code.exe", "devenv.exe", "WINWORD.EXE", "EXCEL.EXE", "POWERPNT.EXE",
		"OUTLOOK.EXE", "OneNote.exe", "Obsidian.exe", "Notion.exe",
		"cmd.exe", "powershell.exe", "pwsh.exe", "WindowsTerminal.exe", "conhost.exe",
		"steamwebhelper.exe", "EpicGamesLauncher.exe", "Spotify.exe", "obs64.exe"
	};

	internal static bool IsDesktopApp(string exe)
	{
		if (string.IsNullOrEmpty(exe))
		{
			return false;
		}
		int slash = exe.LastIndexOfAny(new[] { '\\', '/' });
		return DesktopApps.Contains((slash >= 0) ? exe.Substring(slash + 1) : exe);
	}

	// Delay before the mouse follows a new target: going from a game's profile back to the desktop profile waits,
	// so a quick alt-tab does not switch slots twice; everything else is immediate.
	public static int MouseDelay(bool wasGame, bool nowGame, bool debounce)
	{
		if (debounce && wasGame && !nowGame)
		{
			return RestoreDelayMs;
		}
		return 0;
	}
}
