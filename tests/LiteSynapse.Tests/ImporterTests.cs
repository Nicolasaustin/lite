using System.Collections.Generic;
using System.Text.Json.Nodes;
using Xunit;

namespace LiteSynapse.Tests;

// Payloads use the shape of the user's Synapse 4 exports (profiles.*.synapse4, decoded).
public class ImporterTests
{
	private static JsonNode Payload(string mappings, string side, string quickEffects = "{\"selectedEffectId\": 1, \"selectedEffectSetting\": {\"color1\": \"#ffc0c0\"}}",
		string brightness = "{\"isEnabled\": true, \"value\": 100}")
	{
		return JsonNode.Parse("{\"name\": \"P\", \"pollingRate\": 1000, \"quickEffects\": " + quickEffects + ", \"brightness\": " + brightness +
			", \"dpiStages\": {\"stages\": [{\"x\": 400, \"y\": 400, \"visible\": true, \"index\": 0}, {\"x\": 800, \"y\": 800, \"visible\": true, \"index\": 1}], \"active\": 2}" +
			", \"mappings\": [" + mappings + "], \"sidePanelMappings\": {\"12ButtonSide\": [" + side + "]}}");
	}

	private const string SwapMappings =
		"{\"inputID\": \"LeftClick\", \"outputType\": \"mouseGroup\", \"mouseGroup\": {\"mouseAssignment\": \"Menu\"}}," +
		"{\"inputID\": \"RightClick\", \"outputType\": \"mouseGroup\", \"mouseGroup\": {\"mouseAssignment\": \"Click\"}}," +
		"{\"inputID\": \"ScrollButton\", \"outputType\": \"keyboardGroup\", \"keyboardGroup\": {\"key\": \"KEY_SPACEBAR\", \"keyGroup\": 0, \"modifiers\": []}}";

	private static string Key(string input, string key, string mods = "")
	{
		return "{\"inputID\": \"" + input + "\", \"outputType\": \"keyboardGroup\", \"keyboardGroup\": {\"key\": \"" + key + "\", \"keyGroup\": 0, \"modifiers\": [" + mods + "]}}";
	}

	[Fact]
	public void Keys_modifiers_and_dpi_come_over()
	{
		List<string> notes = new List<string>();
		MouseProfile p = Importer.MouseFrom(Payload(SwapMappings, Key("KEY_3", "KEY_T", "\"KEY_LEFT_ALT\"")), notes);
		Assert.Equal("P", p.Name);
		Assert.Equal("KEY_SPACEBAR", p.Buttons["Scroll"].Key);
		Assert.Equal("KEY_T", p.Buttons["Side3"].Key);
		Assert.Equal(new[] { "KEY_LEFT_ALT" }, p.Buttons["Side3"].Mods);
		Assert.Equal(new[] { 400, 800 }, p.DpiStages);
		Assert.Equal(2, p.DpiActive);
		Assert.Equal(800, p.Dpi);
		Assert.Empty(notes);
	}

	[Fact]
	public void Old_exports_number_the_last_side_buttons_ten_to_twelve()
	{
		MouseProfile p = Importer.MouseFrom(Payload(SwapMappings, Key("KEY_10", "KEY_A") + "," + Key("KEY_11", "KEY_S") + "," + Key("KEY_12", "KEY_D")));
		Assert.Equal("KEY_A", p.Buttons["Side10"].Key);
		Assert.Equal("KEY_S", p.Buttons["Side11"].Key);
		Assert.Equal("KEY_D", p.Buttons["Side12"].Key);
		Assert.Equal("Side10", Importer.ButtonId("KEY_0"));
		Assert.Equal("Side11", Importer.ButtonId("KEY_HYPEN"));
		Assert.Equal("Side12", Importer.ButtonId("KEY_EQUAL"));
	}

	[Fact]
	public void Keyboard_function_without_a_key_becomes_disabled()
	{
		MouseProfile p = Importer.MouseFrom(Payload(SwapMappings,
			"{\"inputID\": \"KEY_3\", \"outputType\": \"keyboardGroup\", \"keyboardGroup\": {}}," +
			"{\"inputID\": \"KEY_4\", \"outputType\": \"keyboardGroup\", \"keyboardGroup\": {}}"));
		Assert.Equal("DISABLED", p.Buttons["Side3"].Key);
		Assert.Equal("DISABLED", p.Buttons["Side4"].Key);
		Assert.Empty(p.Buttons["Side3"].Mods);
	}

	[Fact]
	public void Middle_click_on_a_side_button_becomes_mouse_middle()
	{
		List<string> notes = new List<string>();
		MouseProfile p = Importer.MouseFrom(Payload(SwapMappings,
			"{\"inputID\": \"KEY_4\", \"outputType\": \"mouseGroup\", \"mouseGroup\": {\"mouseAssignment\": \"ScrollButton\"}}," +
			"{\"inputID\": \"KEY_5\", \"outputType\": \"mouseGroup\", \"mouseGroup\": {\"mouseAssignment\": \"Back\"}}"), notes);
		Assert.Equal("MOUSE_MIDDLE", p.Buttons["Side4"].Key);
		Assert.False(p.Buttons.ContainsKey("Side5"));
		Assert.Single(notes);
		Assert.Contains("Side5", notes[0]);
	}

	[Fact]
	public void Left_handed_click_swap_is_read_from_the_click_mappings()
	{
		Assert.True(Importer.MouseFrom(Payload(SwapMappings, "")).ClickSwap);
		// Valorant (1): no LeftClick/RightClick mappings = the mouse's own order.
		Assert.False(Importer.MouseFrom(Payload(Key("ScrollButton", "KEY_SPACEBAR"), "")).ClickSwap);
		string unswapped = "{\"inputID\": \"LeftClick\", \"outputType\": \"mouseGroup\", \"mouseGroup\": {\"mouseAssignment\": \"Click\"}}," +
			"{\"inputID\": \"RightClick\", \"outputType\": \"mouseGroup\", \"mouseGroup\": {\"mouseAssignment\": \"Menu\"}}";
		Assert.False(Importer.MouseFrom(Payload(unswapped, "")).ClickSwap);
	}

	[Fact]
	public void Quick_effects_and_brightness_become_lighting()
	{
		MouseLightingSettings s = Importer.MouseFrom(Payload(SwapMappings, "")).Lighting;
		Assert.Equal("static", s.Effect);
		Assert.Equal("#FFC0C0", s.Color);
		Assert.Equal(100, s.Brightness);
		s = Importer.MouseFrom(Payload(SwapMappings, "", "{\"selectedEffectId\": 3}", "{\"isEnabled\": true, \"value\": 60}")).Lighting;
		Assert.Equal("spectrum", s.Effect);
		Assert.Equal(60, s.Brightness);
		s = Importer.MouseFrom(Payload(SwapMappings, "", "{\"selectedEffectId\": 3}", "{\"isEnabled\": false, \"value\": 60}")).Lighting;
		Assert.Equal(0, s.Brightness);
		List<string> notes = new List<string>();
		Assert.Null(Importer.MouseFrom(Payload(SwapMappings, "", "{\"selectedEffectId\": 9}"), notes).Lighting);
		Assert.Single(notes);
		Assert.Null(Importer.LightingFrom(JsonNode.Parse("{\"name\": \"x\"}")));
	}
}
