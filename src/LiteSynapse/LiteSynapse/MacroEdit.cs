using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace LiteSynapse;

// The macro editor's logic without the window: the words for a step, text as key steps, a recording as steps, the
// names a macro may take, the keys it is bound to across the keyboard profiles, rename and delete with those
// bindings, and the export file. Tested; MacroPage only draws and calls.
internal static class MacroEdit
{
	public const int MouseLeft = 1;

	public const int MouseRight = 2;

	public const int MouseMiddle = 3;

	public const int MouseBack = 4;

	public const int MouseForward = 5;

	// MacroStep.Mouse -> words (index 0 unused).
	public static readonly string[] MouseNames = new string[6] { "", "Left click", "Right click", "Middle click", "Back (X1)", "Forward (X2)" };

	// The keyboard keys a macro can be bound to (KbProfile.MacroKeys), in the order the pages show them.
	public static readonly (string Key, string Label)[] BindKeys = new (string, string)[4]
	{
		("KEY_UP_ARROW", "UP ARROW"),
		("KEY_DOWN_ARROW", "DOWN ARROW"),
		("KEY_LEFT_ARROW", "LEFT ARROW"),
		("KEY_RIGHT_ARROW", "RIGHT ARROW")
	};

	// How a recording's timing is kept: as recorded, dropped, or a fixed gap between every step.
	public const string DelaysRecorded = "recorded";

	public const string DelaysNone = "none";

	public const string DelaysFixed = "fixed";

	// Waits longer than this in a recording are cut to it: a pause to think is not part of the macro.
	public const int MaxRecordedDelayMs = 10000;

	public static string MouseName(int button)
	{
		return (button >= 1 && button < MouseNames.Length) ? MouseNames[button] : ("Mouse " + button);
	}

	// "W", "Numpad Enter", "Left Shift"... for a key step.
	public static string KeyLabel(int vk, bool ext)
	{
		if (vk == 13 && ext)
		{
			return "Numpad Enter";
		}
		string n = Keys2.NameFromVk(vk);
		return (n == null) ? ("Key " + vk) : Pretty(n);
	}

	// "KEY_LEFT_ARROW" -> "Left Arrow" (the same words Ui.PrettyKey shows on the device pages).
	public static string Pretty(string keyName)
	{
		if (string.IsNullOrEmpty(keyName))
		{
			return "";
		}
		if (!keyName.StartsWith("KEY_", StringComparison.Ordinal))
		{
			return keyName;
		}
		string words = string.Join(" ", keyName.Substring(4).Split('_')
			.Select((string w) => (w.Length <= 1) ? w : (char.ToUpperInvariant(w[0]) + w.Substring(1).ToLowerInvariant())));
		return words.Replace("Hypen", "-").Replace("Equal", "=").Replace("Spacebar", "Space")
			.Replace("Open Square Bracket", "[").Replace("Close Square Bracket", "]");
	}

	// "PRESS" / "RELEASE" / "WAIT": the step's kind, as the list's tag column.
	public static string Tag(MacroStep s)
	{
		if (s == null || !s.IsEvent)
		{
			return "WAIT";
		}
		return s.Down ? "PRESS" : "RELEASE";
	}

	// What the step does, in words: "W", "Left click", "50 ms".
	public static string What(MacroStep s)
	{
		if (s == null)
		{
			return "";
		}
		if (s.Mouse != 0)
		{
			return MouseName(s.Mouse);
		}
		if (s.Vk != 0)
		{
			return KeyLabel(s.Vk, s.Ext);
		}
		return s.DelayMs + " ms";
	}

	// The whole step on one line: "Press W", "Release Left click", "Wait 50 ms".
	public static string Describe(MacroStep s)
	{
		if (s == null)
		{
			return "";
		}
		if (!s.IsEvent)
		{
			return "Wait " + s.DelayMs + " ms";
		}
		return (s.Down ? "Press " : "Release ") + What(s);
	}

	// How long the macro takes: every wait, since steps with no wait go out together.
	public static int DurationMs(IEnumerable<MacroStep> steps)
	{
		int ms = 0;
		foreach (MacroStep s in steps ?? Enumerable.Empty<MacroStep>())
		{
			if (s != null && s.DelayMs > 0)
			{
				ms += s.DelayMs;
			}
		}
		return ms;
	}

	public static string DurationText(int ms)
	{
		return (ms / 1000.0).ToString("0.000", CultureInfo.InvariantCulture) + " s";
	}

	public static MacroStep KeyStep(int vk, bool ext, bool down, int delayMs = 0)
	{
		return new MacroStep { Vk = vk, Ext = ext, Down = down, DelayMs = delayMs };
	}

	public static MacroStep MouseStep(int button, bool down, int delayMs = 0)
	{
		return new MacroStep { Mouse = button, Down = down, DelayMs = delayMs };
	}

	public static MacroStep WaitStep(int ms)
	{
		return new MacroStep { DelayMs = Math.Max(0, ms) };
	}

