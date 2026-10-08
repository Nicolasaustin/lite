using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace LiteSynapse.Tests;

// Runs against copies of the user's real profiles.json and games.json (the originals are never touched).
public class DataMigrationsTests : IDisposable
{
	private readonly ProfileStore store;

	private readonly List<GameRule> games;

	private readonly string dir;

	public DataMigrationsTests()
	{
		(store, games, dir) = TestData.LoadRealCopy();
	}

	public void Dispose()
	{
		Directory.Delete(dir, recursive: true);
	}

	private MouseProfile Mouse(string name)
	{
		return store.Mouse.Single((MouseProfile p) => p.Name == name);
	}

	private GameRule Rule(string profile)
	{
		return games.Single((GameRule g) => g.Profile == profile);
	}

	[Fact]
	public void Real_data_gets_the_expected_additions()
	{
		Assert.Equal(0, store.MigrationVersion);
		DataMigrations.Result r = DataMigrations.Run(store, games);
		Assert.True(r.Changed);
		Assert.Equal(47, r.Lighting);
		Assert.Equal(8, r.Buttons);
		Assert.Equal(18, r.Exes);
		Assert.Equal(DataMigrations.CurrentVersion, store.MigrationVersion);
		Assert.All(store.Mouse, (MouseProfile p) => Assert.NotNull(p.Lighting));
		Assert.All(store.Mouse, (MouseProfile p) => Assert.Equal(100, p.Lighting.Brightness));
		Assert.Equal(30, store.Mouse.Count((MouseProfile p) => p.Lighting.Effect == "static"));
		Assert.Equal(17, store.Mouse.Count((MouseProfile p) => p.Lighting.Effect == "spectrum"));
		Assert.Equal("#00FF00", Mouse("CS2").Lighting.Color);
		Assert.Equal("#C000C0", Mouse("SIEGE").Lighting.Color);
		Assert.Equal("#FFC0C0", Mouse("rust").Lighting.Color);
		Assert.Equal("spectrum", Mouse("cyberpunk").Lighting.Effect);
		Assert.Equal("DISABLED", Mouse("APEXLEGENDS").Buttons["Side3"].Key);
		Assert.Equal("DISABLED", Mouse("APEXLEGENDS").Buttons["Side4"].Key);
		Assert.Equal("DISABLED", Mouse("deadrising").Buttons["Side1"].Key);
		Assert.Equal("DISABLED", Mouse("Fallout76").Buttons["Side2"].Key);
		Assert.Equal("DISABLED", Mouse("Halo").Buttons["Side2"].Key);
		Assert.Equal("DISABLED", Mouse("phas").Buttons["Side2"].Key);
		Assert.Equal("DISABLED", Mouse("phas").Buttons["Side3"].Key);
		Assert.Equal("MOUSE_MIDDLE", Mouse("Tarky").Buttons["Side4"].Key);
		Assert.Equal(new[] { "Embr.exe", "FireProject.exe" }, Rule("embr").Exes);
		Assert.Equal(new[] { "ProjectZomboid64.exe", "ProjectZomboid32.exe" }, Rule("zomb").Exes);
		Assert.Equal(6, Rule("deadrising").Exes.Count);
		Assert.Equal(new[] { "cod.exe", "BlackOps6.exe", "BlackOpsColdWar.exe", "ModernWarfare.exe", "cod22-cod.exe", "cod23-cod.exe", "sp22-cod.exe", "sp23-cod.exe" }, Rule("Call of Duty").Exes);
		Assert.Equal(new[] { "tlou-i.exe", "tlou-ii.exe", "tlou-i-l.exe", "tlou-ii-l.exe" }, Rule("lastofus").Exes);
		Assert.Equal(new[] { "haloinfinite.exe", "MCC-Win64-Shipping.exe", "MCCWinStore-Win64-Shipping.exe" }, Rule("Halo").Exes);
		Assert.Equal(new[] { "PortalWars-Win64-Shipping.exe", "PortalWars.exe" }, Rule("splitgate").Exes);
		Assert.Equal(new[] { "Wow.exe", "WowClassic.exe" }, Rule("WorldofWarcraft").Exes);
	}

