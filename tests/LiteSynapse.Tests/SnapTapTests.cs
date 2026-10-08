using System;
using System.Collections.Generic;
using Xunit;

namespace LiteSynapse.Tests;

public class SnapTapTests
{
	private const int A = 65;

	private const int D = 68;

	// Simulates what the game sees: real events that were passed through plus our injected events.
	private sealed class Game
	{
		public readonly HashSet<int> Held = new HashSet<int>();

		public readonly List<string> Injected = new List<string>();

		public void Apply(int vk, bool down)
		{
			if (down)
			{
				Held.Add(vk);
			}
			else
			{
				Held.Remove(vk);
			}
		}

		public Action<int, bool> Sender => (int vk, bool down) =>
		{
			Injected.Add((down ? "+" : "-") + (char)vk);
			Apply(vk, down);
		};
	}

	private static void Real(SnapTap s, Game g, int vk, bool down)
	{
		if (!s.OnReal(vk, !down, g.Sender))
		{
			g.Apply(vk, down);
		}
	}

	[Fact]
	public void NewerKeyWins_AndOlderKeyComesBackOnRelease()
	{
		SnapTap s = new SnapTap(A, D);
		Game g = new Game();
		Real(s, g, A, down: true);
		Assert.Equal(new[] { A }, g.Held);
		Real(s, g, D, down: true);
		Assert.Equal(new[] { D }, g.Held);
		Assert.Equal(new[] { "-A" }, g.Injected);
		Real(s, g, D, down: false);
		Assert.Equal(new[] { A }, g.Held);
		Assert.Equal(new[] { "-A", "+A" }, g.Injected);
		Real(s, g, A, down: false);
		Assert.Empty(g.Held);
	}

	[Fact]
	public void RealPressesPassThrough_OnlyOppositeKeyIsInjected()
	{
		SnapTap s = new SnapTap(A, D);
		Game g = new Game();
		Assert.False(s.OnReal(A, up: false, g.Sender));
		Assert.False(s.OnReal(D, up: false, g.Sender));
		Assert.Single(g.Injected);
	}

	[Fact]
	public void AutoRepeatOfOverriddenKeyIsSwallowed_ActiveKeyRepeatPasses()
	{
		SnapTap s = new SnapTap(A, D);
		Game g = new Game();
		Real(s, g, A, down: true);
		Real(s, g, D, down: true);
		Assert.True(s.OnReal(A, up: false, g.Sender));
		Assert.False(s.OnReal(D, up: false, g.Sender));
		Assert.Equal(new[] { D }, g.Held);
	}

	[Fact]
	public void ReleaseOfAlreadyLiftedKeyIsSwallowed()
	{
		SnapTap s = new SnapTap(A, D);
		Game g = new Game();
		Real(s, g, A, down: true);
		Real(s, g, D, down: true);
		Assert.True(s.OnReal(A, up: true, g.Sender));
		Assert.Equal(new[] { D }, g.Held);
		Real(s, g, D, down: false);
		Assert.Empty(g.Held);
	}

	[Fact]
	public void ResetOnlyLiftsKeysWeInjected()
	{
		SnapTap s = new SnapTap(A, D);
		Game g = new Game();
		Real(s, g, A, down: true);
		s.Reset(g.Sender);
		Assert.Empty(g.Injected);
		Real(s, g, A, down: true);
		Real(s, g, D, down: true);
		Real(s, g, D, down: false);
		g.Injected.Clear();
		s.Reset(g.Sender);
		Assert.Equal(new[] { "-A" }, g.Injected);
	}

	[Fact]
	public void OutputPath_MatchesLegacySequence()
	{
		SnapTap s = new SnapTap(A, D);
		Game g = new Game();
		s.OnOutput(A, down: true, g.Sender);
		s.OnOutput(D, down: true, g.Sender);
		s.OnOutput(D, down: false, g.Sender);
		s.OnOutput(A, down: false, g.Sender);
		Assert.Equal(new[] { "+A", "-A", "+D", "-D", "+A", "-A" }, g.Injected);
		Assert.Empty(g.Held);
	}

	[Fact]
	public void OtherKeysAreNotHandled()
	{
		SnapTap s = new SnapTap(A, D);
		Assert.True(s.Handles(A));
		Assert.True(s.Handles(D));
		Assert.False(s.Handles(87));
		Assert.False(s.Handles(83));
	}

	// Random A/D press/release/auto-repeat sequences. Invariants the game must always see:
	// never both keys; nothing when nothing is held; the only held key when one is held;
	// the most recently pressed key when both are held.
	[Fact]
	public void Fuzz_GameAlwaysSeesLastInputWins()
	{
		Random rnd = new Random(12345);
		for (int run = 0; run < 2000; run++)
		{
			SnapTap s = new SnapTap(A, D);
			Game g = new Game();
			HashSet<int> phys = new HashSet<int>();
			int last = 0;
			for (int step = 0; step < 60; step++)
			{
				int vk = (rnd.Next(2) == 0) ? A : D;
				int action = rnd.Next(3);
				if (action == 0 && !phys.Contains(vk))
				{
					phys.Add(vk);
					last = vk;
					Real(s, g, vk, down: true);
				}
				else if (action == 1 && phys.Contains(vk))
				{
					phys.Remove(vk);
					Real(s, g, vk, down: false);
				}
				else if (action == 2 && phys.Contains(vk))
				{
					Real(s, g, vk, down: true);
				}
				Assert.False(g.Held.Contains(A) && g.Held.Contains(D), $"run {run} step {step}: both keys held");
				if (phys.Count == 0)
				{
					Assert.Empty(g.Held);
				}
				else if (phys.Count == 1)
				{
					Assert.Equal(phys, g.Held);
				}
				else
				{
					Assert.Equal(new[] { last }, g.Held);
				}
			}
		}
	}
}
