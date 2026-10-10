using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// LiveProfile against the fake mouse: what is written, what is only switched, and what is left alone.
public class LiveProfileTests : IDisposable
{
	private readonly string dir;

	private readonly string table;

	public LiveProfileTests()
	{
		dir = Path.Combine(Path.GetTempPath(), "litesynapse-live-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(dir);
		table = Path.Combine(dir, "onboard.json");
	}

	public void Dispose()
	{
		Directory.Delete(dir, recursive: true);
	}

	private static MouseProfile Profile(string name, string side1 = "KEY_1")
	{
		return new MouseProfile
		{
			Name = name,
			Dpi = 800,
			DpiStages = new List<int> { 800 },
			DpiActive = 1,
			Buttons = new Dictionary<string, KeyTarget> { ["Side1"] = new KeyTarget { Key = side1 } }
		};
	}

	// Slots the fake mouse had written (the slot-select command 05/02 starts every onboard write).
	private static List<int> Writes(RazerDevice dev)
	{
		return (from s in dev.Sent
			where s.Class == 5 && s.Id == 2
			select (int)s.Args[0]).ToList();
	}

	[Fact]
	public void FirstGameWritesALiveSlotThenSwitches()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		Assert.Null(live.Apply(dev, Profile("CS2")));
		Assert.Equal(new List<int> { 1 }, Writes(dev));
		Assert.Equal(new List<int> { 1 }, dev.SlotSwitches);
		Assert.True(live.Wrote);
		Assert.True(live.Switched);
		Assert.Equal(1, live.LiveSlot);
		Assert.Equal(3, live.ReturnSlot);
		Assert.Contains("\"CS2\" is running on the mouse (slot 1", live.Status);
		OnboardSlots saved = OnboardSlots.Load(table);
		Assert.Equal("CS2", saved[1].Name);
		Assert.Equal(OnboardSlots.ContentHash(Profile("CS2")), saved[1].Hash);
	}

