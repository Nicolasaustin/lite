using System;
using System.Collections.Generic;
using System.Linq;

namespace LiteSynapse;

internal static class DefaultGames
{
	private static readonly Dictionary<string, string[]> Known = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
	{
		["Amongus"] = new string[1] { "Among Us.exe" },
		["APEXLEGENDS"] = new string[2] { "r5apex.exe", "r5apex_dx12.exe" },
		["Arc Raiders"] = new string[1] { "PioneerGame.exe" },
		["BF"] = new string[4] { "bf6.exe", "BF2042.exe", "bfv.exe", "bf1.exe" },
		["crabgame"] = new string[1] { "Crab Game.exe" },
		["cyberpunk"] = new string[1] { "Cyberpunk2077.exe" },
		["Call of Duty"] = new string[8] { "cod.exe", "BlackOps6.exe", "BlackOpsColdWar.exe", "ModernWarfare.exe", "cod22-cod.exe", "cod23-cod.exe", "sp22-cod.exe", "sp23-cod.exe" },
		["CS2"] = new string[1] { "cs2.exe" },
		["Deceit"] = new string[1] { "Deceit.exe" },
		["DEADLOCK"] = new string[1] { "project8.exe" },
		["deadrising"] = new string[6] { "DRDR.exe", "DeadRising.exe", "deadrising2.exe", "deadrising2otr.exe", "deadrising3.exe", "deadrising4.exe" },
		["embr"] = new string[2] { "Embr.exe", "FireProject.exe" },
		["fortnite"] = new string[1] { "FortniteClient-Win64-Shipping.exe" },
		["Fallout76"] = new string[1] { "Fallout76.exe" },
		["gta"] = new string[2] { "GTA5.exe", "GTA5_Enhanced.exe" },
		["h1z1"] = new string[1] { "H1Z1.exe" },
		["Halo"] = new string[3] { "haloinfinite.exe", "MCC-Win64-Shipping.exe", "MCCWinStore-Win64-Shipping.exe" },
		["Harrypotter"] = new string[1] { "HogwartsLegacy.exe" },
		["lastofus"] = new string[4] { "tlou-i.exe", "tlou-ii.exe", "tlou-i-l.exe", "tlou-ii-l.exe" },
		["lethalcompany"] = new string[1] { "Lethal Company.exe" },
		["LeagueOfLegends"] = new string[1] { "League of Legends.exe" },
		["metro"] = new string[1] { "MetroExodus.exe" },
		["minecraft"] = new string[2] { "Minecraft.Windows.exe", "javaw.exe" },
		["MarvelRivals"] = new string[1] { "Marvel-Win64-Shipping.exe" },
		["new world"] = new string[1] { "NewWorld.exe" },
		["overcooked"] = new string[1] { "Overcooked2.exe" },
		["Overwatch"] = new string[1] { "Overwatch.exe" },
		["phas"] = new string[1] { "Phasmophobia.exe" },
		["PEAK"] = new string[1] { "PEAK.exe" },
		["PUBG"] = new string[1] { "TslGame.exe" },
		["raft"] = new string[1] { "Raft.exe" },
		["roblox"] = new string[1] { "RobloxPlayerBeta.exe" },
		["rogue"] = new string[1] { "RogueCompany.exe" },
		["rust"] = new string[1] { "RustClient.exe" },
		["REPO"] = new string[1] { "REPO.exe" },
		["splitgate"] = new string[2] { "PortalWars-Win64-Shipping.exe", "PortalWars.exe" },
		["Schedule1"] = new string[1] { "Schedule I.exe" },
		["SeaOfthieves"] = new string[1] { "SoTGame.exe" },
		["SIEGE"] = new string[2] { "RainbowSix.exe", "RainbowSix_Vulkan.exe" },
		["Tarky"] = new string[1] { "EscapeFromTarkov.exe" },
		["Valorant"] = new string[1] { "VALORANT-Win64-Shipping.exe" },
		["WorldofWarcraft"] = new string[2] { "Wow.exe", "WowClassic.exe" },
		["zomb"] = new string[2] { "ProjectZomboid64.exe", "ProjectZomboid32.exe" }
	};

	// Exe names known for a profile name (empty when none).
	internal static IReadOnlyList<string> ExesFor(string profile)
	{
		if (profile != null && Known.TryGetValue(profile, out var exes))
		{
			return exes;
		}
		return Array.Empty<string>();
	}

	public static List<GameRule> Make(ProfileStore s)
	{
		return (from p in s.Mouse
			where !p.Name.StartsWith("PC-Default")
			select new GameRule
			{
				Profile = p.Name,
				Exes = (Known.TryGetValue(p.Name, out var value) ? value.ToList() : new List<string>()),
				KeyboardProfile = ((p.Name == "CS2") ? "gamer - S3" : "")
			}).ToList();
	}
}
