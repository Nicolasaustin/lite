using System;
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace LiteSynapse.Tests;

public class OnboardSlotsTests
{
	private static MouseProfile Profile(string name = "CS2", int dpi = 800)
	{
		return new MouseProfile
		{
			Name = name,
			Dpi = dpi,
			DpiStages = new List<int> { 400, dpi },
			DpiActive = 2,
			Buttons = new Dictionary<string, KeyTarget>
			{
				["Side1"] = new KeyTarget { Key = "KEY_1" },
				["Side2"] = new KeyTarget { Key = "KEY_2" }
			},
			Lighting = new MouseLightingSettings { Effect = "static", Color = "#00FF00", Brightness = 100 }
		};
	}

	private static readonly long[] NoUse = new long[6];

	[Fact]
	public void ReadsTheOldFormat()
	{
		OnboardSlots t = OnboardSlots.Parse("{\n  \"3\": \"CS2\"\n}");
		Assert.Equal("CS2", t[3].Name);
		Assert.Null(t[3].Hash);
		Assert.Null(t[1]);
		Assert.Equal(new Dictionary<string, string> { ["3"] = "CS2" }, t.Names());
	}

	[Fact]
	public void ReadsTheNewFormatAndMixedEntries()
	{
		OnboardSlots t = OnboardSlots.Parse("{\"1\":{\"name\":\"CS2\",\"hash\":\"ABC\"},\"3\":\"Valorant\",\"9\":\"x\",\"two\":\"y\",\"2\":{\"Name\":\"Apex\"}}");
		Assert.Equal("CS2", t[1].Name);
		Assert.Equal("ABC", t[1].Hash);
		Assert.Equal("Apex", t[2].Name);
		Assert.Null(t[2].Hash);
		Assert.Equal("Valorant", t[3].Name);
		Assert.Equal(new[] { 1, 2, 3 }, t.Slots);
	}