	// A press and a release of the same key or button, with gapMs between them (0: both in one send).
	public static List<MacroStep> Tap(MacroStep down, int gapMs)
	{
		MacroStep up = new MacroStep { Vk = down.Vk, Ext = down.Ext, Mouse = down.Mouse, Down = false };
		down.Down = true;
		down.DelayMs = Math.Max(0, gapMs);
		return new List<MacroStep> { down, up };
	}

	// Text as key steps on a US layout: letters, digits, space, Enter, Tab and the punctuation on the keyboard,
	// each as press and release (with Left Shift held for the shifted ones), gapMs after every character but the
	// last. Characters the layout does not have are skipped.
	public static List<MacroStep> TextSteps(string text, int gapMs)
	{
		List<MacroStep> steps = new List<MacroStep>();
		List<(int Vk, bool Shift)> keys = new List<(int, bool)>();
		foreach (char c in text ?? "")
		{
			if (CharKey(c, out int vk, out bool shift))
			{
				keys.Add((vk, shift));
			}
		}
		for (int i = 0; i < keys.Count; i++)
		{
			(int vk, bool shift) = keys[i];
			bool ext = Keys2.IsExt(vk);
			if (shift)
			{
				steps.Add(KeyStep(160, ext: false, down: true));
			}
			steps.Add(KeyStep(vk, ext, down: true));
			steps.Add(KeyStep(vk, ext, down: false));
			if (shift)
			{
				steps.Add(KeyStep(160, ext: false, down: false));
			}
			if (i < keys.Count - 1 && gapMs > 0)
			{
				steps[steps.Count - 1].DelayMs = gapMs;
			}
		}
		return steps;
	}

	// The key (and whether Shift is held) that types c on a US layout.
	internal static bool CharKey(char c, out int vk, out bool shift)
	{
		vk = 0;
		shift = false;
		if (c >= 'a' && c <= 'z')
		{
			vk = char.ToUpperInvariant(c);
			return true;
		}
		if (c >= 'A' && c <= 'Z')
		{
			vk = c;
			shift = true;
			return true;
		}
		if (c >= '0' && c <= '9')
		{
			vk = c;
			return true;
		}
		switch (c)
		{
		case ' ': vk = 32; return true;
		case '\n': vk = 13; return true;
		case '\t': vk = 9; return true;
		case '\r': return false;
		}
		const string shifted = ")!@#$%^&*(";
		int digit = shifted.IndexOf(c);
		if (digit >= 0)
		{
			vk = '0' + digit;
			shift = true;
			return true;
		}
		(char Plain, char Shift, int Vk)[] oem = new (char, char, int)[11]
		{
			('-', '_', 189), ('=', '+', 187), ('[', '{', 219), (']', '}', 221), ('\\', '|', 220), (';', ':', 186),
			('\'', '"', 222), (',', '<', 188), ('.', '>', 190), ('/', '?', 191), ('`', '~', 192)
		};
		foreach ((char plain, char sh, int code) in oem)
		{
			if (c == plain || c == sh)
			{
				vk = code;
				shift = c == sh;
				return true;
			}
		}
		return false;
	}

	// One key or mouse event of a recording, with when it happened.
	public readonly struct Recorded
	{
		public readonly int Vk;

		public readonly bool Ext;

		public readonly int Mouse;

		public readonly bool Down;

		public readonly long TimeMs;

		public Recorded(int vk, bool ext, int mouse, bool down, long timeMs)
		{
			Vk = vk;
			Ext = ext;
			Mouse = mouse;
			Down = down;
			TimeMs = timeMs;
		}
	}

	// A recording as steps. delays: DelaysRecorded keeps the time between events (cut to MaxRecordedDelayMs),
	// DelaysNone drops it (everything goes out at once, in order), DelaysFixed puts fixedMs between every event.
	public static List<MacroStep> FromRecording(IList<Recorded> events, string delays, int fixedMs)
	{
		List<MacroStep> steps = new List<MacroStep>();
		if (events == null)
		{
			return steps;
		}
		for (int i = 0; i < events.Count; i++)
		{
			Recorded e = events[i];
			MacroStep s = (e.Mouse != 0) ? MouseStep(e.Mouse, e.Down) : KeyStep(e.Vk, e.Ext, e.Down);
			if (i < events.Count - 1)
			{
				switch (delays)
				{
				case DelaysRecorded:
					s.DelayMs = (int)Math.Min(MaxRecordedDelayMs, Math.Max(0L, events[i + 1].TimeMs - e.TimeMs));
					break;
				case DelaysFixed:
					s.DelayMs = Math.Max(0, fixedMs);
					break;
				default:
					s.DelayMs = 0;
					break;
				}
			}
			steps.Add(s);
		}
		return steps;
	}

