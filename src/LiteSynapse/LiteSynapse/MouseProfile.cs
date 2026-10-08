using System.Collections.Generic;

namespace LiteSynapse;

public class MouseProfile
{
	public string Name { get; set; }

	public int PollingRate { get; set; } = 1000;

	public int Dpi { get; set; } = 800;

	public List<int> DpiStages { get; set; } = new List<int>();

	public int DpiActive { get; set; } = 1;

	public Dictionary<string, KeyTarget> Buttons { get; set; } = new Dictionary<string, KeyTarget>();
}
