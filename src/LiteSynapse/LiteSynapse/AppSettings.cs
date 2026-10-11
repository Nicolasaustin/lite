using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

// data/settings.json. New fields default to the old behaviour, so an existing file changes nothing.
public class AppSettings
{
	// "Run selected profiles on the mouse itself" (live onboard slots).
	public bool LiveWrite { get; set; } = true;

	public string DefaultMouse { get; set; } = "CS2";

	public string DefaultKeyboard { get; set; } = "gamer - S3";

	// When on: no Snap Tap, macros or software remaps while an anti-cheat game is in front. Off unless the user
	// turns it on.
	public bool CompetitiveSafe { get; set; }

	// "Switch profiles automatically when a game starts". Picking a profile by hand turns it off for the session
	// only; the Settings switch is what is remembered.
	public bool Auto { get; set; } = true;

	// Keyboard profile for game rules that have none ("" = none, as before).
	public string GameKeyboardDefault { get; set; } = "";

	// "Run macros anywhere": when on, the active keyboard profile's macros (the arrow-key triggers) work on the
	// desktop and in other apps too, not only in a linked game - so a macro fires even with a game running in the
	// background. Competitive safe still blocks them while an anti-cheat game is the FOREGROUND window. Off unless
	// turned on. This does NOT turn on Snap Tap off-game (that would reorder A/D while typing); SnapEverywhere does.
	public bool MacrosEverywhere { get; set; }

	// "Run Snap Tap everywhere too": when on (and MacrosEverywhere is on), Snap Tap's A/D last-input-wins also runs
	// on the desktop and in other apps, not only in a linked game. It changes how A and D behave while typing (a
	// quick "ad" can drop or swap a letter), so it is a separate opt-in and is off unless the user turns it on.
	public bool SnapEverywhere { get; set; }

	// Fields written by other versions survive a load/save round trip.
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }
}
