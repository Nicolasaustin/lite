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

	// A private snapshot for the device worker: it enumerates Buttons/DpiStages/Lighting on its own (BelowNormal)
	// thread while the UI can be editing the live profile on the UI thread. Copying the mutable collections (the
	// entries themselves are replaced wholesale by the UI, never mutated in place, so sharing their references is
	// safe) means a long USB write never enumerates a dictionary the UI is changing underneath it.
	public MouseProfile Clone()
	{
		MouseProfile c = (MouseProfile)MemberwiseClone();
		c.DpiStages = (DpiStages != null) ? new List<int>(DpiStages) : null;
		c.Buttons = (Buttons != null) ? new Dictionary<string, KeyTarget>(Buttons) : null;
		c.Lighting = Lighting?.Snapshot();
		return c;
	}
}
