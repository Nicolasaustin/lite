using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LiteSynapse;

internal static class OnboardMemory
{
	public static readonly string[] SlotNames = new string[6] { "", "White", "Red", "Green", "Blue", "Cyan" };

	internal static int ButtonCode(string id)
	{
		switch (id)
		{
		case "Scroll":
			return 3;
		case "M1":
			return 11;
		case "M2":
			return 12;
		case "TiltL":
			return 52;
		case "TiltR":
			return 53;
		default:
		{
			if (id.StartsWith("Side") && int.TryParse(id.Substring(4), out var n) && n >= 1 && n <= 12)
			{
				return 63 + n;
			}
			return 0;
		}
		}
	}

	public static int Usage(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return 0;
		}
		string n = (key.StartsWith("KEY_") ? key.Substring(4) : key);
		if (n.Length == 1)
		{
			char c = n[0];
			if (c >= 'A' && c <= 'Z')
			{
				return 4 + (n[0] - 65);
			}
		}
		if (n.Length == 1)
		{
			char c = n[0];
			if (c >= '1' && c <= '9')
			{
				return 30 + (n[0] - 49);
			}
		}
		if (n == "0")
		{
			return 39;
		}
		if (n.Length >= 2 && n[0] == 'F' && int.TryParse(n.Substring(1), out var f) && f >= 1 && f <= 12)
		{
			return 58 + (f - 1);
		}
		switch (n)
		{
		case "ENTER":
			return 40;
		case "ESCAPE":
			return 41;
		case "BACKSPACE":
			return 42;
		case "TAB":
			return 43;
		case "SPACEBAR":
			return 44;
		case "HYPEN":
		case "HYPHEN":
			return 45;
		case "EQUAL":
			return 46;
		case "OPEN_SQUARE_BRACKET":
			return 47;
		case "CLOSE_SQUARE_BRACKET":
			return 48;
		case "BACKSLASH":
			return 49;
		case "SEMICOLON":
			return 51;
		case "APOSTROPHE":
			return 52;
		case "GRAVE":
			return 53;
		case "COMMA":
			return 54;
		case "PERIOD":
			return 55;
		case "SLASH":
			return 56;
		case "CAPS_LOCK":
			return 57;
		case "RIGHT_ARROW":
			return 79;
		case "LEFT_ARROW":
			return 80;
		case "DOWN_ARROW":
			return 81;
		case "UP_ARROW":
			return 82;
		case "LEFT_CTRL":
			return 224;
		case "LEFT_SHIFT":
			return 225;
		case "LEFT_ALT":
			return 226;
		case "RIGHT_CTRL":
			return 228;
		case "RIGHT_SHIFT":
			return 229;
		case "RIGHT_ALT":
			return 230;
		default:
			return 0;
		}
	}

	internal static int ModMask(IEnumerable<string> mods)
	{
		return mods.Aggregate(0, (int m, string s) => m | (s switch
		{
			"KEY_LEFT_CTRL" => 1, 
			"KEY_LEFT_SHIFT" => 2, 
			"KEY_LEFT_ALT" => 4, 
			"KEY_RIGHT_CTRL" => 16, 
			"KEY_RIGHT_SHIFT" => 32, 
			"KEY_RIGHT_ALT" => 64, 
			_ => 0, 
		}));
	}

	public static string Write(RazerDevice dev, int slot, MouseProfile p, Action<string> progress = null)
	{
		if (dev == null)
		{
			return "Naga not found";
		}
		if ((slot < 1 || slot > 5) ? true : false)
		{
			return "Slot must be 1-5";
		}
		byte txn = 3;
		byte S = (byte)slot;
		string err = null;
		progress?.Invoke("Selecting slot");
		if (!Send(5, 2, 1, new byte[1] { S }))
		{
			return err;
		}
		if (!Send(0, 142, 2, new byte[2] { S, 0 }))
		{
			return err;
		}
		progress?.Invoke("Writing profile info");
		byte[] blob = new byte[250];
		Guid.NewGuid().ToByteArray().CopyTo(blob, 0);
		byte[] name = Encoding.UTF8.GetBytes((p.Name.Length > 40) ? p.Name.Substring(0, 40) : p.Name);
		Array.Copy(name, 0, blob, 16, name.Length);
		for (int off = 0; off < 250; off += 64)
		{
			int len = Math.Min(64, 250 - off);
			byte[] a = new byte[5 + len];
			a[0] = S;
			a[2] = (byte)off;
			a[4] = 250;
			Array.Copy(blob, off, a, 5, len);
			if (!Send(5, 8, (byte)(5 + len), a))
			{
				return err;
			}
		}
		progress?.Invoke("Writing DPI");
		List<int> stages = ((p.DpiStages.Count > 0) ? p.DpiStages : new List<int> { p.Dpi }).Select((int d) => Math.Clamp(d, 100, 20000)).Take(5).ToList();
		int active = Math.Clamp((p.DpiStages.Count <= 0) ? 1 : p.DpiActive, 1, stages.Count);
		int dpi = stages[active - 1];
		if (!Send(4, 5, 7, new byte[7]
		{
			S,
			(byte)(dpi >> 8),
			(byte)dpi,
			(byte)(dpi >> 8),
			(byte)dpi,
			0,
			0
		}))
		{
			return err;
		}
		List<byte> st = new List<byte>
		{
			S,
			(byte)active,
			(byte)stages.Count
		};
		for (int i = 0; i < stages.Count; i++)
		{
			st.AddRange(new byte[7]
			{
				(byte)(i + 1),
				(byte)(stages[i] >> 8),
				(byte)stages[i],
				(byte)(stages[i] >> 8),
				(byte)stages[i],
				0,
				0
			});
		}
		if (!Send(4, 6, (byte)st.Count, st.ToArray()))
		{
			return err;
		}
		if (!Send(15, 4, 3, new byte[3] { S, 0, 255 }))
		{
			return err;
		}
		byte[] array = new byte[6] { 0, 1, 0, 1, 1, 2 };
		array[0] = S;
		if (!Send(2, 12, 10, array))
		{
			return err;
		}
		byte[] array2 = new byte[6] { 0, 2, 0, 1, 1, 1 };
		array2[0] = S;
		if (!Send(2, 12, 10, array2))
		{
			return err;
		}
		progress?.Invoke("Writing buttons");
		foreach (var (id, t) in p.Buttons)
		{
			int code = ButtonCode(id);
			int use = Usage(t.Key);
			if (code != 0 && use != 0 && !Send(2, 12, 10, new byte[7]
			{
				S,
				(byte)code,
				0,
				2,
				2,
				(byte)ModMask(t.Mods),
				(byte)use
			}))
			{
				return err;
			}
		}
		Send(6, 142, 14, new byte[14]);
		return null;
		bool Send(byte cls, byte b, byte size, byte[] args)
		{
			int st2 = dev.Raw(txn++, cls, b, size, args);
			if (txn > 31)
			{
				txn = 0;
			}
			if (st2 != 2)
			{
				err = $"mouse rejected command {cls:X2}/{b:X2} (status {st2:X2})";
				return false;
			}
			return true;
		}
	}
}
