using System;

namespace LiteSynapse;

// What the Performance tab says about one row, worked out from what the scan read. Kept apart from the drawing
// so the rules are tested.
//
// Every row answers two questions the same way, whatever kind of thing it is:
//
//   Is it ON or OFF?            The thing itself: "Pointer acceleration", "Print Spooler", "Steam at sign-in".
//   Is that HELPING or HURTING? Whether the side it is on is the one that is better for games.
//
// Before this the tab mixed the two: a row titled "Pointer acceleration off" showed "ON" when acceleration was
// off, a service showed "AUTOMATIC", and the buttons said "turn ticked on" - so there was no way to tell at a
// glance whether something was costing frames.
internal static class PerfModel
{
	// The side the thing is on is the better one for games.
	public const string Helping = "helping";

	// The side the thing is on costs performance.
	public const string Hurting = "hurting";

	// A security feature on its default side: it costs a little, and it protects you. Not called "hurting",
	// because that would read as "turn this off", which is a trade-off and not a fix.
	public const string SafeDefault = "safe";

	// Windows will not say.
	public const string Unknown = "unknown";

	// A tweak's scan state ("on" = the setting is applied) as the state of the thing it switches.
	public static string ThingState(string tweakState, bool perfOn)
	{
		switch (tweakState)
		{
		case "on":
			return perfOn ? "on" : "off";
		case "off":
			return perfOn ? "off" : "on";
		case "part":
		case "other":
			return tweakState;
		default:
			return "unknown";
		}
	}

	// A service's start type as on/off: disabled is off, anything that can start is on.
	public static string ServiceState(string scanState)
	{
		switch (scanState)
		{
		case "disabled":
		case "off":
		case "absent":
			return "off";
		case "auto":
		case "manual":
		case "running":
		case "stopped":
		case "on":
		case "enabled":
		case "ready":
			return "on";
		default:
			return "unknown";
		}
	}

	// Whether the side the thing is on helps. helpsWhenOn: which side does. Risky rows (security features)
	// on their protective side are a safe default rather than "hurting".
	public static string Verdict(string thingState, bool helpsWhenOn, string risk)
	{
		switch (thingState)
		{
		case "on":
		case "off":
		{
			bool helping = (thingState == "on") == helpsWhenOn;
			if (helping)
			{
				return Helping;
			}
			return (risk == Tweaks.Risky) ? SafeDefault : Hurting;
		}
		case "part":
			// Half applied: some of the gain is missing.
			return (risk == Tweaks.Risky) ? SafeDefault : Hurting;
		default:
			return Unknown;
		}
	}

	// true for on, false for off, null when it is neither.
	public static bool? AsBool(string thingState)
	{
		return thingState switch
		{
			"on" => true,
			"off" => false,
			_ => null
		};
	}

	public static string StateLabel(string thingState)
	{
		return thingState switch
		{
			"on" => "ON",
			"off" => "OFF",
			"part" => "PARTLY ON",
			"other" => "CUSTOM",
			_ => "?"
		};
	}

	public static string VerdictLabel(string verdict)
	{
		return verdict switch
		{
			Helping => "HELPING",
			Hurting => "HURTING",
			SafeDefault => "SAFE DEFAULT",
			_ => "CAN'T TELL"
		};
	}

	// For a tweak: what to ask Apply for so the thing ends up on the wanted side.
	public static bool TweakOn(bool wantThingOn, bool perfOn)
	{
		return wantThingOn == perfOn;
	}

	// The next position of a row's switch when it is clicked. current: what the PC is set to (null when it
	// cannot be read); want: the pending change (null when none). Returns the new pending change.
	//
	// A known row flips between "change it" and "leave it". An unknown one goes to the helping side first,
	// then the other side, then back to "leave it".
	public static bool? NextWant(bool? current, bool? want, bool helpsWhenOn)
	{
		if (current.HasValue)
		{
			return want.HasValue ? null : !current.Value;
		}
		if (!want.HasValue)
		{
			return helpsWhenOn;
		}
		return (want.Value == helpsWhenOn) ? !helpsWhenOn : null;
	}

	// Is this a change at all? Wanting the side it is already on is not.
	public static bool IsChange(bool? current, bool? want)
	{
		return want.HasValue && (!current.HasValue || current.Value != want.Value);
	}

	// Start-up programs that help a gaming PC rather than cost it: a process-priority tool is the reason
	// someone's games run well, and LiteSynapse itself does the profile switching. Shown as helping when on,
	// and "fix all" turns them back on rather than off.
	public static bool HelpsAtSignIn(string name, string path)
	{
		string text = (name ?? "") + "|" + (path ?? "");
		foreach (string word in new[] { "ProcessLasso", "Process Lasso", "ProcessGovernor", "Bitsum", "LiteSynapse" })
		{
			if (text.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return true;
			}
		}
		return false;
	}

	// Start-up entries that are Windows' own security: turning one off is a trade-off, not a fix.
	public static bool IsSecurity(string name, string path)
	{
		string text = (name ?? "") + "|" + (path ?? "");
		return text.IndexOf("SecurityHealth", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("Windows Defender", StringComparison.OrdinalIgnoreCase) >= 0
			|| text.IndexOf("MSASCui", StringComparison.OrdinalIgnoreCase) >= 0;
	}
}
