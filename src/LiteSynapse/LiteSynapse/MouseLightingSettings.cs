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

	// Zones that light differently from the rest, keyed by MouseLighting zone id ("scroll", "logo", "side").
	// null or a missing zone = the zone uses the settings above. Entries never have zones of their own.
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public Dictionary<string, MouseLightingSettings> Zones { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }

	// The settings a zone shows: its own, or the profile's.
	public MouseLightingSettings For(string zone)
	{
		if (Zones != null && zone != null && Zones.TryGetValue(zone, out MouseLightingSettings z) && z != null)
		{
			return z;
		}
		return this;
	}
}
