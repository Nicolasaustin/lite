using System;
using System.Collections.Generic;

namespace LiteSynapse;

// Snap Tap ("last input wins") for one pair of opposite keys, A and D by default.
// Pure state machine with no Windows calls: it decides what the game should see and asks the caller to
// send the minimum number of synthetic events. Real presses and releases are passed through untouched;
// only the opposite key's release (and its re-press when the newer key is let go) is injected.
internal sealed class SnapTap
{
	private readonly int keyA;

	private readonly int keyB;

	// Keys physically held right now.
	private readonly HashSet<int> phys = new HashSet<int>();

	// Keys the game currently sees as held.
	private readonly HashSet<int> seen = new HashSet<int>();

	// Keys whose current "held" state was injected by us (so Reset only lifts what we pressed).
	private readonly HashSet<int> injected = new HashSet<int>();

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

	private int Other(int vk)
	{
		if (vk != keyA)
		{
			return keyA;
		}
		return keyB;
	}

	// A real keyboard event for one of the pair. Returns true when the real event must be swallowed
	// (auto-repeat of a key we lifted, or the release of a key the game already sees as up).
	public bool OnReal(int vk, bool up, Action<int, bool> send)
	{
		int other = Other(vk);
		if (!up)
		{
			if (phys.Contains(vk))
			{
				return !seen.Contains(vk);
			}
			phys.Add(vk);
			if (seen.Remove(other))
			{
				injected.Remove(other);
				send(other, arg2: false);
			}
			seen.Add(vk);
			injected.Remove(vk);
			return false;
		}
		phys.Remove(vk);
		bool wasSeen = seen.Remove(vk);
		injected.Remove(vk);
		if (phys.Contains(other) && seen.Add(other))
		{
			injected.Add(other);
			send(other, arg2: true);
		}
		return !wasSeen;
	}

	// A synthetic key we want the game to receive (a remapped Naga button or a delayed keyboard key):
	// everything is injected, with the same last-input-wins rule.
	public void OnOutput(int vk, bool down, Action<int, bool> send)
	{
		int other = Other(vk);
		if (down)
		{
			if (phys.Contains(vk))
			{
				if (seen.Contains(vk))
				{
					send(vk, arg2: true);
				}
				return;
			}
			phys.Add(vk);
			if (seen.Remove(other))
			{
				injected.Remove(other);
				send(other, arg2: false);
			}
			seen.Add(vk);
			injected.Add(vk);
			send(vk, arg2: true);
			return;
		}
		phys.Remove(vk);
		if (seen.Remove(vk))
		{
			injected.Remove(vk);
			send(vk, arg2: false);
		}
		if (phys.Contains(other) && seen.Add(other))
		{
			injected.Add(other);
			send(other, arg2: true);
		}
	}

	// Forget all state, lifting only keys we injected.
	public void Reset(Action<int, bool> send)
	{
		foreach (int vk in injected)
		{
			send(vk, arg2: false);
		}
		injected.Clear();
		seen.Clear();
		phys.Clear();
	}
}
