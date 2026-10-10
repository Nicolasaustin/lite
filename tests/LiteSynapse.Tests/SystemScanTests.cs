using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// The scanner's rules: what it flags as worth a look, and what it says about Windows Update's uploads. It
// never decides that something is malware, so there is nothing to test for that.
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
}

// The service catalog: what the Performance page may offer, what it must never offer, and that every row can
// be put back.
public class ServiceCatalogTests
{
	[Fact]
	public void TheInputStackAndTheServicesWindowsCannotBootWithoutAreNeverOffered()
	{
		// Without these there is no mouse to undo the change with, which is the whole reason for the list.
		foreach (string critical in new[]
		{
			"mouclass", "mouhid", "kbdclass", "kbdhid", "mshidkmdf", "HidUsb", "hidi2c",
			"usbhub", "USBXHCI", "usbccgp",
			"DcomLaunch", "RpcSs", "RpcEptMapper", "PlugPlay", "Power", "LSM", "UserManager", "ProfSvc",
			"CryptSvc", "Winmgmt", "gpsvc", "StorSvc", "Audiosrv", "AudioEndpointBuilder",
			"nsi", "Dhcp", "Dnscache", "Themes", "TrustedInstaller",
			// With this off nothing can ask for administrator rights, so the change could not be undone from
			// the page that made it.
			"Appinfo"
		})
		{
			Assert.True(ServiceCatalog.IsProtected(critical), critical + " must never be offered");
			Assert.Null(ServiceCatalog.Find(critical));
			// Not even by asking for it directly.
			Assert.Equal("$failed = 0\nexit $failed\n",
				ServiceCatalog.Script(new[] { (critical, false) }));
		}
		Assert.True(ServiceCatalog.IsProtected(""));
		Assert.True(ServiceCatalog.IsProtected(null));
	}

	[Fact]
	public void TheProtectedListAndTheOfferedListDoNotOverlap()
	{
		foreach (ServiceCatalog.Row row in ServiceCatalog.All)
		{
			Assert.False(ServiceCatalog.IsProtected(row.Service),
				row.Service + " is both offered and protected");
		}
	}

	[Fact]
	public void EveryRowSaysWhatItCostsAndHowToPutItBack()
	{
		Assert.All(ServiceCatalog.All, (ServiceCatalog.Row row) =>
		{
			Assert.False(string.IsNullOrWhiteSpace(row.Service));
			Assert.False(string.IsNullOrWhiteSpace(row.Title));
			Assert.False(string.IsNullOrWhiteSpace(row.Costs));
			Assert.False(string.IsNullOrWhiteSpace(row.Lose));
			Assert.Contains(row.Risk, new[] { Tweaks.Safe, Tweaks.Caution, Tweaks.Risky });
			// The start type to restore has to be a real one, or "turn it back on" cannot work.
			Assert.Contains(row.Default, new[] { ServiceCatalog.Auto, ServiceCatalog.Manual, ServiceCatalog.Disabled });
		});
		// No row appears twice.
		Assert.Equal(ServiceCatalog.All.Length,
			ServiceCatalog.All.Select((ServiceCatalog.Row r) => r.Service.ToLowerInvariant()).Distinct().Count());
	}

