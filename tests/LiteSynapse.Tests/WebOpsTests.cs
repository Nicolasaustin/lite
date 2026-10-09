using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// The profile, game and macro edits behind the window's commands.
public class WebOpsTests
{
	private static readonly string[] ButtonIds =
	{
		"Side1", "Side2", "Side3", "Side4", "Side5", "Side6", "Side7", "Side8", "Side9", "Side10",
		"Side11", "Side12", "Scroll", "M1", "M2", "TiltL", "TiltR"
	};

	private static ProfileStore Store()
	{
		return new ProfileStore
		{
			Mouse = new List<MouseProfile>
			{
				new MouseProfile { Name = "CS2", Dpi = 800, PollingRate = 1000, DpiStages = new List<int> { 400, 800 }, DpiActive = 2, Buttons = new Dictionary<string, KeyTarget> { ["Side8"] = new KeyTarget { Key = "KEY_W" } } },
				new MouseProfile { Name = "Tarky", Dpi = 1600, PollingRate = 500 }
			},
			Keyboard = new List<KbProfile>
			{
				new KbProfile { Name = "gamer - S3", Brightness = 7, MacroKeys = new Dictionary<string, string> { ["KEY_UP_ARROW"] = "W Jumpthrow", ["KEY_RIGHT_ARROW"] = "Jumpthrow" } },
				new KbProfile { Name = "office", Brightness = 50 }
			},
			Macros = new List<Macro>
			{
				new Macro { Name = "Jumpthrow" },
				new Macro { Name = "W Jumpthrow" }
			}
		};
	}

	private static List<GameRule> Games()
	{
		return new List<GameRule>
		{
			new GameRule { Profile = "CS2", Exes = new List<string> { "cs2.exe" }, KeyboardProfile = "gamer - S3" },
			new GameRule { Profile = "Tarky", Exes = new List<string> { "EscapeFromTarkov.exe" }, KeyboardProfile = "gamer - S3" }
		};
	}

	// ---- Names ----

	[Fact]
	public void NamesAreTrimmedAndChecked()
	{
		Assert.Equal("CS2 copy", WebOps.CleanName("  CS2 copy "));
		Assert.Throws<BridgeException>(() => WebOps.CleanName("   "));
		Assert.Throws<BridgeException>(() => WebOps.CleanName(null));
		Assert.Throws<BridgeException>(() => WebOps.CleanName(new string('x', 65)));
		Assert.Throws<BridgeException>(() => WebOps.CleanName("a\tb"));
	}

	[Fact]
	public void NameTakenIgnoresCaseAndTheProfileItself()
	{
		string[] names = { "CS2", "Tarky" };
		Assert.True(WebOps.NameTaken(names, "cs2"));
		Assert.False(WebOps.NameTaken(names, "cs2", "CS2"));
		Assert.False(WebOps.NameTaken(names, "Valorant"));
	}

	[Fact]
	public void CopyNamesCountUp()
	{
		Assert.Equal("CS2 copy", WebOps.CopyName(new[] { "CS2" }, "CS2"));
		Assert.Equal("CS2 copy 2", WebOps.CopyName(new[] { "CS2", "cs2 COPY" }, "CS2"));
		Assert.Equal("CS2 copy 3", WebOps.CopyName(new[] { "CS2", "CS2 copy", "CS2 copy 2" }, "CS2"));
	}

	// ---- Add, duplicate, rename, delete ----

	[Fact]
	public void AddMouseTakesDpiAndPollingFromTheActiveProfile()
	{
		ProfileStore s = Store();
		List<MouseProfile> before = s.Mouse;
		MouseProfile p = WebOps.AddMouse(s, " New ", s.Mouse[0]);
		Assert.Equal("New", p.Name);
		Assert.Equal(800, p.Dpi);
		Assert.Equal(1000, p.PollingRate);
		Assert.Equal(new[] { 400, 800 }, p.DpiStages);
		Assert.Equal(2, p.DpiActive);
		Assert.Empty(p.Buttons);
		Assert.NotSame(s.Mouse[0].DpiStages, p.DpiStages);
		// The list is replaced, not changed, so a reader on another thread keeps a consistent copy.
		Assert.NotSame(before, s.Mouse);
		Assert.Equal(2, before.Count);
		Assert.Equal(3, s.Mouse.Count);
		Assert.Throws<BridgeException>(() => WebOps.AddMouse(s, "new", null));
	}

