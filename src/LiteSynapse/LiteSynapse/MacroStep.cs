namespace LiteSynapse;

// One step of a macro: a key going down or up (Vk), a mouse button going down or up (Mouse: 1 left, 2 right, 3 middle,
// 4 back, 5 forward), or, with both 0, a wait. DelayMs is the wait after the step (steps with no wait between them
// are sent together, see MacroPlan). Mouse is new in build 23; older files simply have none.
public class MacroStep
{
	public int Vk { get; set; }

	public bool Ext { get; set; }

	public bool Down { get; set; }

	public int DelayMs { get; set; }

	public int Mouse { get; set; }

	// A key or mouse event, as opposed to a plain wait.
	public bool IsEvent => Vk != 0 || Mouse != 0;
}