	[Fact]
	public void Unidentified_games_are_not_linked()
	{
		DataMigrations.Run(store, games);
		foreach (string profile in new[] { "SPECTRE", "dealer", "paintgame", "Marathon" })
		{
			Assert.Empty(Rule(profile).Exes);
		}
		Assert.Equal(new[] { "project8.exe" }, Rule("DEADLOCK").Exes);
		Assert.DoesNotContain(games.SelectMany((GameRule g) => g.Exes), (string e) => e.Equals("DEADLOCK.exe", StringComparison.OrdinalIgnoreCase));
	}

	[Fact]
	public void Running_again_changes_nothing()
	{
		Assert.True(DataMigrations.Apply(store, games));
		string before = JsonSerializer.Serialize(store) + JsonSerializer.Serialize(games);
		Assert.False(DataMigrations.Apply(store, games));
		Assert.Equal(before, JsonSerializer.Serialize(store) + JsonSerializer.Serialize(games));
		// Even when the version is lost, the content is not added twice.
		store.MigrationVersion = 0;
		DataMigrations.Result r = DataMigrations.Run(store, games);
		Assert.Equal(0, r.Lighting + r.Buttons + r.Exes);
		Assert.Equal(before, JsonSerializer.Serialize(store) + JsonSerializer.Serialize(games));
	}

	[Fact]
	public void Nothing_the_user_has_is_removed_or_changed()
	{
		var (original, originalGames, originalDir) = TestData.LoadRealCopy();
		try
		{
			DataMigrations.Run(store, games);
			Assert.Equal(original.Mouse.Select((MouseProfile p) => p.Name), store.Mouse.Select((MouseProfile p) => p.Name));
			foreach (MouseProfile o in original.Mouse)
			{
				MouseProfile m = Mouse(o.Name);
				Assert.Equal((o.PollingRate, o.Dpi, o.DpiActive, o.ClickSwap), (m.PollingRate, m.Dpi, m.DpiActive, m.ClickSwap));
				Assert.Equal(o.DpiStages, m.DpiStages);
				foreach (var (id, t) in o.Buttons)
				{
					Assert.Equal(t.Key, m.Buttons[id].Key);
					Assert.Equal(t.Mods, m.Buttons[id].Mods);
				}
				Assert.True(m.Buttons.Count - o.Buttons.Count <= 2);
			}
			Assert.Equal(JsonSerializer.Serialize(original.Keyboard), JsonSerializer.Serialize(store.Keyboard));
			Assert.Equal(JsonSerializer.Serialize(original.Macros), JsonSerializer.Serialize(store.Macros));
			Assert.Equal(originalGames.Select((GameRule g) => (g.Profile, g.KeyboardProfile)), games.Select((GameRule g) => (g.Profile, g.KeyboardProfile)));
			foreach (GameRule o in originalGames)
			{
				Assert.Equal(o.Exes, Rule(o.Profile).Exes.Take(o.Exes.Count));
			}
		}
		finally
		{
			Directory.Delete(originalDir, recursive: true);
		}
	}

	[Fact]
	public void Values_the_user_set_win_over_the_migration()
	{
		Mouse("CS2").Lighting = new MouseLightingSettings { Effect = "off", Color = "#123456", Brightness = 3 };
		Mouse("APEXLEGENDS").Buttons["Side3"] = new KeyTarget { Key = "KEY_X" };
		Mouse("Tarky").Buttons["Side4"] = new KeyTarget { Key = "KEY_4", Mods = new List<string> { "KEY_LEFT_ALT" } };
		// An exe that already belongs to another rule is never added: the first matching rule would change.
		Rule("CS2").Exes.Add("fireproject.EXE");
		DataMigrations.Result r = DataMigrations.Run(store, games);
		Assert.Equal(46, r.Lighting);
		Assert.Equal(6, r.Buttons);
		Assert.Equal(17, r.Exes);
		Assert.Equal(("off", "#123456", 3), (Mouse("CS2").Lighting.Effect, Mouse("CS2").Lighting.Color, Mouse("CS2").Lighting.Brightness));
		Assert.Equal("KEY_X", Mouse("APEXLEGENDS").Buttons["Side3"].Key);
		Assert.Equal("DISABLED", Mouse("APEXLEGENDS").Buttons["Side4"].Key);
		Assert.Equal("KEY_4", Mouse("Tarky").Buttons["Side4"].Key);
		Assert.Equal(new[] { "KEY_LEFT_ALT" }, Mouse("Tarky").Buttons["Side4"].Mods);
		Assert.Equal(new[] { "Embr.exe" }, Rule("embr").Exes);
	}