	[Fact]
	public void AddMouseWithoutStagesGetsOneStage()
	{
		ProfileStore s = Store();
		MouseProfile p = WebOps.AddMouse(s, "X", s.Mouse[1]);
		Assert.Equal(new[] { 1600 }, p.DpiStages);
		Assert.Equal(1, p.DpiActive);
		MouseProfile q = WebOps.AddMouse(s, "Y", null);
		Assert.Equal(800, q.Dpi);
		Assert.Equal(1000, q.PollingRate);
	}

	[Fact]
	public void AddKeyboardKeepsBrightnessAndEffect()
	{
		ProfileStore s = Store();
		s.Keyboard[0].Effect = "breathing";
		KbProfile p = WebOps.AddKeyboard(s, "K", s.Keyboard[0]);
		Assert.Equal(7, p.Brightness);
		Assert.Equal("breathing", p.Effect);
		Assert.Empty(p.MacroKeys);
		Assert.Throws<BridgeException>(() => WebOps.AddKeyboard(s, "OFFICE", null));
	}

	[Fact]
	public void DuplicateIsADeepCopyRightAfterTheOriginal()
	{
		ProfileStore s = Store();
		MouseProfile copy = WebOps.DuplicateMouse(s, "CS2");
		Assert.Equal("CS2 copy", copy.Name);
		Assert.Same(copy, s.Mouse[1]);
		copy.Buttons["Side8"].Key = "KEY_E";
		Assert.Equal("KEY_W", s.Mouse[0].Buttons["Side8"].Key);
		Assert.Equal("CS2 copy 2", WebOps.DuplicateMouse(s, "CS2").Name);
		Assert.Equal("office copy", WebOps.DuplicateKeyboard(s, "office").Name);
		Assert.Throws<BridgeException>(() => WebOps.DuplicateMouse(s, "nope"));
	}

	[Fact]
	public void RenameMouseFollowsGameRules()
	{
		ProfileStore s = Store();
		List<GameRule> games = Games();
		Assert.Equal("Counter-Strike", WebOps.RenameMouse(s, games, "CS2", " Counter-Strike "));
		Assert.Equal("Counter-Strike", s.Mouse[0].Name);
		Assert.Equal("Counter-Strike", games[0].Profile);
		Assert.Equal("Tarky", games[1].Profile);
		Assert.Throws<BridgeException>(() => WebOps.RenameMouse(s, games, "Counter-Strike", "tarky"));
		Assert.Throws<BridgeException>(() => WebOps.RenameMouse(s, games, "nope", "x"));
		// A case-only rename of the same profile is allowed.
		Assert.Equal("TARKY", WebOps.RenameMouse(s, games, "Tarky", "TARKY"));
		Assert.Equal("TARKY", games[1].Profile);
	}

	[Fact]
	public void RenameKeyboardFollowsGameRules()
	{
		ProfileStore s = Store();
		List<GameRule> games = Games();
		WebOps.RenameKeyboard(s, games, "gamer - S3", "gamer");
		Assert.All(games, g => Assert.Equal("gamer", g.KeyboardProfile));
		Assert.Throws<BridgeException>(() => WebOps.RenameKeyboard(s, games, "gamer", "Office"));
	}

	[Fact]
	public void DeleteMouseRemovesItsGameRule()
	{
		ProfileStore s = Store();
		List<GameRule> games = Games();
		WebOps.DeleteMouse(s, games, "Tarky");
		Assert.DoesNotContain(s.Mouse, p => p.Name == "Tarky");
		Assert.Single(games);
		Assert.Throws<BridgeException>(() => WebOps.DeleteMouse(s, games, "Tarky"));
	}

	[Fact]
	public void DeleteKeyboardClearsItFromGameRules()
	{
		ProfileStore s = Store();
		List<GameRule> games = Games();
		WebOps.DeleteKeyboard(s, games, "gamer - S3");
		Assert.Single(s.Keyboard);
		Assert.All(games, g => Assert.Equal("", g.KeyboardProfile));
	}

	[Fact]
	public void ResetReplacesTheCollections()
	{
		ProfileStore s = Store();
		Dictionary<string, KeyTarget> old = s.Mouse[0].Buttons;
		WebOps.ResetMouse(s.Mouse[0]);
		Assert.Empty(s.Mouse[0].Buttons);
		Assert.Single(old);
		WebOps.ResetKeyboard(s.Keyboard[0]);
		Assert.Empty(s.Keyboard[0].MacroKeys);
	}

	// ---- DPI, polling, buttons, lighting ----

