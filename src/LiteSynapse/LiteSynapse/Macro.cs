using System.Collections.Generic;

namespace LiteSynapse;

public class Macro
{
	public string Name { get; set; }

	public List<MacroStep> Steps { get; set; } = new List<MacroStep>();
}
