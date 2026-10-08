using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

public class MouseProfile
{
	public string Name { get; set; }

	public int PollingRate { get; set; } = 1000;

	public int Dpi { get; set; } = 800;

	public List<int> DpiStages { get; set; } = new List<int>();

	public int DpiActive { get; set; } = 1;

	public Dictionary<string, KeyTarget> Buttons { get; set; } = new Dictionary<string, KeyTarget>();

	// null = leave the mouse lighting as it is.
	public MouseLightingSettings Lighting { get; set; }

	// Left-handed click swap written to onboard memory (left button sends right click and the other way round).
	public bool ClickSwap { get; set; } = true;

	// Fields this version does not know survive a load/save round trip.
	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }
}
