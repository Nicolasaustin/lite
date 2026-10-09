using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace LiteSynapse;

// Validation and edits behind the window's commands, on the stored model only (no devices, no Windows APIs),
// ported from the classic window. A bad request throws BridgeException with a message for the user.
//
// The service reads profiles from other threads (the device worker writes the active profile to the mouse),
// so collections are replaced, not changed in place: a reader keeps a consistent copy.
internal static class WebOps
{
	public const int MaxNameLength = 64;

	public const int MinDpi = 100;

	public const int MaxDpi = 20000;

	public const int MaxDpiStages = 5;

	public const int MaxMacroSteps = 2000;

	public const int MaxMacroDelayMs = 60000;

	public const int MaxExes = 50;

	public const string DefaultAccent = "mint";

	public static readonly string[] Accents = new string[5] { "mint", "green", "blue", "violet", "amber" };

	public static readonly int[] PollingRates = new int[3] { 125, 500, 1000 };

	// Keys that can run a macro (keyboard profiles).
	public static readonly string[] MacroKeys = new string[4] { "KEY_UP_ARROW", "KEY_DOWN_ARROW", "KEY_LEFT_ARROW", "KEY_RIGHT_ARROW" };

	// Modifier names, in the order they are stored.
	public static readonly string[] Modifiers = new string[8]
	{
		"KEY_LEFT_CTRL", "KEY_LEFT_SHIFT", "KEY_LEFT_ALT", "KEY_LEFT_WIN",
		"KEY_RIGHT_CTRL", "KEY_RIGHT_SHIFT", "KEY_RIGHT_ALT", "KEY_RIGHT_WIN"
	};

	private static readonly char[] BadFileChars = "<>:\"/\\|?*".ToCharArray();

	// ---- Names -------------------------------------------------------------------------------------------

	// Trimmed name; throws when empty, too long or holding control characters.
	public static string CleanName(string name, string what = "Name")
	{
		string n = (name ?? "").Trim();
		if (n.Length == 0)
		{
			throw new BridgeException(what + " is empty.");
		}
		if (n.Length > MaxNameLength)
		{
			throw new BridgeException($"{what} is too long (at most {MaxNameLength} characters).");
		}
		if (n.Any(char.IsControl))
		{
			throw new BridgeException(what + " contains invalid characters.");
		}
		return n;
	}

	// True when another entry (other than 'except') has this name, ignoring case.
	public static bool NameTaken(IEnumerable<string> names, string name, string except = null)
	{
		return names.Any(n => n != null && n != except && string.Equals(n, name, StringComparison.OrdinalIgnoreCase));
	}

	// "X copy", then "X copy 2", "X copy 3", ...
	public static string CopyName(IEnumerable<string> names, string baseName)
	{
		List<string> all = names.ToList();
		string n = baseName + " copy";
		int i = 2;
		while (NameTaken(all, n))
		{
			n = $"{baseName} copy {i++}";
		}
		return n;
	}

	public static MouseProfile FindMouse(ProfileStore s, string name)
	{
		return s.Mouse.FirstOrDefault(p => p != null && p.Name == name) ?? throw new BridgeException($"No mouse profile named \"{name}\".");
	}

	public static KbProfile FindKeyboard(ProfileStore s, string name)
	{
		return s.Keyboard.FirstOrDefault(p => p != null && p.Name == name) ?? throw new BridgeException($"No keyboard profile named \"{name}\".");
	}

	// ---- Add, duplicate, rename, delete, reset -------------------------------------------------------------

