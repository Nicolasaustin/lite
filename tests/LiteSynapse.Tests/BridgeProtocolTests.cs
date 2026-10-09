using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteSynapse.Tests;

// Messages of the window bridge (docs/bridge-api.md v1.2): parsing requests, reply/push shapes, the State JSON.
public class BridgeProtocolTests
{
	private static JsonObject Obj(string json)
	{
		return JsonNode.Parse(json).AsObject();
	}

	private static string[] Keys(JsonObject o)
	{
		return o.Select(kv => kv.Key).OrderBy(k => k, StringComparer.Ordinal).ToArray();
	}

	// ---- Requests ----

	[Fact]
	public void ParsesARequest()
	{
		Assert.True(WebProtocol.TryParse("{\"id\":7,\"cmd\":\"setAuto\",\"args\":{\"on\":true}}", out WebRequest r, out string error));
		Assert.Null(error);
		Assert.Equal(7, r.Id);
		Assert.Equal("setAuto", r.Cmd);
		Assert.True(r.Args.Bool("on"));
	}

	[Fact]
	public void ParsesARequestPostedAsAJsonString()
	{
		string inner = "{\"id\":2,\"cmd\":\"getState\",\"args\":{}}";
		Assert.True(WebProtocol.TryParse(JsonSerializer.Serialize(inner), out WebRequest r, out _));
		Assert.Equal(2, r.Id);
		Assert.Equal("getState", r.Cmd);
	}

	[Theory]
	[InlineData("{\"id\":1,\"cmd\":\"getState\"}")]
	[InlineData("{\"id\":1,\"cmd\":\"getState\",\"args\":null}")]
	public void MissingArgsAreAnEmptyObject(string json)
	{
		Assert.True(WebProtocol.TryParse(json, out WebRequest r, out _));
		Assert.False(r.Args.Has("on"));
		Assert.Equal(200, r.Args.Int("lines", 1, 5000, 200));
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("[1,2]")]
	[InlineData("{\"cmd\":\"getState\"}")]
	[InlineData("{\"id\":\"1\",\"cmd\":\"getState\"}")]
	[InlineData("{\"id\":1.5,\"cmd\":\"getState\"}")]
	public void RejectsMessagesWithoutAnId(string json)
	{
		Assert.False(WebProtocol.TryParse(json, out WebRequest r, out string error));
		Assert.Equal(-1, r.Id);
		Assert.False(string.IsNullOrEmpty(error));
	}

	[Theory]
	[InlineData("{\"id\":4}")]
	[InlineData("{\"id\":4,\"cmd\":\"\"}")]
	[InlineData("{\"id\":4,\"cmd\":\"setAuto\",\"args\":[true]}")]
	public void BadRequestsKeepTheirIdSoTheyCanBeAnswered(string json)
	{
		Assert.False(WebProtocol.TryParse(json, out WebRequest r, out string error));
		Assert.Equal(4, r.Id);
		Assert.False(string.IsNullOrEmpty(error));
	}

	// ---- Args ----

	[Fact]
	public void IntsAreRangeCheckedAndRounded()
	{
		WebArgs a = WebArgs.Parse("{\"value\":42.5,\"text\":\"7\",\"big\":101,\"bad\":\"x\",\"nan\":true}");
		Assert.Equal(43, a.Int("value", 0, 100));
		Assert.Equal(7, a.Int("text", 0, 100));
		Assert.Contains("between 0 and 100", Assert.Throws<BridgeException>(() => a.Int("big", 0, 100)).Message);
		Assert.Throws<BridgeException>(() => a.Int("bad", 0, 100));
		Assert.Throws<BridgeException>(() => a.Int("nan", 0, 100));
		Assert.Contains("missing", Assert.Throws<BridgeException>(() => a.Int("none", 0, 100)).Message);
	}

	[Fact]
	public void BoolsAcceptTrueFalseAndOneZero()
	{
		WebArgs a = WebArgs.Parse("{\"t\":true,\"f\":false,\"one\":1,\"zero\":0,\"two\":2,\"s\":\"true\"}");
		Assert.True(a.Bool("t"));
		Assert.False(a.Bool("f"));
		Assert.True(a.Bool("one"));
		Assert.False(a.Bool("zero"));
		Assert.Throws<BridgeException>(() => a.Bool("two"));
		Assert.Throws<BridgeException>(() => a.Bool("s"));
	}

