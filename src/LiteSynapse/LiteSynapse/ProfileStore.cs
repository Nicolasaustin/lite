using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

public class ProfileStore
{
	public List<MouseProfile> Mouse { get; set; } = new List<MouseProfile>();

	public List<KbProfile> Keyboard { get; set; } = new List<KbProfile>();

	public List<Macro> Macros { get; set; } = new List<Macro>();

	// Highest DataMigrations version already applied to this data (0 = none).
	public int MigrationVersion { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }
}
