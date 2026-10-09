using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Xunit;

namespace LiteSynapse.Tests;

public class ModelTests
{
	private static string TempFile()
	{
		return Path.Combine(Path.GetTempPath(), "litesynapse-model-" + Guid.NewGuid().ToString("N") + ".json");
	}

	private static T RoundTrip<T>(string json) where T : new()
	{
		string path = TempFile();
		try
		{
			File.WriteAllText(path, json);
			T v = Cfg.Load<T>(path);
			Cfg.Save(path, v);
			return Cfg.Load<T>(path);
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void Files_without_the_new_fields_get_the_defaults()
	{
		ProfileStore s = RoundTrip<ProfileStore>("{\"Mouse\": [{\"Name\": \"CS2\", \"Buttons\": {\"Side1\": {\"Key\": \"KEY_1\", \"Mods\": []}}}]," +
			"\"Keyboard\": [{\"Name\": \"gamer - S3\", \"Brightness\": 7, \"SnapTap\": true, \"MacroKeys\": {}}], \"Macros\": []}");
		MouseProfile m = s.Mouse.Single();
		Assert.Null(m.Lighting);
		Assert.True(m.ClickSwap);
		KbProfile k = s.Keyboard.Single();
		Assert.Equal("static", k.Effect);
		Assert.Equal(2, k.EffectSpeed);
		Assert.Equal(0, s.MigrationVersion);
		MouseLightingSettings l = new MouseLightingSettings();
		Assert.Equal(("static", "#00FF00", 100), (l.Effect, l.Color, l.Brightness));
		Assert.Null(RoundTrip<List<GameRule>>("[{\"Profile\": \"CS2\", \"Exes\": [\"cs2.exe\"]}]").Single().PathContains);
	}

	[Fact]
	public void Unknown_fields_survive_a_save_on_every_stored_class()
	{
		ProfileStore s = RoundTrip<ProfileStore>("{\"Future\": 1, \"Mouse\": [{\"Name\": \"A\", \"Tilt\": \"x\", " +
			"\"Lighting\": {\"Effect\": \"static\", \"Color\": \"#010203\", \"Brightness\": 9, \"Gradient\": [1]}, " +
			"\"Buttons\": {\"Side1\": {\"Key\": \"KEY_1\", \"Mods\": [], \"Turbo\": true}}}], " +
			"\"Keyboard\": [{\"Name\": \"K\", \"GamingMode\": {\"On\": true}}], \"Macros\": [{\"Name\": \"M\", \"Steps\": [], \"Loop\": 3}]}");
		Assert.Equal(1, s.ExtraData["Future"].GetInt32());
		MouseProfile m = s.Mouse.Single();
		Assert.Equal("x", m.ExtraData["Tilt"].GetString());
		Assert.Equal(("static", "#010203", 9), (m.Lighting.Effect, m.Lighting.Color, m.Lighting.Brightness));
		Assert.Equal(1, m.Lighting.ExtraData["Gradient"][0].GetInt32());
		Assert.Null(m.Lighting.Zones);
		Assert.True(m.Buttons["Side1"].ExtraData["Turbo"].GetBoolean());
		Assert.True(s.Keyboard.Single().ExtraData["GamingMode"].GetProperty("On").GetBoolean());
		Assert.Equal(3, s.Macros.Single().ExtraData["Loop"].GetInt32());
		GameRule g = RoundTrip<List<GameRule>>("[{\"Profile\": \"CS2\", \"Exes\": [], \"Note\": \"n\", \"PathContains\": \"steamapps\"}]").Single();
		Assert.Equal("n", g.ExtraData["Note"].GetString());
		Assert.Equal("steamapps", g.PathContains);
	}

	// Loading and saving the real profiles.json keeps every value it had.
	[Fact]
	public void Real_profiles_lose_nothing_on_save()
	{
		string dir = TestData.CopyRealData();
		try
		{
			foreach (string file in new[] { "profiles.json", "games.json" })
			{
				string path = Path.Combine(dir, file);
				JsonNode before = JsonNode.Parse(File.ReadAllText(path));
				if (file == "profiles.json")
				{
					Cfg.Save(path, Cfg.Load<ProfileStore>(path));
				}
				else
				{
					Cfg.Save(path, Cfg.Load<List<GameRule>>(path));
				}
				JsonNode after = JsonNode.Parse(File.ReadAllText(path));
				AssertContains(before, after, file);
			}
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	// Every value in expected is also in actual (actual may have more properties).
	private static void AssertContains(JsonNode expected, JsonNode actual, string where)
	{
		switch (expected)
		{
		case JsonObject o:
			foreach (var (key, value) in o)
			{
				Assert.True(actual is JsonObject a && a.ContainsKey(key), where + "." + key);
				AssertContains(value, actual[key], where + "." + key);
			}
			break;
		case JsonArray arr:
			Assert.Equal(arr.Count, actual.AsArray().Count);
			for (int i = 0; i < arr.Count; i++)
			{
				AssertContains(arr[i], actual[i], $"{where}[{i}]");
			}
			break;
		default:
			Assert.Equal(expected?.ToJsonString(), actual?.ToJsonString());
			break;
		}
	}

	[Fact]
	public void Key_target_constants()
	{
		Assert.Equal("DISABLED", KeyTarget.Disabled);
		Assert.Equal("MOUSE_MIDDLE", KeyTarget.MouseMiddle);
	}
}

public class GameRuleTests
{
	[Fact]
	public void Exe_names_match_case_insensitively()
	{
		GameRule r = new GameRule { Profile = "CS2", Exes = new List<string> { "cs2.exe" } };
		Assert.True(r.Matches("CS2.EXE"));
		Assert.True(r.Matches("cs2.exe", @"C:\Games\cs2.exe"));
		Assert.False(r.Matches("cs.exe"));
		Assert.False(r.Matches(null));
		Assert.False(new GameRule { Exes = null }.Matches("cs2.exe"));
	}

	[Fact]
	public void Path_contains_narrows_the_match()
	{
		GameRule r = new GameRule { Profile = "Halo", Exes = new List<string> { "game.exe" }, PathContains = "Halo" };
		Assert.True(r.Matches("game.exe", @"D:\SteamLibrary\steamapps\common\HALO\game.exe"));
		Assert.False(r.Matches("game.exe", @"D:\Other\game.exe"));
		Assert.False(r.Matches("game.exe"));
	}

	[Fact]
	public void Find_prefers_a_path_rule_then_the_first_listed()
	{
		GameRule general = new GameRule { Profile = "A", Exes = new List<string> { "game.exe" } };
		GameRule second = new GameRule { Profile = "B", Exes = new List<string> { "GAME.exe" } };
		GameRule specific = new GameRule { Profile = "C", Exes = new List<string> { "game.exe" }, PathContains = "special" };
		List<GameRule> rules = new List<GameRule> { general, null, second, specific };
		Assert.Same(specific, GameRule.Find(rules, "game.exe", @"C:\special\game.exe"));
		Assert.Same(general, GameRule.Find(rules, "game.exe", @"C:\plain\game.exe"));
		Assert.Same(general, GameRule.Find(rules, "game.exe"));
		Assert.Null(GameRule.Find(rules, "other.exe"));
		Assert.Null(GameRule.Find(null, "game.exe"));
	}
}

public class AntiCheatTests
{
	[Fact]
	public void The_flagged_profiles_are_protected()
	{
		string[] expected = { "APEXLEGENDS", "BF", "Call of Duty", "CS2", "DEADLOCK", "fortnite", "gta", "h1z1", "Halo", "LeagueOfLegends", "MarvelRivals", "new world", "PUBG", "rogue", "rust", "splitgate", "SIEGE", "Tarky", "Valorant" };
		Assert.Equal(expected.OrderBy((string s) => s), AntiCheat.Profiles.OrderBy((string s) => s));
		Assert.All(expected, (string p) => Assert.True(AntiCheat.IsProtected(p), p));
	}

	[Theory]
	[InlineData("cs2")]
	[InlineData("VALORANT")]
	[InlineData(" Tarky ")]
	[InlineData("NEW WORLD")]
	public void Matching_ignores_case_and_surrounding_spaces(string name)
	{
		Assert.True(AntiCheat.IsProtected(name));
	}

	[Theory]
	[InlineData("minecraft")]
	[InlineData("PEAK")]
	[InlineData("CS")]
	[InlineData("")]
	[InlineData(null)]
	public void Other_profiles_are_not_protected(string name)
	{
		Assert.False(AntiCheat.IsProtected(name));
	}

	[Theory]
	[InlineData("cs2.exe", true)]
	[InlineData("CS2.EXE", true)]
	[InlineData("VALORANT-Win64-Shipping.exe", true)]
	[InlineData("r5apex_dx12.exe", true)]
	[InlineData("project8.exe", true)]
	[InlineData("League of Legends.exe", true)]
	[InlineData("GTA5_Enhanced.exe", true)]
	[InlineData("PEAK.exe", false)]
	[InlineData("javaw.exe", false)]
	[InlineData("LiteSynapse.exe", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void Protected_games_are_also_known_by_their_program(string exe, bool expected)
	{
		Assert.Equal(expected, AntiCheat.IsProtectedExe(exe));
	}

	[Fact]
	public void Every_protected_game_has_a_known_program()
	{
		Assert.All(AntiCheat.Profiles, (string p) => Assert.NotEmpty(DefaultGames.ExesFor(p)));
	}
}

public class DefaultGamesTests
{
	[Fact]
	public void Fresh_installs_get_the_known_exes_and_no_exe_twice()
	{
		var (store, _, dir) = TestData.LoadRealCopy();
		try
		{
			List<GameRule> rules = DefaultGames.Make(store);
			Assert.Equal(store.Mouse.Count, rules.Count);
			List<string> all = rules.SelectMany((GameRule r) => r.Exes).ToList();
			Assert.Equal(all.Count, all.Distinct(StringComparer.OrdinalIgnoreCase).Count());
			Assert.Equal("gamer - S3", rules.Single((GameRule r) => r.Profile == "CS2").KeyboardProfile);
			Assert.Contains("FireProject.exe", rules.Single((GameRule r) => r.Profile == "embr").Exes);
			Assert.Equal(2, rules.Single((GameRule r) => r.Profile == "zomb").Exes.Count);
			Assert.Equal(6, rules.Single((GameRule r) => r.Profile == "deadrising").Exes.Count);
			foreach (string unknown in new[] { "SPECTRE", "dealer", "paintgame", "Marathon" })
			{
				Assert.Empty(rules.Single((GameRule r) => r.Profile == unknown).Exes);
			}
			Assert.DoesNotContain("DEADLOCK.exe", all, StringComparer.OrdinalIgnoreCase);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void Defaults_include_everything_the_migration_adds()
	{
		foreach (var (profile, exes) in DataMigrations.GameExes)
		{
			Assert.All(exes, (string e) => Assert.Contains(e, DefaultGames.ExesFor(profile)));
		}
		Assert.Empty(DefaultGames.ExesFor("nothing"));
		Assert.Empty(DefaultGames.ExesFor(null));
	}

	[Fact]
	public void Fresh_defaults_followed_by_the_migration_add_no_exes()
	{
		var (store, _, dir) = TestData.LoadRealCopy();
		try
		{
			List<GameRule> rules = DefaultGames.Make(store);
			Assert.Equal(0, DataMigrations.Run(store, rules).Exes);
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}
}