	[Fact]
	public void Missing_profiles_and_rules_are_not_created()
	{
		store.Mouse.RemoveAll((MouseProfile p) => p.Name == "Tarky");
		games.RemoveAll((GameRule g) => g.Profile == "zomb");
		int rules = games.Count;
		DataMigrations.Result r = DataMigrations.Run(store, games);
		Assert.Equal(46, r.Lighting);
		Assert.Equal(7, r.Buttons);
		Assert.Equal(16, r.Exes);
		Assert.Equal(rules, games.Count);
		Assert.DoesNotContain(store.Mouse, (MouseProfile p) => p.Name == "Tarky");
	}

	[Fact]
	public void Migrated_data_survives_a_save_and_load()
	{
		Assert.True(DataMigrations.Apply(store, games));
		string profiles = Path.Combine(dir, "profiles.json");
		string gamesFile = Path.Combine(dir, "games.json");
		Cfg.Save(profiles, store);
		Cfg.Save(gamesFile, games);
		ProfileStore loaded = Cfg.Load<ProfileStore>(profiles);
		List<GameRule> loadedGames = Cfg.Load<List<GameRule>>(gamesFile);
		Assert.Equal(DataMigrations.CurrentVersion, loaded.MigrationVersion);
		Assert.Equal("MOUSE_MIDDLE", loaded.Mouse.Single((MouseProfile p) => p.Name == "Tarky").Buttons["Side4"].Key);
		Assert.Equal("#8080FF", loaded.Mouse.Single((MouseProfile p) => p.Name == "fortnite").Lighting.Color);
		Assert.Equal(JsonSerializer.Serialize(store), JsonSerializer.Serialize(loaded));
		Assert.Equal(JsonSerializer.Serialize(games), JsonSerializer.Serialize(loadedGames));
		Assert.False(DataMigrations.Apply(loaded, loadedGames));
	}

	[Fact]
	public void Migrated_buttons_and_lighting_can_be_written_to_the_mouse()
	{
		DataMigrations.Run(store, games);
		foreach (MouseProfile p in store.Mouse)
		{
			OnboardMemory.BuildCommands(1, p, out List<string> problems);
			Assert.Empty(problems);
			Assert.All(p.Buttons.Values, (KeyTarget t) => Assert.True(OnboardMemory.CanWrite(t), p.Name + " " + t.Key));
		}
	}

	[Fact]
	public void No_store_means_no_change()
	{
		Assert.False(DataMigrations.Apply(null, games));
		Assert.False(DataMigrations.Run(new ProfileStore { MigrationVersion = DataMigrations.CurrentVersion }, games).Changed);
		// No games list: profiles are still migrated.
		Assert.True(DataMigrations.Apply(store, null));
		Assert.Equal(DataMigrations.CurrentVersion, store.MigrationVersion);
	}

	[Fact]
	public void Lighting_table_covers_every_profile_of_the_user()
	{
		Assert.All(store.Mouse, (MouseProfile p) => Assert.NotNull(DataMigrations.LightingFor(p.Name)));
		Assert.Equal("spectrum", DataMigrations.LightingFor("Valorant (1)").Effect);
		Assert.Null(DataMigrations.LightingFor("Some new game"));
		Assert.Null(DataMigrations.LightingFor(null));
		// Each call returns a fresh object, so profiles never share settings.
		Assert.NotSame(DataMigrations.LightingFor("CS2"), DataMigrations.LightingFor("CS2"));
	}
}
