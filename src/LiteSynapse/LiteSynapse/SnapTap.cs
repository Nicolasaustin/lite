using System;

namespace LiteSynapse;

// Snap Tap ("last input wins") for one pair of opposite keys, A and D by default.
// Pure state machine with no Windows calls: it decides what the game should see and asks the caller to
// send the minimum number of synthetic events. Real presses and releases are passed through untouched;
// only the opposite key's release (and its re-press when the newer key is let go) is injected.
//
// This runs on the keyboard hook thread for every A and D event, so the state is six flags rather than
// three sets: there are only ever two keys, and a flag costs a load and a store where a hash set costs a hash,
// a bucket walk and, on the very first press, an allocation the GC would later have to collect.
internal sealed class SnapTap
{
	private readonly int keyA;

	private readonly int keyB;

	// Index 0 is keyA, index 1 is keyB.
	// Physically held right now.
	private readonly bool[] phys = new bool[2];

	// Seen by the game as held. At most one of the pair at a time - that is the whole point.
	private readonly bool[] seen = new bool[2];

	// That "held" was injected by us, so Reset only lifts what we pressed.
	private readonly bool[] injected = new bool[2];

	public SnapTap(int keyA = 65, int keyB = 68)
	{
		this.keyA = keyA;
		this.keyB = keyB;
	}

	public bool Handles(int vk)
	{
		if (vk != keyA)
		{
			return vk == keyB;
		}
		return true;
	}

	// Callers check Handles first, so anything that is not keyA is keyB.
	private int Index(int vk)
	{
		return (vk == keyA) ? 0 : 1;
	}

	private int Key(int index)
	{
		return (index == 0) ? keyA : keyB;
	}

	// A real keyboard event for one of the pair. Returns true when the real event must be swallowed
	// (auto-repeat of a key we lifted, or the release of a key the game already sees as up).
	public bool OnReal(int vk, bool up, Action<int, bool> send)
	{
		int i = Index(vk);
		int o = 1 - i;
		if (!up)
		{
			if (phys[i])
			{
				// Auto-repeat: let it through only if the game still sees this key as down.
				return !seen[i];
			}
			phys[i] = true;
			if (seen[o])
			{
				seen[o] = false;
				injected[o] = false;
				send(Key(o), arg2: false);
			}
			seen[i] = true;
			injected[i] = false;
			return false;
		}
		if (!phys[i])
		{
			// Released a key we never saw go down (held before Snap Tap turned on, or across a reset):
			// let the real release through so the game can never be left with it stuck.
			seen[i] = false;
			injected[i] = false;
			return false;
		}
		phys[i] = false;
		bool wasSeen = seen[i];
		seen[i] = false;
		injected[i] = false;
		if (phys[o] && !seen[o])
		{
			seen[o] = true;
			injected[o] = true;
			send(Key(o), arg2: true);
		}
		return !wasSeen;
	}

	// A synthetic key we want the game to receive (a remapped Naga button or a delayed keyboard key):
	// everything is injected, with the same last-input-wins rule.
	public void OnOutput(int vk, bool down, Action<int, bool> send)
	{
		int i = Index(vk);
		int o = 1 - i;
		if (down)
		{
			if (phys[i])
			{
				if (seen[i])
				{
					send(vk, arg2: true);
				}
				return;
			}
			phys[i] = true;
			if (seen[o])
			{
				seen[o] = false;
				injected[o] = false;
				send(Key(o), arg2: false);
			}
			seen[i] = true;
			injected[i] = true;
			send(vk, arg2: true);
			return;
		}
		phys[i] = false;
		if (seen[i])
		{
			seen[i] = false;
			injected[i] = false;
			send(vk, arg2: false);
		}
		if (phys[o] && !seen[o])
		{
			seen[o] = true;
			injected[o] = true;
			send(Key(o), arg2: true);
		}
	}

	// Forget all state, lifting only keys we injected.
	public void Reset(Action<int, bool> send)
	{
		for (int i = 0; i < 2; i++)
		{
			if (injected[i])
			{
				send(Key(i), arg2: false);
			}
		}
		Array.Clear(phys);
		Array.Clear(seen);
		Array.Clear(injected);
	}
}