	[Fact]
	public void TheOnesThatTakeSomethingAwaySaySo()
	{
		Assert.Contains("printing", ServiceCatalog.Find("Spooler").Lose, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("Xbox", ServiceCatalog.Find("XblGameSave").Lose);
		// hidserv is the hotkey service, not the input stack: the row has to say the mouse keeps working.
		Assert.Contains("mouse", ServiceCatalog.Find("hidserv").Lose, StringComparison.OrdinalIgnoreCase);
	}

	[Fact]
	public void TurningOffDisablesBeforeItStopsAndNeverDeletes()
	{
		string script = ServiceCatalog.Script(new[] { ("DiagTrack", false), ("SysMain", false) });
		Assert.StartsWith("$failed = 0\n", script);
		Assert.EndsWith("exit $failed\n", script);
		// Disabled first, so nothing restarts it between the stop and the config change.
		Assert.True(script.IndexOf("start= disabled", StringComparison.Ordinal)
			< script.IndexOf("sc.exe stop", StringComparison.Ordinal));
		Assert.DoesNotContain("sc.exe delete", script);
		Assert.DoesNotContain("Remove-Item", script);
	}

	[Fact]
	public void TurningBackOnRestoresTheStartTypeWindowsShips()
	{
		// DiagTrack ships automatic, MapsBroker automatic, Fax manual.
		string script = ServiceCatalog.Script(new[] { ("DiagTrack", true), ("Fax", true) });
		Assert.Contains("sc.exe config 'DiagTrack' start= auto", script);
		Assert.Contains("sc.exe config 'Fax' start= demand", script);
		// An automatic service is also started, so turning it back on actually puts it back.
		Assert.Contains("sc.exe start 'DiagTrack'", script);
		Assert.DoesNotContain("sc.exe start 'Fax'", script);
	}

	[Fact]
	public void NothingSelectedChangesNothing()
	{
		Assert.Equal("$failed = 0\nexit $failed\n", ServiceCatalog.Script(null));
		Assert.Equal("$failed = 0\nexit $failed\n", ServiceCatalog.Script(new (string, bool)[0]));
		Assert.Equal("$failed = 0\nexit $failed\n", ServiceCatalog.Script(new[] { ("NotAServiceWeKnow", false) }));
	}
}

// The Windows-settings catalog. The point of these tests is that every entry goes both ways and that the
// script generator cannot be talked into touching something that is not in the catalog.
public class TweaksTests
{
	[Fact]
	public void EveryEntryIsCompleteAndReversible()
	{
		Assert.All(Tweaks.Catalog, (Tweaks.Tweak t) =>
		{
			Assert.False(string.IsNullOrWhiteSpace(t.Id));
			Assert.False(string.IsNullOrWhiteSpace(t.Title));
			Assert.False(string.IsNullOrWhiteSpace(t.What));
			Assert.Contains(t.Risk, new[] { Tweaks.Safe, Tweaks.Caution, Tweaks.Risky });
			Assert.Contains(t.Group, Tweaks.Groups.Select(((string Id, string Title, string Blurb) g) => g.Id));
			// Something has to actually happen, in both directions.
			Assert.True(t.Values.Length > 0 || t.OnCommands.Length > 0, t.Id + " does nothing when turned on");
			Assert.True(t.Values.Length > 0 || t.OffCommands.Length > 0, t.Id + " cannot be turned off again");
			foreach (Tweaks.Reg r in t.Values)
			{
				Assert.Contains(r.Hive, new[] { "HKLM", "HKCU" });
				Assert.False(string.IsNullOrWhiteSpace(r.Key));
				Assert.False(string.IsNullOrWhiteSpace(r.Name));
				Assert.Contains(r.Kind, new[] { "dword", "string", "qword" });
				// Both states must be written out, or the entry is one-directional.
				Assert.NotNull(r.On);
				Assert.NotNull(r.Off);
			}
		});
		Assert.Equal(Tweaks.Catalog.Length,
			Tweaks.Catalog.Select((Tweaks.Tweak t) => t.Id.ToLowerInvariant()).Distinct().Count());
	}

	[Fact]
	public void TheLegacyControlPanelValuesAreWrittenAsStrings()
	{
		// Everything under Control Panel\Mouse, Control Panel\Keyboard and Control Panel\Desktop that
		// Windows itself writes is REG_SZ - they are win.ini settings the registry inherited. A DWord written
		// over one of them is a value of the wrong type, which Windows reads as absent and the Control Panel
		// slider cannot display. These are also the settings most likely to be copied from a guide that got
		// the type wrong, so the rule is asserted rather than trusted.
		string[] mustBeStrings =
		{
			"MouseSpeed", "MouseThreshold1", "MouseThreshold2", "MouseSensitivity",
			"KeyboardDelay", "KeyboardSpeed", "MenuShowDelay", "DragFullWindows", "MinAnimate"
		};
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			foreach (Tweaks.Reg r in t.Values)
			{
				if (Array.IndexOf(mustBeStrings, r.Name) >= 0)
				{
					Assert.Equal("string", r.Kind);
				}
			}
		}
		// And the one value under Control Panel\Mouse that genuinely is a DWord stays one.
		Assert.Equal("dword", Tweaks.Find("rawthrottle").Values.Single().Kind);
	}

