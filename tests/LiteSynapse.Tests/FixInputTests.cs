using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// FIX DEAD RAZER DEVICES and the gate on TURN ON RAZER SERVICES (build 22): the decisions, the words, and the shipped
// recovery script.
public class FixInputTests
{
	[Fact]
	public void TheFixIsAnAdvertisedAction()
	{
		Assert.Contains("fixInput", RazerLeftovers.Actions);
		Assert.Equal("fix-razer-input.ps1", RazerLeftovers.FixScript);
	}

	[Fact]
	public void TurnOnRefusesWhileADeviceIsWedged()
	{
		RazerLeftovers r = new RazerLeftovers { Checked = true, SynapseInstalled = true, RzCommon = true };
		r.FilterDrivers.Add("RzDev_008d (Naga)");
		r.DeadDevices.Add(new DeadDevice { Name = "Razer Naga Left-Handed Edition", Problem = 39, Service = "RzDev_008d" });
		string why = RazerLeftovers.EnableRefusal(r);
		Assert.NotNull(why);
		Assert.StartsWith("Not turned on", why);
		Assert.Contains("Windows error 39 (driver missing or corrupt)", why);
		Assert.Contains("FIX DEAD RAZER DEVICES", why);
	}

	[Fact]
	public void TurnOnRefusesWhenStartingTheServicesWouldInstallTheDriver()
	{
		// Synapse installed, Razer's drivers removed: the services would put the drivers back on the fly.
		RazerLeftovers r = new RazerLeftovers { Checked = true, SynapseInstalled = true, RzCommon = false };
		string why = RazerLeftovers.EnableRefusal(r);
		Assert.NotNull(why);
		Assert.Contains("reinstall Synapse", why);
		Assert.Contains("FIX DEAD RAZER DEVICES", why);
		// The text never offers to turn them on anyway.
		Assert.DoesNotContain("anyway", why);
	}

	[Fact]
	public void TurnOnIsAllowedWhileRazersDriversAreInPlace()
	{
		RazerLeftovers withFilters = new RazerLeftovers { Checked = true, SynapseInstalled = true };
		withFilters.FilterDrivers.Add("RzDev_0235 (BlackWidow Lite)");
		Assert.Null(RazerLeftovers.EnableRefusal(withFilters));
		// RzCommon alone proves the stack is installed (the devices may be unplugged right now).
		RazerLeftovers withCommon = new RazerLeftovers { Checked = true, SynapseInstalled = true, RzCommon = true };
		Assert.Null(RazerLeftovers.EnableRefusal(withCommon));
		// Without Synapse there is nothing that could install a driver; EnableServices reports "nothing to turn on".
		RazerLeftovers noSynapse = new RazerLeftovers { Checked = true, SynapseInstalled = false };
		Assert.Null(RazerLeftovers.EnableRefusal(noSynapse));
		Assert.Null(RazerLeftovers.EnableRefusal(null));
	}

	[Theory]
	[InlineData(0, "OK")]
	[InlineData(10, "cannot start")]
	[InlineData(22, "disabled")]
	[InlineData(39, "driver missing or corrupt")]
	[InlineData(48, "driver blocked")]
	[InlineData(999, "code 999")]
	public void ProblemCodesReadInWords(int code, string words)
	{
		Assert.Equal(words, RazerLeftovers.ProblemText(code));
	}

	[Fact]
	public void ADeadDeviceIsLabelledWithWindowsCode()
	{
		DeadDevice d = new DeadDevice { Name = " Razer BlackWidow Lite ", Problem = 10, Service = "RzDev_0235" };
		Assert.Equal("Razer BlackWidow Lite: Windows error 10 (cannot start)", d.Label);
		Assert.Equal("Razer device: Windows error 39 (driver missing or corrupt)", new DeadDevice { Problem = 39 }.Label);
	}

	[Fact]
	public void AnUnnamedDeviceIsNamedFromItsUsbId()
	{
		Assert.Equal("Razer Naga", RazerLeftovers.DeviceName(new[] { "USB\\VID_1532&PID_008D&REV_0200", "USB\\VID_1532&PID_008D" }));
		Assert.Equal("Razer BlackWidow Lite", RazerLeftovers.DeviceName(new[] { "HID\\VID_1532&PID_0235&MI_00" }));
		Assert.Equal("Razer device", RazerLeftovers.DeviceName(new[] { "USB\\VID_1532&PID_0084" }));
		Assert.Equal("Razer device", RazerLeftovers.DeviceName(null));
	}

	[Fact]
	public void TheScriptRunsByItsFullPathOnly()
	{
		string args = RazerLeftovers.FixInputArguments("C:\\Program Files\\LiteSynapse\\tools\\fix-razer-input.ps1");
		Assert.Equal("-NoProfile -ExecutionPolicy Bypass -File \"C:\\Program Files\\LiteSynapse\\tools\\fix-razer-input.ps1\"", args);
	}

	[Fact]
	public void TheShippedScriptIsTheReviewedOne()
	{
		string path = FindInRepo(Path.Combine("src", "LiteSynapse", "tools", "fix-razer-input.ps1"));
		Assert.True(path != null, "src/LiteSynapse/tools/fix-razer-input.ps1 not found above " + AppContext.BaseDirectory);
		string text = File.ReadAllText(path);
		Assert.StartsWith("#Requires -RunAsAdministrator", text);
		// Only Razer's filter names are ever taken out of a device class; Windows' own stay.
		Assert.Contains("$_ -match '^rz'", text);
		Assert.Contains("$_ -notmatch '^rz'", text);
		// Windows' own driver is never removed, and the device entries are removed children first.
		Assert.Contains("mshidkmdf", text);
		Assert.Contains("pnputil.exe /remove-device $d.InstanceId /subtree", text);
		Assert.Contains("Sort-Object { $_.InstanceId.Length } -Descending", text);
		// Every change to a device class is read back before "removed" is printed.
		Assert.Contains("still lists", text);
		// No PowerShell automatic variable is overwritten, and nothing outside ASCII (Windows PowerShell reads the file as ANSI).
		Assert.DoesNotContain("$home", text, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("$input", text, StringComparison.OrdinalIgnoreCase);
		Assert.All(text, (char c) => Assert.True(c < 128, "non-ASCII character in fix-razer-input.ps1: U+" + ((int)c).ToString("X4")));
		// The power plan is never touched.
		Assert.DoesNotContain("powercfg", text, StringComparison.OrdinalIgnoreCase);
	}

	private static string FindInRepo(string relative)
	{
		DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
		for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
		{
			string candidate = Path.Combine(dir.FullName, relative);
			if (File.Exists(candidate))
			{
				return candidate;
			}
		}
		return null;
	}
}