	// Entering a game costs one slot switch and nothing else: the DPI is already in the slot, so no DPI command
	// goes out while the game is taking the foreground.
	[Fact]
	public void AGameStartSendsNoDpiCommand()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		MouseProfile cs2 = Profile("CS2");
		cs2.DpiStages = new List<int> { 400, 1600 };
		cs2.DpiActive = 2;
		Assert.Null(live.Apply(dev, cs2));
		Assert.Empty(dev.DpiWrites);
		Assert.Equal(new List<int> { 1600 }, dev.DpiSeeds);
		// The seeded value is the one the slot write stored.
		Assert.Equal(1600, OnboardMemory.ActiveDpi(cs2));
		Assert.Equal(new List<int> { 1000 }, dev.PollingWrites);
		// Back to the desktop profile and into the game again: still no DPI command.
		Assert.Null(live.Restore(dev));
		Assert.Null(live.Apply(dev, cs2));
		Assert.Empty(dev.DpiWrites);
	}

	[Fact]
	public void AltTabBackIntoTheGameSendsNoWriteAndNoSwitch()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		live.Apply(dev, Profile("CS2"));
		int sent = dev.Sent.Count;
		Assert.Null(live.Apply(dev, Profile("CS2")));
		Assert.Equal(sent, dev.Sent.Count);
		Assert.Equal(new List<int> { 1 }, dev.SlotSwitches);
		Assert.False(live.Switched);
		Assert.False(live.Wrote);
	}

	[Fact]
	public void GameAfterDesktopRestoreOnlySwitches()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		live.Apply(dev, Profile("CS2"));
		Assert.Null(live.Restore(dev));
		Assert.Equal(3, dev.ActiveSlot);
		int sent = dev.Sent.Count;
		Assert.Null(live.Apply(dev, Profile("CS2")));
		Assert.Equal(sent, dev.Sent.Count);
		Assert.Equal(new List<int> { 1, 3, 1 }, dev.SlotSwitches);
		Assert.False(live.Wrote);
	}

	[Fact]
	public void SlotKnowledgeSurvivesARestart()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		new LiveProfile(table).Apply(dev, Profile("CS2"));
		new LiveProfile(table).Restore(dev);
		int sent = dev.Sent.Count;
		Assert.Null(new LiveProfile(table).Apply(dev, Profile("CS2")));
		Assert.Equal(sent, dev.Sent.Count);
		Assert.Equal(1, dev.ActiveSlot);
	}

	[Fact]
	public void TwoGamesUseBothLiveSlotsAndNeverWriteTheActiveOne()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		live.Apply(dev, Profile("CS2"));
		live.Apply(dev, Profile("Valorant", "KEY_2"));
		Assert.Equal(new List<int> { 1, 2 }, Writes(dev));
		Assert.Equal(2, dev.ActiveSlot);
		// Back to CS2: slot 1 still holds it.
		live.Apply(dev, Profile("CS2"));
		Assert.Equal(new List<int> { 1, 2 }, Writes(dev));
		Assert.Equal(1, dev.ActiveSlot);
		// A third game replaces the least recently used live slot (2, Valorant), never the active slot 1.
		live.Apply(dev, Profile("Apex", "KEY_3"));
		Assert.Equal(new List<int> { 1, 2, 2 }, Writes(dev));
		Assert.Equal(2, dev.ActiveSlot);
	}

	[Fact]
	public void AnEditedProfileIsWrittenAgain()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		live.Apply(dev, Profile("CS2"));
		live.Restore(dev);
		live.Apply(dev, Profile("CS2", "KEY_9"));
		Assert.Equal(2, Writes(dev).Count);
		Assert.True(live.Wrote);
	}

	[Fact]
	public void PrewriteFillsAnIdleLiveSlotWithoutSwitching()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		Assert.Null(live.Prewrite(dev, Profile("Valorant")));
		Assert.Equal(new List<int> { 1 }, Writes(dev));
		Assert.Empty(dev.SlotSwitches);
		Assert.Equal(3, dev.ActiveSlot);
		// Nothing to do the second time.
		Assert.Null(live.Prewrite(dev, Profile("Valorant")));
		Assert.Single(Writes(dev));
		// The game then starts with a switch only.
		live.Apply(dev, Profile("Valorant"));
		Assert.Single(Writes(dev));
		Assert.Equal(new List<int> { 1 }, dev.SlotSwitches);
	}

	[Fact]
	public void AFailedWriteIsNotReusedAndTheSlotIsNotActivated()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		dev.RawStatus = (byte cls, byte id, byte[] args) => (cls == 2 && id == 0x0C) ? 3 : 2;
		LiveProfile live = new LiveProfile(table);
		Assert.NotNull(live.Apply(dev, Profile("CS2")));
		Assert.Empty(dev.SlotSwitches);
		Assert.Equal(3, dev.ActiveSlot);
		Assert.Null(OnboardSlots.Load(table)[1]);
		dev.RawStatus = (byte cls, byte id, byte[] args) => 2;
		Assert.Null(live.Apply(dev, Profile("CS2")));
		Assert.Equal(2, Writes(dev).Count);
	}

	[Fact]
	public void TheOldFileFormatIsReadButItsContentIsNotTrusted()
	{
		File.WriteAllText(table, "{\n  \"3\": \"CS2\"\n}");
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		Assert.Equal(new Dictionary<string, string> { ["3"] = "CS2" }, live.Names());
		live.Apply(dev, Profile("CS2"));
		Assert.Equal(new List<int> { 1 }, Writes(dev));
		Dictionary<string, string> names = live.Names();
		Assert.Equal("CS2", names["3"]);
		Assert.Equal("CS2", names["1"]);
	}

	[Fact]
	public void AFileRewrittenByAnotherWriterIsReloaded()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		live.Apply(dev, Profile("CS2"));
		live.Restore(dev);
		// The classic window wrote slot 1 by hand and saved the old format: slot 1's content is unknown now.
		File.WriteAllText(table, "{\"1\":\"Something else\"}");
		File.SetLastWriteTimeUtc(table, DateTime.UtcNow.AddSeconds(5));
		live.Apply(dev, Profile("CS2"));
		Assert.Equal(2, Writes(dev).Count);
	}

	[Fact]
	public void UnreadableActiveSlotIsAnError()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = null };
		LiveProfile live = new LiveProfile(table);
		Assert.Equal("could not read the mouse's active slot", live.Apply(dev, Profile("CS2")));
		Assert.Empty(dev.Sent);
	}

	[Fact]
	public void ManualWriteRecordsTheSlot()
	{
		RazerDevice dev = new RazerDevice { ActiveSlot = 3 };
		LiveProfile live = new LiveProfile(table);
		Assert.Null(live.WriteSlot(dev, 4, Profile("PEAK")));
		Assert.Equal("PEAK", OnboardSlots.Load(table)[4].Name);
		Assert.Equal(OnboardSlots.ContentHash(Profile("PEAK")), OnboardSlots.Load(table)[4].Hash);
	}
}
