using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

public class OnboardMemoryTests
{
	private static string Hex(byte cls, byte id, byte size, byte[] args)
	{
		return $"{cls:X2}/{id:X2} size {size}: {Convert.ToHexString(args)}";
	}

	private static List<string> Hex(IEnumerable<OnboardCommand> commands)
	{
		return commands.Select((OnboardCommand c) => Hex(c.Class, c.Id, c.Size, c.Args)).ToList();
	}

	private static MouseProfile Profile(params (string Id, string Key, string[] Mods)[] buttons)
	{
		MouseProfile p = new MouseProfile
		{
			Name = "Test",
			Dpi = 800,
			DpiStages = new List<int> { 400, 800 },
			DpiActive = 2
		};
		foreach (var (id, key, mods) in buttons)
		{
			p.Buttons[id] = new KeyTarget { Key = key, Mods = mods.ToList() };
		}
		return p;
	}

	// The 02/0C button commands after the two click-swap commands, keyed by button code.
	private static Dictionary<byte, OnboardCommand> ButtonCommands(List<OnboardCommand> list)
	{
		return list.Where((OnboardCommand c) => c.Class == 2 && c.Id == 12 && c.Args[1] > 2).ToDictionary((OnboardCommand c) => c.Args[1]);
	}

	[Fact]
	public void Usage_covers_every_key_name_with_a_distinct_code()
	{
		foreach (string name in Keys2.AllNames)
		{
			Assert.True(OnboardMemory.Usage(name) > 0, name);
		}
		Assert.Equal(Keys2.AllNames.Count, Keys2.AllNames.Select(OnboardMemory.Usage).Distinct().Count());
	}

	[Theory]
	[InlineData("KEY_A", 0x04)]
	[InlineData("KEY_Z", 0x1D)]
	[InlineData("KEY_1", 0x1E)]
	[InlineData("KEY_0", 0x27)]
	[InlineData("KEY_F1", 0x3A)]
	[InlineData("KEY_F12", 0x45)]
	[InlineData("KEY_F13", 0x68)]
	[InlineData("KEY_F24", 0x73)]
	[InlineData("KEY_PRINT_SCREEN", 0x46)]
	[InlineData("KEY_SCROLL_LOCK", 0x47)]
	[InlineData("KEY_PAUSE", 0x48)]
	[InlineData("KEY_INSERT", 0x49)]
	[InlineData("KEY_HOME", 0x4A)]
	[InlineData("KEY_PAGE_UP", 0x4B)]
	[InlineData("KEY_DELETE", 0x4C)]
	[InlineData("KEY_END", 0x4D)]
	[InlineData("KEY_PAGE_DOWN", 0x4E)]
	[InlineData("KEY_NUM_LOCK", 0x53)]
	[InlineData("KEY_NUMPAD_DIVIDE", 0x54)]
	[InlineData("KEY_NUMPAD_MULTIPLY", 0x55)]
	[InlineData("KEY_NUMPAD_MINUS", 0x56)]
	[InlineData("KEY_NUMPAD_PLUS", 0x57)]
	[InlineData("KEY_NUMPAD_1", 0x59)]
	[InlineData("KEY_NUMPAD_9", 0x61)]
	[InlineData("KEY_NUMPAD_0", 0x62)]
	[InlineData("KEY_NUMPAD_PERIOD", 0x63)]
	[InlineData("KEY_MENU", 0x65)]
	[InlineData("KEY_LEFT_WIN", 0xE3)]
	[InlineData("KEY_RIGHT_WIN", 0xE7)]
	[InlineData("KEY_HYPEN", 0x2D)]
	[InlineData("KEY_HYPHEN", 0x2D)]
	[InlineData("DISABLED", 0)]
	[InlineData("MOUSE_MIDDLE", 0)]
	[InlineData("", 0)]
	[InlineData(null, 0)]
	public void Hid_usages(string key, int usage)
	{
		Assert.Equal(usage, OnboardMemory.Usage(key));
	}

