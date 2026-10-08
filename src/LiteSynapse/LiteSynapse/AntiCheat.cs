using System;
using System.Collections.Generic;

namespace LiteSynapse;

// Mouse profiles whose game uses kernel or server-side anti-cheat (from the anti-cheat flag in Razer's game list).
internal static class AntiCheat
{
	public static readonly string[] Profiles = new string[]
	{
		"APEXLEGENDS", "BF", "Call of Duty", "CS2", "fortnite", "h1z1", "Halo", "MarvelRivals",
		"new world", "PUBG", "rogue", "rust", "splitgate", "SIEGE", "Tarky", "Valorant"
	};

	private static readonly HashSet<string> Set = new HashSet<string>(Profiles, StringComparer.OrdinalIgnoreCase);

	public static bool IsProtected(string profileName)
	{
		return !string.IsNullOrWhiteSpace(profileName) && Set.Contains(profileName.Trim());
	}
}