	[Fact]
	public void StringsAndLists()
	{
		WebArgs a = WebArgs.Parse("{\"name\":\"CS2\",\"num\":3,\"list\":[\"a\",null,\"b\"],\"mixed\":[\"a\",1],\"stages\":[400,800.4]}");
		Assert.Equal("CS2", a.Str("name"));
		Assert.Null(a.Str("missing", optional: true));
		Assert.Throws<BridgeException>(() => a.Str("num"));
		Assert.Equal(new[] { "a", "b" }, a.Strings("list"));
		Assert.Empty(a.Strings("missing", optional: true));
		Assert.Throws<BridgeException>(() => a.Strings("mixed"));
		Assert.Equal(new[] { 400, 800 }, a.Ints("stages", 100, 20000));
	}

	[Fact]
	public void KindIsMouseOrKeyboard()
	{
		Assert.True(WebArgs.Parse("{\"kind\":\"mouse\"}").IsMouseKind());
		Assert.False(WebArgs.Parse("{\"kind\":\"keyboard\"}").IsMouseKind());
		Assert.Throws<BridgeException>(() => WebArgs.Parse("{\"kind\":\"Mouse\"}").IsMouseKind());
		Assert.Throws<BridgeException>(() => WebArgs.Empty.IsMouseKind());
	}

	[Fact]
	public void NestedObjectsAndObjectLists()
	{
		WebArgs a = WebArgs.Parse("{\"macro\":{\"name\":\"J\",\"steps\":[{\"vk\":65,\"down\":true},{\"vk\":0,\"delayMs\":20}]}}");
		WebArgs m = a.Obj("macro");
		Assert.Equal("J", m.Str("name"));
		List<WebArgs> steps = m.Objects("steps");
		Assert.Equal(2, steps.Count);
		Assert.Equal(65, steps[0].Int("vk", 0, 255));
		Assert.Equal(20, steps[1].Int("delayMs", 0, 60000, 0));
		Assert.Throws<BridgeException>(() => a.Objects("macro"));
	}

	// ---- Replies and pushes ----

	[Fact]
	public void ReplyShape()
	{
		JsonObject o = Obj(WebProtocol.Reply(3, new NameResult("CS2 copy")));
		Assert.Equal(new[] { "id", "ok", "result", "type" }, Keys(o));
		Assert.Equal("reply", (string)o["type"]);
		Assert.Equal(3, (int)o["id"]);
		Assert.True((bool)o["ok"]);
		Assert.Equal("CS2 copy", (string)o["result"]["name"]);
	}

	[Fact]
	public void ReplyResultsOfEveryCommandShape()
	{
		Assert.Null(Obj(WebProtocol.Reply(1, null))["result"]);
		Assert.Equal("log text", (string)Obj(WebProtocol.Reply(1, "log text"))["result"]);
		Assert.Equal(2, Obj(WebProtocol.Reply(1, new List<string> { "a.exe", "b.exe" }))["result"].AsArray().Count);
		Assert.True(Obj(WebProtocol.Reply(1, new PathResult(null)))["result"].AsObject().ContainsKey("path"));
		Assert.Equal("done", (string)Obj(WebProtocol.Reply(1, new MessageResult("done")))["result"]["message"]);
		JsonObject onboard = Obj(WebProtocol.Reply(1, new OnboardResult(false, "Failed: x")))["result"].AsObject();
		Assert.Equal(new[] { "message", "ok" }, Keys(onboard));
		Assert.Null(Obj(WebProtocol.Reply(1, new ExeResult(null)))["result"]["exe"]);
		JsonObject zones = Obj(WebProtocol.Reply(1, new ZonesResult(new List<ZoneDto> { new ZoneDto(0, "All zones") })))["result"].AsObject();
		Assert.Equal(0, (int)zones["zones"][0]["id"]);
		Assert.Equal("All zones", (string)zones["zones"][0]["name"]);
		JsonObject info = Obj(WebProtocol.Reply(1, new DeviceInfoDto
		{
			Mouse = new MouseInfoDto { Dpi = 800, Polling = 1000, Slot = 3, SlotName = "Green" }
		}))["result"].AsObject();
		Assert.Equal(new[] { "keyboard", "mouse" }, Keys(info));
		Assert.Null(info["keyboard"]);
		Assert.Equal(new[] { "dpi", "polling", "slot", "slotName" }, Keys(info["mouse"].AsObject()));
	}

	[Fact]
	public void ReplyRefusesTypesTheContextDoesNotKnow()
	{
		Assert.Throws<NotSupportedException>(() => WebProtocol.Reply(1, new { name = "x" }));
	}

	[Fact]
	public void ErrorShape()
	{
		JsonObject o = Obj(WebProtocol.Error(9, "No mouse profile named \"X\"."));
		Assert.Equal(new[] { "error", "id", "ok", "type" }, Keys(o));
		Assert.False((bool)o["ok"]);
		Assert.Equal("No mouse profile named \"X\".", (string)o["error"]);
		Assert.False(string.IsNullOrEmpty((string)Obj(WebProtocol.Error(9, ""))["error"]));
	}

