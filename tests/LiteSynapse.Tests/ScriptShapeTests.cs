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
		// Both halves of the split, since each is a script in its own right that gets run on its own.
		foreach (string scope in new[] { Tweaks.UserScope, Tweaks.MachineScope })
		{
			yield return ($"every setting on ({scope})",
				Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList(), scope));
			yield return ($"every setting off ({scope})",
				Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList(), scope));
		}
		yield return ("every service off",
			ServiceCatalog.Script(ServiceCatalog.All.Select((ServiceCatalog.Row r) => (r.Service, false)).ToList()));
		yield return ("every service on",
			ServiceCatalog.Script(ServiceCatalog.All.Select((ServiceCatalog.Row r) => (r.Service, true)).ToList()));
		yield return ("devices kept awake",
			DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\6&1a&0&3_0", "USB\\VID_1532&PID_0235\\5&b&0&1_0" },
				powerPlan: true, allowPowerSaving: false));
		// What Apply on the Device page really runs: both directions, the plan, and the read-back, with a path
		// carrying both kinds of apostrophe.
		const string devFile = "C:\\Users\\o'brien\\O\u2019Neil\\data\\device-power.txt";
		yield return ("devices both ways and read back", DeviceTuning.MixedScript(
			new[] { "USB\\VID_1532&PID_008D\\6&1a&0&3_0", "USB\\VID_1532&PID_008D&MI_00\\7&2&0&0000_0" },
			new[] { "USB\\VID_1532&PID_0235\\5&b&0&1_0" }, planAllow: false, devFile));
		yield return ("devices kept awake only", DeviceTuning.MixedScript(new[] { "USB\\VID_1532&PID_008D\\6&1a&0&3_0" }, null, null, devFile));
		yield return ("device plan only", DeviceTuning.MixedScript(null, null, true, devFile));
		// The loader that runs an oversized script from a file.
		yield return ("loader", "$failed = 0\n" +
			RazerLeftovers.Loader("C:\\Users\\o'brien\\O\u2019Neil\\data\\run-1.ps1", RazerLeftovers.Sha256("x")) + "exit $failed\n");
		// The administrator read of the boot settings, alone and at the end of a settings run.
		yield return ("admin read", "$failed = 0\n" + Tweaks.AdminReadScript("C:\\Users\\o'brien\\perf-admin.txt") + "exit $failed\n");
		yield return ("services and settings with the admin read", SystemScanner.WithAdminRead(SystemScanner.Combine(
			ServiceCatalog.Script(new[] { ("DiagTrack", false), ("SysMain", false) }, report: true),
			Tweaks.Script(new[] { ("dynamictick", true) }, Tweaks.MachineScope, "C:\\r.txt", force: true))));
		yield return ("services only with a result file", SystemScanner.Combine(
			ServiceCatalog.Script(new[] { ("DiagTrack", false) }, report: true),
			Tweaks.Script(null, Tweaks.MachineScope, "C:\\r.txt", force: true)));
		yield return ("devices read as admin",
			"$failed = 0\n" + DeviceTuning.QueryToFile("C:\\x\\device-power.txt") + "exit $failed\n");
		// The settings scripts as Apply really runs them: with a result file, whose path can carry an
		// apostrophe (a user name like O'Brien).
		const string result = "C:\\Users\\o'brien\\AppData\\Roaming\\LiteSynapse\\perf-result-1.txt";
		foreach (string scope in new[] { Tweaks.UserScope, Tweaks.MachineScope })
		{
			yield return ($"every setting on with a result file ({scope})",
				Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList(), scope, result));
			yield return ($"every setting off with a result file ({scope})",
				Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList(), scope, result));
		}
		yield return ("services and every setting merged, with a result file", SystemScanner.Combine(
			ServiceCatalog.Script(ServiceCatalog.All.Select((ServiceCatalog.Row r) => (r.Service, false)).ToList()),
			Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList(), Tweaks.MachineScope, result)));
		yield return ("devices handed back",
			DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\6&1a&0&3_0" },
				powerPlan: true, allowPowerSaving: true));
		// The merged script is the one that actually runs when a selection spans services and settings: the
		// halves are stitched together by stripping each one's counter and exit line. That stitching is where
		// a structural mistake would land, so it is checked at its largest and at its smallest.
		yield return ("services and every setting merged", SystemScanner.Combine(
			ServiceCatalog.Script(ServiceCatalog.All.Select((ServiceCatalog.Row r) => (r.Service, false)).ToList()),
			Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList(), Tweaks.MachineScope)));
		yield return ("one service and one setting merged", SystemScanner.Combine(
			ServiceCatalog.Script(new[] { ("DiagTrack", false) }),
			Tweaks.Script(new[] { ("priority", true) }, Tweaks.MachineScope)));
		// The halves can also be empty, which is what an all-start-up selection looks like by the time it
		// reaches here.
		yield return ("nothing on either side merged", SystemScanner.Combine(
			ServiceCatalog.Script(null), Tweaks.Script(null, Tweaks.MachineScope)));
		yield return ("only settings merged", SystemScanner.Combine(
			ServiceCatalog.Script(null), Tweaks.Script(new[] { ("nagle", true) }, Tweaks.MachineScope)));
		yield return ("only services merged", SystemScanner.Combine(
			ServiceCatalog.Script(new[] { ("Spooler", false) }), Tweaks.Script(null, Tweaks.MachineScope)));

		// One of each kind together, which is what a real selection looks like.
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			foreach (string scope in new[] { Tweaks.UserScope, Tweaks.MachineScope })
			{
				yield return ($"just {t.Id} on ({scope})", Tweaks.Script(new[] { (t.Id, true) }, scope));
				yield return ($"just {t.Id} off ({scope})", Tweaks.Script(new[] { (t.Id, false) }, scope));
			}
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
				// Each quote character is counted on its own: PowerShell pairs any of the five, and an escaped one
				// is doubled, so every kind has to come out even.
				if (new[] { '\'', '\u2018', '\u2019', '\u201A', '\u201B' }.Any((char q) => lines[i].Count((char c) => c == q) % 2 != 0))
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
