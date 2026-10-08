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

	// The user's real macros, read from the data folder that ships with the repo.
	private static List<Macro> UserMacros()
	{
		string dir = Directory.GetCurrentDirectory();
		while (dir != null && !Directory.Exists(Path.Combine(dir, ".git")))
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
}