	// A new mouse profile with the DPI and polling rate of 'template' (the active profile), no remaps.
	public static MouseProfile AddMouse(ProfileStore s, string name, MouseProfile template)
	{
		name = CleanName(name, "Profile name");
		if (NameTaken(s.Mouse.Select(p => p?.Name), name))
		{
			throw new BridgeException($"A mouse profile named \"{name}\" already exists.");
		}
		MouseProfile p = new MouseProfile
		{
			Name = name,
			Dpi = template?.Dpi ?? 800,
			PollingRate = template?.PollingRate ?? 1000
		};
		List<int> stages = template?.DpiStages;
		if (stages != null && stages.Count >= 1 && stages.Count <= MaxDpiStages && template.DpiActive >= 1 && template.DpiActive <= stages.Count)
		{
			p.DpiStages = stages.ToList();
			p.DpiActive = template.DpiActive;
		}
		else
		{
			p.DpiStages = new List<int> { p.Dpi };
			p.DpiActive = 1;
		}
		s.Mouse = new List<MouseProfile>(s.Mouse) { p };
		return p;
	}

	public static KbProfile AddKeyboard(ProfileStore s, string name, KbProfile template)
	{
		name = CleanName(name, "Profile name");
		if (NameTaken(s.Keyboard.Select(p => p?.Name), name))
		{
			throw new BridgeException($"A keyboard profile named \"{name}\" already exists.");
		}
		KbProfile p = new KbProfile
		{
			Name = name,
			Brightness = template?.Brightness ?? 100,
			Effect = template?.Effect ?? "static",
			EffectSpeed = template?.EffectSpeed ?? 2
		};
		s.Keyboard = new List<KbProfile>(s.Keyboard) { p };
		return p;
	}

	// A copy named "X copy" (or "X copy 2", ...), placed right after the original.
	public static MouseProfile DuplicateMouse(ProfileStore s, string name)
	{
		MouseProfile src = FindMouse(s, name);
		MouseProfile copy = Clone(src);
		copy.Name = CopyName(s.Mouse.Select(p => p?.Name), src.Name);
		List<MouseProfile> list = s.Mouse.ToList();
		list.Insert(list.IndexOf(src) + 1, copy);
		s.Mouse = list;
		return copy;
	}

	public static KbProfile DuplicateKeyboard(ProfileStore s, string name)
	{
		KbProfile src = FindKeyboard(s, name);
		KbProfile copy = Clone(src);
		copy.Name = CopyName(s.Keyboard.Select(p => p?.Name), src.Name);
		List<KbProfile> list = s.Keyboard.ToList();
		list.Insert(list.IndexOf(src) + 1, copy);
		s.Keyboard = list;
		return copy;
	}

	// Deep copy through JSON, as profiles.json stores it (unknown fields included).
	public static T Clone<T>(T value)
	{
		return JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
	}

	// Renames the profile and every game rule that uses it. Returns the new (trimmed) name.
	public static string RenameMouse(ProfileStore s, List<GameRule> games, string from, string to)
	{
		MouseProfile p = FindMouse(s, from);
		to = CleanName(to, "New name");
		if (to == from)
		{
			return to;
		}
		if (NameTaken(s.Mouse.Select(x => x?.Name), to, from))
		{
			throw new BridgeException($"A mouse profile named \"{to}\" already exists.");
		}
		p.Name = to;
		foreach (GameRule g in games)
		{
			if (g.Profile == from)
			{
				g.Profile = to;
			}
		}
		return to;
	}

	public static string RenameKeyboard(ProfileStore s, List<GameRule> games, string from, string to)
	{
		KbProfile p = FindKeyboard(s, from);
		to = CleanName(to, "New name");
		if (to == from)
		{
			return to;
		}
		if (NameTaken(s.Keyboard.Select(x => x?.Name), to, from))
		{
			throw new BridgeException($"A keyboard profile named \"{to}\" already exists.");
		}
		p.Name = to;
		foreach (GameRule g in games)
		{
			if (g.KeyboardProfile == from)
			{
				g.KeyboardProfile = to;
			}
		}
		return to;
	}

	// Removes the profile and its game rules. The caller refuses the starting profile first.
	public static void DeleteMouse(ProfileStore s, List<GameRule> games, string name)
	{
		FindMouse(s, name);
		s.Mouse = s.Mouse.Where(p => p != null && p.Name != name).ToList();
		games.RemoveAll(g => g.Profile == name);
	}