	[Fact]
	public void DpiStages()
	{
		MouseProfile p = new MouseProfile { Name = "x" };
		List<int> stages = new List<int> { 400, 800, 1600 };
		WebOps.SetDpiStages(p, stages, 3);
		Assert.Equal(1600, p.Dpi);
		Assert.Equal(3, p.DpiActive);
		Assert.NotSame(stages, p.DpiStages);
		Assert.Throws<BridgeException>(() => WebOps.SetDpiStages(p, new List<int>(), 1));
		Assert.Throws<BridgeException>(() => WebOps.SetDpiStages(p, new List<int> { 400, 800, 1200, 1600, 3200, 6400 }, 1));
		Assert.Throws<BridgeException>(() => WebOps.SetDpiStages(p, new List<int> { 50 }, 1));
		Assert.Throws<BridgeException>(() => WebOps.SetDpiStages(p, new List<int> { 20001 }, 1));
		Assert.Throws<BridgeException>(() => WebOps.SetDpiStages(p, new List<int> { 400 }, 2));
		Assert.Equal(1600, p.Dpi);
	}

	[Fact]
	public void PollingRates()
	{
		MouseProfile p = new MouseProfile();
		WebOps.SetPolling(p, 500);
		Assert.Equal(500, p.PollingRate);
		Assert.Throws<BridgeException>(() => WebOps.SetPolling(p, 250));
	}

	[Theory]
	[InlineData("KEY_W", "KEY_W")]
	[InlineData("key_w", "KEY_W")]
	[InlineData(" KEY_SPACEBAR ", "KEY_SPACEBAR")]
	[InlineData("KEY_HYPEN", "KEY_HYPHEN")]
	[InlineData("disabled", "DISABLED")]
	[InlineData("Mouse_Middle", "MOUSE_MIDDLE")]
	[InlineData("KEY_F13", "KEY_F13")]
	[InlineData("KEY_NUMPAD_PERIOD", "KEY_NUMPAD_PERIOD")]
	[InlineData("W", null)]
	[InlineData("KEY_NOPE", null)]
	[InlineData("", null)]
	public void CanonicalKeys(string input, string expected)
	{
		Assert.Equal(expected, WebOps.CanonicalKey(input));
	}

	[Fact]
	public void EveryKeyNameIsAccepted()
	{
		foreach (string name in Keys2.AllNames)
		{
			Assert.Equal(name, WebOps.CanonicalKey(name));
		}
	}

	[Fact]
	public void ButtonTargets()
	{
		Assert.Null(WebOps.ButtonTarget("", new[] { "KEY_LEFT_CTRL" }));
		KeyTarget t = WebOps.ButtonTarget("KEY_A", new[] { "key_right_alt", "KEY_LEFT_SHIFT", "KEY_LEFT_SHIFT" });
		Assert.Equal("KEY_A", t.Key);
		Assert.Equal(new[] { "KEY_LEFT_SHIFT", "KEY_RIGHT_ALT" }, t.Mods);
		Assert.Empty(WebOps.ButtonTarget("DISABLED", new[] { "KEY_LEFT_CTRL" }).Mods);
		Assert.Throws<BridgeException>(() => WebOps.ButtonTarget("KEY_A", new[] { "KEY_FN" }));
		Assert.Throws<BridgeException>(() => WebOps.ButtonTarget("KEY_NOPE", null));
	}

	[Fact]
	public void SetButtonReplacesTheDictionary()
	{
		ProfileStore s = Store();
		MouseProfile p = s.Mouse[0];
		Dictionary<string, KeyTarget> old = p.Buttons;
		WebOps.SetButton(p, ButtonIds, "Scroll", "MOUSE_MIDDLE", null);
		Assert.Equal("MOUSE_MIDDLE", p.Buttons["Scroll"].Key);
		Assert.False(old.ContainsKey("Scroll"));
		WebOps.SetButton(p, ButtonIds, "Side8", "", null);
		Assert.False(p.Buttons.ContainsKey("Side8"));
		Assert.Throws<BridgeException>(() => WebOps.SetButton(p, ButtonIds, "Side13", "KEY_A", null));
	}

