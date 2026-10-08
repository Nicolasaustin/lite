using System.Collections.Generic;

namespace LiteSynapse;

public class KbProfile
{
	public string Name { get; set; }

	public int Brightness { get; set; } = 100;

	public bool SnapTap { get; set; }

	public Dictionary<string, string> MacroKeys { get; set; } = new Dictionary<string, string>();
}