	[Fact]
	public void PushShapes()
	{
		JsonObject p = Obj(WebProtocol.Progress("onboard", "Writing buttons..."));
		Assert.Equal("progress", (string)p["type"]);
		Assert.Equal("onboard", (string)p["op"]);
		Assert.Equal("Writing buttons...", (string)p["text"]);
		JsonObject n = Obj(WebProtocol.Navigate("mouse", "performance"));
		Assert.Equal("navigate", (string)n["type"]);
		Assert.Equal("performance", (string)n["tab"]);
		Assert.False(Obj(WebProtocol.Navigate("settings", null)).ContainsKey("tab"));
		JsonObject s = Obj(WebProtocol.State(new StateDto()));
		Assert.Equal("state", (string)s["type"]);
		Assert.True(s["state"].AsObject().ContainsKey("mouseProfiles"));
	}

	// ---- State ----

	[Fact]
	public void StateHasExactlyTheContractFields()
	{
		JsonObject s = Obj(WebProtocol.State(new StateDto()))["state"].AsObject();
		string[] contract =
		{
			"version", "mouse", "keyboard", "auto", "inGame", "liveWrite", "startup", "liveStatus", "activeMouse",
			"activeKeyboard", "defaultMouse", "defaultKeyboard", "mouseProfiles", "keyboardProfiles", "macros", "games",
			"calibration", "buttonIds", "onboard", "liveSlots", "accent", "competitiveSafe", "gameKeyboardDefault",
			"lastGameExe", "elevatedGame", "razer",
			// Addition (setCloseOnGame).
			"closeOnGame"
		};
		Assert.Equal(contract.OrderBy(k => k, StringComparer.Ordinal).ToArray(), Keys(s));
		Assert.Equal(new[] { "connected", "name" }, Keys(s["mouse"].AsObject()));
		Assert.Equal(new[] { "checked", "filterDrivers", "gmsService", "rzCommon", "synapseInstalled", "synapseRunning" }, Keys(s["razer"].AsObject()));
		Assert.Equal("mint", (string)s["accent"]);
	}

	[Fact]
	public void MouseProfileJson()
	{
		MouseProfile p = new MouseProfile
		{
			Name = "CS2",
			Dpi = 800,
			DpiStages = new List<int> { 400, 800 },
			DpiActive = 2,
			Buttons = new Dictionary<string, KeyTarget>
			{
				["Side8"] = new KeyTarget { Key = "KEY_W" },
				["Side3"] = new KeyTarget { Key = "DISABLED" },
				["M1"] = new KeyTarget { Key = "KEY_A", Mods = new List<string> { "KEY_LEFT_SHIFT" } },
				["Side4"] = new KeyTarget { Key = null }
			},
			Lighting = new MouseLightingSettings { Effect = "static", Color = "#00FF00", Brightness = 100 }
		};
		JsonObject o = JsonNode.Parse(JsonSerializer.Serialize(WebState.Mouse(p), WebJson.Default.MouseProfileDto)).AsObject();
		Assert.Equal(new[] { "antiCheat", "buttons", "clickSwap", "dpi", "dpiActive", "dpiStages", "lighting", "name", "pollingRate" }, Keys(o));
		Assert.True((bool)o["antiCheat"]);
		Assert.True((bool)o["clickSwap"]);
		JsonObject buttons = o["buttons"].AsObject();
		// Button ids stay as they are (not camelCased); empty remaps are left out.
		Assert.Equal(new[] { "M1", "Side3", "Side8" }, Keys(buttons));
		Assert.Equal("KEY_W", (string)buttons["Side8"]["key"]);
		Assert.Empty(buttons["Side8"]["mods"].AsArray());
		Assert.Equal("KEY_LEFT_SHIFT", (string)buttons["M1"]["mods"][0]);
		Assert.Equal(new[] { "brightness", "color", "effect" }, Keys(o["lighting"].AsObject()));
	}

	[Fact]
	public void MouseLightingNullIsWrittenAsNull()
	{
		JsonObject o = JsonNode.Parse(JsonSerializer.Serialize(WebState.Mouse(new MouseProfile { Name = "Amongus" }), WebJson.Default.MouseProfileDto)).AsObject();
		Assert.True(o.ContainsKey("lighting"));
		Assert.Null(o["lighting"]);
		Assert.False((bool)o["antiCheat"]);
	}