	[Fact]
	public void MouseLightSettings()
	{
		MouseLightingSettings l = WebOps.MouseLight("Static", "#00ff7f", 80);
		Assert.Equal("static", l.Effect);
		Assert.Equal("#00FF7F", l.Color);
		Assert.Equal(80, l.Brightness);
		Assert.Equal("#123456", WebOps.MouseLight("spectrum", "123456", 0).Color);
		Assert.Throws<BridgeException>(() => WebOps.MouseLight("rainbow", "#000000", 50));
		Assert.Throws<BridgeException>(() => WebOps.MouseLight("static", "green", 50));
		Assert.Throws<BridgeException>(() => WebOps.MouseLight("static", "#000000", 101));
	}

	[Fact]
	public void MouseLightingPerZoneOrAll()
	{
		MouseProfile p = new MouseProfile { Name = "CS2" };
		// One zone on a profile without lighting: the rest get the default, the zone its own.
		WebOps.SetMouseLight(p, "logo", WebOps.MouseLight("static", "#FF0000", 100));
		Assert.Equal("#00FF00", p.Lighting.Color);
		Assert.Equal("#FF0000", p.Lighting.For("logo").Color);
		Assert.Equal("#00FF00", p.Lighting.For("scroll").Color);
		// Setting the zone back to the same as the rest drops it.
		WebOps.SetMouseLight(p, "logo", WebOps.MouseLight("static", "#00ff00", 100));
		Assert.Null(p.Lighting.Zones);
		// "all" (or no zone) sets every zone and drops per-zone settings.
		WebOps.SetMouseLight(p, "side", WebOps.MouseLight("breathing", "#0000FF", 40));
		Assert.NotNull(p.Lighting.Zones);
		WebOps.SetMouseLight(p, "all", WebOps.MouseLight("static", "#FFFFFF", 100));
		Assert.Null(p.Lighting.Zones);
		Assert.Equal("#FFFFFF", p.Lighting.For("side").Color);
		WebOps.SetMouseLight(p, null, WebOps.MouseLight("off", "#FFFFFF", 100));
		Assert.Equal("off", p.Lighting.Effect);
		Assert.Throws<BridgeException>(() => WebOps.SetMouseLight(p, "wheel", WebOps.MouseLight("static", "#FFFFFF", 100)));
	}

	// ---- Keyboard ----

	[Fact]
	public void KeyboardEffects()
	{
		KbProfile p = new KbProfile();
		WebOps.SetKbEffect(p, "Breathing", 3);
		Assert.Equal("breathing", p.Effect);
		Assert.Equal(3, p.EffectSpeed);
		Assert.Throws<BridgeException>(() => WebOps.SetKbEffect(p, "wave", 2));
		// The BlackWidow Lite has no reactive or starlight effect.
		Assert.Throws<BridgeException>(() => WebOps.SetKbEffect(p, "starlight", 2));
		Assert.Throws<BridgeException>(() => WebOps.SetKbEffect(p, "reactive", 2));
		Assert.Throws<BridgeException>(() => WebOps.SetKbEffect(p, "static", 4));
	}

	[Fact]
	public void MacroKeys()
	{
		ProfileStore s = Store();
		KbProfile p = s.Keyboard[1];
		WebOps.SetMacroKey(s, p, "KEY_DOWN_ARROW", "Jumpthrow");
		Assert.Equal("Jumpthrow", p.MacroKeys["KEY_DOWN_ARROW"]);
		WebOps.SetMacroKey(s, p, "KEY_DOWN_ARROW", "");
		Assert.Empty(p.MacroKeys);
		Assert.Throws<BridgeException>(() => WebOps.SetMacroKey(s, p, "KEY_W", "Jumpthrow"));
		Assert.Throws<BridgeException>(() => WebOps.SetMacroKey(s, p, "KEY_UP_ARROW", "nope"));
	}

	// ---- Games ----

	[Fact]
	public void ProgramNamesAreCleaned()
	{
		List<string> exes = WebOps.CleanExes(new[] { " C:\\Games\\cs2.exe ", "\"D:/Steam/game.exe\"", "cs2", "CS2.EXE", "", null, "VALORANT-Win64-Shipping.exe" });
		Assert.Equal(new[] { "cs2.exe", "game.exe", "VALORANT-Win64-Shipping.exe" }, exes);
		Assert.Throws<BridgeException>(() => WebOps.CleanExes(new[] { "bad|name.exe" }));
		Assert.Throws<BridgeException>(() => WebOps.CleanExes(new[] { "a?.exe" }));
		Assert.Empty(WebOps.CleanExes(null));
	}

