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
			// Something has to actually happen, in both directions.
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
	public void TheLowLatencyPlanIsMadeActiveBeforeItsTimeoutsAreSet()
	{
		// powercfg /change edits whichever scheme is ACTIVE. With /setactive last, the three timeouts landed
		// on the plan that happened to be active - Balanced, on a first run - so our plan never got them and
		// the user's Balanced plan was left unable to sleep, with nothing to put it back.
		string on = Both(("powerplan", true));
		int active = on.IndexOf("/setactive " + Tweaks.PlanGuid, StringComparison.Ordinal);
		int change = on.IndexOf("/change monitor-timeout-ac", StringComparison.Ordinal);
		Assert.True(active >= 0 && change >= 0);
		Assert.True(active < change, "the plan has to be active before /change can edit it");
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
			string script = Both((step.Id, step.On));
			int at = script.IndexOf(step.Expect, StringComparison.Ordinal);
			Assert.True(at >= 0, step.Id + " does not run " + step.Expect);
			// The lines straight after the removal are its own try/catch, and must not check the exit code.
			// (Turning the power plan off also puts Balanced back, and that step IS checked, so only the
			// removal's own block is looked at here.)
			string after = script.Substring(at, Math.Min(120, script.Length - at));
			Assert.DoesNotContain("$failed++", after);
		}
		// The marker is never left in the script itself.
		string all = Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, true)).ToList())
			+ Both(Tweaks.Catalog.Select((Tweaks.Tweak t) => (t.Id, false)).ToList());
		Assert.DoesNotContain("  ?", all);
		Assert.DoesNotContain("?bcdedit", all);
		Assert.DoesNotContain("?powercfg", all);
		// A step that really can fail is still checked: setting a boot value, and the power plan's own writes.
		Assert.Contains("if ($LASTEXITCODE -ne 0) { $failed++ }", Both(("dynamictick", true)));
		Assert.Contains("if ($LASTEXITCODE -ne 0) { $failed++ }", Both(("powerplan", true)));
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
		Assert.Equal("other", Tweaks.StateOf(Tweaks.Find("flip"), (Tweaks.Reg r) => "something else entirely"));
		// An entry with no registry values of its own cannot be read this way.
		Assert.Equal("unknown", Tweaks.StateOf(Tweaks.Find("dynamictick"), (Tweaks.Reg r) => null));
		Assert.Equal("unknown", Tweaks.StateOf(null, (Tweaks.Reg r) => null));
	}

	[Fact]
	public void TheLowLatencyPowerPlanIsBuiltAndCanBeRemovedAgain()
	{
		string on = Both(("powerplan", true));
		Assert.Contains("powercfg.exe /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61", on);
		Assert.Contains(Tweaks.PlanGuid, on);
		// Processor minimum state and core parking at 100.
		Assert.Contains("893dee8e-2bef-41e0-89c6-b55d0929964c 100", on);
		Assert.Contains("0cc5b647-c1df-4637-891a-dec35c318583 100", on);
		// USB selective suspend off.
		Assert.Contains("48e6b7a6-50f5-4782-a5d4-53bb8f07e226 0", on);
		string off = Both(("powerplan", false));
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
	public void AnApostropheInAnInstanceNameCannotEndTheLiteral()
	{
		// The instance name comes from WMI rather than from a person, so this is belt-and-braces - but it is
		// the one string in these scripts that is not a catalog literal, and it costs nothing to be sure.
		string crafted = "USB\\VID_1532&PID_008D\\6&x'; sc.exe delete WinDefend; '_0";
		string script = DeviceTuning.Script(new[] { crafted }, powerPlan: false, allowPowerSaving: false);
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