	[Fact]
	public void EveryEntryThatGivesSomethingUpSaysWhat()
	{
		foreach (Tweaks.Tweak t in Tweaks.Catalog.Where((Tweaks.Tweak x) => x.Risk != Tweaks.Safe))
		{
			Assert.False(string.IsNullOrWhiteSpace(t.Cost), t.Id + " is not safe but says no cost");
		}
		// The security trade-offs are the ones that must never be presented as free.
		foreach (Tweaks.Tweak t in Tweaks.Catalog.Where((Tweaks.Tweak x) => x.Group == "sec"))
		{
			Assert.Equal(Tweaks.Risky, t.Risk);
			Assert.True(t.Cost.Length > 60, t.Id + " needs a real explanation of what it gives up");
		}
	}

	[Fact]
	public void NothingInTheCatalogTouchesTheInputStackOrDeletesAnything()
	{
		string all = Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList())
			+ Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList());
		foreach (string never in new[]
		{
			"mshidkmdf", "mouhid", "kbdhid", "HidUsb", "mouclass\\\\", "sc.exe delete",
			"Remove-Item -Path", "Remove-Item -LiteralPath", "Format-Volume", "Stop-Service -Name TrustedInstaller"
		})
		{
			Assert.DoesNotContain(never, all);
		}
		// The queue-size entries are the only ones under mouclass/kbdclass, and they only set a value.
		Assert.Contains("mouclass\\Parameters", all);
		Assert.Contains("kbdclass\\Parameters", all);
	}

	[Fact]
	public void OnlyWhatWasAskedForEndsUpInTheScript()
	{
		string one = Tweaks.Script(new[] { ("accel", true) });
		Assert.Contains("MouseThreshold1", one);
		Assert.DoesNotContain("EnableLUA", one);
		Assert.DoesNotContain("bcdedit", one);
		Assert.StartsWith("$failed = 0\n", one);
		Assert.EndsWith("exit $failed\n", one);
		// An id that is not in the catalog is skipped, not guessed at.
		Assert.Equal("$failed = 0\nexit $failed\n", Tweaks.Script(new[] { ("rm -rf /", true) }));
		Assert.Equal("$failed = 0\nexit $failed\n", Tweaks.Script(new (string, bool)[0]));
		Assert.Equal("$failed = 0\nexit $failed\n", Tweaks.Script(null));
	}

	[Fact]
	public void OnAndOffProduceDifferentScriptsForEveryEntry()
	{
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			string on = Tweaks.Script(new[] { (t.Id, true) });
			string off = Tweaks.Script(new[] { (t.Id, false) });
			// Pointer speed is the one entry whose two states ask for the same value (the middle notch), so it
			// is the only one allowed to produce the same script both ways.
			if (t.Id == "pointerspeed")
			{
				continue;
			}
			Assert.NotEqual(on, off);
		}
	}

	[Fact]
	public void TheHelpersAreOnlyIncludedWhenSomethingNeedsThem()
	{
		// The pointer helper is a block of C# compiled at run time: it must not be emitted for a selection
		// that never calls it.
		string plain = Tweaks.Script(new[] { ("gamemode", true) });
		Assert.DoesNotContain("LiteSynapse.Pointer", plain);
		Assert.DoesNotContain("LiteSynapse.Net", plain);
		Assert.Contains("LiteSynapse.Pointer", Tweaks.Script(new[] { ("accel", true) }));
		Assert.Contains("LiteSynapse.Net", Tweaks.Script(new[] { ("nagle", true) }));
		// And only once, however many entries want it.
		string both = Tweaks.Script(new[] { ("accel", true), ("pointerspeed", true) });
		Assert.Equal(1, both.Split("Add-Type").Length - 1);
	}

	[Theory]
	// 0xFFFFFFFF does not fit a signed int, and New-ItemProperty takes a signed one: it has to arrive as -1,
	// bracketed so it cannot read as a parameter name.
	[InlineData("netthrottle", "(-1)")]
	// A hex value in the catalog is written as the decimal Windows expects.
	[InlineData("priority", "38")]
	public void NumbersAreWrittenAsWindowsWantsThem(string id, string expected)
	{
		Assert.Contains("-Value " + expected + " ", Tweaks.Script(new[] { (id, true) }));
	}

	[Fact]
	public void RemovingSomethingThatIsNotThereIsNotAFailedStep()
	{
		// bcdedit /deletevalue on a value that was never set exits non-zero, and so does powercfg /delete on a
		// plan that is not there. Both mean "already in the state you asked for", so neither may be counted.
		foreach ((string Id, bool On, string Expect) step in new[]
		{
			("platformclock", true, "bcdedit.exe /deletevalue useplatformclock"),
			("dynamictick", false, "bcdedit.exe /deletevalue disabledynamictick"),
			("dep", false, "bcdedit.exe /deletevalue nx"),
			("powerplan", false, "powercfg.exe /delete " + Tweaks.PlanGuid)
		})
		{
			string script = Tweaks.Script(new[] { (step.Id, step.On) });
			int at = script.IndexOf(step.Expect, StringComparison.Ordinal);
			Assert.True(at >= 0, step.Id + " does not run " + step.Expect);
			// The lines straight after the removal are its own try/catch, and must not check the exit code.
			// (Turning the power plan off also puts Balanced back, and that step IS checked, so only the
			// removal's own block is looked at here.)
			string after = script.Substring(at, Math.Min(120, script.Length - at));
			Assert.DoesNotContain("$failed++", after);
		}
		// The marker is never left in the script itself.
		string all = Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList())
			+ Tweaks.Script(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList());
		Assert.DoesNotContain("  ?", all);
		Assert.DoesNotContain("?bcdedit", all);
		Assert.DoesNotContain("?powercfg", all);
		// A step that really can fail is still checked: setting a boot value, and the power plan's own writes.
		Assert.Contains("if ($LASTEXITCODE -ne 0) { $failed++ }", Tweaks.Script(new[] { ("dynamictick", true) }));
		Assert.Contains("if ($LASTEXITCODE -ne 0) { $failed++ }", Tweaks.Script(new[] { ("powerplan", true) }));
	}

	[Fact]
	public void AStaleExitCodeIsNotCountedAsAFailedStep()
	{
		// Run() clears $LASTEXITCODE first, because a .NET call or a cmdlet does not set it and the check
		// would otherwise read whatever external command ran before it in the script.
		string script = Tweaks.Script(new[] { ("accel", true) });
		int clear = script.IndexOf("$global:LASTEXITCODE = 0", StringComparison.Ordinal);
		int check = script.IndexOf("if ($LASTEXITCODE -ne 0)", StringComparison.Ordinal);
		Assert.True(clear >= 0, "the exit code is never cleared");
		Assert.True(clear < check, "the exit code is checked before it is cleared");
	}

	[Theory]
	[InlineData("0x26", 38)]
	[InlineData("38", 38)]
	[InlineData("0xFFFFFFFF", 4294967295L)]
	[InlineData("10", 10)]
	public void HexAndDecimalReadTheSame(string text, long expected)
	{
		Assert.True(Tweaks.TryNumber(text, out long value));
		Assert.Equal(expected, value);
	}

	[Theory]
	[InlineData("", false)]
	[InlineData("   ", false)]
	[InlineData("abc", false)]
	[InlineData(null, false)]
	public void WhatIsNotANumberIsNotTreatedAsOne(string text, bool expected)
	{
		Assert.Equal(expected, Tweaks.TryNumber(text, out _));
	}

	[Fact]
	public void AValueMatchesWhateverFormItWasWrittenIn()
	{
		// 0x26 in the catalog and 38 in the registry are the same setting.
		Assert.True(Tweaks.Matches("0x26", 38));
		Assert.True(Tweaks.Matches("38", 38));
		Assert.True(Tweaks.Matches("0", "0"));
		Assert.False(Tweaks.Matches("0", "1"));
		// Delete means "the value should not be there".
		Assert.True(Tweaks.Matches(Tweaks.Delete, null));
		Assert.False(Tweaks.Matches(Tweaks.Delete, 1));
		Assert.False(Tweaks.Matches("1", null));
	}

	[Fact]
	public void StateIsOnOnlyWhenEveryValueIsSet()
	{
		Tweaks.Tweak accel = Tweaks.Find("accel");
		// All three thresholds at zero: acceleration is off, so the entry is on.
		Assert.Equal("on", Tweaks.StateOf(accel, (Tweaks.Reg r) => "0"));
		// Windows' own values: the entry is off.
		Assert.Equal("off", Tweaks.StateOf(accel, (Tweaks.Reg r) => r.Off));
		// One of the three left behind by something else: half set, which is worth showing as its own state.
		Assert.Equal("part", Tweaks.StateOf(accel, (Tweaks.Reg r) =>
			(r.Name == "MouseSpeed") ? "0" : r.Off));
		// An entry with no registry values of its own cannot be read this way.
		Assert.Equal("unknown", Tweaks.StateOf(Tweaks.Find("dynamictick"), (Tweaks.Reg r) => null));
		Assert.Equal("unknown", Tweaks.StateOf(null, (Tweaks.Reg r) => null));
	}

	[Fact]
	public void TheLowLatencyPowerPlanIsBuiltAndCanBeRemovedAgain()
	{
		string on = Tweaks.Script(new[] { ("powerplan", true) });
		Assert.Contains("powercfg.exe /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", on);
		Assert.Contains(Tweaks.PlanGuid, on);
		// Processor minimum state and core parking at 100.
		Assert.Contains("893dee8e-2bef-41e0-89c6-b55d0929964c 100", on);
		Assert.Contains("0cc5b647-c1df-4637-891a-dec35c318583 100", on);
		// USB selective suspend off.
		Assert.Contains("48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0", on);
		string off = Tweaks.Script(new[] { ("powerplan", false) });
		// Turning it off puts Balanced back and removes the plan we made, not one of the user's own.
		Assert.Contains("powercfg.exe /setactive scheme_balanced", off);
		Assert.Contains("powercfg.exe /delete " + Tweaks.PlanGuid, off);
		Assert.DoesNotContain("/duplicatescheme", off);
	}

	[Fact]
	public void TheBootEntriesOnlyUseBcdeditAndAlwaysHaveAWayBack()
	{
		foreach (string id in new[] { "dynamictick", "platformclock", "dep" })
		{
			Tweaks.Tweak t = Tweaks.Find(id);
			Assert.NotEmpty(t.OnCommands);
			Assert.NotEmpty(t.OffCommands);
			Assert.True(t.NeedsReboot, id + " is a boot setting and must say it needs a restart");
			Assert.True(t.NeedsAdmin);
		}
		// Leaving the timer alone is done by deleting the overrides, never by setting one.
		string on = Tweaks.Script(new[] { ("platformclock", true) });
		Assert.Contains("bcdedit.exe /deletevalue useplatformclock", on);
		Assert.Contains("bcdedit.exe /deletevalue tscsyncpolicy", on);
		Assert.DoesNotContain("/set useplatformclock", on);
	}

	[Fact]
	public void EverySourceAndRiskHasWordsForTheWindow()
	{
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			Assert.False(string.IsNullOrWhiteSpace(Tweaks.SourceText(t.Source)));
			Assert.False(string.IsNullOrWhiteSpace(Tweaks.RiskText(t.Risk)));
		}
		foreach ((string id, string title, string blurb) in Tweaks.Groups)
		{
			Assert.False(string.IsNullOrWhiteSpace(title));
			Assert.False(string.IsNullOrWhiteSpace(blurb));
			Assert.Equal(title, Tweaks.GroupTitle(id));
			// Every group has at least one entry, or the window draws an empty heading.
			Assert.Contains(Tweaks.Catalog, (Tweaks.Tweak t) => t.Group == id);
		}
	}

	[Fact]
	public void AnythingUnderHklmOrRunAsAToolNeedsAdminRights()
	{
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			bool hklm = t.Values.Any((Tweaks.Reg r) => r.Hive == "HKLM");
			bool tool = t.OnCommands.Concat(t.OffCommands).Any((string c) =>
				c.Contains("bcdedit") || c.Contains("powercfg") || c.Contains("netsh")
				|| c.Contains("MMAgent") || c.Contains("LiteSynapse.Net"));
			if (hklm || tool)
			{
				Assert.True(t.NeedsAdmin, t.Id + " writes outside this account and must ask for admin rights");
			}
		}
		// And the mouse settings, which the user will flip most often, must not need it.
		Assert.False(Tweaks.Find("accel").NeedsAdmin);
		Assert.False(Tweaks.Find("rawthrottle").NeedsAdmin);
		Assert.False(Tweaks.Find("gamemode").NeedsAdmin);
	}
}