	// Removes the profile; game rules that used it switch no keyboard profile any more.
	public static void DeleteKeyboard(ProfileStore s, List<GameRule> games, string name)
	{
		FindKeyboard(s, name);
		s.Keyboard = s.Keyboard.Where(p => p != null && p.Name != name).ToList();
		foreach (GameRule g in games)
		{
			if (g.KeyboardProfile == name)
			{
				g.KeyboardProfile = "";
			}
		}
	}

	// Mouse: clears the button remaps. Keyboard: clears the macro keys.
	public static void ResetMouse(MouseProfile p)
	{
		p.Buttons = new Dictionary<string, KeyTarget>();
	}

	public static void ResetKeyboard(KbProfile p)
	{
		p.MacroKeys = new Dictionary<string, string>();
	}

	// ---- Mouse performance and buttons ---------------------------------------------------------------------

	// 1..5 stages of 100..20000 DPI; active is 1-based. The profile's DPI becomes the active stage.
	public static void SetDpiStages(MouseProfile p, IReadOnlyList<int> stages, int active)
	{
		if (stages == null || stages.Count < 1 || stages.Count > MaxDpiStages)
		{
			throw new BridgeException($"Use 1 to {MaxDpiStages} DPI stages.");
		}
		foreach (int dpi in stages)
		{
			if (dpi < MinDpi || dpi > MaxDpi)
			{
				throw new BridgeException($"DPI must be between {MinDpi} and {MaxDpi}.");
			}
		}
		if (active < 1 || active > stages.Count)
		{
			throw new BridgeException($"The active stage must be between 1 and {stages.Count}.");
		}
		p.DpiStages = stages.ToList();
		p.DpiActive = active;
		p.Dpi = stages[active - 1];
	}

	public static void SetPolling(MouseProfile p, int hz)
	{
		if (Array.IndexOf(PollingRates, hz) < 0)
		{
			throw new BridgeException("Polling rate must be 125, 500 or 1000 Hz.");
		}
		p.PollingRate = hz;
	}

