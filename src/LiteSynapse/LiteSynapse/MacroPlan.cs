using System.Collections.Generic;

namespace LiteSynapse;

// A macro turned into send batches ahead of time. Consecutive steps with no delay become one batch that is
// sent with a single SendInput call, so those events land in the same input frame in exactly that order
// (e.g. jump + release attack for a jumpthrow). A step's delay flushes its batch and then waits.
internal sealed class MacroPlan
{
	internal readonly struct Key
	{
		public readonly int Vk;

		public readonly bool Ext;

		public readonly bool Down;

		// A mouse button (MacroStep.Mouse) instead of a key; 0 for a key.
		public readonly int Mouse;

		public Key(int vk, bool ext, bool down, int mouse = 0)
		{
			Vk = vk;
			Ext = ext;
			Down = down;
			Mouse = mouse;
		}
	}

	internal readonly struct Segment
	{
		public readonly Key[] Keys;

		public readonly int DelayMs;

		public Segment(Key[] keys, int delayMs)
		{
			Keys = keys;
			DelayMs = delayMs;
		}
	}

	public readonly Segment[] Segments;

	// Keys still held when the macro ends (released afterwards so nothing stays stuck).
	public readonly Key[] LeftDown;

	// Keys still held after each segment (as key-ups), so a macro stopped part way releases exactly what it holds.
	public readonly Key[][] HeldAfter;

	public MacroPlan(IList<MacroStep> steps)
	{
		List<Segment> segs = new List<Segment>();
		List<Key[]> heldAfter = new List<Key[]>();
		List<Key> cur = new List<Key>();
		HashSet<(int, bool)> held = new HashSet<(int, bool)>();
		List<(int, bool)> order = new List<(int, bool)>();
		foreach (MacroStep s in steps)
		{
			if (s.Vk != 0 || s.Mouse != 0)
			{
				cur.Add(new Key(s.Vk, s.Ext, s.Down, s.Mouse));
				// Held set: a mouse button counts as a negative "key" so it can never collide with a vk.
				(int, bool) k = ((s.Mouse != 0) ? (-s.Mouse) : s.Vk, s.Ext);
				if (s.Down)
				{
					if (held.Add(k))
					{
						order.Add(k);
					}
				}
				else if (held.Remove(k))
				{
					order.Remove(k);
				}
			}
			if (s.DelayMs > 0)
			{
				segs.Add(new Segment(cur.ToArray(), s.DelayMs));
				heldAfter.Add(Ups(order));
				cur.Clear();
			}
		}
		if (cur.Count > 0)
		{
			segs.Add(new Segment(cur.ToArray(), 0));
			heldAfter.Add(Ups(order));
		}
		Segments = segs.ToArray();
		HeldAfter = heldAfter.ToArray();
		LeftDown = Ups(order);
	}

	private static Key[] Ups(List<(int, bool)> order)
	{
		return order.ConvertAll(((int, bool) k) => (k.Item1 < 0)
			? new Key(0, k.Item2, down: false, -k.Item1)
			: new Key(k.Item1, k.Item2, down: false)).ToArray();
	}
}
