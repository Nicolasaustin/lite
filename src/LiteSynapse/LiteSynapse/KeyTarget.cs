using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

// What a mouse button sends: a KEY_* name, "DISABLED" (nothing) or "MOUSE_MIDDLE" (middle click), plus modifier keys.
public class KeyTarget
{
	public const string Disabled = "DISABLED";

	public const string MouseMiddle = "MOUSE_MIDDLE";

	public string Key { get; set; }

	public List<string> Mods { get; set; } = new List<string>();

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }
}