// The device-latency page: reading the power-management query, and the script that changes it.
public class DeviceTuningTests
{
	[Theory]
	[InlineData("USB\\VID_1532&PID_008D\\6&1ad0a2c8&0&3_0", true)]
	[InlineData("USB\\VID_1532&PID_0235\\5&x&0&1_0", true)]
	[InlineData("usb\\vid_1532&pid_008d\\a_0", true)]
	// Another Razer device, and other vendors, are left alone.
	[InlineData("USB\\VID_1532&PID_0099\\x_0", false)]
	[InlineData("USB\\VID_046D&PID_C52B\\x_0", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void OnlyOurTwoDevices(string instance, bool expected)
	{
		Assert.Equal(expected, DeviceTuning.IsOurDevice(instance));
	}

	[Fact]
	public void TheQueryOutputIsRead()
	{
		TunedDevice d = DeviceTuning.ParseDevice("True|USB\\VID_1532&PID_008D\\6&x&0&3_0|Razer Naga");
		Assert.NotNull(d);
		Assert.True(d.PowerSaving);
		Assert.Equal("Razer Naga", d.Name);
		Assert.Equal("USB\\VID_1532&PID_008D\\6&x&0&3_0", d.Instance);
		// No friendly name: one is made from the product id rather than shown blank.
		TunedDevice e = DeviceTuning.ParseDevice("False|USB\\VID_1532&PID_0235\\x_0|");
		Assert.False(e.PowerSaving);
		Assert.Equal("Razer BlackWidow Lite", e.Name);
	}

	[Theory]
	// Noise from PowerShell, another vendor's device, and anything malformed is ignored rather than shown.
	[InlineData("")]
	[InlineData("   ")]
	[InlineData(null)]
	[InlineData("some stray line")]
	[InlineData("True|USB\\VID_046D&PID_C52B\\x_0|Logitech")]
	[InlineData("maybe|USB\\VID_1532&PID_008D\\x_0|Razer Naga")]
	[InlineData("True|")]
	public void AnythingElseIsIgnored(string line)
	{
		Assert.Null(DeviceTuning.ParseDevice(line));
	}

	[Theory]
	[InlineData(null, "unknown")]
	[InlineData(0, "off")]
	[InlineData(1, "on")]
	public void ThePowerPlansUsbSetting(int? value, string expected)
	{
		Assert.Equal(expected, DeviceTuning.SuspendState(value));
	}

	[Fact]
	public void KeepingADeviceAwakeSetsBothCheckboxes()
	{
		string script = DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\x_0" }, powerPlan: false, allowPowerSaving: false);
		Assert.Contains("MSPower_DeviceEnable", script);
		Assert.Contains("MSPower_DeviceWakeEnable", script);
		Assert.Contains("Enable = $false", script);
		Assert.DoesNotContain("Enable = $true", script);
		// The instance name is quoted and matched exactly, not used as a pattern.
		Assert.Contains("-eq 'USB\\VID_1532&PID_008D\\x_0'", script);
	}

	[Fact]
	public void LettingWindowsManageItAgainIsTheSameCallTheOtherWay()
	{
		string script = DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\x_0" }, powerPlan: true, allowPowerSaving: true);
		Assert.Contains("Enable = $true", script);
		Assert.Contains(DeviceTuning.UsbSuspendSetting + " 1", script);
		string other = DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\x_0" }, powerPlan: true, allowPowerSaving: false);
		Assert.Contains(DeviceTuning.UsbSuspendSetting + " 0", other);
	}

	[Fact]
	public void ADeviceThatIsNotOursIsNeverTouched()
	{
		string script = DeviceTuning.Script(new[] { "USB\\VID_046D&PID_C52B\\x_0", "", null }, powerPlan: false, allowPowerSaving: false);
		Assert.Equal("$failed = 0\nexit $failed\n", script);
		Assert.Equal("$failed = 0\nexit $failed\n", DeviceTuning.Script(null, powerPlan: false, allowPowerSaving: false));
	}

	[Fact]
	public void TheSummarySaysWhatIsLeft()
	{
		DeviceTuningState none = new DeviceTuningState();
		Assert.Contains("No Razer device", DeviceTuning.Summary(none));
		Assert.Contains("No Razer device", DeviceTuning.Summary(null));
		DeviceTuningState awake = new DeviceTuningState
		{
			Devices = new List<TunedDevice> { new TunedDevice { Instance = "a", PowerSaving = false } }
		};
		Assert.Contains("no longer power", DeviceTuning.Summary(awake));
		Assert.False(awake.NeedsWork);
		DeviceTuningState asleep = new DeviceTuningState
		{
			Devices = new List<TunedDevice>
			{
				new TunedDevice { Instance = "a", PowerSaving = true },
				new TunedDevice { Instance = "b", PowerSaving = false }
			}
		};
		Assert.Contains("1 of 2", DeviceTuning.Summary(asleep));
		Assert.True(asleep.NeedsWork);
		// The power plan on its own is also work worth doing.
		Assert.True(new DeviceTuningState { UsbSuspend = "on" }.NeedsWork);
		Assert.False(new DeviceTuningState { UsbSuspend = "unknown" }.NeedsWork);
	}
}
