using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

public class Keys2Tests
{
	// The key list the UI, Keys2 and OnboardMemory.Usage must all agree on.
	internal static IEnumerable<string> ContractNames()
	{
		for (char c = 'A'; c <= 'Z'; c++)
		{
			yield return "KEY_" + c;
		}
		for (char d = '0'; d <= '9'; d++)
		{
			yield return "KEY_" + d;
		}
		for (int f = 1; f <= 24; f++)
		{
			yield return "KEY_F" + f;
		}
		foreach (string n in new[]
		{
			"SPACEBAR", "TAB", "ENTER", "ESCAPE", "BACKSPACE", "CAPS_LOCK", "LEFT_SHIFT", "RIGHT_SHIFT", "LEFT_CTRL",
			"RIGHT_CTRL", "LEFT_ALT", "RIGHT_ALT", "LEFT_WIN", "RIGHT_WIN", "MENU", "GRAVE", "HYPHEN", "EQUAL",
			"OPEN_SQUARE_BRACKET", "CLOSE_SQUARE_BRACKET", "BACKSLASH", "SEMICOLON", "APOSTROPHE", "COMMA", "PERIOD",
			"SLASH", "UP_ARROW", "DOWN_ARROW", "LEFT_ARROW", "RIGHT_ARROW", "INSERT", "DELETE", "HOME", "END", "PAGE_UP",
			"PAGE_DOWN", "PRINT_SCREEN", "SCROLL_LOCK", "PAUSE", "NUM_LOCK", "NUMPAD_PLUS", "NUMPAD_MINUS",
			"NUMPAD_MULTIPLY", "NUMPAD_DIVIDE", "NUMPAD_PERIOD"
		})
		{
			yield return "KEY_" + n;
		}
		for (int n = 0; n <= 9; n++)
		{
			yield return "KEY_NUMPAD_" + n;
		}
	}

	[Fact]
	public void AllNames_is_exactly_the_contract_list()
	{
		Assert.Equal(ContractNames().OrderBy((string s) => s), Keys2.AllNames.OrderBy((string s) => s));
	}

	[Fact]
	public void Every_name_round_trips_through_its_virtual_key()
	{
		foreach (string name in Keys2.AllNames)
		{
			int vk = Keys2.Vk(name);
			Assert.True(vk > 0, name);
			Assert.Equal(name, Keys2.NameFromVk(vk));
		}
	}

	[Fact]
	public void No_two_names_share_a_virtual_key()
	{
		List<IGrouping<int, string>> dupes = (from n in Keys2.AllNames
			group n by Keys2.Vk(n) into g
			where g.Count() > 1
			select g).ToList();
		Assert.Empty(dupes);
		Assert.Equal(Keys2.AllNames.Count, Keys2.AllNames.Distinct().Count());
	}

	[Theory]
	[InlineData("KEY_INSERT", 45)]
	[InlineData("KEY_DELETE", 46)]
	[InlineData("KEY_HOME", 36)]
	[InlineData("KEY_END", 35)]
	[InlineData("KEY_PAGE_UP", 33)]
	[InlineData("KEY_PAGE_DOWN", 34)]
	[InlineData("KEY_PRINT_SCREEN", 44)]
	[InlineData("KEY_SCROLL_LOCK", 145)]
	[InlineData("KEY_PAUSE", 19)]
	[InlineData("KEY_NUM_LOCK", 144)]
	[InlineData("KEY_NUMPAD_0", 96)]
	[InlineData("KEY_NUMPAD_9", 105)]
	[InlineData("KEY_NUMPAD_MULTIPLY", 106)]
	[InlineData("KEY_NUMPAD_PLUS", 107)]
	[InlineData("KEY_NUMPAD_MINUS", 109)]
	[InlineData("KEY_NUMPAD_PERIOD", 110)]
	[InlineData("KEY_NUMPAD_DIVIDE", 111)]
	[InlineData("KEY_LEFT_WIN", 91)]
	[InlineData("KEY_RIGHT_WIN", 92)]
	[InlineData("KEY_MENU", 93)]
	[InlineData("KEY_F1", 112)]
	[InlineData("KEY_F12", 123)]
	[InlineData("KEY_F13", 124)]
	[InlineData("KEY_F24", 135)]
	[InlineData("KEY_W", 87)]
	[InlineData("KEY_0", 48)]
	[InlineData("KEY_SPACEBAR", 32)]
	[InlineData("KEY_LEFT_SHIFT", 160)]
	[InlineData("KEY_HYPHEN", 189)]
	public void Virtual_key_codes(string name, int vk)
	{
		Assert.Equal(vk, Keys2.Vk(name));
	}

	[Fact]
	public void Legacy_and_short_spellings_are_read_but_never_produced()
	{
		Assert.Equal(189, Keys2.Vk("KEY_HYPEN"));
		Assert.Equal(189, Keys2.Vk("HYPEN"));
		Assert.Equal("KEY_HYPHEN", Keys2.NameFromVk(189));
		Assert.Equal(87, Keys2.Vk("W"));
		Assert.Equal(87, Keys2.Vk("key_w"));
	}

	[Theory]
	[InlineData("DISABLED")]
	[InlineData("MOUSE_MIDDLE")]
	[InlineData("")]
	[InlineData(null)]
	[InlineData("KEY_NOPE")]
	public void Non_keys_have_no_virtual_key(string name)
	{
		Assert.Equal(0, Keys2.Vk(name));
	}

	[Fact]
	public void Generic_modifiers_map_to_the_left_key()
	{
		Assert.Equal("KEY_LEFT_SHIFT", Keys2.NameFromVk(16));
		Assert.Equal("KEY_LEFT_CTRL", Keys2.NameFromVk(17));
		Assert.Equal("KEY_LEFT_ALT", Keys2.NameFromVk(18));
		Assert.Null(Keys2.NameFromVk(0));
		Assert.Null(Keys2.NameFromVk(255));
	}

	[Fact]
	public void Extended_flag_is_set_for_exactly_the_E0_keys()
	{
		HashSet<string> extended = new HashSet<string>
		{
			"KEY_UP_ARROW", "KEY_DOWN_ARROW", "KEY_LEFT_ARROW", "KEY_RIGHT_ARROW", "KEY_INSERT", "KEY_DELETE", "KEY_HOME",
			"KEY_END", "KEY_PAGE_UP", "KEY_PAGE_DOWN", "KEY_RIGHT_CTRL", "KEY_RIGHT_ALT", "KEY_PRINT_SCREEN",
			"KEY_NUM_LOCK", "KEY_NUMPAD_DIVIDE", "KEY_LEFT_WIN", "KEY_RIGHT_WIN", "KEY_MENU"
		};
		foreach (string name in Keys2.AllNames)
		{
			Assert.True(extended.Contains(name) == Keys2.IsExt(Keys2.Vk(name)), name);
		}
	}
}
