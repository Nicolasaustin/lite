using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LiteSynapse;

public class GameRule
{
	public string Profile { get; set; }

	public List<string> Exes { get; set; } = new List<string>();

	public string KeyboardProfile { get; set; } = "";

	// Optional. When set, the rule only matches if the full exe path contains this text (case-insensitive),
	// e.g. to tell apart two games that ship an exe with the same name.
	[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
	public string PathContains { get; set; }

	[JsonExtensionData]
	public Dictionary<string, JsonElement> ExtraData { get; set; }

	// True when exeName (file name only) is one of Exes and, if PathContains is set, fullPath contains it.
	public bool Matches(string exeName, string fullPath = null)
	{
		if (string.IsNullOrEmpty(exeName) || Exes == null)
		{
			return false;
		}
		bool listed = false;
		foreach (string exe in Exes)
		{
			if (string.Equals(exe, exeName, StringComparison.OrdinalIgnoreCase))
			{
				listed = true;
				break;
			}
		}
		if (!listed)
		{
			return false;
		}
		if (string.IsNullOrEmpty(PathContains))
		{
			return true;
		}
		return fullPath != null && fullPath.Contains(PathContains, StringComparison.OrdinalIgnoreCase);
	}

	// The rule for a foreground program. A rule whose PathContains matched wins over one without PathContains,
	// otherwise the first matching rule wins (the order games.json lists them in).
	public static GameRule Find(IEnumerable<GameRule> rules, string exeName, string fullPath = null)
	{
		if (rules == null)
		{
			return null;
		}
		GameRule general = null;
		foreach (GameRule rule in rules)
		{
			if (rule == null || !rule.Matches(exeName, fullPath))
			{
				continue;
			}
			if (!string.IsNullOrEmpty(rule.PathContains))
			{
				return rule;
			}
			general ??= rule;
		}
		return general;
	}
}