	[Fact]
	public void Usage_agrees_with_the_previous_writer_for_every_key_it_knew()
	{
		foreach (string name in Keys2.AllNames)
		{
			int legacy = LegacyOnboard.Usage(name);
			if (legacy != 0)
			{
				Assert.Equal(legacy, OnboardMemory.Usage(name));
			}
		}
	}

	[Fact]
	public void Modifier_mask_includes_the_windows_keys()
	{
		Assert.Equal(0x08, OnboardMemory.ModMask(new[] { "KEY_LEFT_WIN" }));
		Assert.Equal(0x80, OnboardMemory.ModMask(new[] { "KEY_RIGHT_WIN" }));
		Assert.Equal(0x01 | 0x02 | 0x04 | 0x08 | 0x10 | 0x20 | 0x40 | 0x80, OnboardMemory.ModMask(new[]
		{
			"KEY_LEFT_CTRL", "KEY_LEFT_SHIFT", "KEY_LEFT_ALT", "KEY_LEFT_WIN", "KEY_RIGHT_CTRL", "KEY_RIGHT_SHIFT",
			"KEY_RIGHT_ALT", "KEY_RIGHT_WIN"
		}));
		Assert.Equal(0, OnboardMemory.ModMask(new[] { "KEY_A" }));
		Assert.Equal(0, OnboardMemory.ModMask(null));
	}

	// Factory-default commands for the unmapped buttons, keyed by button code.
	private static Dictionary<byte, OnboardCommand> DefaultCommands(MouseProfile p, List<OnboardCommand> list)
	{
		HashSet<int> codes = OnboardMemory.FactoryButtons.Where(d => !p.Buttons.ContainsKey(d.Id)).Select(d => OnboardMemory.ButtonCode(d.Id)).ToHashSet();
		return list.Where((OnboardCommand c) => c.Class == 2 && c.Id == 12 && codes.Contains(c.Args[1])).ToDictionary((OnboardCommand c) => c.Args[1]);
	}

	// Besides the deterministic GUID, the only intended difference from the writer that works on the user's mouse:
	// buttons the profile does not remap get their factory function written (so no slot keeps an old assignment).
	[Fact]
	public void Existing_profiles_produce_the_exact_bytes_of_the_previous_writer()
	{
		var (store, _, dir) = TestData.LoadRealCopy();
		try
		{
			Assert.Equal(47, store.Mouse.Count);
			foreach (MouseProfile p in store.Mouse)
			{
				Assert.Null(p.Lighting);
				Assert.True(p.ClickSwap);
				for (int slot = 1; slot <= 5; slot++)
				{
					List<OnboardCommand> built = OnboardMemory.BuildCommands(slot, p, out List<string> problems);
					List<string> legacy = LegacyOnboard.Commands(slot, p, OnboardMemory.ProfileGuid(p).ToByteArray())
						.Select(c => Hex(c.Class, c.Id, c.Size, c.Args)).ToList();
					Dictionary<byte, OnboardCommand> defaults = DefaultCommands(p, built);
					Assert.Equal(legacy, Hex(built.Where((OnboardCommand c) => !defaults.ContainsValue(c))));
					Assert.Empty(problems);
					Assert.All(built.Where((OnboardCommand c) => !defaults.ContainsValue(c)), (OnboardCommand c) => Assert.Null(c.Unverified));
				}
			}
		}
		finally
		{
			Directory.Delete(dir, recursive: true);
		}
	}

