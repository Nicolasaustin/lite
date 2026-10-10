using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Xunit;

namespace LiteSynapse.Tests;

public class MacroPlanTests
{
	private static MacroStep Key(int vk, bool down, int delay = 0, bool ext = false)
	{
		return new MacroStep
		{
			Vk = vk,
			Down = down,
			DelayMs = delay,
			Ext = ext
		};
	}

	private static MacroStep Wait(int ms)
	{
		return new MacroStep
		{
			Vk = 0,
			DelayMs = ms
		};
	}

	private static MacroStep Mouse(int button, bool down, int delay = 0)
	{
		return new MacroStep { Mouse = button, Down = down, DelayMs = delay };
	}

	[Fact]
	public void AMouseButtonMacroPlansAsOneSegmentAndReleasesWhatItHeld()
	{
		// Hold left, hold a key, release both at the end with no waits: one segment, both released after.
		MacroPlan plan = new MacroPlan(new[]
		{
			Mouse(1, down: true),
			Key('W', down: true),
			Key('W', down: false),
			Mouse(1, down: false)
		});
		Assert.Single(plan.Segments);
		Assert.Equal(4, plan.Segments[0].Keys.Length);
		Assert.Equal(1, plan.Segments[0].Keys[0].Mouse);
		// Nothing is left held (both were released inside the macro).
		Assert.Empty(plan.LeftDown);
	}

	[Fact]
	public void AMouseButtonLeftDownIsReleasedAfter()
	{
		// Press and hold the forward button, never release it: the plan releases it so it cannot stick.
		MacroPlan plan = new MacroPlan(new[] { Mouse(5, down: true) });
		Assert.Single(plan.LeftDown);
		Assert.Equal(5, plan.LeftDown[0].Mouse);
		Assert.False(plan.LeftDown[0].Down);
	}

	[Fact]
	public void AMouseButtonAndAKeyWithTheSameNumberDoNotCollide()
	{
		// Mouse button 1 and vk 1 must be tracked apart (the held set keys mouse buttons negatively).
		MacroPlan plan = new MacroPlan(new[]
		{
			Mouse(1, down: true),
			Key(1, down: true)
		});
		Assert.Equal(2, plan.LeftDown.Length);
		Assert.Contains(plan.LeftDown, (MacroPlan.Key k) => k.Mouse == 1);
		Assert.Contains(plan.LeftDown, (MacroPlan.Key k) => k.Vk == 1 && k.Mouse == 0);
	}

	// The user's real macros, read from the data folder that ships with the repo.
	private static List<Macro> UserMacros()
	{
		string dir = Directory.GetCurrentDirectory();
		// .git is a folder in a clone and a file in a git worktree.
		while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")) && !File.Exists(Path.Combine(dir, ".git")))
		{
			dir = Path.GetDirectoryName(dir);
		}
		Assert.NotNull(dir);
		string json = File.ReadAllText(Path.Combine(dir, "LiteSynapse", "data", "profiles.json"));
		using JsonDocument doc = JsonDocument.Parse(json);
		return JsonSerializer.Deserialize<List<Macro>>(doc.RootElement.GetProperty("Macros").GetRawText());
	}

	[Fact]
	public void Jumpthrow_IsOneAtomicBatchInOrder()
	{
		Macro m = UserMacros().Single((Macro x) => x.Name == "Jumpthrow");
		MacroPlan p = new MacroPlan(m.Steps);
		Assert.Single(p.Segments);
		Assert.Equal(0, p.Segments[0].DelayMs);
		Assert.Equal(m.Steps.Select((MacroStep s) => (s.Vk, s.Ext, s.Down)), p.Segments[0].Keys.Select((MacroPlan.Key k) => (k.Vk, k.Ext, k.Down)));
		Assert.Empty(p.LeftDown);
	}

