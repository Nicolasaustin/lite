using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

public class KbProfile
{
	public string Name { get; set; }

	public int Brightness { get; set; } = 100;

	public bool SnapTap { get; set; }

	public Dictionary<string, string> MacroKeys { get; set; } = new Dictionary<string, string>();

	// One of KbLighting.Effects.
	public string Effect { get; set; } = "static";

	// 1 (slow) .. 3 (fast); used by reactive and starlight.
	public int EffectSpeed { get; set; } = 2;

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }
}
