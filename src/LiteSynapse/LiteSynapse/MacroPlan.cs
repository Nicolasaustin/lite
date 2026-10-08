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

		public Key(int vk, bool ext, bool down)
		{
			Vk = vk;
			Ext = ext;
			Down = down;
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

	public MacroPlan(IList<MacroStep> steps)
	{
		List<Segment> segs = new List<Segment>();
		List<Key> cur = new List<Key>();
		HashSet<(int, bool)> held = new HashSet<(int, bool)>();
		List<(int, bool)> order = new List<(int, bool)>();
		foreach (MacroStep s in steps)
		{
			if (s.Vk != 0)
			{
				cur.Add(new Key(s.Vk, s.Ext, s.Down));
				(int, bool) k = (s.Vk, s.Ext);
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
				cur.Clear();
			}
		}
		if (cur.Count > 0)
		{
			segs.Add(new Segment(cur.ToArray(), 0));
		}
		Segments = segs.ToArray();
		LeftDown = order.ConvertAll(((int, bool) k) => new Key(k.Item1, k.Item2, down: false)).ToArray();
	}
}