	// The stored spelling of a button target: KEY_* in canonical form (legacy KEY_HYPEN -> KEY_HYPHEN,
	// any case), DISABLED or MOUSE_MIDDLE. null when it is none of these.
	public static string CanonicalKey(string key)
	{
		string k = key?.Trim();
		if (string.IsNullOrEmpty(k))
		{
			return null;
		}
		if (string.Equals(k, KeyTarget.Disabled, StringComparison.OrdinalIgnoreCase))
		{
			return KeyTarget.Disabled;
		}
		if (string.Equals(k, KeyTarget.MouseMiddle, StringComparison.OrdinalIgnoreCase))
		{
			return KeyTarget.MouseMiddle;
		}
		if (!k.StartsWith("KEY_", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}
		int vk = Keys2.Vk(k);
		return (vk == 0) ? null : Keys2.NameFromVk(vk);
	}

	public static string CanonicalModifier(string mod)
	{
		string m = mod?.Trim();
		return Modifiers.FirstOrDefault(x => string.Equals(x, m, StringComparison.OrdinalIgnoreCase));
	}

	// What a button sends. Empty key = null (no remap: the button sends its default). Modifiers are only kept
	// for keys; DISABLED and MOUSE_MIDDLE take none.
	public static KeyTarget ButtonTarget(string key, IEnumerable<string> mods)
	{
		if (string.IsNullOrWhiteSpace(key))
		{
			return null;
		}
		string canon = CanonicalKey(key) ?? throw new BridgeException($"Unknown key \"{key}\".");
		HashSet<string> chosen = new HashSet<string>();
		if (canon != KeyTarget.Disabled && canon != KeyTarget.MouseMiddle)
		{
			foreach (string mod in mods ?? Enumerable.Empty<string>())
			{
				chosen.Add(CanonicalModifier(mod) ?? throw new BridgeException($"Unknown modifier \"{mod}\"."));
			}
		}
		return new KeyTarget
		{
			Key = canon,
			Mods = Modifiers.Where(chosen.Contains).ToList()
		};
	}

	public static void SetButton(MouseProfile p, IReadOnlyCollection<string> buttonIds, string id, string key, IEnumerable<string> mods)
	{
		if (string.IsNullOrEmpty(id) || !buttonIds.Contains(id))
		{
			throw new BridgeException($"Unknown button \"{id}\".");
		}
		KeyTarget target = ButtonTarget(key, mods);
		Dictionary<string, KeyTarget> buttons = new Dictionary<string, KeyTarget>(p.Buttons ?? new Dictionary<string, KeyTarget>());
		if (target == null)
		{
			buttons.Remove(id);
		}
		else
		{
			buttons[id] = target;
		}
		p.Buttons = buttons;
	}

	// The lighting to store on a mouse profile. color is "#RRGGBB" (stored upper case).
	public static MouseLightingSettings MouseLight(string effect, string color, int brightness)
	{
		string e = LiteSynapse.MouseLighting.Effects.FirstOrDefault(x => string.Equals(x, effect, StringComparison.OrdinalIgnoreCase))
			?? throw new BridgeException($"Unknown lighting effect \"{effect}\".");
		if (!LiteSynapse.MouseLighting.TryParseColor(color, out byte r, out byte g, out byte b))
		{
			throw new BridgeException("Color must look like #RRGGBB.");
		}
		if (brightness < 0 || brightness > 100)
		{
			throw new BridgeException("Brightness must be between 0 and 100.");
		}
		return new MouseLightingSettings
		{
			Effect = e,
			Color = $"#{r:X2}{g:X2}{b:X2}",
			Brightness = brightness
		};
	}

	// ---- Keyboard ------------------------------------------------------------------------------------------

	public static void SetKbEffect(KbProfile p, string effect, int speed)
	{
		string e = KbLighting.Effects.FirstOrDefault(x => string.Equals(x, effect, StringComparison.OrdinalIgnoreCase))
			?? throw new BridgeException($"The BlackWidow Lite has no \"{effect}\" effect. Use off, static or breathing.");
		if (speed < 1 || speed > 3)
		{
			throw new BridgeException("Effect speed must be 1, 2 or 3.");
		}
		p.Effect = e;
		p.EffectSpeed = speed;
	}

	// macro "" removes the macro from that key.
	public static void SetMacroKey(ProfileStore s, KbProfile p, string key, string macro)
	{
		if (Array.IndexOf(MacroKeys, key) < 0)
		{
			throw new BridgeException("Only the arrow keys can run macros.");
		}
		Dictionary<string, string> keys = new Dictionary<string, string>(p.MacroKeys ?? new Dictionary<string, string>());
		if (string.IsNullOrEmpty(macro))
		{
			keys.Remove(key);
		}
		else
		{
			if (!s.Macros.Any(m => m?.Name == macro))
			{
				throw new BridgeException($"No macro named \"{macro}\".");
			}
			keys[key] = macro;
		}
		p.MacroKeys = keys;
	}

	// ---- Games ---------------------------------------------------------------------------------------------

	// Program file names as game rules match them: "C:\Games\cs2.exe" -> "cs2.exe", "cs2" -> "cs2.exe".
	// Empty entries are dropped, duplicates (any case) kept once.
	public static List<string> CleanExes(IEnumerable<string> exes)
	{
		List<string> list = new List<string>();
		foreach (string raw in exes ?? Enumerable.Empty<string>())
		{
			string e = (raw ?? "").Trim().Trim('"').Trim().Replace('/', '\\');
			int slash = e.LastIndexOf('\\');
			if (slash >= 0)
			{
				e = e.Substring(slash + 1).Trim();
			}
			if (e.Length == 0)
			{
				continue;
			}
			if (e.Length > 255 || e.IndexOfAny(BadFileChars) >= 0 || e.Any(char.IsControl))
			{
				throw new BridgeException($"\"{raw}\" is not a program file name.");
			}
			if (e.IndexOf('.') < 0)
			{
				e += ".exe";
			}
			if (!list.Any(x => string.Equals(x, e, StringComparison.OrdinalIgnoreCase)))
			{
				list.Add(e);
			}
		}
		if (list.Count > MaxExes)
		{
			throw new BridgeException($"A game can have at most {MaxExes} programs.");
		}
		return list;
	}

	// The runningApps list: "name.exe" of each process that has a window, sorted and each once (any case),
	// without LiteSynapse itself and the Windows shell (Explorer, Start, Settings, ...), which are never games.
	public static List<string> RunningAppNames(IEnumerable<(int Pid, string Name)> processes, ICollection<int> withWindow, int selfPid, string selfExe)
	{
		SortedSet<string> names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach ((int pid, string name) in processes ?? Enumerable.Empty<(int, string)>())
		{
			if (pid == selfPid || string.IsNullOrWhiteSpace(name) || withWindow == null || !withWindow.Contains(pid))
			{
				continue;
			}
			string exe = name.Trim() + ".exe";
			if (!ServicePlan.IsShellOrSelf(exe, selfExe))
			{
				names.Add(exe);
			}
		}
		return names.ToList();
	}

	public static GameRule FindGame(List<GameRule> games, string profile)
	{
		return games.FirstOrDefault(g => g != null && g.Profile == profile);
	}

	// Links a mouse profile to a game (no program yet: it is never switched to automatically until one is set).
	public static GameRule AddGame(ProfileStore s, List<GameRule> games, string profile)
	{
		FindMouse(s, profile);
		if (FindGame(games, profile) != null)
		{
			throw new BridgeException($"\"{profile}\" is already linked to a game.");
		}
		GameRule rule = new GameRule
		{
			Profile = profile
		};
		games.Add(rule);
		return rule;
	}

	// Sets the programs of the profile's game rule, creating the rule if the profile has none.
	public static GameRule SetGameExes(ProfileStore s, List<GameRule> games, string profile, IEnumerable<string> exes)
	{
		List<string> clean = CleanExes(exes);
		GameRule rule = FindGame(games, profile) ?? AddGame(s, games, profile);
		rule.Exes = clean;
		return rule;
	}

	// keyboardProfile "" = the game switches no keyboard profile.
	public static void SetGameKeyboard(ProfileStore s, List<GameRule> games, string profile, string keyboardProfile)
	{
		if (FindGame(games, profile) == null)
		{
			throw new BridgeException($"\"{profile}\" is not linked to a game.");
		}
		if (!string.IsNullOrEmpty(keyboardProfile))
		{
			FindKeyboard(s, keyboardProfile);
		}
		foreach (GameRule g in games)
		{
			if (g != null && g.Profile == profile)
			{
				g.KeyboardProfile = keyboardProfile ?? "";
			}
		}
	}

	public static void RemoveGame(List<GameRule> games, string profile)
	{
		if (games.RemoveAll(g => g != null && g.Profile == profile) == 0)
		{
			throw new BridgeException($"\"{profile}\" is not linked to a game.");
		}
	}

	// ---- Macros --------------------------------------------------------------------------------------------

	// A checked macro: vk 0 = a pure delay step, otherwise a key press or release (virtual-key code 1..255).
	public static Macro CleanMacro(string name, IReadOnlyList<MacroStep> steps)
	{
		name = CleanName(name, "Macro name");
		if (steps == null)
		{
			throw new BridgeException("The macro has no steps list.");
		}
		if (steps.Count > MaxMacroSteps)
		{
			throw new BridgeException($"A macro can have at most {MaxMacroSteps} steps.");
		}
		List<MacroStep> list = new List<MacroStep>();
		foreach (MacroStep s in steps)
		{
			if (s == null)
			{
				continue;
			}
			if (s.Vk < 0 || s.Vk > 255)
			{
				throw new BridgeException($"Step key code {s.Vk} is not a key.");
			}
			if (s.DelayMs < 0 || s.DelayMs > MaxMacroDelayMs)
			{
				throw new BridgeException($"A step delay must be between 0 and {MaxMacroDelayMs} ms.");
			}
			list.Add(new MacroStep
			{
				Vk = s.Vk,
				Ext = s.Vk != 0 && s.Ext,
				Down = s.Vk != 0 && s.Down,
				DelayMs = s.DelayMs
			});
		}
		return new Macro
		{
			Name = name,
			Steps = list
		};
	}

	// Adds m, or replaces the macro called originalName. A rename also renames the keyboard profiles' macro keys.
	public static void SaveMacro(ProfileStore s, string originalName, Macro m)
	{
		Macro old = null;
		if (!string.IsNullOrEmpty(originalName))
		{
			old = s.Macros.FirstOrDefault(x => x?.Name == originalName) ?? throw new BridgeException($"No macro named \"{originalName}\".");
		}
		if (NameTaken(s.Macros.Select(x => x?.Name), m.Name, old?.Name))
		{
			throw new BridgeException($"A macro named \"{m.Name}\" already exists.");
		}
		m.ExtraData = old?.ExtraData;
		List<Macro> list = s.Macros.ToList();
		if (old != null)
		{
			list[list.IndexOf(old)] = m;
		}
		else
		{
			list.Add(m);
		}
		s.Macros = list;
		if (old != null && old.Name != m.Name)
		{
			ReplaceMacroKeys(s, old.Name, m.Name);
		}
	}

	// Removes the macro and every key that ran it.
	public static void DeleteMacro(ProfileStore s, string name)
	{
		if (!s.Macros.Any(x => x?.Name == name))
		{
			throw new BridgeException($"No macro named \"{name}\".");
		}
		s.Macros = s.Macros.Where(x => x != null && x.Name != name).ToList();
		ReplaceMacroKeys(s, name, null);
	}

	// Points macro keys that run 'from' to 'to' (null = removes them).
	private static void ReplaceMacroKeys(ProfileStore s, string from, string to)
	{
		foreach (KbProfile kp in s.Keyboard)
		{
			if (kp?.MacroKeys == null || !kp.MacroKeys.ContainsValue(from))
			{
				continue;
			}
			Dictionary<string, string> keys = new Dictionary<string, string>();
			foreach (KeyValuePair<string, string> kv in kp.MacroKeys)
			{
				if (kv.Value != from)
				{
					keys[kv.Key] = kv.Value;
				}
				else if (to != null)
				{
					keys[kv.Key] = to;
				}
			}
			kp.MacroKeys = keys;
		}
	}

	// ---- Misc ----------------------------------------------------------------------------------------------

	public static bool IsAccent(string accent)
	{
		return Array.IndexOf(Accents, accent) >= 0;
	}

	// A file name for an export: characters Windows does not allow become '_'.
	public static string SafeFileName(string name)
	{
		char[] chars = (name ?? "").Select(c => (c < ' ' || Array.IndexOf(BadFileChars, c) >= 0) ? '_' : c).ToArray();
		string n = new string(chars).Trim().TrimEnd('.');
		return (n.Length == 0) ? "profile" : n;
	}

	// The slots the window may write: 2..5, except the ones live profiles use right now.
	public static void CheckOnboardSlot(int slot, IReadOnlyCollection<int> liveSlots)
	{
		if (slot < 2 || slot > 5)
		{
			throw new BridgeException("Slot must be 2 to 5.");
		}
		if (liveSlots != null && liveSlots.Contains(slot))
		{
			throw new BridgeException($"Slot {slot} holds the live profiles while \"Run selected profiles on the mouse itself\" is on. Pick slot 3, 4 or 5, or turn that setting off.");
		}
	}
}
