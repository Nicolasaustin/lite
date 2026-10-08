using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteSynapse;

// One-time, add-only fixes for data imported by earlier versions. Nothing the user set is removed or overwritten:
// a value is only added where the field is empty. ProfileStore.MigrationVersion records what has run.
internal static class DataMigrations
{
	public const int CurrentVersion = 1;

	internal sealed class Result
	{
		public int Lighting;

		public int Buttons;

		public int Exes;

		public bool VersionChanged;

		public bool Changed => VersionChanged || Lighting + Buttons + Exes > 0;

		public override string ToString()
		{
			return $"lighting on {Lighting} profiles, {Buttons} buttons, {Exes} game exes";
		}
	}

	// Version 1: the Synapse settings the first importer dropped (key: profiles, migration.json) and exe names
	// for game rules (key: gms3, link-suggestions.json; high/medium confidence only).

	// Static colours per mouse profile; every other profile listed in SpectrumProfiles used spectrum cycling.
	private static readonly Dictionary<string, string> StaticColors = new Dictionary<string, string>
	{
		["APEXLEGENDS"] = "#00FF00",
		["CS2"] = "#00FF00",
		["DEADLOCK"] = "#00FF00",
		["paintgame"] = "#00FF00",
		["PEAK"] = "#00FF00",
		["raft"] = "#00FF00",
		["roblox"] = "#00FF00",
		["REPO"] = "#00FF00",
		["Schedule1"] = "#00FF00",
		["SPECTRE"] = "#00FF00",
		["crabgame"] = "#008000",
		["rogue"] = "#008000",
		["splitgate"] = "#008000",
		["SeaOfthieves"] = "#008000",
		["embr"] = "#008080",
		["Harrypotter"] = "#008080",
		["Valorant"] = "#008080",
		["BF"] = "#404040",
		["Call of Duty"] = "#404040",
		["zomb"] = "#404040",
		["lethalcompany"] = "#FFFFFF",
		["Marathon"] = "#FFFFFF",
		["Tarky"] = "#FFFFFF",
		["Amongus"] = "#C06000",
		["Arc Raiders"] = "#808000",
		["fortnite"] = "#8080FF",
		["lastofus"] = "#FF8000",
		["PUBG"] = "#804000",
		["rust"] = "#FFC0C0",
		["SIEGE"] = "#C000C0"
	};

	private static readonly string[] SpectrumProfiles = new string[19]
	{
		"cyberpunk", "deadrising", "dealer", "Deceit", "Fallout76", "gta", "h1z1", "Halo", "LeagueOfLegends", "metro",
		"minecraft", "MarvelRivals", "new world", "overcooked", "Overwatch", "phas", "Valorant (1)", "Valorant (2)", "WorldofWarcraft"
	};

	// Side buttons Synapse had switched off (empty keyboard function) or set to middle click.
	private static readonly (string Profile, string Button, string Key)[] ButtonFixes = new (string, string, string)[8]
	{
		("APEXLEGENDS", "Side3", KeyTarget.Disabled),
		("APEXLEGENDS", "Side4", KeyTarget.Disabled),
		("deadrising", "Side1", KeyTarget.Disabled),
		("Fallout76", "Side2", KeyTarget.Disabled),
		("Halo", "Side2", KeyTarget.Disabled),
		("phas", "Side2", KeyTarget.Disabled),
		("phas", "Side3", KeyTarget.Disabled),
		("Tarky", "Side4", KeyTarget.MouseMiddle)
	};

	// Exes added to existing game rules. Deliberately absent: SPECTRE, dealer, paintgame, Marathon (no reliable exe)
	// and DEADLOCK.exe (project8.exe owns the game window).
	internal static readonly (string Profile, string[] Exes)[] GameExes = new (string, string[])[8]
	{
		("embr", new string[1] { "FireProject.exe" }),
		("zomb", new string[2] { "ProjectZomboid64.exe", "ProjectZomboid32.exe" }),
		("deadrising", new string[6] { "DRDR.exe", "DeadRising.exe", "deadrising2.exe", "deadrising2otr.exe", "deadrising3.exe", "deadrising4.exe" }),
		("Call of Duty", new string[4] { "cod22-cod.exe", "cod23-cod.exe", "sp22-cod.exe", "sp23-cod.exe" }),
		("lastofus", new string[2] { "tlou-i-l.exe", "tlou-ii-l.exe" }),
		("Halo", new string[1] { "MCCWinStore-Win64-Shipping.exe" }),
		("splitgate", new string[2] { "PortalWars-Win64-Shipping.exe", "PortalWars.exe" }),
		("WorldofWarcraft", new string[2] { "Wow.exe", "WowClassic.exe" })
	};

	// Runs the migrations this data has not had yet. True when store or games changed (save both then).
	public static bool Apply(ProfileStore store, List<GameRule> games)
	{
		Result r = Run(store, games);
		if (r.Changed)
		{
			Cfg.Log($"data migration {CurrentVersion}: {r}");
		}
		return r.Changed;
	}

	internal static Result Run(ProfileStore store, List<GameRule> games)
	{
		Result r = new Result();
		if (store == null || store.MigrationVersion >= CurrentVersion)
		{
			return r;
		}
		AddLighting(store, r);
		AddButtons(store, r);
		AddGameExes(games, r);
		store.MigrationVersion = CurrentVersion;
		r.VersionChanged = true;
		return r;
	}

	internal static MouseLightingSettings LightingFor(string profile)
	{
		if (profile == null)
		{
			return null;
		}
		if (StaticColors.TryGetValue(profile, out var color))
		{
			return new MouseLightingSettings { Effect = "static", Color = color, Brightness = 100 };
		}
		if (Array.IndexOf(SpectrumProfiles, profile) >= 0)
		{
			return new MouseLightingSettings { Effect = "spectrum", Brightness = 100 };
		}
		return null;
	}

	private static void AddLighting(ProfileStore store, Result r)
	{
		foreach (MouseProfile p in store.Mouse ?? new List<MouseProfile>())
		{
			if (p == null || p.Lighting != null)
			{
				continue;
			}
			MouseLightingSettings s = LightingFor(p.Name);
			if (s != null)
			{
				p.Lighting = s;
				r.Lighting++;
			}
		}
	}

	private static void AddButtons(ProfileStore store, Result r)
	{
		foreach (var (profile, button, key) in ButtonFixes)
		{
			MouseProfile p = store.Mouse?.FirstOrDefault((MouseProfile x) => x?.Name == profile);
			if (p == null)
			{
				continue;
			}
			p.Buttons ??= new Dictionary<string, KeyTarget>();
			if (!p.Buttons.ContainsKey(button))
			{
				p.Buttons[button] = new KeyTarget { Key = key };
				r.Buttons++;
			}
		}
	}

	private static void AddGameExes(List<GameRule> games, Result r)
	{
		if (games == null)
		{
			return;
		}
		foreach (var (profile, exes) in GameExes)
		{
			GameRule rule = games.FirstOrDefault((GameRule g) => g?.Profile == profile);
			if (rule == null)
			{
				continue;
			}
			rule.Exes ??= new List<string>();
			foreach (string exe in exes)
			{
				// Never add an exe another rule already claims: the first matching rule would change.
				if (!games.Any((GameRule g) => g?.Exes != null && g.Exes.Contains(exe, StringComparer.OrdinalIgnoreCase)))
				{
					rule.Exes.Add(exe);
					r.Exes++;
				}
			}
		}
	}
}
