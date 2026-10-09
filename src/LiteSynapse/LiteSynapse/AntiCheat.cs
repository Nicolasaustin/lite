using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteSynapse;

// Mouse profiles whose game uses kernel or server-side anti-cheat: the anti-cheat flag in Razer's game list, plus
// games that added such anti-cheat since (DEADLOCK: VAC, LeagueOfLegends: Vanguard, gta: BattlEye).
internal static class AntiCheat
{
	public static readonly string[] Profiles = new string[]
	{
		"APEXLEGENDS", "BF", "Call of Duty", "CS2", "DEADLOCK", "fortnite", "gta", "h1z1", "Halo", "LeagueOfLegends",
		"MarvelRivals", "new world", "PUBG", "rogue", "rust", "splitgate", "SIEGE", "Tarky", "Valorant"
	};

	private static readonly HashSet<string> Set = new HashSet<string>(Profiles, StringComparer.OrdinalIgnoreCase);

	// Those games' programs (cs2.exe, VALORANT-Win64-Shipping.exe, ...), so the protection also holds when the game's
	// profile was renamed, its exe linked to another profile, or the game not linked at all.
	private static readonly HashSet<string> Exes = new HashSet<string>(Profiles.SelectMany(DefaultGames.ExesFor), StringComparer.OrdinalIgnoreCase);

	public static bool IsProtected(string profileName)
	{
		return !string.IsNullOrWhiteSpace(profileName) && Set.Contains(profileName.Trim());
	}

	public static bool IsProtectedExe(string exe)
	{
		return !string.IsNullOrWhiteSpace(exe) && Exes.Contains(exe.Trim());
	}
}