	[Fact]
	public void WJumpthrow_SplitsAtTheWaitAndKeepsTiming()
	{
		Macro m = UserMacros().Single((Macro x) => x.Name == "W Jumpthrow");
		MacroPlan p = new MacroPlan(m.Steps);
		Assert.Equal(2, p.Segments.Length);
		Assert.Equal(7, p.Segments[0].Keys.Length);
		Assert.Equal(200, p.Segments[0].DelayMs);
		Assert.Single(p.Segments[1].Keys);
		Assert.Equal(106, p.Segments[1].Keys[0].Vk);
		Assert.False(p.Segments[1].Keys[0].Down);
		Assert.Empty(p.LeftDown);
	}

	[Fact]
	public void TotalWaitEqualsSumOfDelays()
	{
		foreach (Macro m in UserMacros())
		{
			MacroPlan p = new MacroPlan(m.Steps);
			Assert.Equal(m.Steps.Sum((MacroStep s) => s.DelayMs), p.Segments.Sum((MacroPlan.Segment s) => s.DelayMs));
			Assert.Equal(m.Steps.Count((MacroStep s) => s.Vk != 0), p.Segments.Sum((MacroPlan.Segment s) => s.Keys.Length));
		}
	}

	[Fact]
	public void DelayOnAKeyStepFlushesThatKeyFirst()
	{
		MacroPlan p = new MacroPlan(new List<MacroStep>
		{
			Key(87, down: true, 50),
			Key(32, down: true),
			Key(32, down: false),
			Key(87, down: false)
		});
		Assert.Equal(2, p.Segments.Length);
		Assert.Single(p.Segments[0].Keys);
		Assert.Equal(50, p.Segments[0].DelayMs);
		Assert.Equal(3, p.Segments[1].Keys.Length);
	}

	[Fact]
	public void KeysLeftDownAreReleasedAfterwards()
	{
		MacroPlan p = new MacroPlan(new List<MacroStep>
		{
			Key(87, down: true),
			Key(13, down: true, 0, ext: true),
			Wait(10),
			Key(87, down: false)
		});
		Assert.Single(p.LeftDown);
		Assert.Equal(13, p.LeftDown[0].Vk);
		Assert.True(p.LeftDown[0].Ext);
		Assert.False(p.LeftDown[0].Down);
	}

	[Fact]
	public void EmptyMacroHasNoSegments()
	{
		MacroPlan p = new MacroPlan(new List<MacroStep>());
		Assert.Empty(p.Segments);
		Assert.Empty(p.LeftDown);
	}

	[Fact]
	public void KeysHeldAfterEachSegmentAreKnownSoAStoppedMacroReleasesThem()
	{
		// W down, wait, Space down+up, wait, W up: a stop during the waits must lift W only.
		MacroPlan p = new MacroPlan(new List<MacroStep>
		{
			Key(0x57, down: true, delay: 500),
			Key(0x20, down: true), Key(0x20, down: false, delay: 300),
			Key(0x57, down: false)
		});
		Assert.Equal(3, p.Segments.Length);
		Assert.Equal(p.Segments.Length, p.HeldAfter.Length);
		Assert.Equal(new[] { 0x57 }, p.HeldAfter[0].Select(k => k.Vk));
		Assert.Equal(new[] { 0x57 }, p.HeldAfter[1].Select(k => k.Vk));
		Assert.Empty(p.HeldAfter[2]);
		Assert.All(p.HeldAfter.SelectMany(h => h), k => Assert.False(k.Down));
		Assert.Empty(p.LeftDown);
		// A macro that ends with a key still down: the last entry is LeftDown.
		MacroPlan q = new MacroPlan(new List<MacroStep> { Key(0x41, down: true, delay: 20), Key(0x44, down: true) });
		Assert.Equal(q.LeftDown.Select(k => k.Vk), q.HeldAfter[^1].Select(k => k.Vk));
		Assert.Equal(new[] { 0x41, 0x44 }, q.LeftDown.Select(k => k.Vk));
		Assert.Empty(new MacroPlan(new List<MacroStep>()).HeldAfter);
	}
}
