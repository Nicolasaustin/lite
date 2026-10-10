using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace LiteSynapse;

// One Razer feature report sent while writing an onboard slot.
internal sealed record OnboardCommand(byte Class, byte Id, byte Size, byte[] Args)
{
	// Progress text shown before this command is sent.
	public string Progress { get; init; }

	// true: a rejection aborts the write (the original behaviour). false: the write goes on.
	public bool Required { get; init; } = true;

	// For a command that is not required: reported when the mouse rejects it (null = ignored silently).
	public string Label { get; init; }

	// Set on encodings nobody has confirmed on the mouse yet; logged with the mouse's answer on every write.
	public string Unverified { get; init; }
}

internal static class OnboardMemory
{
	public static readonly string[] SlotNames = new string[6] { "", "White", "Red", "Green", "Blue", "Cyan" };

	// What a button does when the profile does not remap it: the Naga's factory function. Synapse's out-of-box
	// profile has no mappings at all and names the side buttons by these keys (KEY_1 .. KEY_EQUAL). Written for every
	// unmapped button, so a slot never keeps the previous profile's assignment. The tilts and the two top buttons
	// are missing on purpose: their factory codes (horizontal scroll, DPI stage up/down) are not known, so an
	// unmapped one is left as the slot has it.
	public static readonly IReadOnlyList<(string Id, string Key)> FactoryButtons = new (string, string)[13]
	{
		("Side1", "KEY_1"), ("Side2", "KEY_2"), ("Side3", "KEY_3"), ("Side4", "KEY_4"), ("Side5", "KEY_5"),
		("Side6", "KEY_6"), ("Side7", "KEY_7"), ("Side8", "KEY_8"), ("Side9", "KEY_9"), ("Side10", "KEY_0"),
		("Side11", "KEY_HYPHEN"), ("Side12", "KEY_EQUAL"), ("Scroll", KeyTarget.MouseMiddle)
	};

