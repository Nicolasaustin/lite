using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

public class Macro
{
	public string Name { get; set; }

	public List<MacroStep> Steps { get; set; } = new List<MacroStep>();

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }
}
