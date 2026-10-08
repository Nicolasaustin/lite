using System.Collections.Generic;

namespace LiteSynapse;

public class ProfileStore
{
	public List<MouseProfile> Mouse { get; set; } = new List<MouseProfile>();

	public List<KbProfile> Keyboard { get; set; } = new List<KbProfile>();

	public List<Macro> Macros { get; set; } = new List<Macro>();
}