	[Fact]
	public void Unmapped_buttons_get_their_factory_function()
	{
		MouseProfile p = Profile(("Side1", "KEY_W", new string[0]), ("M1", "KEY_LEFT_SHIFT", new string[0]));
		List<OnboardCommand> list = OnboardMemory.BuildCommands(2, p, out List<string> problems);
		Assert.Empty(problems);
		Dictionary<byte, OnboardCommand> buttons = ButtonCommands(list);
		// Side1 keeps its remap; Side2..Side12 send 2..9, 0, -, = (HID usages 0x1F..0x27, 0x2D, 0x2E).
		Assert.Equal(new byte[] { 2, 64, 0, 2, 2, 0, 0x1A }, buttons[64].Args);
		byte[] usages = { 0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27, 0x2D, 0x2E };
		for (int n = 2; n <= 12; n++)
		{
			Assert.Equal(new byte[] { 2, (byte)(63 + n), 0, 2, 2, 0, usages[n - 2] }, buttons[(byte)(63 + n)].Args);
			Assert.True(buttons[(byte)(63 + n)].Required);
		}
		// The wheel click becomes the middle button (not yet confirmed on the mouse, so a rejection is only reported).
		Assert.Equal(new byte[] { 2, 3, 0, 1, 1, 3 }, buttons[3].Args);
		Assert.False(buttons[3].Required);
		// M1 keeps its remap; M2 and the tilts have no known factory code and are not written.
		Assert.Equal(new byte[] { 2, 11, 0, 2, 2, 0, 0xE1 }, buttons[11].Args);
		Assert.False(buttons.ContainsKey(12));
		Assert.False(buttons.ContainsKey(52));
		Assert.False(buttons.ContainsKey(53));
		// A profile that remaps everything writes no defaults.
		MouseProfile full = Profile();
		foreach (var (id, _) in OnboardMemory.FactoryButtons)
		{
			full.Buttons[id] = new KeyTarget { Key = "KEY_A" };
		}
		Assert.Empty(DefaultCommands(full, OnboardMemory.BuildCommands(1, full)));
	}

	[Fact]
	public void Resetting_a_remap_changes_what_is_written()
	{
		// Removing a remap writes the factory key instead of leaving the slot's old assignment.
		MouseProfile p = Profile(("Side8", "KEY_W", new string[0]));
		string remapped = OnboardSlots.ContentHash(p);
		p.Buttons.Remove("Side8");
		Assert.NotEqual(remapped, OnboardSlots.ContentHash(p));
		Assert.Equal(new byte[] { 1, 71, 0, 2, 2, 0, 0x25 }, ButtonCommands(OnboardMemory.BuildCommands(1, p))[71].Args);
	}

	[Fact]
	public void Button_actions_for_keys_disabled_and_middle_click()
	{
		MouseProfile p = Profile(("Side1", "KEY_W", new[] { "KEY_LEFT_CTRL", "KEY_LEFT_WIN" }), ("Side3", "DISABLED", new string[0]),
			("Side4", "MOUSE_MIDDLE", new string[0]), ("Scroll", "KEY_F13", new string[0]));
		List<OnboardCommand> list = OnboardMemory.BuildCommands(3, p, out List<string> problems);
		Assert.Empty(problems);
		Dictionary<byte, OnboardCommand> buttons = ButtonCommands(list);
		// The four remaps, plus the factory keys of the other side buttons (Side2, Side5..Side12).
		Assert.Equal(4 + 9, buttons.Count);
		// Side1 = 64: keyboard type 2, length 2, {mods, usage}.
		Assert.Equal(new byte[] { 3, 64, 0, 2, 2, 0x09, 0x1A }, buttons[64].Args);
		Assert.True(buttons[64].Required);
		Assert.Null(buttons[64].Unverified);
		// Side3 = 66: type 0, length 0.
		Assert.Equal(new byte[] { 3, 66, 0, 0, 0 }, buttons[66].Args);
		Assert.False(buttons[66].Required);
		Assert.Contains("DISABLED", buttons[66].Unverified);
		// Side4 = 67: mouse button type 1, length 1, button 3.
		Assert.Equal(new byte[] { 3, 67, 0, 1, 1, 3 }, buttons[67].Args);
		Assert.False(buttons[67].Required);
		Assert.Contains("MOUSE_MIDDLE", buttons[67].Unverified);
		// Scroll = 3.
		Assert.Equal(new byte[] { 3, 3, 0, 2, 2, 0, 0x68 }, buttons[3].Args);
		Assert.All(buttons.Values, (OnboardCommand c) => Assert.Equal(10, c.Size));
	}

