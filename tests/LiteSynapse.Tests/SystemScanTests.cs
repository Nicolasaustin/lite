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
	public void TheScriptOnlyEverNamesTheCatalogsOwnSpelling()
	{
		// The id that reaches Script() comes from a registry subkey name, so this is the place an injection
		// would arrive. Script() looks the name up and emits the catalog's string, not the caller's: a name
		// that does not match an entry exactly produces nothing, and one that does is written as the catalog
		// spells it. Both halves matter, so both are asserted.
		foreach (string crafted in new[]
		{
			"DiagTrack'; Remove-Item -Recurse C:\\ #",
			"DiagTrack`; evil",
			"DiagTrack ; evil",
			"DiagTrack\nevil",
			"$(evil)",
			"'; sc.exe delete WinDefend; '"
		})
		{
			string script = ServiceCatalog.Script(new[] { (crafted, false) });
			Assert.Equal("$failed = 0\nexit $failed\n", script);
		}
		// Matched case-insensitively, but written out as the catalog has it.
		string ok = ServiceCatalog.Script(new[] { ("  diagtrack  ", false) });
		Assert.Contains("'DiagTrack'", ok);
		Assert.DoesNotContain("diagtrack", ok);
		// And a name carrying an apostrophe that somehow did match would still be quoted, never bare.
		Assert.DoesNotContain("sc.exe config DiagTrack", ok);
	}

	[Fact]
	public void APerUserTemplateIsNotStoppedByName()
	{
		// What runs is an instance with a logon-id suffix (OneSyncSvc_49cd3), so "sc stop OneSyncSvc" cannot
		// reach it. The start-type change is what takes effect, at the next sign-in, and the row says so.
		foreach (string name in new[] { "OneSyncSvc", "CDPUserSvc", "PimIndexMaintenanceSvc", "MessagingService", "BcastDVRUserService" })
		{
			ServiceCatalog.Row row = ServiceCatalog.Find(name);
			Assert.True(row.PerUser, name + " is a per-user template");
			Assert.Contains("next sign-in", row.Lose);
			string off = ServiceCatalog.Script(new[] { (name, false) });
			Assert.Contains("start= disabled", off);
			Assert.DoesNotContain("sc.exe stop", off);
			// And turning it back on does not try to start a template either.
			Assert.DoesNotContain("sc.exe start", ServiceCatalog.Script(new[] { (name, true) }));
		}
		// An ordinary service still gets both.
		string normal = ServiceCatalog.Script(new[] { ("DiagTrack", false) });
		Assert.Contains("sc.exe stop", normal);
	}

	[Fact]
	public void ARowWindowsShipsDisabledIsNotOfferedAsSomethingToTurnBackOn()
	{
		// Writing "disabled" over "disabled" and then reporting a change is worse than not offering it.
		foreach (string name in new[] { "tzautoupdate", "RemoteRegistry", "RemoteAccess" })
		{
			Assert.Equal(ServiceCatalog.Disabled, ServiceCatalog.Find(name).Default);
		}
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
	// The work is emitted in two halves - this account's registry, run without elevation, and everything
	// else, run with it - so a test that wants the whole picture asks for both.
	private static string Both(params (string Id, bool On)[] choices)
	{
		return Tweaks.Script(choices, Tweaks.UserScope) + Tweaks.Script(choices, Tweaks.MachineScope);
	}

	private static string Both(List<(string Id, bool On)> choices)
	{
		return Tweaks.Script(choices, Tweaks.UserScope) + Tweaks.Script(choices, Tweaks.MachineScope);
	}


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
			// Something has to actually happen, in both directions - except for a row that only reads (the power
			// plan), which must do nothing at all.
			if (t.ReportOnly)
			{
				Assert.True(t.Values.Length == 0 && t.OnCommands.Length == 0 && t.OffCommands.Length == 0,
					t.Id + " is read only and must not write anything");
				return;
			}
			Assert.True(t.Values.Length > 0 || t.OnCommands.Length > 0, t.Id + " does nothing when turned on");
			Assert.True(t.Values.Length > 0 || t.OffCommands.Length > 0, t.Id + " cannot be turned off again");
			foreach (Tweaks.Reg r in t.Values)
			{
				Assert.Contains(r.Hive, new[] { "HKLM", "HKCU" });
				Assert.False(string.IsNullOrWhiteSpace(r.Key));
				Assert.False(string.IsNullOrWhiteSpace(r.Name));
				Assert.Contains(r.Kind, new[] { "dword", "string", "qword", "keylist" });
				// Both states must be written out, or the entry is one-directional.
				Assert.NotNull(r.On);
				Assert.NotNull(r.Off);
			}
		});
		Assert.Equal(Tweaks.Catalog.Length,
			Tweaks.Catalog.Select((Tweaks.Tweak t) => t.Id.ToLowerInvariant()).Distinct().Count());
	}

	[Fact]
	public void AMultiKeyValueIsMergedIntoRatherThanOverwritten()
	{
		// DirectXUserGlobalSettings is one REG_SZ holding a key=value list that Windows manages too - it also
		// carries the user's global Auto HDR preference. Writing the whole string threw that away, and
		// nothing put it back.
		string on = Both(("flip", true));
		Assert.Contains("Get-ItemProperty", on);
		Assert.Contains("$map[", on);
		Assert.Contains("'SwapEffectUpgradeEnable'", on);
		Assert.Contains("'VRROptimizeEnable'", on);
		// The old whole-value write must be gone.
		Assert.DoesNotContain("-Value 'VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;'", on);
	}

	[Fact]
	public void AKeyListReadsBackWhateverOrderWindowsWroteIt()
	{
		// Order is Windows' choice, and other keys in the same value are the user's business: every key we
		// asked for has to be present with the value we asked for, and nothing more is required.
		Assert.True(Tweaks.Matches("A=0;B=0;", "B=0;A=0;"));
		Assert.True(Tweaks.Matches("A=0;B=0;", "A=0;B=0;AutoHDREnable=1;"));
		Assert.False(Tweaks.Matches("A=0;B=0;", "A=0;B=1;"));
		Assert.False(Tweaks.Matches("A=0;B=0;", "A=0;"));
		Assert.Equal(2, Tweaks.Pairs("A=0;B=1;").Count);
		Assert.Equal("1", Tweaks.Pairs("A=0;B=1;")["b"]);
		Assert.True(Tweaks.IsKeyList("A=0;B=1;"));
		Assert.False(Tweaks.IsKeyList("38"));
		Assert.False(Tweaks.IsKeyList(null));
	}

	[Fact]
	public void TheHiveSplitPutsEachHalfWhereItBelongs()
	{
		// An entry owning both hives has to be split: an elevated PowerShell's HKCU: is the elevating
		// administrator's profile, which on a standard-user account is somebody else's.
		string user = Tweaks.Script(new[] { ("gamedvr", true) }, Tweaks.UserScope);
		string machine = Tweaks.Script(new[] { ("gamedvr", true) }, Tweaks.MachineScope);
		Assert.Contains("HKCU:", user);
		Assert.DoesNotContain("HKLM:", user);
		Assert.Contains("HKLM:", machine);
		Assert.DoesNotContain("HKCU:", machine);
		// And HasWork agrees with what each half actually emits.
		Assert.True(Tweaks.HasWork(new[] { ("gamedvr", true) }, Tweaks.UserScope));
		Assert.True(Tweaks.HasWork(new[] { ("gamedvr", true) }, Tweaks.MachineScope));
		// A machine-only entry has nothing for the unelevated half, so no PowerShell is started for it.
		Assert.False(Tweaks.HasWork(new[] { ("priority", true) }, Tweaks.UserScope));
		Assert.True(Tweaks.HasWork(new[] { ("priority", true) }, Tweaks.MachineScope));
		// The pointer calls belong to the user, and must not be sent to the elevated half.
		Assert.Contains("LiteSynapse.Pointer", Tweaks.Script(new[] { ("accel", true) }, Tweaks.UserScope));
		Assert.Equal("$failed = 0\nexit $failed\n", Tweaks.Script(new[] { ("accel", true) }, Tweaks.MachineScope));
		Assert.False(Tweaks.HasWork(null, Tweaks.UserScope));
	}

	[Fact]
	public void ThePowerPlanIsNeverWrittenFromHere()
	{
		// The power plan is the user's (they switch between their own optimizer plans): the row only reads it.
		Tweaks.Tweak plan = Tweaks.Find("powerplan");
		Assert.True(plan.ReportOnly);
		Assert.Empty(plan.Values);
		Assert.Empty(plan.OnCommands);
		Assert.Empty(plan.OffCommands);
		foreach (string scope in new[] { Tweaks.UserScope, Tweaks.MachineScope })
		{
			Assert.False(Tweaks.HasWork(new[] { ("powerplan", true) }, scope));
			Assert.False(Tweaks.HasWork(new[] { ("powerplan", false) }, scope));
		}
		Assert.DoesNotContain("powercfg", Both(("powerplan", true)));
		Assert.DoesNotContain("powercfg", Both(("powerplan", false)));
		// Nothing in the whole catalog, either way, touches a plan: hibernation is the only powercfg call left,
		// and it is not part of any plan.
		string all = Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList())
			+ Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList());
		foreach (string verb in new[] { "/setactive", "/s ", "/setacvalueindex", "/setdcvalueindex", "/duplicatescheme",
			"/delete", "/change", "/import", "/x ", "/attributes" })
		{
			Assert.DoesNotContain("powercfg.exe " + verb, all);
		}
		foreach (string line in all.Split('\n').Where((string l) => l.Contains("powercfg")))
		{
			Assert.Contains("/hibernate", line);
		}
	}

	[Fact]
	public void ADeleteThatWasRefusedIsCountedAsAFailure()
	{
		// SilentlyContinue hid "the value was not there" and "Windows would not let me" equally, so a
		// policy-locked key reported complete success. The value is read back instead.
		string off = Both(("mitigations", false));
		Assert.Contains("Remove-ItemProperty", off);
		Assert.Contains("Get-ItemProperty", off);
		Assert.Contains("{ $failed++ }", off);
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
		string all = Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList())
			+ Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList());
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
		string one = Both(("accel", true));
		Assert.Contains("MouseThreshold1", one);
		Assert.DoesNotContain("EnableLUA", one);
		Assert.DoesNotContain("bcdedit", one);
		Assert.StartsWith("$failed = 0\n", one);
		Assert.EndsWith("exit $failed\n", one);
		// An id that is not in the catalog is skipped, not guessed at.
		foreach (string scope in new[] { Tweaks.UserScope, Tweaks.MachineScope })
		{
			Assert.Equal("$failed = 0\nexit $failed\n", Tweaks.Script(new[] { ("rm -rf /", true) }, scope));
			Assert.Equal("$failed = 0\nexit $failed\n", Tweaks.Script(new (string, bool)[0], scope));
			Assert.Equal("$failed = 0\nexit $failed\n", Tweaks.Script(null, scope));
		}
	}

	[Fact]
	public void OnAndOffProduceDifferentScriptsForEveryEntry()
	{
		foreach (Tweaks.Tweak t in Tweaks.Catalog)
		{
			string on = Both((t.Id, true));
			string off = Both((t.Id, false));
			// Pointer speed is the one entry whose two states ask for the same value (the middle notch), so it
			// is the only switchable one allowed to produce the same script both ways. A read-only row produces
			// nothing either way (ThePowerPlanIsNeverWrittenFromHere).
			if (t.Id == "pointerspeed" || t.ReportOnly)
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
		string plain = Both(("gamemode", true));
		Assert.DoesNotContain("LiteSynapse.Pointer", plain);
		Assert.DoesNotContain("LiteSynapse.Net", plain);
		Assert.Contains("LiteSynapse.Pointer", Both(("accel", true)));
		Assert.Contains("LiteSynapse.Net", Both(("nagle", true)));
		// And only once per half, however many entries want it.
		string one = Tweaks.Script(new[] { ("accel", true), ("pointerspeed", true) }, Tweaks.UserScope);
		Assert.Equal(1, one.Split("Add-Type").Length - 1);
	}

	[Theory]
	// 0xFFFFFFFF does not fit a signed int, and New-ItemProperty takes a signed one: it has to arrive as -1,
	// bracketed so it cannot read as a parameter name.
	[InlineData("netthrottle", "(-1)")]
	// A hex value in the catalog is written as the decimal Windows expects.
	[InlineData("priority", "38")]
	public void NumbersAreWrittenAsWindowsWantsThem(string id, string expected)
	{
		Assert.Contains("-Value " + expected + " ", Both((id, true)));
	}

	[Fact]
	public void RemovingSomethingThatIsNotThereIsNotAFailedStep()
	{
		// bcdedit /deletevalue on a value that was never set exits non-zero, which means "already in the state you
		// asked for", so it may not be counted.
		foreach ((string Id, bool On, string Expect) step in new[]
		{
			("platformclock", true, "bcdedit.exe /deletevalue useplatformclock"),
			("dynamictick", false, "bcdedit.exe /deletevalue disabledynamictick"),
			("dep", false, "bcdedit.exe /deletevalue nx")
		})
		{
			string script = Both((step.Id, step.On));
			int at = script.IndexOf(step.Expect, StringComparison.Ordinal);
			Assert.True(at >= 0, step.Id + " does not run " + step.Expect);
			// The lines straight after the removal are its own try/catch, and must not check the exit code.
			string after = script.Substring(at, Math.Min(120, script.Length - at));
			Assert.DoesNotContain("$failed++", after);
		}
		// The marker is never left in the script itself.
		string all = Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList())
			+ Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList());
		Assert.DoesNotContain("  ?", all);
		Assert.DoesNotContain("?bcdedit", all);
		Assert.DoesNotContain("?powercfg", all);
		// A step that really can fail is still checked: setting a boot value.
		Assert.Contains("if ($LASTEXITCODE -ne 0) { $failed++ }", Both(("dynamictick", true)));
	}

	[Fact]
	public void AStaleExitCodeIsNotCountedAsAFailedStep()
	{
		// Run() clears $LASTEXITCODE first, because a .NET call or a cmdlet does not set it and the check
		// would otherwise read whatever external command ran before it in the script.
		string script = Both(("accel", true));
		int clear = script.IndexOf("$global:LASTEXITCODE = 0", StringComparison.Ordinal);
		int check = script.IndexOf("if ($LASTEXITCODE -ne 0)", StringComparison.Ordinal);
		Assert.True(clear >= 0, "the exit code is never cleared");
		Assert.True(clear < check, "the exit code is checked before it is cleared");
	}

	[Theory]
	[InlineData("0x26", 38)]
	[InlineData("38", 38)]
	// Narrowed to 32 bits on purpose: a DWORD read back out of the registry is a signed int, so 0xFFFFFFFF
	// has to compare equal to the -1 that Windows hands us or the setting reads as never applied.
	[InlineData("0xFFFFFFFF", -1L)]
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
		// Set to something that is neither state: reported as its own thing, because for an entry with one
		// value "half set" would be plainly wrong.
		Assert.Equal("other", Tweaks.StateOf(accel, (Tweaks.Reg r) => "99"));
		// A key list is ours or not: anything without our keys at our values is the Windows side, including
		// Windows' own toggle for the same keys and its Auto HDR key in the same value.
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("flip"), (Tweaks.Reg r) => "something else entirely"));
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("flip"), (Tweaks.Reg r) => "SwapEffectUpgradeEnable=1;"));
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("flip"), (Tweaks.Reg r) =>
			"AutoHDREnable=1;VRROptimizeEnable=0;SwapEffectUpgradeEnable=0;"));
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("flip"), (Tweaks.Reg r) => null));
		// An entry with no registry values of its own cannot be read this way.
		Assert.Equal("unknown", Tweaks.StateOf(Tweaks.Find("dynamictick"), (Tweaks.Reg r) => null));
		Assert.Equal("unknown", Tweaks.StateOf(null, (Tweaks.Reg r) => null));
	}

	[Fact]
	public void AMissingValueIsWindowsDefault()
	{
		// Game Mode is on when its values are missing, which is how Windows ships: ON, not "set by something else".
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("gamemode"), (Tweaks.Reg r) => null));
		// Background apps are allowed when GlobalUserDisabled is missing: the Windows side.
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("bgapps"), (Tweaks.Reg r) => null));
		// HAGS missing means the driver's own default, which differs per GPU: cannot tell.
		Assert.Equal("unknown", Tweaks.StateOf(Tweaks.Find("hags"), (Tweaks.Reg r) => null));
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("hags"), (Tweaks.Reg r) => 2));
	}

	[Fact]
	public void AnotherOptimizersEquallyGoodValueCountsAsApplied()
	{
		// Values tweak guides use besides ours: the PC is tuned, and the row says so.
		// Short quanta with the 3:1 boost however it is spelled - Windows' own 2 included, which on a client
		// edition means exactly what 26 hex spells out. Long or unboosted slices are the other side.
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("priority"), (Tweaks.Reg r) => 0x2A));
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("priority"), (Tweaks.Reg r) => 2));
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("priority"), (Tweaks.Reg r) => null));
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("priority"), (Tweaks.Reg r) => 0x18));
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("priority"), (Tweaks.Reg r) => 0x28));
		Assert.True(Tweaks.Find("priority").OneWay);
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("mousequeue"), (Tweaks.Reg r) => 20));
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("mousequeue"), (Tweaks.Reg r) => 100));
		Assert.Equal("on", Tweaks.StateOf(Tweaks.Find("responsiveness"), (Tweaks.Reg r) => 0));
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("responsiveness"), (Tweaks.Reg r) => 20));
		Assert.Equal("other", Tweaks.StateOf(Tweaks.Find("responsiveness"), (Tweaks.Reg r) => 14));
	}

	[Fact]
	public void WindowsOwnDefaultValuesReadAsTheOffSide()
	{
		// Telemetry at Windows' own levels, advertising ID enabled, "let Windows choose" effects: the Windows side.
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("telemetry"), (Tweaks.Reg r) => r.Name switch
		{
			"AllowTelemetry" => 3,
			"Enabled" => 1,
			_ => null
		}));
		// Game Mode turned off in Settings writes only AutoGameModeEnabled.
		Assert.Equal("off", Tweaks.StateOf(Tweaks.Find("gamemode"), (Tweaks.Reg r) => (r.Name == "AutoGameModeEnabled") ? 0 : null));
	}

	[Fact]
	public void TurningWindowedOptimizationsBackOnKeepsWindowsOwnKeys()
	{
		// Only our two keys come out; Auto HDR and anything else Windows keeps in the value stays.
		string off = Tweaks.Script(new[] { ("flip", false) }, Tweaks.UserScope);
		Assert.Contains("$map.Remove('VRROptimizeEnable')", off);
		Assert.Contains("$map.Remove('SwapEffectUpgradeEnable')", off);
		Assert.DoesNotContain("$map['AutoHDREnable']", off);
	}

	[Fact]
	public void MemoryCompressionIsMarkedOnlyWhenItsCommandsWentThrough()
	{
		string on = Tweaks.Script(new[] { ("prefetch", true) }, Tweaks.MachineScope);
		int command = on.IndexOf("Disable-MMAgent -MemoryCompression", StringComparison.Ordinal);
		int value = on.IndexOf("EnablePrefetcher", StringComparison.Ordinal);
		Assert.True(command >= 0 && value > command, "the commands come first");
		Assert.Contains("if ($failed -eq $before) {", on);
		Dictionary<string, string> read = Tweaks.ParseAdminRead("mmagent|True|True\n");
		Assert.Equal("off", read["prefetch-mm"]);
		Assert.Equal("on", Tweaks.ParseAdminRead("mmagent|False|False\n")["prefetch-mm"]);
		Assert.Equal("part", Tweaks.ParseAdminRead("mmagent|False|True\n")["prefetch-mm"]);
	}

	[Fact]
	public void PointerSpeedIsOnlyRightAtTheMiddleNotch()
	{
		Tweaks.Tweak t = Tweaks.Find("pointerspeed");
		Assert.True(t.OneWay);
		Assert.Equal("on", Tweaks.StateOf(t, (Tweaks.Reg r) => "10"));
		// Any other notch scales the mouse's counts: the hurting side, not "set by something else".
		Assert.Equal("off", Tweaks.StateOf(t, (Tweaks.Reg r) => "12"));
		Assert.Equal("off", Tweaks.StateOf(t, (Tweaks.Reg r) => "6"));
		// There is nothing to put back, so the off side writes nothing.
		Assert.DoesNotContain("MouseSensitivity", Tweaks.Script(new[] { ("pointerspeed", false) }, Tweaks.UserScope));
		Assert.Contains("MouseSensitivity", Tweaks.Script(new[] { ("pointerspeed", true) }, Tweaks.UserScope));
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
		string on = Both(("platformclock", true));
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
		Assert.True(d.Known);
		// Led by which of our devices it is, then what Windows calls it.
		Assert.Equal("Razer Naga Left-Handed Edition - Razer Naga", d.Name);
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
	// The query says so on its own line, because the process exit code cannot: with -ErrorAction
	// SilentlyContinue the script succeeded whether or not WMI answered, so "no devices" and "refused"
	// looked identical and the window told the user to plug the mouse in either way.
	[InlineData("READABLE|1", true)]
	[InlineData("READABLE|0", false)]
	[InlineData("  READABLE|1  ", true)]
	[InlineData("True|USB\\VID_1532&PID_008D\\x_0|Razer Naga", null)]
	[InlineData("", null)]
	[InlineData(null, null)]
	public void WhetherTheQueryCouldReadAtAll(string line, bool? expected)
	{
		Assert.Equal(expected, DeviceTuning.ParseReadable(line));
	}

	[Fact]
	public void TheQueryAsksForThePowerManagementClassAndSaysIfItFailed()
	{
		Assert.Contains("MSPower_DeviceEnable", DeviceTuning.QueryScript);
		Assert.Contains("READABLE|$ok", DeviceTuning.QueryScript);
		// -ErrorAction Stop, so the catch is what sets $ok: SilentlyContinue would make the failure invisible.
		Assert.Contains("-ErrorAction Stop", DeviceTuning.QueryScript);
		// The power-management suffix is any number, not just _0: a composite device uses _1, _2 and so on.
		Assert.Contains("'_\\d+$'", DeviceTuning.QueryScript);
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
		string script = DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\x_0" }, allowPowerSaving: false);
		Assert.Contains("MSPower_DeviceEnable", script);
		Assert.Contains("MSPower_DeviceWakeEnable", script);
		Assert.Contains("Enable = $false", script);
		Assert.DoesNotContain("Enable = $true", script);
		// The instance name is quoted and matched exactly, not used as a pattern - without the power-management
		// suffix on either side, so a Plug and Play id finds its WMI instance.
		Assert.Contains("-replace '_\\d+$', '') -eq 'USB\\VID_1532&PID_008D\\x'", script);
	}

	[Fact]
	public void WithoutAdminRightsTheDevicesAreStillListedAsUnknown()
	{
		// The unelevated query cannot read root\WMI; Plug and Play still lists the devices, with "?" for the state.
		Assert.Contains("Get-PnpDevice", DeviceTuning.QueryScript);
		TunedDevice d = DeviceTuning.ParseDevice("?|USB\\VID_1532&PID_008D\\6&x&0&3|USB Composite Device");
		Assert.NotNull(d);
		Assert.False(d.Known);
		Assert.Equal("Razer Naga Left-Handed Edition - USB Composite Device", d.Name);
		DeviceTuningState s = DeviceTuning.Parse("READABLE|0\n?|USB\\VID_1532&PID_008D\\6&x&0&3|USB Composite Device\n");
		Assert.False(s.Readable);
		Assert.Single(s.Devices);
		Assert.False(s.NeedsWork);
		Assert.Contains("administrator", DeviceTuning.Summary(s));
	}

	[Fact]
	public void TheLastAdminReadFillsInWhatTheUnelevatedReadCouldNot()
	{
		DeviceTuningState now = DeviceTuning.Parse("READABLE|0\n?|USB\\VID_1532&PID_008D\\6&x&0&3|\n?|USB\\VID_1532&PID_0235\\5&y|\n");
		// The admin read names its WMI instances with the suffix, and in another case.
		DeviceTuningState admin = DeviceTuning.Parse("\uFEFFREADABLE|1\nTrue|usb\\vid_1532&pid_008d\\6&X&0&3_0|Naga\nFalse|USB\\VID_1532&PID_0235\\5&y_0|\n");
		Assert.True(admin.Readable);
		DateTime at = new DateTime(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);
		DeviceTuning.Merge(now, admin, at);
		Assert.Equal(2, now.Devices.Count);
		Assert.All(now.Devices, (TunedDevice x) => Assert.True(x.Known));
		Assert.True(now.Devices[0].PowerSaving);
		Assert.False(now.Devices[1].PowerSaving);
		// The elevated step matches on the WMI name, so that is the one kept.
		Assert.EndsWith("_0", now.Devices[0].Instance);
		Assert.Equal(at, now.AdminAtUtc);
		Assert.True(now.NeedsWork);
		// An admin read that itself failed adds nothing.
		DeviceTuningState again = DeviceTuning.Parse("READABLE|0\n?|USB\\VID_1532&PID_008D\\6&x&0&3|\n");
		DeviceTuning.Merge(again, DeviceTuning.Parse("READABLE|0\n"), at);
		Assert.False(again.Devices[0].Known);
		Assert.Null(again.AdminAtUtc);
	}

	[Fact]
	public void TheChangeReadsTheDevicesBackInTheSameElevatedRun()
	{
		string script = DeviceTuning.MixedScript(new[] { "USB\\VID_1532&PID_008D\\x_0" }, new[] { "USB\\VID_1532&PID_0235\\y_0" },
			"C:\\Users\\me\\AppData\\Local\\Programs\\LiteSynapse\\data\\device-power.txt");
		Assert.StartsWith("$failed = 0\n", script);
		Assert.EndsWith("exit $failed\n", script);
		// Both directions in one run: one consent prompt.
		Assert.Contains("Enable = $false", script);
		Assert.Contains("Enable = $true", script);
		// The power plan is the user's: the device change never writes it.
		Assert.DoesNotContain("powercfg", script);
		// The read-back comes after the change, and goes to the file.
		Assert.True(script.LastIndexOf("Set-CimInstance", StringComparison.Ordinal) < script.IndexOf("READABLE|$ok", StringComparison.Ordinal));
		Assert.Contains("Set-Content -LiteralPath 'C:\\Users\\me\\AppData\\Local\\Programs\\LiteSynapse\\data\\device-power.txt'", script);
		// The halves are stitched: one counter, one exit, so the count cannot be reset halfway.
		Assert.Equal(1, script.Split("$failed = 0").Length - 1);
		Assert.Equal(1, script.Split("exit $failed").Length - 1);
		// Nothing switched: only the read.
		string none = DeviceTuning.MixedScript(null, null, "C:\\x.txt");
		Assert.DoesNotContain("Set-CimInstance", none);
		Assert.DoesNotContain("powercfg", none);
	}

	[Fact]
	public void ADeviceTheLastAdminReadNeverSawStaysOnThePage()
	{
		// The admin read saw only the Naga on port 3. Since then the Naga moved to port 4 and the BlackWidow
		// was plugged in: both must stay listed (unknown), so they can still be switched and read back.
		DeviceTuningState now = DeviceTuning.Parse("READABLE|0\nLISTED|1\n" +
			"?|USB\\VID_1532&PID_008D\\6&x&0&4|\n?|USB\\VID_1532&PID_0235\\5&b&0&1|\n");
		DeviceTuningState admin = DeviceTuning.Parse("READABLE|1\nTrue|USB\\VID_1532&PID_008D\\6&x&0&3_0|Naga\n");
		DeviceTuning.Merge(now, admin, DateTime.UtcNow);
		Assert.Equal(2, now.Devices.Count);
		Assert.All(now.Devices, (TunedDevice d) => Assert.False(d.Known));
		// A HID child of a device the read did see has no switch of its own and goes.
		DeviceTuningState seen = DeviceTuning.Parse("READABLE|0\nLISTED|1\n" +
			"?|USB\\VID_1532&PID_008D\\6&x&0&3|\n?|USB\\VID_1532&PID_008D&MI_02\\7&y&0&0002|\n");
		DeviceTuning.Merge(seen, admin, DateTime.UtcNow);
		Assert.Single(seen.Devices);
		Assert.True(seen.Devices[0].Known);
	}

	[Fact]
	public void NothingPluggedInShowsNothingEvenWithAnOldAdminRead()
	{
		DeviceTuningState admin = DeviceTuning.Parse("READABLE|1\nTrue|USB\\VID_1532&PID_008D\\6&x&0&3_0|Naga\n");
		DeviceTuningState unplugged = DeviceTuning.Parse("READABLE|0\nLISTED|1\n");
		Assert.True(unplugged.Listed);
		DeviceTuning.Merge(unplugged, admin, DateTime.UtcNow);
		Assert.Empty(unplugged.Devices);
		// When the listing itself could not run, the old read is the best there is.
		DeviceTuningState unknown = DeviceTuning.Parse("READABLE|0\nLISTED|0\n");
		DeviceTuning.Merge(unknown, admin, DateTime.UtcNow);
		Assert.Single(unknown.Devices);
	}

	[Fact]
	public void EveryCharacterPowerShellTreatsAsAQuoteIsEscaped()
	{
		// A user name like O\u2019Brien, or a start-up entry's file name crafted to end the literal inside the
		// elevated Defender scan: each of the five quote characters is doubled.
		foreach (char q in new[] { '\'', '\u2018', '\u2019', '\u201A', '\u201B' })
		{
			string quoted = RazerLeftovers.Ps("C:\\Users\\O" + q + "Brien\\x" + q + ";calc;" + q + ".exe");
			Assert.Equal("'C:\\Users\\O" + q + q + "Brien\\x" + q + q + ";calc;" + q + q + ".exe'", quoted);
		}
		Assert.Equal("''", RazerLeftovers.Ps(null));
	}
	[Fact]
	public void LettingWindowsManageItAgainIsTheSameCallTheOtherWay()
	{
		string script = DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\x_0" }, allowPowerSaving: true);
		Assert.Contains("Enable = $true", script);
		string other = DeviceTuning.Script(new[] { "USB\\VID_1532&PID_008D\\x_0" }, allowPowerSaving: false);
		Assert.Contains("Enable = $false", other);
		// Neither direction touches the power plan's own USB setting.
		Assert.DoesNotContain("powercfg", script + other);
	}

	[Fact]
	public void ADeviceThatIsNotOursIsNeverTouched()
	{
		string script = DeviceTuning.Script(new[] { "USB\\VID_046D&PID_C52B\\x_0", "", null }, allowPowerSaving: false);
		Assert.Equal("$failed = 0\nexit $failed\n", script);
		Assert.Equal("$failed = 0\nexit $failed\n", DeviceTuning.Script(null, allowPowerSaving: false));
	}

	[Fact]
	public void AnApostropheInAnInstanceNameCannotEndTheLiteral()
	{
		// The instance name comes from WMI rather than from a person, so this is belt-and-braces - but it is
		// the one string in these scripts that is not a catalog literal, and it costs nothing to be sure.
		string crafted = "USB\\VID_1532&PID_008D\\6&x'; sc.exe delete WinDefend; '_0";
		string script = DeviceTuning.Script(new[] { crafted }, allowPowerSaving: false);
		// The text is of course still in the script - it is the data being matched. What matters is that it
		// stays inside the literal: each apostrophe is doubled, so the quote count on every line is even and
		// nothing after the name is read as a command.
		Assert.Contains("''", script);
		Assert.Contains("-eq '", script);
		foreach (string line in script.Split('\n'))
		{
			Assert.True(line.Count((char c) => c == '\'') % 2 == 0, "unbalanced quotes: " + line);
		}
	}

	[Fact]
	public void TheSummarySaysWhatIsLeft()
	{
		// Read fine, found nothing: the mouse is probably unplugged.
		DeviceTuningState none = new DeviceTuningState { Readable = true };
		Assert.Contains("No Razer device", DeviceTuning.Summary(none));
		Assert.Contains("No Razer device", DeviceTuning.Summary(null));
		// Could not read at all: saying "plug the mouse in" would send the user after the wrong problem.
		DeviceTuningState refused = new DeviceTuningState { Readable = false };
		Assert.Contains("administrator", DeviceTuning.Summary(refused));
		Assert.DoesNotContain("Plug the mouse in", DeviceTuning.Summary(refused));
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