	[Fact]
	public void GameRules()
	{
		ProfileStore s = Store();
		List<GameRule> games = new List<GameRule>();
		GameRule r = WebOps.AddGame(s, games, "Tarky");
		Assert.Empty(r.Exes);
		Assert.Throws<BridgeException>(() => WebOps.AddGame(s, games, "Tarky"));
		Assert.Throws<BridgeException>(() => WebOps.AddGame(s, games, "nope"));
		// Setting programs on a profile without a rule creates the rule.
		WebOps.SetGameExes(s, games, "CS2", new[] { "cs2" });
		Assert.Equal(new[] { "cs2.exe" }, WebOps.FindGame(games, "CS2").Exes);
		WebOps.SetGameKeyboard(s, games, "CS2", "office");
		Assert.Equal("office", WebOps.FindGame(games, "CS2").KeyboardProfile);
		WebOps.SetGameKeyboard(s, games, "CS2", "");
		Assert.Equal("", WebOps.FindGame(games, "CS2").KeyboardProfile);
		Assert.Throws<BridgeException>(() => WebOps.SetGameKeyboard(s, games, "CS2", "nope"));
		Assert.Throws<BridgeException>(() => WebOps.SetGameKeyboard(s, new List<GameRule>(), "CS2", ""));
		WebOps.RemoveGame(games, "CS2");
		Assert.Null(WebOps.FindGame(games, "CS2"));
		Assert.Throws<BridgeException>(() => WebOps.RemoveGame(games, "CS2"));
	}

	// ---- Macros ----

	[Fact]
	public void MacroStepsAreChecked()
	{
		Macro m = WebOps.CleanMacro(" Jump ", new List<MacroStep>
		{
			new MacroStep { Vk = 32, Down = true },
			new MacroStep { Vk = 0, Down = true, Ext = true, DelayMs = 15 },
			null,
			new MacroStep { Vk = 32, Down = false }
		});
		Assert.Equal("Jump", m.Name);
		Assert.Equal(3, m.Steps.Count);
		// A delay step is never a key press.
		Assert.False(m.Steps[1].Down);
		Assert.False(m.Steps[1].Ext);
		Assert.Equal(15, m.Steps[1].DelayMs);
		Assert.Throws<BridgeException>(() => WebOps.CleanMacro("x", new List<MacroStep> { new MacroStep { Vk = 256 } }));
		Assert.Throws<BridgeException>(() => WebOps.CleanMacro("x", new List<MacroStep> { new MacroStep { Vk = 0, DelayMs = 60001 } }));
		Assert.Throws<BridgeException>(() => WebOps.CleanMacro("", new List<MacroStep>()));
		Assert.Throws<BridgeException>(() => WebOps.CleanMacro("x", null));
	}

	[Fact]
	public void SavingAMacroUnderANewNameMovesItsKeys()
	{
		ProfileStore s = Store();
		s.Macros[0].ExtraData = new Dictionary<string, System.Text.Json.JsonElement> { ["guid"] = System.Text.Json.JsonDocument.Parse("\"abc\"").RootElement };
		WebOps.SaveMacro(s, "Jumpthrow", new Macro { Name = "Jump throw", Steps = new List<MacroStep> { new MacroStep { Vk = 109, Down = true } } });
		Assert.Equal(new[] { "Jump throw", "W Jumpthrow" }, s.Macros.Select(m => m.Name));
		Assert.Single(s.Macros[0].Steps);
		Assert.True(s.Macros[0].ExtraData.ContainsKey("guid"));
		Assert.Equal("Jump throw", s.Keyboard[0].MacroKeys["KEY_RIGHT_ARROW"]);
		Assert.Equal("W Jumpthrow", s.Keyboard[0].MacroKeys["KEY_UP_ARROW"]);
	}

	[Fact]
	public void MacroNamesStayUnique()
	{
		ProfileStore s = Store();
		Assert.Throws<BridgeException>(() => WebOps.SaveMacro(s, null, new Macro { Name = "jumpthrow" }));
		Assert.Throws<BridgeException>(() => WebOps.SaveMacro(s, "Jumpthrow", new Macro { Name = "w jumpthrow" }));
		Assert.Throws<BridgeException>(() => WebOps.SaveMacro(s, "nope", new Macro { Name = "x" }));
		WebOps.SaveMacro(s, "Jumpthrow", new Macro { Name = "JUMPTHROW" });
		WebOps.SaveMacro(s, null, new Macro { Name = "New" });
		Assert.Equal(3, s.Macros.Count);
	}