	[Fact]
	public void KeyboardProfileAndMacroJson()
	{
		KbProfile kb = new KbProfile
		{
			Name = "gamer - S3",
			Brightness = 7,
			SnapTap = true,
			MacroKeys = new Dictionary<string, string> { ["KEY_UP_ARROW"] = "W Jumpthrow" }
		};
		StateDto s = new StateDto();
		WebState.FillStore(s, new ProfileStore
		{
			Keyboard = new List<KbProfile> { kb },
			Macros = new List<Macro> { new Macro { Name = "Jumpthrow", Steps = new List<MacroStep> { new MacroStep { Vk = 109, Down = true } } } }
		}, new List<GameRule> { new GameRule { Profile = "CS2", Exes = new List<string> { "cs2.exe" }, KeyboardProfile = null } });
		JsonObject state = Obj(WebProtocol.State(s))["state"].AsObject();
		JsonObject k = state["keyboardProfiles"][0].AsObject();
		Assert.Equal(new[] { "brightness", "effect", "effectSpeed", "macroKeys", "name", "snapTap" }, Keys(k));
		Assert.Equal("W Jumpthrow", (string)k["macroKeys"]["KEY_UP_ARROW"]);
		JsonObject step = state["macros"][0]["steps"][0].AsObject();
		Assert.Equal(new[] { "delayMs", "down", "ext", "vk" }, Keys(step));
		JsonObject game = state["games"][0].AsObject();
		Assert.Equal(new[] { "exes", "keyboardProfile", "profile" }, Keys(game));
		Assert.Equal("", (string)game["keyboardProfile"]);
	}

	// ---- Contract conformance (source level: Bridge.cs needs Windows, so its command table is read as text) ----

	private static string RepoFile(params string[] parts)
	{
		for (DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
		{
			if (File.Exists(Path.Combine(dir.FullName, "docs", "bridge-api.md")))
			{
				return Path.Combine(new[] { dir.FullName }.Concat(parts).ToArray());
			}
		}
		throw new InvalidOperationException("docs/bridge-api.md not found above " + AppContext.BaseDirectory);
	}

	private static SortedSet<string> Matches(string text, string pattern)
	{
		return new SortedSet<string>(Regex.Matches(text, pattern, RegexOptions.Multiline).Select(m => m.Groups[1].Value), StringComparer.Ordinal);
	}

	[Fact]
	public void BridgeHandlesEveryContractCommand()
	{
		SortedSet<string> contract = Matches(File.ReadAllText(RepoFile("docs", "bridge-api.md")), @"^\| `([a-zA-Z]+)` \|");
		string bridge = File.ReadAllText(RepoFile("src", "LiteSynapse", "LiteSynapse", "Bridge.cs"));
		SortedSet<string> handled = Matches(bridge, @"^\s*\[""([a-zA-Z]+)""\] = ");
		Assert.True(contract.Count >= 40, "command table not found in docs/bridge-api.md");
		Assert.Empty(contract.Except(handled));
		// No command beyond the contract (setCloseOnGame is documented there now).
		Assert.Contains("setCloseOnGame", contract);
		Assert.Empty(handled.Except(contract));
		// Commands that skip the state push afterwards must exist and must not change anything.
		Match readOnly = Regex.Match(bridge, @"ReadOnlyCommands = [^{]*\{([^}]*)\}");
		Assert.True(readOnly.Success);
		SortedSet<string> skipPush = Matches(readOnly.Groups[1].Value, @"""([a-zA-Z]+)""");
		Assert.Empty(skipPush.Except(handled));
		foreach (string mutating in new[] { "selectProfile", "setDpiStages", "setButton", "saveMacro", "onboardWrite", "importProfiles", "duplicateProfile", "linkRunningGame", "razerCleanup" })
		{
			Assert.DoesNotContain(mutating, skipPush);
		}
	}

	[Fact]
	public void TheUsersRealProfilesSerialize()
	{
		var (store, games, dir) = TestData.LoadRealCopy();
		try
		{
			StateDto s = new StateDto();
			WebState.FillStore(s, store, games);
			JsonObject state = Obj(WebProtocol.State(s))["state"].AsObject();
			JsonArray mice = state["mouseProfiles"].AsArray();
			Assert.Equal(store.Mouse.Count, mice.Count);
			Assert.True(mice.Count >= 40);
			Assert.Contains(mice, m => (string)m["name"] == "CS2" && (bool)m["antiCheat"]);
			foreach (JsonNode m in mice)
			{
				foreach (KeyValuePair<string, JsonNode> b in m["buttons"].AsObject())
				{
					string key = (string)b.Value["key"];
					Assert.True(key == "DISABLED" || key == "MOUSE_MIDDLE" || key.StartsWith("KEY_"), $"{m["name"]} {b.Key} = {key}");
				}
			}
			Assert.Equal(games.Count, state["games"].AsArray().Count);
		}
		finally
		{
			System.IO.Directory.Delete(dir, recursive: true);
		}
	}
}