	[Fact]
	public void Click_swap_is_written_only_when_enabled()
	{
		MouseProfile p = Profile();
		List<OnboardCommand> swapped = OnboardMemory.BuildCommands(2, p);
		List<OnboardCommand> click = swapped.Where((OnboardCommand c) => c.Class == 2 && c.Id == 12).ToList();
		Assert.Equal(new byte[] { 2, 1, 0, 1, 1, 2 }, click[0].Args);
		Assert.Equal(new byte[] { 2, 2, 0, 1, 1, 1 }, click[1].Args);
		p.ClickSwap = false;
		click = OnboardMemory.BuildCommands(2, p).Where((OnboardCommand c) => c.Class == 2 && c.Id == 12 && c.Args[1] <= 2).ToList();
		Assert.Equal(new byte[] { 2, 1, 0, 1, 1, 1 }, click[0].Args);
		Assert.Equal(new byte[] { 2, 2, 0, 1, 1, 2 }, click[1].Args);
		Assert.Equal(2, click.Count);
	}

	[Fact]
	public void Lighting_is_stored_with_the_slot_instead_of_the_plain_brightness_write()
	{
		MouseProfile p = Profile(("Side1", "KEY_1", new string[0]));
		p.Lighting = new MouseLightingSettings { Effect = "static", Color = "#112233", Brightness = 50 };
		List<OnboardCommand> list = OnboardMemory.BuildCommands(4, p, out List<string> problems);
		Assert.Empty(problems);
		List<string> hex = Hex(list);
		// Slot select, profile select, 4 info blocks, DPI, DPI stages, then lighting per zone.
		Assert.Equal(new List<string>
		{
			"0F/02 size 9: 040101000001112233", "0F/04 size 3: 040180",
			"0F/02 size 9: 040401000001112233", "0F/04 size 3: 040480",
			"0F/02 size 9: 041001000001112233", "0F/04 size 3: 041080"
		}, hex.Skip(8).Take(6).ToList());
		Assert.DoesNotContain("0F/04 size 3: 0400FF", hex);
		Assert.Equal("02/0C size 10: 040100010102", hex[14]);
		Assert.All(list.Skip(8).Take(6), (OnboardCommand c) => Assert.False(c.Required));
		Assert.Equal("Writing lighting", list[8].Progress);
		Assert.NotNull(list[8].Unverified);

		p.Lighting = new MouseLightingSettings { Effect = "spectrum", Brightness = 100 };
		hex = Hex(OnboardMemory.BuildCommands(4, p));
		Assert.Equal(new List<string>
		{
			"0F/02 size 6: 040103", "0F/04 size 3: 0401FF",
			"0F/02 size 6: 040403", "0F/04 size 3: 0404FF",
			"0F/02 size 6: 041003", "0F/04 size 3: 0410FF"
		}, hex.Skip(8).Take(6).ToList());
	}

	[Fact]
	public void Without_lighting_the_previous_brightness_write_stays()
	{
		MouseProfile p = Profile();
		List<string> hex = Hex(OnboardMemory.BuildCommands(5, p));
		Assert.Equal("0F/04 size 3: 0500FF", hex[8]);
		Assert.Equal(1, hex.Count((string h) => h.StartsWith("0F/")));
	}

	[Fact]
	public void Invalid_lighting_is_reported_and_falls_back_to_the_brightness_write()
	{
		MouseProfile p = Profile();
		p.Lighting = new MouseLightingSettings { Effect = "rainbow" };
		List<string> hex = Hex(OnboardMemory.BuildCommands(5, p, out List<string> problems));
		Assert.Equal("0F/04 size 3: 0500FF", hex[8]);
		Assert.Single(problems);
		Assert.Contains("rainbow", problems[0]);
	}