	[Fact]
	public void DeletingAMacroRemovesItsKeys()
	{
		ProfileStore s = Store();
		WebOps.DeleteMacro(s, "W Jumpthrow");
		Assert.Single(s.Macros);
		Assert.False(s.Keyboard[0].MacroKeys.ContainsKey("KEY_UP_ARROW"));
		Assert.Equal("Jumpthrow", s.Keyboard[0].MacroKeys["KEY_RIGHT_ARROW"]);
		Assert.Throws<BridgeException>(() => WebOps.DeleteMacro(s, "W Jumpthrow"));
	}

	// ---- Misc ----

	[Fact]
	public void ExportFileNames()
	{
		Assert.Equal("Valorant (1)", WebOps.SafeFileName("Valorant (1)"));
		Assert.Equal("a_b_c", WebOps.SafeFileName("a/b:c"));
		Assert.Equal("profile", WebOps.SafeFileName("..."));
	}

	[Fact]
	public void OnboardSlotsTheWindowMayWrite()
	{
		int[] live = { 1, 2 };
		Assert.Throws<BridgeException>(() => WebOps.CheckOnboardSlot(1, live));
		Assert.Throws<BridgeException>(() => WebOps.CheckOnboardSlot(2, live));
		Assert.Throws<BridgeException>(() => WebOps.CheckOnboardSlot(6, live));
		WebOps.CheckOnboardSlot(3, live);
		WebOps.CheckOnboardSlot(5, live);
		// With live profiles off, slots 1 and 2 are still LiteSynapse's: it moves the mouse off them.
		Assert.Throws<BridgeException>(() => WebOps.CheckOnboardSlot(2, new int[0]));
		Assert.Throws<BridgeException>(() => WebOps.CheckOnboardSlot(1, null));
		WebOps.CheckOnboardSlot(4, null);
	}

	[Fact]
	public void RunningAppsAreSortedUniqueAndSkipTheShell()
	{
		(int, string)[] processes =
		{
			(10, "cs2"), (11, "explorer"), (12, "Discord"), (13, "discord"), (14, "svchost"), (15, "LiteSynapse"),
			(16, "steam"), (17, ""), (18, "TextInputHost")
		};
		HashSet<int> withWindow = new HashSet<int> { 10, 11, 12, 13, 15, 16, 17, 18 };
		List<string> apps = WebOps.RunningAppNames(processes, withWindow, selfPid: 15, selfExe: "LiteSynapse.exe");
		// svchost has no window; explorer and TextInputHost are the shell; LiteSynapse is this process.
		Assert.Equal(new[] { "cs2.exe", "Discord.exe", "steam.exe" }, apps);
		Assert.Empty(WebOps.RunningAppNames(processes, new HashSet<int>(), 15, "LiteSynapse.exe"));
	}

	[Theory]
	[InlineData(-1, 0, false, null, null)]
	[InlineData(0, 0, false, "overview", null)]
	[InlineData(1, 0, false, "macros", null)]
	[InlineData(2, 0, false, "games", null)]
	[InlineData(3, 0, false, "mouse", "buttons")]
	[InlineData(3, 1, false, "mouse", "performance")]
	[InlineData(3, 3, false, "mouse", "calibration")]
	[InlineData(3, 9, false, "mouse", null)]
	[InlineData(4, 0, false, "keyboard", "macrokeys")]
	[InlineData(4, 1, false, "keyboard", "snaptap")]
	[InlineData(4, 2, false, "keyboard", "lighting")]
	[InlineData(5, 0, false, "settings", null)]
	[InlineData(-1, 0, true, "mouse", "onboard")]
	[InlineData(3, 2, true, "mouse", "onboard")]
	public void ClassicPageNumbersOpenTheMatchingPage(int page, int tab, bool mem, string expectedPage, string expectedTab)
	{
		(string p, string t) = WebProtocol.PageFor(page, tab, mem);
		Assert.Equal(expectedPage, p);
		Assert.Equal(expectedTab, t);
	}

	[Fact]
	public void PrefsRepairBadValues()
	{
		UiPrefs p = new UiPrefs { Accent = "pink", Width = 50, Height = 99999 };
		p.Normalize();
		Assert.Equal("mint", p.Accent);
		Assert.Equal(UiPrefs.DefaultWidth, p.Width);
		Assert.Equal(UiPrefs.DefaultHeight, p.Height);
		Assert.True(p.CloseOnGame);
		UiPrefs q = new UiPrefs { Accent = "violet", Width = 1400, Height = 900 };
		q.Normalize();
		Assert.Equal("violet", q.Accent);
		Assert.Equal(1400, q.Width);
	}
}
