using System.Collections.Generic;

namespace LiteSynapse;

internal static class Keys2
{
	private static readonly Dictionary<string, int> Named = new Dictionary<string, int>
	{
		["SPACEBAR"] = 32,
		["TAB"] = 9,
		["LEFT_SHIFT"] = 160,
		["RIGHT_SHIFT"] = 161,
		["LEFT_CTRL"] = 162,
		["RIGHT_CTRL"] = 163,
		["LEFT_ALT"] = 164,
		["RIGHT_ALT"] = 165,
		["CAPS_LOCK"] = 20,
		["EQUAL"] = 187,
		["HYPEN"] = 189,
		["HYPHEN"] = 189,
		["OPEN_SQUARE_BRACKET"] = 219,
		["CLOSE_SQUARE_BRACKET"] = 221,
		["SLASH"] = 191,
		["UP_ARROW"] = 38,
		["DOWN_ARROW"] = 40,
		["LEFT_ARROW"] = 37,
		["RIGHT_ARROW"] = 39,
		["ENTER"] = 13,
		["ESCAPE"] = 27,
		["BACKSPACE"] = 8,
		["GRAVE"] = 192,
		["COMMA"] = 188,
		["PERIOD"] = 190,
		["SEMICOLON"] = 186,
		["APOSTROPHE"] = 222,
		["BACKSLASH"] = 220
	};

	private static readonly HashSet<int> ExtendedVk = new HashSet<int>
	{
		38, 40, 37, 39, 163, 165, 45, 46, 36, 35,
		33, 34
	};

	public static int Vk(string name)
	{
		if (string.IsNullOrEmpty(name))
		{
			return 0;
		}
		string n = (name.StartsWith("KEY_") ? name.Substring(4) : name);
		if (Named.TryGetValue(n, out var v))
		{
			return v;
		}
		if (n.Length == 1 && char.IsLetterOrDigit(n[0]))
		{
			return char.ToUpperInvariant(n[0]);
		}
		if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n.Substring(1), out var f) && f >= 1 && f <= 24)
		{
			return 111 + f;
		}
		return 0;
	}

	public static bool IsExt(int vk)
	{
		return ExtendedVk.Contains(vk);
	}

	public static string NameFromVk(int vk)
	{
		vk = vk switch
		{
			16 => 160, 
			17 => 162, 
			18 => 164, 
			_ => vk, 
		};
		foreach (KeyValuePair<string, int> kv in Named)
		{
			if (kv.Value == vk && kv.Key != "HYPHEN")
			{
				return "KEY_" + kv.Key;
			}
		}
		bool flag;
		switch (vk)
		{
		case 48:
		case 49:
		case 50:
		case 51:
		case 52:
		case 53:
		case 54:
		case 55:
		case 56:
		case 57:
		case 65:
		case 66:
		case 67:
		case 68:
		case 69:
		case 70:
		case 71:
		case 72:
		case 73:
		case 74:
		case 75:
		case 76:
		case 77:
		case 78:
		case 79:
		case 80:
		case 81:
		case 82:
		case 83:
		case 84:
		case 85:
		case 86:
		case 87:
		case 88:
		case 89:
		case 90:
			flag = true;
			break;
		default:
			flag = false;
			break;
		}
		if (flag)
		{
			return "KEY_" + (char)vk;
		}
		if (vk >= 112 && vk <= 135)
		{
			return "KEY_F" + (vk - 111);
		}
		return null;
	}
}
