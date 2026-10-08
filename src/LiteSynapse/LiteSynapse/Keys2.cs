using System;
using System.Collections.Generic;

namespace LiteSynapse;

// KEY_* names <-> Windows virtual-key codes. Vk() is called from the keyboard hook, so lookups never allocate.
internal static class Keys2
{
	// Canonical names (without the "KEY_" prefix) and their virtual-key codes. NameFromVk returns these.
	private static readonly (string Name, int Vk)[] Table = BuildTable();

	// Old spelling used by earlier versions and by Synapse; accepted when reading, never produced.
	private static readonly (string Name, int Vk)[] Legacy = new (string, int)[1] { ("HYPEN", 189) };

	// Keys whose scan code needs the extended (E0) flag when injected.
	private static readonly HashSet<int> ExtendedVk = new HashSet<int>
	{
		37, 38, 39, 40, // arrows
		45, 46, 36, 35, 33, 34, // Insert, Delete, Home, End, Page Up, Page Down
		163, 165, // right Ctrl, right Alt
		44, 144, 111, // Print Screen, Num Lock, numpad /
		91, 92, 93 // left Win, right Win, Menu
	};

	// Both "KEY_X" and "X" forms, case-insensitive.
	private static readonly Dictionary<string, int> ByName = BuildByName();

	private static readonly Dictionary<int, string> ByVk = BuildByVk();

	// Every canonical KEY_* name, in table order.
	public static readonly IReadOnlyList<string> AllNames = Array.ConvertAll(Table, (e) => "KEY_" + e.Name);

	private static (string, int)[] BuildTable()
	{
		List<(string, int)> t = new List<(string, int)>();
		for (char c = 'A'; c <= 'Z'; c++)
		{
			t.Add((c.ToString(), c));
		}
		for (char d = '0'; d <= '9'; d++)
		{
			t.Add((d.ToString(), d));
		}
		for (int f = 1; f <= 24; f++)
		{
			t.Add(("F" + f, 111 + f));
		}
		t.AddRange(new (string, int)[]
		{
			("SPACEBAR", 32), ("TAB", 9), ("ENTER", 13), ("ESCAPE", 27), ("BACKSPACE", 8), ("CAPS_LOCK", 20),
			("LEFT_SHIFT", 160), ("RIGHT_SHIFT", 161), ("LEFT_CTRL", 162), ("RIGHT_CTRL", 163),
			("LEFT_ALT", 164), ("RIGHT_ALT", 165), ("LEFT_WIN", 91), ("RIGHT_WIN", 92), ("MENU", 93),
			("GRAVE", 192), ("HYPHEN", 189), ("EQUAL", 187), ("OPEN_SQUARE_BRACKET", 219), ("CLOSE_SQUARE_BRACKET", 221),
			("BACKSLASH", 220), ("SEMICOLON", 186), ("APOSTROPHE", 222), ("COMMA", 188), ("PERIOD", 190), ("SLASH", 191),
			("UP_ARROW", 38), ("DOWN_ARROW", 40), ("LEFT_ARROW", 37), ("RIGHT_ARROW", 39),
			("INSERT", 45), ("DELETE", 46), ("HOME", 36), ("END", 35), ("PAGE_UP", 33), ("PAGE_DOWN", 34),
			("PRINT_SCREEN", 44), ("SCROLL_LOCK", 145), ("PAUSE", 19), ("NUM_LOCK", 144),
			("NUMPAD_PLUS", 107), ("NUMPAD_MINUS", 109), ("NUMPAD_MULTIPLY", 106), ("NUMPAD_DIVIDE", 111), ("NUMPAD_PERIOD", 110)
		});
		for (int n = 0; n <= 9; n++)
		{
			t.Add(("NUMPAD_" + n, 96 + n));
		}
		return t.ToArray();
	}

	private static Dictionary<string, int> BuildByName()
	{
		Dictionary<string, int> d = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, vk) in Table)
		{
			d["KEY_" + name] = vk;
			d[name] = vk;
		}
		foreach (var (name2, vk2) in Legacy)
		{
			d["KEY_" + name2] = vk2;
			d[name2] = vk2;
		}
		return d;
	}

	private static Dictionary<int, string> BuildByVk()
	{
		Dictionary<int, string> d = new Dictionary<int, string>();
		foreach (var (name, vk) in Table)
		{
			d.TryAdd(vk, "KEY_" + name);
		}
		return d;
	}

	// Virtual-key code for "KEY_W" (or "W"); 0 for unknown names, "DISABLED" and "MOUSE_MIDDLE".
	public static int Vk(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return 0;
		}
		if (ByName.TryGetValue(name, out var vk))
		{
			return vk;
		}
		return 0;
	}

	public static bool IsExt(int vk)
	{
		return ExtendedVk.Contains(vk);
	}

	// Canonical KEY_* name for a virtual-key code; generic Shift/Ctrl/Alt map to the left key. null if unknown.
	public static string NameFromVk(int vk)
	{
		vk = vk switch
		{
			16 => 160,
			17 => 162,
			18 => 164,
			_ => vk,
		};
		if (ByVk.TryGetValue(vk, out var name))
		{
			return name;
		}
		return null;
	}
}
