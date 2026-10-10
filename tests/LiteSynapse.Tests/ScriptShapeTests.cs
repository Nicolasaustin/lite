using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace LiteSynapse.Tests;

// Everything the Performance page does to Windows is a PowerShell script this app writes and then hands to an
// elevated powershell.exe. A script that does not parse fails as a whole, and the user sees "0 steps applied"
// with no clue why - so the shape of every script the generators can produce is checked here.
//
// These are structural checks, not a PowerShell parser: balanced braces and here-strings, a catch for every
// try, no unterminated string literal, and the $failed/exit contract the exit code depends on.
public class ScriptShapeTests
{
	// Every script the three generators can produce, at their largest.
	private static IEnumerable<(string Name, string Script)> AllScripts()
	{
		yield return ("every setting on",
			Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList()));
		yield return ("every setting off",
			Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList()));
		yield return ("every service off",
			ServiceCatalog.Script(ServiceCatalog.All.Select((ServiceCatalog.Row r) => (r.Service, false)).ToList()));
		yield return ("every service on",
			ServiceCatalog.Script(ServiceCatalog.All.Select((ServiceCatalog.Row r) => (r.Service, true)).ToList()));
		yield return ("devices kept awake",
			DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\6&1a&0&3_0", "USB\\VID_1532&PID_0235\\5&b&0&1_0" },
				powerPlan: true, allowPowerSaving: false));
		yield return ("devices handed back",
			DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\6&1a&0&3_0" },
				powerPlan: true, allowPowerSaving: true));
		// One of each kind together, which is what a real selection looks like.
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			yield return ("just " + t.Id + " on", Tweaks.Script(new[] { (t.Id, true) }));
			yield return ("just " + t.Id + " off", Tweaks.Script(new[] { (t.Id, false) }));
		}
	}

	// A here-string body is literal text, so braces and quotes inside it mean nothing to the parser.
	private static string OutsideHereStrings(string script)
	{
		return Regex.Replace(script, "@'\n.*?\n'@", "HERESTRING", RegexOptions.Singleline);
	}

	[Fact]
	public void EveryScriptIsStructurallySound()
	{
		List<string> problems = new List<string>();
		foreach ((string name, string script) in AllScripts())
		{
			string code = OutsideHereStrings(script);
			int opened = Regex.Matches(script, "@'\n").Count;
			int closed = Regex.Matches(script, "^'@", RegexOptions.Multiline).Count;
			if (opened != closed)
			{
				problems.Add($"{name}: {opened} here-string(s) opened, {closed} closed");
			}
			int open = code.Count((char c) => c == '{');
			int close = code.Count((char c) => c == '}');
			if (open != close)
			{
				problems.Add($"{name}: {open} '{{' and {close} '}}'");
			}
			int tries = Regex.Matches(code, @"^\s*try \{", RegexOptions.Multiline).Count;
			int catches = Regex.Matches(code, @"\} catch \{").Count;
			if (tries != catches)
			{
				problems.Add($"{name}: {tries} try and {catches} catch");
			}
			// An odd number of apostrophes on a line means a literal that was never closed, which is exactly
			// what a quoting mistake looks like.
			string[] lines = code.Split('\n');
			for (int i = 0; i < lines.Length; i++)
			{
				if (lines[i].Count((char c) => c == '\'') % 2 != 0)
				{
					problems.Add($"{name}: line {i + 1} has an unclosed string: {lines[i].Trim()}");
				}
			}
		}
		Assert.Empty(problems);
	}

	[Fact]
	public void EveryScriptKeepsTheExitCodeContract()
	{
		// The window reads the exit code as "how many steps failed", so a script that does not start the
		// counter or does not return it would report success for a run that did nothing.
		foreach ((string name, string script) in AllScripts())
		{
			Assert.True(script.StartsWith("$failed = 0\n", StringComparison.Ordinal), name + " does not start the counter");
			Assert.True(script.EndsWith("exit $failed\n", StringComparison.Ordinal), name + " does not return the counter");
			// And exactly once each, so a merged script cannot reset the count halfway.
			Assert.Equal(1, Regex.Matches(script, @"^\$failed = 0$", RegexOptions.Multiline).Count);
			Assert.Equal(1, Regex.Matches(script, @"^exit \$failed$", RegexOptions.Multiline).Count);
		}
	}

	[Fact]
	public void NoScriptCarriesAnUnexpandedPlaceholderOrAnEmptyValue()
	{
		foreach ((string name, string script) in AllScripts())
		{
			// A C# interpolation that did not happen, or a null that became "".
			Assert.DoesNotContain("{0}", script);
			Assert.DoesNotContain("-Name '' ", script);
			Assert.DoesNotContain("-LiteralPath '' ", script);
			Assert.DoesNotContain("-Value  ", script);
			// The optional-command marker is handled, never emitted.
			Assert.DoesNotContain("  " + Tweaks.Optional, script);
		}
	}
}