	// A name not in use: wanted, else "wanted (2)", "wanted (3)", ...
	public static string UniqueName(IEnumerable<string> existing, string wanted)
	{
		HashSet<string> taken = new HashSet<string>(existing ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
		string baseName = string.IsNullOrWhiteSpace(wanted) ? "Macro" : wanted.Trim();
		if (!taken.Contains(baseName))
		{
			return baseName;
		}
		for (int n = 2; ; n++)
		{
			string candidate = baseName + " (" + n + ")";
			if (!taken.Contains(candidate))
			{
				return candidate;
			}
		}
	}

	// Why a name cannot be used, or null.
	public static string NameProblem(IEnumerable<string> existing, string name, string allowed = null)
	{
		if (string.IsNullOrWhiteSpace(name))
		{
			return "A macro needs a name.";
		}
		string n = name.Trim();
		if (n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
		{
			return "A macro name cannot contain \\ / : * ? \" < > |.";
		}
		if (allowed != null && string.Equals(n, allowed, StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		if ((existing ?? Enumerable.Empty<string>()).Any((string e) => string.Equals(e, n, StringComparison.OrdinalIgnoreCase)))
		{
			return "There is already a macro called \"" + n + "\".";
		}
		return null;
	}

	// Where a macro is bound: "UP ARROW in \"gamer - S3\"", for every keyboard profile.
	public static List<string> Bindings(ProfileStore store, string macro)
	{
		List<string> found = new List<string>();
		foreach (KbProfile p in store?.Keyboard ?? new List<KbProfile>())
		{
			foreach (KeyValuePair<string, string> kv in p?.MacroKeys ?? new Dictionary<string, string>())
			{
				if (string.Equals(kv.Value, macro, StringComparison.Ordinal))
				{
					found.Add(BindLabel(kv.Key) + " in \"" + p.Name + "\"");
				}
			}
		}
		return found;
	}

	public static string BindLabel(string key)
	{
		foreach ((string k, string label) in BindKeys)
		{
			if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
			{
				return label;
			}
		}
		return Pretty(key);
	}

	// Renames the macro and every binding that names it. The bindings moved, for the message.
	public static List<string> Rename(ProfileStore store, string oldName, string newName)
	{
		List<string> moved = new List<string>();
		Macro m = store.Macros.FirstOrDefault((Macro x) => x.Name == oldName);
		if (m == null)
		{
			return moved;
		}
		m.Name = newName;
		foreach (KbProfile p in store.Keyboard ?? new List<KbProfile>())
		{
			foreach (string key in (p?.MacroKeys ?? new Dictionary<string, string>()).Keys.ToList())
			{
				if (string.Equals(p.MacroKeys[key], oldName, StringComparison.Ordinal))
				{
					p.MacroKeys[key] = newName;
					moved.Add(BindLabel(key) + " in \"" + p.Name + "\"");
				}
			}
		}
		return moved;
	}

	// Removes the macro and every binding that names it. The bindings removed, for the message.
	public static List<string> Delete(ProfileStore store, string name)
	{
		List<string> removed = Bindings(store, name);
		store.Macros.RemoveAll((Macro x) => x.Name == name);
		foreach (KbProfile p in store.Keyboard ?? new List<KbProfile>())
		{
			foreach (string key in (p?.MacroKeys ?? new Dictionary<string, string>()).Keys.ToList())
			{
				if (string.Equals(p.MacroKeys[key], name, StringComparison.Ordinal))
				{
					p.MacroKeys.Remove(key);
				}
			}
		}
		return removed;
	}

	// A copy to edit, so REVERT can go back to what is stored.
	public static Macro Copy(Macro m, string name = null)
	{
		return new Macro
		{
			Name = name ?? m?.Name,
			Steps = (m?.Steps ?? new List<MacroStep>()).Select((MacroStep s) => new MacroStep
			{
				Vk = s.Vk,
				Ext = s.Ext,
				Down = s.Down,
				DelayMs = s.DelayMs,
				Mouse = s.Mouse
			}).ToList()
		};
	}

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		WriteIndented = true
	};

	// The export file: the macro as LiteSynapse stores it.
	public static string ToJson(Macro m)
	{
		return JsonSerializer.Serialize(new Macro { Name = m.Name, Steps = m.Steps }, JsonOptions);
	}

	public static Macro FromJson(string json)
	{
		Macro m;
		try
		{
			m = JsonSerializer.Deserialize<Macro>(json ?? "");
		}
		catch (JsonException ex)
		{
			throw new InvalidDataException("Not a LiteSynapse macro file: " + ex.Message);
		}
		if (m == null || string.IsNullOrWhiteSpace(m.Name) || m.Steps == null)
		{
			throw new InvalidDataException("Not a LiteSynapse macro file: it has no name or no steps.");
		}
		foreach (MacroStep s in m.Steps)
		{
			if (s == null || s.DelayMs < 0 || s.Mouse < 0 || s.Mouse > MouseForward || s.Vk < 0 || s.Vk > 255)
			{
				throw new InvalidDataException("Not a LiteSynapse macro file: a step is out of range.");
			}
		}
		return m;
	}
}
