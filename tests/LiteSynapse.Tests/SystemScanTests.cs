using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// The scanner's rules: what it flags as worth a look, what it is allowed to turn off, and what it says about
// Windows Update's uploads. It never decides that something is malware, so there is nothing to test for that.
public class SystemScanTests
{
	[Theory]
	[InlineData("C:\\Users\\me\\AppData\\Local\\Temp\\updtsvc.exe", true)]
	[InlineData("C:\\Windows\\Temp\\x.exe", true)]
	[InlineData("C:\\Users\\me\\Downloads\\setup.exe", true)]
	[InlineData("C:\\Users\\Public\\run.exe", true)]
	[InlineData("C:/Users/me/AppData/Local/Temp/x.exe", true)]
	[InlineData("C:\\Program Files\\Discord\\Discord.exe", false)]
	[InlineData("C:\\Users\\me\\AppData\\Local\\Discord\\Update.exe", false)]
	[InlineData("C:\\Windows\\System32\\svchost.exe", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void LooseLocations(string path, bool expected)
	{
		Assert.Equal(expected, ScanRules.IsLooseLocation(path));
	}

	[Theory]
	// A missing file comes first, whatever else is true of it.
	[InlineData(false, true, "Microsoft", "C:\\gone.exe", "missing")]
	// Where it runs from matters more than a signature: signed malware exists.
	[InlineData(true, true, "Someone", "C:\\Users\\me\\AppData\\Local\\Temp\\a.exe", "location")]
	[InlineData(true, false, "", "C:\\Users\\me\\Downloads\\a.exe", "location")]
	[InlineData(true, false, "", "C:\\Program Files\\App\\a.exe", "unsigned")]
	[InlineData(true, true, "Valve Corp.", "C:\\Program Files\\Steam\\steam.exe", "")]
	public void WhatMakesAnItemWorthALook(bool exists, bool signed, string publisher, string path, string expected)
	{
		Assert.Equal(expected, ScanRules.Flag(exists, signed, publisher, path));
		// Every flag has words for the user, and a clean item has none.
		Assert.Equal(expected.Length > 0, ScanRules.FlagText(expected).Length > 0);
	}

	[Fact]
	public void FlaggedItemsAreListedFirst()
	{
		string[] flags = { "", "unsigned", "missing", "location" };
		Assert.Equal(new[] { "location", "unsigned", "missing", "" }, flags.OrderBy(ScanRules.Rank).ToArray());
	}

	[Fact]
	public void OnlyTheAllowlistCanBeTurnedOff()
	{
		Assert.NotNull(ScanRules.OptionalService("DiagTrack"));
		Assert.NotNull(ScanRules.OptionalService("sysmain"));
		Assert.NotNull(ScanRules.OptionalService(" Spooler "));
		// Nothing Windows needs, and nothing security-related, is ever offered.
		foreach (string critical in new[]
		{
			"WinDefend", "wscsvc", "SecurityHealthService", "mpssvc", "BFE", "RpcSs", "Dhcp", "Dnscache",
			"nsi", "LanmanWorkstation", "Power", "Themes", "AudioSrv", "Audiosrv", "UserManager", "ProfSvc",
			"BITS", "wuauserv", "EventLog", "Schedule", "CryptSvc", "TrustedInstaller", "HidServ", "mouclass"
		})
		{
			Assert.Null(ScanRules.OptionalService(critical));
		}
		Assert.Null(ScanRules.OptionalService(""));
		Assert.Null(ScanRules.OptionalService(null));
	}

	[Fact]
	public void EveryOptionalServiceSaysWhatItCostsAndWhatYouLose()
	{
		Assert.All(ScanRules.Optional, row =>
		{
			Assert.False(string.IsNullOrWhiteSpace(row.Service));
			Assert.False(string.IsNullOrWhiteSpace(row.Title));
			Assert.False(string.IsNullOrWhiteSpace(row.Costs));
			Assert.False(string.IsNullOrWhiteSpace(row.Lose));
		});
		// The ones that take something away say so, so the window can warn before it turns them off.
		Assert.Contains("Xbox", ScanRules.OptionalService("XblGameSave").Value.Lose);
		Assert.Contains("printing", ScanRules.OptionalService("Spooler").Value.Lose, StringComparison.OrdinalIgnoreCase);
	}

	[Theory]
	[InlineData("GoogleUpdate", true)]
	[InlineData("OneDrive", true)]
	[InlineData("NvTelemetryContainer", true)]
	[InlineData("RazerAppEngine", true)]
	[InlineData("steamservice", true)]
	[InlineData("explorer", false)]
	[InlineData("cs2", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void KnownBackgroundUploaders(string name, bool expected)
	{
		Assert.Equal(expected, ScanRules.NetworkHog(name).HasValue);
	}

	[Theory]
	// 0 and 99 take updates from Microsoft only; 1, 2 and 3 also upload to other PCs.
	[InlineData(0, false)]
	[InlineData(99, false)]
	[InlineData(100, false)]
	[InlineData(1, true)]
	[InlineData(2, true)]
	[InlineData(3, true)]
	// No value is the Windows default, which shares on the local network.
	[InlineData(null, true)]
	public void WindowsUpdateUploads(int? mode, bool expected)
	{
		Assert.Equal(expected, ScanRules.UploadsUpdates(mode));
		Assert.False(string.IsNullOrWhiteSpace(ScanRules.DeliveryText(mode)));
	}

	[Fact]
	public void TheTurnOffScriptDisablesBeforeItStopsAndOnlyTouchesWhatWasAsked()
	{
		string script = SystemScanner.Script(new List<string> { "DiagTrack", "SysMain" }, delivery: true);
		Assert.StartsWith("$failed = 0\n", script);
		Assert.EndsWith("exit $failed\n", script);
		Assert.Contains("@('DiagTrack', 'SysMain')", script);
		Assert.True(script.IndexOf("start= disabled", StringComparison.Ordinal) < script.IndexOf("sc.exe stop", StringComparison.Ordinal));
		Assert.Contains("DODownloadMode", script);
		Assert.Contains("-Value 0", script);
		// Nothing is deleted, ever.
		Assert.DoesNotContain("sc.exe delete", script);
		Assert.DoesNotContain("Remove-Item", script);
		// Without the delivery option the registry is not touched at all.
		string onlyServices = SystemScanner.Script(new List<string> { "DiagTrack" }, delivery: false);
		Assert.DoesNotContain("DODownloadMode", onlyServices);
		// Nothing selected: the script does nothing.
		string empty = SystemScanner.Script(new List<string>(), delivery: false);
		Assert.Equal("$failed = 0\nexit $failed\n", empty);
		Assert.Equal(empty, SystemScanner.Script(null, delivery: false));
	}
}
