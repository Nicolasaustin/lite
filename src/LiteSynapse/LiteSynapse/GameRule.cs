using System.Collections.Generic;

namespace LiteSynapse;

public class GameRule
{
	public string Profile { get; set; }

	public List<string> Exes { get; set; } = new List<string>();

	public string KeyboardProfile { get; set; } = "";
}