	[Fact]
	public void Profile_blob_is_deterministic()
	{
		MouseProfile a = Profile(("Side1", "KEY_1", new string[0]), ("Side2", "KEY_2", new string[0]));
		MouseProfile b = Profile(("Side2", "KEY_2", new string[0]), ("Side1", "KEY_1", new string[0]));
		Assert.Equal(Hex(OnboardMemory.BuildCommands(1, a)), Hex(OnboardMemory.BuildCommands(1, a)));
		Assert.Equal(OnboardMemory.ProfileGuid(a), OnboardMemory.ProfileGuid(b));
		Assert.Equal(OnboardMemory.ProfileBlob(a), OnboardMemory.ProfileBlob(b));
		Guid before = OnboardMemory.ProfileGuid(a);
		a.Buttons["Side1"].Key = "KEY_3";
		Assert.NotEqual(before, OnboardMemory.ProfileGuid(a));
		a.Buttons["Side1"].Key = "KEY_1";
		Assert.Equal(before, OnboardMemory.ProfileGuid(a));
		a.ClickSwap = false;
		Assert.NotEqual(before, OnboardMemory.ProfileGuid(a));
		a.ClickSwap = true;
		a.Lighting = new MouseLightingSettings();
		Assert.NotEqual(before, OnboardMemory.ProfileGuid(a));
		// The name follows the GUID in the info block.
		byte[] blob = OnboardMemory.ProfileBlob(a);
		Assert.Equal(250, blob.Length);
		Assert.Equal((byte)'T', blob[16]);
	}

	[Fact]
	public void Unencodable_buttons_are_reported_and_the_rest_is_written()
	{
		MouseProfile p = Profile(("Side1", "KEY_NOPE", new string[0]), ("Side13", "KEY_A", new string[0]),
			("Side2", "KEY_B", new string[0]), ("Side5", "DISABLED", new[] { "KEY_LEFT_CTRL" }));
		p.Buttons["Side6"] = null;
		List<OnboardCommand> list = OnboardMemory.BuildCommands(1, p, out List<string> problems);
		Assert.Equal(4, problems.Count);
		Assert.Contains(problems, (string s) => s.StartsWith("Side1:") && s.Contains("KEY_NOPE"));
		Assert.Contains(problems, (string s) => s.StartsWith("Side13:"));
		Assert.Contains(problems, (string s) => s.StartsWith("Side5:") && s.Contains("KEY_LEFT_CTRL"));
		Assert.Contains(problems, (string s) => s.StartsWith("Side6:"));
		// Side13 does not exist; Side1 and Side6 cannot be written as set, so they get their factory key (1, 6), like
		// the buttons the profile leaves alone. Nothing keeps the slot's old assignment.
		Dictionary<byte, OnboardCommand> buttons = ButtonCommands(list);
		Assert.Equal(new byte[] { 3, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75 }, buttons.Keys.OrderBy((byte b) => b));
		Assert.Equal(new byte[] { 1, 64, 0, 2, 2, 0, 0x1E }, buttons[64].Args);
		Assert.Equal(new byte[] { 1, 65, 0, 2, 2, 0, 0x05 }, buttons[65].Args);
		Assert.Equal(new byte[] { 1, 69, 0, 2, 2, 0, 0x23 }, buttons[69].Args);
		Assert.False(OnboardMemory.CanWrite(p.Buttons["Side1"]));
		Assert.True(OnboardMemory.CanWrite(p.Buttons["Side2"]));
		Assert.True(OnboardMemory.CanWrite(new KeyTarget { Key = "MOUSE_MIDDLE" }));
		Assert.False(OnboardMemory.CanWrite(null));
	}

	[Fact]
	public void Write_sends_the_built_commands_with_rolling_transaction_ids()
	{
		MouseProfile p = Profile(("Side1", "KEY_1", new string[0]), ("Side9", "KEY_NOPE", new string[0]));
		RazerDevice dev = new RazerDevice();
		List<string> progress = new List<string>();
		Assert.Null(OnboardMemory.Write(dev, 3, p, progress.Add));
		Assert.Equal(Hex(OnboardMemory.BuildCommands(3, p)), dev.Sent.Select(s => Hex(s.Class, s.Id, s.Size, s.Args)).ToList());
		Assert.Equal(Enumerable.Range(3, dev.Sent.Count).Select((int i) => (byte)i), dev.Sent.Select(s => s.Txn));
		Assert.Equal(new[] { "Selecting slot", "Writing profile info", "Writing DPI", "Writing buttons" }, progress.Take(4));
		Assert.Contains(progress, (string s) => s.StartsWith("Not written: Side9"));
	}