	[Fact]
	public void EmptyOrMissingFileMeansNothingKnown()
	{
		Assert.Empty(OnboardSlots.Parse("").Slots);
		Assert.Empty(OnboardSlots.Parse("[]").Slots);
		Assert.Empty(OnboardSlots.Load(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".json")).Slots);
	}

	[Fact]
	public void SaveAndLoadRoundTrip()
	{
		string path = Path.Combine(Path.GetTempPath(), "litesynapse-onboard-" + Guid.NewGuid().ToString("N"), "onboard.json");
		try
		{
			OnboardSlots t = OnboardSlots.Parse("{\"3\":\"CS2\"}");
			t.Set(1, "Valorant", "H1");
			t.Save(path);
			string json = File.ReadAllText(path);
			Assert.Contains("\"1\"", json);
			Assert.Contains("\"hash\": \"H1\"", json);
			OnboardSlots back = OnboardSlots.Load(path);
			Assert.Equal("Valorant", back[1].Name);
			Assert.Equal("H1", back[1].Hash);
			Assert.Equal("CS2", back[3].Name);
			Assert.Null(back[3].Hash);
		}
		finally
		{
			Directory.Delete(Path.GetDirectoryName(path), recursive: true);
		}
	}

	[Fact]
	public void ContentHashIsStableAndCoversEverythingWritten()
	{
		string h = OnboardSlots.ContentHash(Profile());
		Assert.Equal(h, OnboardSlots.ContentHash(Profile()));
		Assert.Equal(24, h.Length);
		Assert.NotEqual(h, OnboardSlots.ContentHash(Profile(dpi: 1600)));
		Assert.NotEqual(h, OnboardSlots.ContentHash(Profile("CS2 copy")));
		MouseProfile key = Profile();
		key.Buttons["Side2"] = new KeyTarget { Key = "KEY_3" };
		Assert.NotEqual(h, OnboardSlots.ContentHash(key));
		MouseProfile light = Profile();
		light.Lighting.Color = "#FF0000";
		Assert.NotEqual(h, OnboardSlots.ContentHash(light));
		MouseProfile swap = Profile();
		swap.ClickSwap = false;
		Assert.NotEqual(h, OnboardSlots.ContentHash(swap));
	}

	[Fact]
	public void ContentHashIgnoresButtonOrder()
	{
		MouseProfile a = Profile();
		MouseProfile b = Profile();
		b.Buttons = new Dictionary<string, KeyTarget>
		{
			["Side2"] = new KeyTarget { Key = "KEY_2" },
			["Side1"] = new KeyTarget { Key = "KEY_1" }
		};
		Assert.Equal(OnboardSlots.ContentHash(a), OnboardSlots.ContentHash(b));
	}

	[Fact]
	public void ActiveSlotAlreadyHoldingTheProfileSendsNothing()
	{
		OnboardSlots t = new OnboardSlots();
		t.Set(1, "CS2", "H");
		Assert.Equal(new SlotPlan(SlotAction.None, 1), t.PlanApply(1, "H", NoUse));
	}

	[Fact]
	public void AUserSlotWithTheSameProfileIsLeftForALiveSlot()
	{
		// The user wrote CS2 to slot 3 and the mouse is on it: LiteSynapse still moves to a live slot, so changes made
		// while it runs (lighting, DPI) never land in the user's own slot.
		OnboardSlots t = new OnboardSlots();
		t.Set(3, "CS2", "H");
		Assert.Equal(new SlotPlan(SlotAction.Write, 1), t.PlanApply(3, "H", NoUse));
		t.Set(2, "CS2", "H");
		Assert.Equal(new SlotPlan(SlotAction.Switch, 2), t.PlanApply(3, "H", NoUse));
		// Pre-writing does not count the user's slot either.
		OnboardSlots u = new OnboardSlots();
		u.Set(3, "CS2", "H");
		Assert.Equal(new SlotPlan(SlotAction.Write, 1), u.PlanPrewrite(3, "H", NoUse));
	}

	[Fact]
	public void ALiveSlotHoldingTheProfileIsSwitchedToWithoutWriting()
	{
		OnboardSlots t = new OnboardSlots();
		t.Set(2, "CS2", "H");
		Assert.Equal(new SlotPlan(SlotAction.Switch, 2), t.PlanApply(3, "H", NoUse));
		Assert.Equal(new SlotPlan(SlotAction.Switch, 2), t.PlanApply(1, "H", NoUse));
	}

	[Fact]
	public void AUserSlotIsNeverSwitchedToForALiveProfile()
	{
		// Slot 4 holds the same content, but switching there would make "back to your slot" lose track.
		OnboardSlots t = new OnboardSlots();
		t.Set(4, "CS2", "H");
		SlotPlan plan = t.PlanApply(3, "H", NoUse);
		Assert.Equal(SlotAction.Write, plan.Action);
		Assert.True(OnboardSlots.IsLiveSlot(plan.Slot));
	}

	[Fact]
	public void LegacyEntriesWithoutHashAreNeverReused()
	{
		OnboardSlots t = OnboardSlots.Parse("{\"1\":\"CS2\",\"3\":\"CS2\"}");
		Assert.Equal(new SlotPlan(SlotAction.Write, 2), t.PlanApply(1, "H", NoUse));
		Assert.Equal(new SlotPlan(SlotAction.Write, 1), t.PlanApply(3, "H", NoUse));
	}

	[Fact]
	public void AWriteNeverGoesToTheActiveSlot()
	{
		OnboardSlots t = new OnboardSlots();
		for (int active = 1; active <= 5; active++)
		{
			SlotPlan plan = t.PlanApply(active, "H", new long[6] { 0, 5, 9, 0, 0, 0 });
			Assert.Equal(SlotAction.Write, plan.Action);
			Assert.NotEqual(active, plan.Slot);
			Assert.True(OnboardSlots.IsLiveSlot(plan.Slot));
			SlotPlan pre = t.PlanPrewrite(active, "H", new long[6] { 0, 5, 9, 0, 0, 0 });
			Assert.NotEqual(active, pre.Slot);
		}
	}

	[Fact]
	public void TheLeastRecentlyUsedLiveSlotIsOverwritten()
	{
		OnboardSlots t = new OnboardSlots();
		t.Set(1, "CS2", "A");
		t.Set(2, "Valorant", "B");
		// Slot 2 was used after slot 1: a third game goes into slot 1.
		Assert.Equal(new SlotPlan(SlotAction.Write, 1), t.PlanApply(3, "C", new long[6] { 0, 1, 2, 0, 0, 0 }));
		Assert.Equal(new SlotPlan(SlotAction.Write, 2), t.PlanApply(3, "C", new long[6] { 0, 2, 1, 0, 0, 0 }));
		// Nothing known yet: slot 1 first, like before.
		Assert.Equal(new SlotPlan(SlotAction.Write, 1), t.PlanApply(3, "C", null));
	}

	[Fact]
	public void PrewriteSkipsAProfileAlreadyOnTheMouse()
	{
		OnboardSlots t = new OnboardSlots();
		t.Set(2, "CS2", "H");
		Assert.Equal(SlotAction.None, t.PlanPrewrite(3, "H", NoUse).Action);
		Assert.Equal(SlotAction.None, t.PlanPrewrite(2, "H", NoUse).Action);
		Assert.Equal(new SlotPlan(SlotAction.Write, 1), t.PlanPrewrite(2, "OTHER", NoUse));
	}

	[Fact]
	public void ForgetMarksAFailedWriteAsUnknown()
	{
		OnboardSlots t = new OnboardSlots();
		t.Set(1, "CS2", "H");
		t.Forget(1);
		Assert.Null(t[1]);
		Assert.Equal(SlotAction.Write, t.PlanApply(2, "H", NoUse).Action);
	}

	[Fact]
	public void SlotNumbersOutsideOneToFiveAreRejected()
	{
		OnboardSlots t = new OnboardSlots();
		Assert.Throws<ArgumentOutOfRangeException>(() => t.Set(0, "x", "h"));
		Assert.Throws<ArgumentOutOfRangeException>(() => t.Set(6, "x", "h"));
	}
}
