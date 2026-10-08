using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

// Per-profile mouse lighting. Effect is one of MouseLighting.Effects; Color is "#RRGGBB"; Brightness is 0..100.
public class MouseLightingSettings
{
	public string Effect { get; set; } = "static";

	public string Color { get; set; } = "#00FF00";

	public int Brightness { get; set; } = 100;

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }
}
