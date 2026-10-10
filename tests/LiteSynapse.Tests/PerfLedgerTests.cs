using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Xunit;

namespace LiteSynapse.Tests;

// The Performance tab has to say, for every row, whether the change went through. These are the rules for the
// rows Windows will not describe afterwards, and for the ones that wait for a restart.
public class PerfLedgerTests
{
	private static readonly DateTime Boot = new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc);

	private static PerfLedger.Entry Set(bool on, int minutesAfterBoot)
	{
		return new PerfLedger.Entry { On = on, At = Boot.AddMinutes(minutesAfterBoot) };
	}

	[Fact]
	public void NothingOfOursOnRecordShowsWhatWindowsSaid()
	{
		Assert.Equal(("on", false, false), PerfLedger.Resolve("on", null, needsReboot: false, probeIsRunning: false, Boot));
		Assert.Equal(("unknown", false, false), PerfLedger.Resolve(null, null, false, false, Boot));
		Assert.Equal(("unknown", false, false), PerfLedger.Resolve("", null, true, true, Boot));
	}

	[Fact]
	public void ASettingWindowsCannotDescribeShowsWhatWeLastSet()
	{
		// TCP auto-tuning: netsh has no registry value to read. Before the ledger this row was blank forever.
		Assert.Equal(("off", true, false), PerfLedger.Resolve("unknown", Set(false, 30), false, false, Boot));
		Assert.Equal(("on", true, false), PerfLedger.Resolve(null, Set(true, -600), false, false, Boot));
	}

	[Fact]
	public void WhatWindowsSaysWinsOverAnOldRecord()
	{
		// Something else changed it since: the registry is the truth.
		Assert.Equal(("off", false, false), PerfLedger.Resolve("off", Set(true, 30), false, false, Boot));
		Assert.Equal(("part", false, false), PerfLedger.Resolve("part", Set(true, 30), false, false, Boot));
	}

	[Fact]
	public void ARestartSettingChangedThisBootWaitsForTheRestart()
	{
		// DEP: Windows reports the running policy, which is still the old one until the restart.
		Assert.Equal(("on", true, true), PerfLedger.Resolve("off", Set(true, 30), needsReboot: true, probeIsRunning: true, Boot));
		// Already running that way (it was set before as well): nothing to wait for.
		Assert.Equal(("on", false, false), PerfLedger.Resolve("on", Set(true, 30), true, true, Boot));
		// A registry setting that needs a restart: the registry already agrees, the effect waits.
		Assert.Equal(("on", false, true), PerfLedger.Resolve("on", Set(true, 30), true, false, Boot));
		// Dynamic tick: nothing can read it, so ours, waiting.
		Assert.Equal(("on", true, true), PerfLedger.Resolve("unknown", Set(true, 30), true, false, Boot));
		// The registry disagrees with what we set this boot: something changed it after us.
		Assert.Equal(("off", false, false), PerfLedger.Resolve("off", Set(true, 30), true, false, Boot));
	}

	[Fact]
	public void ARestartSettingFromAnEarlierBootIsJustReadAgain()
	{
		// Changed before this boot: the restart has happened, so what Windows reports now is the truth.
		Assert.Equal(("off", false, false), PerfLedger.Resolve("off", Set(true, -30), true, true, Boot));
		Assert.Equal(("on", true, false), PerfLedger.Resolve("unknown", Set(true, -30), true, false, Boot));
	}

	[Fact]
	public void TheLedgerSurvivesARoundTripAndAMissingOrBrokenFile()
	{
		string dir = Path.Combine(Path.GetTempPath(), "ls-ledger-" + Guid.NewGuid().ToString("N"));
		string file = Path.Combine(dir, "perf-applied.json");
		try
		{
			PerfLedger empty = PerfLedger.Load(file);
			Assert.Null(empty.Get("nagle"));
			empty.Set("nagle", true, Boot);
			empty.Set("DEP", false, Boot.AddHours(1));
			empty.Save();
			PerfLedger back = PerfLedger.Load(file);
			Assert.True(back.Get("nagle").On);
			Assert.Equal(Boot, back.Get("nagle").At);
			// Ids are matched without regard to case.
			Assert.False(back.Get("dep").On);
			File.WriteAllText(file, "{ not json");
			Assert.Null(PerfLedger.Load(file).Get("nagle"));
		}
		finally
		{
			try
			{
				Directory.Delete(dir, recursive: true);
			}
			catch
			{
			}
		}
	}

	[Fact]
	public void OnlyCatalogIdsInAResultFileCount()
	{
		HashSet<string> ids = Tweaks.ReadResult("\uFEFFnagle\r\nDEP\n\n  hibernate  \nnot-a-setting\n; rm -rf\n");
		Assert.Equal(new[] { "dep", "hibernate", "nagle" }, ids.OrderBy((string x) => x).ToArray());
		Assert.Empty(Tweaks.ReadResult(null));
		Assert.Empty(Tweaks.ReadResult(""));
	}

	[Fact]
	public void TheScriptReportsEachSettingOnlyWhenEveryStepOfItSucceeded()
	{
		string script = Tweaks.Script(new[] { ("nagle", true), ("hibernate", true) }, Tweaks.MachineScope, "C:\\r.txt");
		Assert.Contains("$okIds = @()", script);
		Assert.Contains("if ($failed -eq $step) { $okIds += 'nagle' }", script);
		Assert.Contains("if ($failed -eq $step) { $okIds += 'hibernate' }", script);
		// The marker comes after the setting's own steps, so a failing step is counted before the check.
		Assert.True(script.IndexOf("powercfg.exe /hibernate off", StringComparison.Ordinal)
			< script.IndexOf("$okIds += 'hibernate'", StringComparison.Ordinal));
		Assert.Contains("Set-Content -LiteralPath 'C:\\r.txt'", script);
		// Without a result file nothing changes from before.
		Assert.DoesNotContain("$okIds", Tweaks.Script(new[] { ("nagle", true) }, Tweaks.MachineScope));
	}

	[Fact]
	public void EverySettingNamesTheThingItSwitchesWithoutADirection()
	{
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			Assert.False(string.IsNullOrWhiteSpace(t.Thing), t.Id + " has no thing name");
			// "Pointer acceleration off" as a thing would make "ON" read as "off is on".
			Assert.False(t.Thing.EndsWith(" off", StringComparison.OrdinalIgnoreCase), t.Id + ": " + t.Thing);
			Assert.False(t.Thing.EndsWith(" on", StringComparison.OrdinalIgnoreCase), t.Id + ": " + t.Thing);
			Assert.False(t.Thing.StartsWith("No ", StringComparison.Ordinal), t.Id + ": " + t.Thing);
			Assert.False(t.Thing.StartsWith("Stop ", StringComparison.Ordinal), t.Id + ": " + t.Thing);
		}
		// A title that says "off" names a thing that helps when off, and "on" one that helps when on.
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			if (t.Title.EndsWith(" off", StringComparison.Ordinal))
			{
				Assert.False(t.PerfOn, t.Id);
			}
			if (t.Title.EndsWith(" on", StringComparison.Ordinal))
			{
				Assert.True(t.PerfOn, t.Id);
			}
		}
	}

	[Fact]
	public void TheBiggestElevatedScriptStillStarts()
	{
		// -EncodedCommand carries the script as base64 of UTF-16 on the command line, which Windows caps at
		// 32,767 characters. A selection of everything came to 87,000 and Process.Start refused it, so nothing
		// was applied. A script that big now goes through a file, behind a short loader that checks its hash.
		string script = SystemScanner.Combine(
			ServiceCatalog.Script(ServiceCatalog.All.Select((ServiceCatalog.Row r) => (r.Service, false)).ToList()),
			Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList(), Tweaks.MachineScope,
				"C:\\Users\\someone\\AppData\\Local\\Programs\\LiteSynapse\\data\\perf-result-0123456789abcdef.txt"));
		Assert.True(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)).Length > RazerLeftovers.MaxEncoded);
		(string encoded, string file) = RazerLeftovers.Command(script);
		try
		{
			Assert.NotNull(file);
			Assert.True(encoded.Length < 32000, $"the command line is {encoded.Length} characters");
			// The file holds exactly the script, without a byte-order mark, and the loader carries its hash.
			byte[] bytes = File.ReadAllBytes(file);
			Assert.NotEqual(0xEF, bytes[0]);
			Assert.Equal(script, Encoding.UTF8.GetString(bytes));
			string loader = Encoding.Unicode.GetString(Convert.FromBase64String(encoded));
			Assert.Contains(RazerLeftovers.Sha256(script), loader);
			Assert.Contains("exit " + RazerLeftovers.Tampered, loader);
			// It runs the text it checked, not the file a second time.
			Assert.Contains(". ([scriptblock]::Create($s))", loader);
			Assert.Equal(1, loader.Split("ReadAllText").Length - 1);
		}
		finally
		{
			if (file != null)
			{
				File.Delete(file);
			}
		}
		// Either way progress records are switched off first: they cost time and filled the log as CLIXML.
		Assert.StartsWith(RazerLeftovers.Quiet, Encoding.Unicode.GetString(Convert.FromBase64String(encoded)));
		// A small script stays on the command line and writes nothing.
		(string small, string none) = RazerLeftovers.Command("$failed = 0\nexit $failed\n");
		Assert.Null(none);
		Assert.Equal(RazerLeftovers.Quiet + "$failed = 0\nexit $failed\n", Encoding.Unicode.GetString(Convert.FromBase64String(small)));
	}

	[Fact]
	public void ProgressOnlyStderrIsNotLoggedButErrorsAre()
	{
		const string progress = "#< CLIXML\n<Objs Version=\"1.1.0.1\" xmlns=\"http://schemas.microsoft.com/powershell/2004/04\"><Obj S=\"progress\" RefId=\"0\"><TN RefId=\"0\"><T>System.Management.Automation.PSCustomObject</T></TN><MS><I64 N=\"SourceId\">1</I64><PR N=\"Record\"><AV>Preparing modules for first use.</AV></PR></MS></Obj></Objs>";
		Assert.True(RazerLeftovers.OnlyProgress(progress));
		Assert.False(RazerLeftovers.OnlyProgress(progress.Replace("</Objs>", "<S S=\"Error\">Access is denied</S></Objs>")));
		Assert.False(RazerLeftovers.OnlyProgress("Get-CimInstance : Access denied"));
		Assert.False(RazerLeftovers.OnlyProgress(""));
	}

	[Fact]
	public void TheLoaderHashMatchesWhatPowerShellComputes()
	{
		// [BitConverter]::ToString(SHA256(UTF8 bytes)).Replace('-','') is upper-case hex, as is Convert.ToHexString.
		Assert.Equal("E3B0C44298FC1C149AFBF4C8996FB92427AE41E4649B934CA495991B7852B855", RazerLeftovers.Sha256(""));
		Assert.Equal(64, RazerLeftovers.Sha256("$failed = 0\n").Length);
	}
}