	private static readonly Dictionary<string, int> Usages = BuildUsages();

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
			if (id != null && id.StartsWith("Side") && int.TryParse(id.Substring(4), out var n) && n >= 1 && n <= 12)
			{
				return 63 + n;
			}
			return 0;
		}
		}
	}

	// HID keyboard usage IDs (USB HID Usage Tables, page 0x07) for every KEY_* name.
	private static Dictionary<string, int> BuildUsages()
	{
		List<(string, int)> u = new List<(string, int)>();
		for (char c = 'A'; c <= 'Z'; c++)
		{
			u.Add((c.ToString(), 0x04 + (c - 'A')));
		}
		for (int d = 1; d <= 9; d++)
		{
			u.Add((d.ToString(), 0x1E + (d - 1)));
		}
		u.Add(("0", 0x27));
		for (int f = 1; f <= 12; f++)
		{
			u.Add(("F" + f, 0x3A + (f - 1)));
		}
		for (int f2 = 13; f2 <= 24; f2++)
		{
			u.Add(("F" + f2, 0x68 + (f2 - 13)));
		}
		for (int n = 1; n <= 9; n++)
		{
			u.Add(("NUMPAD_" + n, 0x59 + (n - 1)));
		}
		u.AddRange(new (string, int)[]
		{
			("ENTER", 0x28), ("ESCAPE", 0x29), ("BACKSPACE", 0x2A), ("TAB", 0x2B), ("SPACEBAR", 0x2C),
			("HYPHEN", 0x2D), ("HYPEN", 0x2D), ("EQUAL", 0x2E), ("OPEN_SQUARE_BRACKET", 0x2F), ("CLOSE_SQUARE_BRACKET", 0x30),
			("BACKSLASH", 0x31), ("SEMICOLON", 0x33), ("APOSTROPHE", 0x34), ("GRAVE", 0x35), ("COMMA", 0x36),
			("PERIOD", 0x37), ("SLASH", 0x38), ("CAPS_LOCK", 0x39),
			("PRINT_SCREEN", 0x46), ("SCROLL_LOCK", 0x47), ("PAUSE", 0x48), ("INSERT", 0x49), ("HOME", 0x4A),
			("PAGE_UP", 0x4B), ("DELETE", 0x4C), ("END", 0x4D), ("PAGE_DOWN", 0x4E),
			("RIGHT_ARROW", 0x4F), ("LEFT_ARROW", 0x50), ("DOWN_ARROW", 0x51), ("UP_ARROW", 0x52),
			("NUM_LOCK", 0x53), ("NUMPAD_DIVIDE", 0x54), ("NUMPAD_MULTIPLY", 0x55), ("NUMPAD_MINUS", 0x56),
			("NUMPAD_PLUS", 0x57), ("NUMPAD_0", 0x62), ("NUMPAD_PERIOD", 0x63), ("MENU", 0x65),
			("LEFT_CTRL", 0xE0), ("LEFT_SHIFT", 0xE1), ("LEFT_ALT", 0xE2), ("LEFT_WIN", 0xE3),
			("RIGHT_CTRL", 0xE4), ("RIGHT_SHIFT", 0xE5), ("RIGHT_ALT", 0xE6), ("RIGHT_WIN", 0xE7)
		});
		Dictionary<string, int> d2 = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		foreach (var (name, usage) in u)
		{
			d2["KEY_" + name] = usage;
			d2[name] = usage;
		}
		return d2;
	}

	// HID usage for "KEY_W" (or "W"); 0 when the name is not a keyboard key.
	public static int Usage(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return 0;
		}
		if (Usages.TryGetValue(key, out var usage))
		{
			return usage;
		}
		return 0;
	}

	// Modifier bit for one modifier name; 0 when unknown.
	internal static int ModBit(string mod)
	{
		return mod switch
		{
			"KEY_LEFT_CTRL" => 0x01,
			"KEY_LEFT_SHIFT" => 0x02,
			"KEY_LEFT_ALT" => 0x04,
			"KEY_LEFT_WIN" => 0x08,
			"KEY_RIGHT_CTRL" => 0x10,
			"KEY_RIGHT_SHIFT" => 0x20,
			"KEY_RIGHT_ALT" => 0x40,
			"KEY_RIGHT_WIN" => 0x80,
			_ => 0,
		};
	}

	internal static int ModMask(IEnumerable<string> mods)
	{
		return (mods ?? Enumerable.Empty<string>()).Aggregate(0, (int m, string s) => m | ModBit(s));
	}

	// The action part of a 02/0C button assignment ({type, length, data...}), or null when the target cannot be
	// written. problem says why; unverified is set for encodings not yet confirmed on the mouse.
	internal static byte[] Action(KeyTarget t, out string unverified, out string problem)
	{
		unverified = null;
		problem = null;
		if (t == null || string.IsNullOrEmpty(t.Key))
		{
			problem = "no key";
			return null;
		}
		if (t.Key == KeyTarget.Disabled)
		{
			// Unverified: "no action" = type 0, length 0 (only the keyboard and mouse-button types are confirmed).
			unverified = "DISABLED as action type 0";
			return new byte[2];
		}
		if (t.Key == KeyTarget.MouseMiddle)
		{
			// Unverified: mouse button 3 = middle (1 = left and 2 = right are confirmed by the click-swap writes).
			unverified = "MOUSE_MIDDLE as mouse button 3";
			return new byte[3] { 1, 1, 3 };
		}
		int usage = Usage(t.Key);
		if (usage == 0)
		{
			problem = "key " + t.Key + " has no onboard code";
			return null;
		}
		return new byte[4]
		{
			2,
			2,
			(byte)ModMask(t.Mods),
			(byte)usage
		};
	}

	// True when Write can put this target on the mouse (a known key, DISABLED or MOUSE_MIDDLE).
	public static bool CanWrite(KeyTarget t)
	{
		return Action(t, out _, out _) != null;
	}

	// Everything Write sends for one slot, in order. Buttons that cannot be written are listed in problems.
	internal static List<OnboardCommand> BuildCommands(int slot, MouseProfile p, out List<string> problems)
	{
		if (slot < 1 || slot > 5)
		{
			throw new ArgumentOutOfRangeException(nameof(slot), "Slot must be 1-5");
		}
		problems = new List<string>();
		byte S = (byte)slot;
		List<OnboardCommand> list = new List<OnboardCommand>
		{
			new OnboardCommand(5, 2, 1, new byte[1] { S }) { Progress = "Selecting slot" },
			new OnboardCommand(0, 142, 2, new byte[2] { S, 0 })
		};
		byte[] blob = ProfileBlob(p);
		for (int off = 0; off < 250; off += 64)
		{
			int len = Math.Min(64, 250 - off);
			byte[] a = new byte[5 + len];
			a[0] = S;
			a[2] = (byte)off;
			a[4] = 250;
			Array.Copy(blob, off, a, 5, len);
			list.Add(new OnboardCommand(5, 8, (byte)(5 + len), a) { Progress = (off == 0) ? "Writing profile info" : null });
		}
		List<int> stages = DpiStages(p, out int active);
		int dpi = stages[active - 1];
		list.Add(new OnboardCommand(4, 5, 7, new byte[7]
		{
			S,
			(byte)(dpi >> 8),
			(byte)dpi,
			(byte)(dpi >> 8),
			(byte)dpi,
			0,
			0
		}) { Progress = "Writing DPI" });
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
		list.Add(new OnboardCommand(4, 6, (byte)st.Count, st.ToArray()));
		string lightingProblem = MouseLighting.Validate(p.Lighting);
		if (p.Lighting != null && lightingProblem == null)
		{
			// Lighting stored with the slot (storage = slot number, like the brightness write below).
			// A rejected zone is reported; it does not stop the buttons from being written.
			bool first = true;
			foreach (MouseLighting.Cmd c in MouseLighting.Commands(p.Lighting, S))
			{
				list.Add(new OnboardCommand(c.Class, c.Id, c.Size, c.Args)
				{
					Progress = first ? "Writing lighting" : null,
					Required = false,
					Label = "lighting (" + c.Zone + ")",
					// Unverified: that 0F/02 takes the slot number as storage (only 0F/04 brightness is confirmed).
					Unverified = first ? "lighting stored with storage = slot" : null
				});
				first = false;
			}
		}
		else
		{
			if (lightingProblem != null)
			{
				problems.Add("lighting: " + lightingProblem);
			}
			list.Add(new OnboardCommand(15, 4, 3, new byte[3] { S, 0, 255 }));
		}
		// Left-handed click swap: physical left sends right click (2) and physical right sends left click (1).
		byte leftSends = (byte)(p.ClickSwap ? 2 : 1);
		byte rightSends = (byte)(p.ClickSwap ? 1 : 2);
		list.Add(new OnboardCommand(2, 12, 10, new byte[6] { S, 1, 0, 1, 1, leftSends }));
		list.Add(new OnboardCommand(2, 12, 10, new byte[6] { S, 2, 0, 1, 1, rightSends }));
		bool firstButton = true;
		Dictionary<string, KeyTarget> mapped = p.Buttons ?? new Dictionary<string, KeyTarget>();
		IEnumerable<(string, KeyTarget)> buttons = mapped.Select((KeyValuePair<string, KeyTarget> kv) => (kv.Key, kv.Value))
			.Concat(from d in FactoryButtons
				where !mapped.ContainsKey(d.Id)
				select (d.Id, new KeyTarget
				{
					Key = d.Key
				}));
		foreach (var (id, target) in buttons)
		{
			KeyTarget t = target;
			int code = ButtonCode(id);
			if (code == 0)
			{
				problems.Add(id + ": unknown button");
				continue;
			}
			byte[] action = Action(t, out var unverified, out var problem);
			if (action == null)
			{
				problems.Add(id + ": " + problem);
				// Not writable: the button gets its factory function rather than keeping the slot's old one.
				string factory = FactoryButtons.FirstOrDefault(((string Id, string Key) d) => d.Id == id).Key;
				if (factory == null)
				{
					continue;
				}
				t = new KeyTarget
				{
					Key = factory
				};
				action = Action(t, out unverified, out _);
			}
			foreach (string mod in t.Mods ?? new List<string>())
			{
				if (ModBit(mod) == 0 || action[0] != 2)
				{
					problems.Add($"{id}: modifier {mod} ignored");
				}
			}
			byte[] args = new byte[3 + action.Length];
			args[0] = S;
			args[1] = (byte)code;
			action.CopyTo(args, 3);
			list.Add(new OnboardCommand(2, 12, 10, args)
			{
				Progress = firstButton ? "Writing buttons" : null,
				Required = (unverified == null),
				Label = (unverified == null) ? null : (id + " (" + t.Key + ")"),
				Unverified = (unverified == null) ? null : (id + ": " + unverified)
			});
			firstButton = false;
		}
		list.Add(new OnboardCommand(6, 142, 14, new byte[14])
		{
			Progress = firstButton ? "Writing buttons" : null,
			Required = false
		});
		return list;
	}

	// The DPI stages a slot write stores for p, and which of them is active (1-based). ActiveDpi is the DPI the
	// mouse runs once that slot is active, so nothing has to be sent after a switch.
	internal static List<int> DpiStages(MouseProfile p, out int active)
	{
		List<int> dpiStages = p.DpiStages ?? new List<int>();
		List<int> stages = ((dpiStages.Count > 0) ? dpiStages : new List<int> { p.Dpi }).Select((int d) => Math.Clamp(d, 100, 20000)).Take(5).ToList();
		active = Math.Clamp((dpiStages.Count <= 0) ? 1 : p.DpiActive, 1, stages.Count);
		return stages;
	}

	public static int ActiveDpi(MouseProfile p)
	{
		List<int> stages = DpiStages(p, out int active);
		return stages[active - 1];
	}

	internal static List<OnboardCommand> BuildCommands(int slot, MouseProfile p)
	{
		return BuildCommands(slot, p, out _);
	}

	// Profile info block: 16-byte GUID, then the name (UTF-8, at most 40 characters).
	internal static byte[] ProfileBlob(MouseProfile p)
	{
		byte[] blob = new byte[250];
		ProfileGuid(p).ToByteArray().CopyTo(blob, 0);
		string name = p.Name ?? "";
		byte[] nameBytes = Encoding.UTF8.GetBytes((name.Length > 40) ? name.Substring(0, 40) : name);
		Array.Copy(nameBytes, 0, blob, 16, nameBytes.Length);
		return blob;
	}

	// Same profile content -> same GUID, so rewriting an unchanged profile writes identical bytes.
	internal static Guid ProfileGuid(MouseProfile p)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Fingerprint(p)));
		byte[] g = new byte[16];
		Array.Copy(hash, g, 16);
		g[7] = (byte)((g[7] & 0x0F) | 0x50); // version 5 style (name-based)
		g[8] = (byte)((g[8] & 0x3F) | 0x80); // RFC 4122 variant
		return new Guid(g);
	}

	// Everything Write puts on the mouse, as one string (button order does not matter).
	internal static string Fingerprint(MouseProfile p)
	{
		StringBuilder sb = new StringBuilder();
		sb.Append(p.Name).Append('\n');
		sb.Append(p.PollingRate).Append('\n');
		sb.Append(p.Dpi).Append('\n');
		sb.Append(string.Join(",", p.DpiStages ?? new List<int>())).Append('\n');
		sb.Append(p.DpiActive).Append('\n');
		sb.Append(p.ClickSwap ? "swap" : "noswap").Append('\n');
		if (p.Lighting != null)
		{
			sb.Append(p.Lighting.Effect).Append(',').Append(p.Lighting.Color).Append(',').Append(p.Lighting.Brightness);
		}
		sb.Append('\n');
		foreach (KeyValuePair<string, KeyTarget> kv in (p.Buttons ?? new Dictionary<string, KeyTarget>()).OrderBy((KeyValuePair<string, KeyTarget> k) => k.Key, StringComparer.Ordinal))
		{
			sb.Append(kv.Key).Append('=').Append(string.Join("+", kv.Value?.Mods ?? new List<string>())).Append('+').Append(kv.Value?.Key).Append(';');
		}
		return sb.ToString();
	}

	// abort: asked before every command; true stops the write (the slot then stays marked unknown, so a half
	// write is never trusted). A pre-write passes "a game came to the front".
	public static string Write(RazerDevice dev, int slot, MouseProfile p, Action<string> progress = null, Func<bool> abort = null)
	{
		if (dev == null)
		{
			return "Naga not found";
		}
		if (slot < 1 || slot > 5)
		{
			return "Slot must be 1-5";
		}
		if (p == null)
		{
			return "no profile";
		}
		List<OnboardCommand> commands = BuildCommands(slot, p, out var problems);
		byte txn = 3;
		foreach (OnboardCommand c in commands)
		{
			if (abort != null && abort())
			{
				return "stopped: a game came to the front";
			}
			if (c.Progress != null)
			{
				progress?.Invoke(c.Progress);
			}
			int status = dev.Raw(txn++, c.Class, c.Id, c.Size, c.Args);
			if (txn > 31)
			{
				txn = 0;
			}
			bool ok = status == 2;
			if (c.Unverified != null)
			{
				Cfg.Log($"onboard slot {slot}: {c.Unverified}: " + (ok ? "accepted" : $"rejected (status {status:X2})"));
			}
			if (ok)
			{
				continue;
			}
			if (c.Required)
			{
				return $"mouse rejected command {c.Class:X2}/{c.Id:X2} (status {status:X2})";
			}
			if (c.Label != null)
			{
				problems.Add($"{c.Label} rejected by the mouse (status {status:X2})");
			}
		}
		foreach (string problem in problems)
		{
			progress?.Invoke("Not written: " + problem);
			Cfg.Log($"onboard slot {slot} \"{p.Name}\": not written: {problem}");
		}
		return null;
	}
}