	[Fact]
	public void Transaction_ids_wrap_after_31()
	{
		MouseProfile p = Profile();
		for (int i = 1; i <= 12; i++)
		{
			p.Buttons["Side" + i] = new KeyTarget { Key = "KEY_" + (char)('A' + i) };
		}
		p.Buttons["Scroll"] = new KeyTarget { Key = "KEY_SPACEBAR" };
		p.Buttons["M1"] = new KeyTarget { Key = "KEY_Q" };
		p.Buttons["M2"] = new KeyTarget { Key = "KEY_E" };
		p.Buttons["TiltL"] = new KeyTarget { Key = "KEY_R" };
		p.Buttons["TiltR"] = new KeyTarget { Key = "KEY_T" };
		RazerDevice dev = new RazerDevice();
		Assert.Null(OnboardMemory.Write(dev, 1, p));
		Assert.Equal(29, dev.Sent.Count);
		Assert.Equal(31, dev.Sent[28].Txn);
		p.Buttons["Side12"] = new KeyTarget { Key = "KEY_Z" };
		p.Buttons["Extra"] = null;
		p.Lighting = new MouseLightingSettings();
		dev = new RazerDevice();
		Assert.Null(OnboardMemory.Write(dev, 1, p));
		Assert.Equal(31, dev.Sent[28].Txn);
		Assert.Equal(0, dev.Sent[29].Txn);
		Assert.Equal(1, dev.Sent[30].Txn);
	}

	[Fact]
	public void Write_stops_when_a_required_command_is_rejected()
	{
		MouseProfile p = Profile(("Side1", "KEY_1", new string[0]));
		RazerDevice dev = new RazerDevice
		{
			RawStatus = (byte cls, byte id, byte[] args) => (cls == 4 && id == 5) ? 3 : 2
		};
		string err = OnboardMemory.Write(dev, 2, p);
		Assert.Equal("mouse rejected command 04/05 (status 03)", err);
		Assert.Equal(4, dev.Sent.Last().Class);
		Assert.DoesNotContain(dev.Sent, s => s.Class == 2);
	}

	[Fact]
	public void Write_goes_on_when_an_unverified_command_is_rejected()
	{
		MouseProfile p = Profile(("Side3", "DISABLED", new string[0]), ("Side4", "KEY_4", new string[0]));
		p.Lighting = new MouseLightingSettings { Effect = "static", Color = "#FFFFFF" };
		RazerDevice dev = new RazerDevice
		{
			RawStatus = (byte cls, byte id, byte[] args) => ((cls == 2 && args.Length == 5) || (cls == 15 && args[1] == 0x10)) ? 5 : 2
		};
		List<string> progress = new List<string>();
		Assert.Null(OnboardMemory.Write(dev, 2, p, progress.Add));
		Assert.Contains(dev.Sent, s => s.Class == 2 && s.Args[1] == 67);
		Assert.Equal(6, dev.Sent.Last().Class);
		Assert.Contains(progress, (string s) => s.Contains("Side3 (DISABLED) rejected by the mouse (status 05)"));
		Assert.Contains(progress, (string s) => s.Contains("lighting (right side) rejected"));
	}

	[Fact]
	public void Write_rejects_bad_arguments_without_sending()
	{
		RazerDevice dev = new RazerDevice();
		Assert.Equal("Naga not found", OnboardMemory.Write(null, 1, Profile()));
		Assert.Equal("Slot must be 1-5", OnboardMemory.Write(dev, 0, Profile()));
		Assert.Equal("Slot must be 1-5", OnboardMemory.Write(dev, 6, Profile()));
		Assert.NotNull(OnboardMemory.Write(dev, 1, null));
		Assert.Empty(dev.Sent);
	}
}
