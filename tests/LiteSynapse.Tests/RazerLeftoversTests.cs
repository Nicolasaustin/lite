using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace LiteSynapse.Tests;

public class RazerLeftoversTests
{
	[Theory]
	[InlineData(false, 2, 4, "absent")]
	[InlineData(true, 2, 4, "running")]
	[InlineData(true, 4, 4, "running")]
	[InlineData(true, 2, 1, "stopped")]
	[InlineData(true, 3, 1, "stopped")]
	[InlineData(true, 4, 1, "disabled")]
	[InlineData(true, 4, 0, "disabled")]
	[InlineData(true, 2, 2, "running")]
	public void ServiceState(bool exists, int start, int state, string expected)
	{
		Assert.Equal(expected, RazerLeftovers.ServiceState(exists, start, state));
	}

	[Fact]
	public void FiltersAreNamedForTheUser()
	{
		List<string> labels = RazerLeftovers.FilterLabels(new[] { "RzDev_008d", "mouclass", "rzdev_0235", "mshidkmdf", null }).ToList();
		Assert.Equal(new List<string> { "RzDev_008d (Naga)", "RzDev_0235 (BlackWidow Lite)" }, labels);
		Assert.Empty(RazerLeftovers.FilterLabels(null));
	}

	[Theory]
	[InlineData("Razer Synapse", true)]
	[InlineData("Razer Synapse 4", true)]
	[InlineData("RazerAppEngine", true)]
	[InlineData("Razer App Engine", true)]
	[InlineData("Razer Cortex", false)]
	[InlineData("LiteSynapse", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void SynapseUninstallEntries(string name, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsSynapseEntry(name));
	}

	[Theory]
	[InlineData("RazerAppEngine", true)]
	[InlineData("razerappengine", true)]
	[InlineData("RzEngineMon", true)]
	[InlineData("Razer Synapse 3", true)]
	[InlineData("Razer Synapse Service", true)]
	[InlineData("GameManagerService3", false)]
	[InlineData("LiteSynapse", false)]
	[InlineData("msedgewebview2", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void SynapseProcesses(string name, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsSynapseProcess(name));
	}

	[Fact]
	public void MultiSzIsSplit()
	{
		byte[] data = Encoding.Unicode.GetBytes("RzDev_008d\0kbdhid\0\0");
		Assert.Equal(new List<string> { "RzDev_008d", "kbdhid" }, RazerLeftovers.ParseMultiSz(data, data.Length));
		Assert.Empty(RazerLeftovers.ParseMultiSz(null, 0));
	}

	[Fact]
	public void DefaultsMeanNotCheckedAndNothingFound()
	{
		RazerLeftovers r = new RazerLeftovers();
		Assert.False(r.Checked);
		Assert.Equal("absent", r.GmsService);
		Assert.Empty(r.FilterDrivers);
		Assert.Empty(r.Autostart);
		Assert.Empty(r.Services);
		Assert.Equal("absent", r.UpgradeTask);
		Assert.Empty(r.RunOnce);
		Assert.Equal("not checked", r.ToString());
		Assert.True(PlanOf(r).IsEmpty);
	}

	// ---- Background services, autostart, upgrade task (native analysis) ----

	[Theory]
	[InlineData(2, 4, false, "running")]
	[InlineData(4, 4, false, "running")]
	[InlineData(3, 3, false, "running")]
	[InlineData(2, 1, false, "auto")]
	[InlineData(1, 1, false, "auto")]
	[InlineData(3, 1, false, "manual")]
	[InlineData(3, 0, false, "manual")]
	[InlineData(4, 1, false, "disabled")]
	[InlineData(3, 4, true, "removing")]
	public void ServiceMode(int start, int state, bool deleting, string expected)
	{
		Assert.Equal(expected, RazerLeftovers.ServiceMode(start, state, deleting));
		Assert.Equal(expected is "running" or "auto" or "manual", RazerLeftovers.IsOn(expected));
	}

	[Theory]
	[InlineData("\"C:\\Program Files\\Razer\\razer_elevation_service\\razer_elevation_service.exe\" /service", "C:\\Program Files\\Razer\\razer_elevation_service\\razer_elevation_service.exe")]
	[InlineData("C:\\Program Files (x86)\\Razer Chroma SDK\\bin\\RzSDKServer.exe -x", "C:\\Program Files (x86)\\Razer Chroma SDK\\bin\\RzSDKServer.exe")]
	[InlineData("\\??\\C:\\Windows\\temp\\cpuz165\\cpuz165_x64.sys", "\\??\\C:\\Windows\\temp\\cpuz165\\cpuz165_x64.sys")]
	[InlineData("\"C:\\open quote", "C:\\open quote")]
	[InlineData("  ", "")]
	[InlineData(null, "")]
	public void BinaryPathDropsQuotesAndArguments(string command, string expected)
	{
		Assert.Equal(expected, RazerLeftovers.BinaryPath(command));
	}

	[Theory]
	[InlineData("\"C:\\Program Files\\Razer\\RazerAppEngine\\RazerAppEngine.exe\" --autoStart=1", true)]
	[InlineData("C:\\Program Files (x86)\\Razer\\Razer Services\\GMS\\GameManagerService.exe -service", true)]
	[InlineData("C:\\Program Files (x86)\\Razer Chroma SDK\\bin\\RzSDKService.exe", true)]
	[InlineData("C:/ProgramData/razer/x.exe", true)]
	[InlineData("C:\\Razerfoo\\x.exe", false)]
	[InlineData("C:\\Program Files\\Steam\\steam.exe \"C:\\Razer\\x.exe\"", false)]
	[InlineData("C:\\Tools\\Razer.exe", false)]
	[InlineData("%SystemRoot%\\System32\\svchost.exe -k LocalServiceNetworkRestricted -p", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void RazerFolder(string command, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.InRazerFolder(command));
	}

	[Theory]
	[InlineData("razer_elevation_service", 0x10, "\"C:\\Program Files\\Razer\\razer_elevation_service\\razer_elevation_service.exe\"", true)]
	[InlineData("Razer Chroma SDK Server", 0x10, "C:\\Program Files (x86)\\Razer Chroma SDK\\bin\\RzSDKServer.exe", true)]
	[InlineData("HapticService", 0x110, "C:\\Program Files\\Razer\\Sensa\\HapticService.exe", true)]
	[InlineData("HapticService", 0x10, "C:\\Program Files\\Other\\HapticService.exe", false)]
	[InlineData("cpuz165", 0x01, "\\??\\C:\\Windows\\temp\\cpuz165\\cpuz165_x64.sys", true)]
	[InlineData("CPUZ165", 0x02, "", true)]
	[InlineData("cpuz165", 0x10, "C:\\Tools\\cpuz.exe", false)]
	[InlineData("cpuz161", 0x01, "\\??\\C:\\Windows\\temp\\cpuz161\\cpuz161_x64.sys", false)]
	[InlineData("Razer Game Manager Service 3", 0x10, "C:\\Program Files (x86)\\Razer\\GMS\\GameManagerService.exe", false)]
	[InlineData("RzDev_008d", 0x01, "C:\\Program Files\\Razer\\drivers\\RzDev_008d.sys", false)]
	[InlineData("Audiosrv", 0x20, "C:\\Program Files\\Razer\\svc.exe", false)]
	[InlineData("VSSrv", 0x10, "C:\\Program Files\\Razer\\svc.exe", false)]
	[InlineData("XTU3SERVICE", 0x10, "C:\\Program Files\\Razer\\svc.exe", false)]
	[InlineData("RazerUserSvc_4f2a1", 0xE0, "C:\\Program Files\\Razer\\user.exe", false)]
	[InlineData("RazerUserSvc", 0x60, "C:\\Program Files\\Razer\\user.exe", true)]
	[InlineData("", 0x10, "C:\\Program Files\\Razer\\svc.exe", false)]
	[InlineData(null, 0x10, "C:\\Program Files\\Razer\\svc.exe", false)]
	public void RazerServicesAreOnlyRazersOwn(string name, int type, string imagePath, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsRazerService(name, type, imagePath));
	}

	[Theory]
	[InlineData("cpuz165", "cpuz165", "CPUID driver (cpuz165)")]
	[InlineData("RzChromaSDK", "Razer Chroma SDK Service", "Razer Chroma SDK Service")]
	[InlineData("RzX", "@%SystemRoot%\\x.dll,-101", "RzX")]
	[InlineData("RzX", "  ", "RzX")]
	[InlineData("RzX", null, "RzX")]
	public void ServiceLabels(string name, string displayName, string expected)
	{
		Assert.Equal(expected, RazerLeftovers.ServiceLabel(name, displayName));
	}

	[Theory]
	[InlineData("RazerAppEngine", "\"C:\\Program Files\\Razer\\RazerAppEngine\\RazerAppEngine.exe\" --url-params=x --autoStart=1", true)]
	[InlineData("razerappengine", "", true)]
	[InlineData("Razer Cortex", "\"C:\\Program Files (x86)\\Razer\\Razer Cortex\\RazerCortex.exe\" /autorun", true)]
	[InlineData("LiteSynapse", "\"C:\\Razer\\LiteSynapse.exe\" --tray", false)]
	[InlineData("Steam", "\"C:\\Program Files (x86)\\Steam\\steam.exe\" -silent", false)]
	[InlineData("", "C:\\Program Files\\Razer\\x.exe", false)]
	[InlineData(null, null, false)]
	public void RazerAutostartEntries(string name, string command, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsRazerRunEntry(name, command));
	}

	private const string TaskXml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
		"<Triggers><LogonTrigger><Enabled>{0}</Enabled></LogonTrigger></Triggers><Settings><StartWhenAvailable>true</StartWhenAvailable>{1}</Settings>" +
		"<Actions><Exec><Command>C:\\Program Files\\Razer\\x.exe</Command></Exec></Actions></Task>";

	[Fact]
	public void UpgradeTaskStateComesFromTheTaskSettings()
	{
		Assert.Equal("ready", RazerLeftovers.TaskStateFromXml(0, string.Format(TaskXml, "true", "")));
		Assert.Equal("ready", RazerLeftovers.TaskStateFromXml(0, string.Format(TaskXml, "false", "<Enabled>true</Enabled>")));
		// A disabled trigger is not a disabled task.
		Assert.Equal("ready", RazerLeftovers.TaskStateFromXml(0, string.Format(TaskXml, "false", "")));
		Assert.Equal("disabled", RazerLeftovers.TaskStateFromXml(0, string.Format(TaskXml, "true", "<Enabled>false</Enabled>")));
		Assert.Equal("ready", RazerLeftovers.TaskStateFromXml(0, "<Task></Task>"));
		Assert.Equal("absent", RazerLeftovers.TaskStateFromXml(1, string.Format(TaskXml, "true", "")));
		Assert.Equal("absent", RazerLeftovers.TaskStateFromXml(0, "ERROR: The system cannot find the file specified."));
		Assert.Equal("absent", RazerLeftovers.TaskStateFromXml(0, ""));
		Assert.Equal("absent", RazerLeftovers.TaskStateFromXml(-1, null));
	}

	[Fact]
	public void ToolOutputIsDecodedFromUtf16OrUtf8()
	{
		string text = "<Task><Settings><Enabled>false</Enabled></Settings></Task>";
		byte[] bom = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes(text)).ToArray();
		Assert.Equal(text, RazerLeftovers.DecodeOutput(bom, bom.Length));
		byte[] utf16 = Encoding.Unicode.GetBytes(text);
		Assert.Equal(text, RazerLeftovers.DecodeOutput(utf16, utf16.Length));
		byte[] utf8 = Encoding.UTF8.GetBytes(text);
		Assert.Equal(text, RazerLeftovers.DecodeOutput(utf8, utf8.Length));
		// Only the first length bytes count (MemoryStream.GetBuffer is larger).
		byte[] padded = utf8.Concat(new byte[64]).ToArray();
		Assert.Equal(text, RazerLeftovers.DecodeOutput(padded, utf8.Length));
		Assert.Equal("", RazerLeftovers.DecodeOutput(null, 5));
		Assert.Equal("", RazerLeftovers.DecodeOutput(utf8, 0));
	}

	[Fact]
	public void TaskManagerStartupSwitch()
	{
		Assert.False(StartupApproved.IsDisabled(null));
		Assert.False(StartupApproved.IsDisabled(new byte[0]));
		Assert.False(StartupApproved.IsDisabled(new byte[] { 2, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
		Assert.False(StartupApproved.IsDisabled(new byte[] { 6, 0, 0, 0 }));
		Assert.True(StartupApproved.IsDisabled(new byte[] { 3, 0, 0, 0 }));
		Assert.True(StartupApproved.IsDisabled(new byte[] { 7, 0, 0, 0 }));
		DateTime when = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc);
		byte[] v = StartupApproved.DisabledValue(when);
		Assert.Equal(12, v.Length);
		Assert.True(StartupApproved.IsDisabled(v));
		Assert.Equal(new byte[] { 3, 0, 0, 0 }, v.Take(4).ToArray());
		Assert.Equal(when, DateTime.FromFileTimeUtc(BitConverter.ToInt64(v, 4)));
	}

	private static RazerLeftovers.ServicesPlan PlanOf(RazerLeftovers r)
	{
		return RazerLeftovers.PlanServices(r);
	}

	private static RazerLeftovers Found()
	{
		return new RazerLeftovers
		{
			Checked = true,
			SynapseInstalled = true,
			GmsService = "stopped",
			Autostart = new List<string> { "RazerAppEngine" },
			Services = new List<RazerService>
			{
				new RazerService { Name = "razer_elevation_service", Label = "Razer Elevation Service", State = "manual" },
				new RazerService { Name = "Razer Chroma SDK Server", Label = "Razer Chroma SDK Server", State = "running" },
				new RazerService { Name = "Razer Chroma SDK Service", Label = "Razer Chroma SDK Service", State = "disabled" },
				new RazerService { Name = "cpuz165", Label = "CPUID driver (cpuz165)", State = "manual", Driver = true },
				new RazerService { Name = "Audiosrv", Label = "Windows Audio", State = "running" },
				null
			},
			UpgradeTask = "ready",
			RunOnce = new List<string> { "RazerAppEngine cpuz-installer", "SomethingElse" }
		};
	}

	[Fact]
	public void PlanTurnsOffWhatIsOnAndNothingElse()
	{
		RazerLeftovers.ServicesPlan p = PlanOf(Found());
		Assert.Equal(new[] { "RazerAppEngine" }, p.Autostart);
		Assert.Equal(new[] { "Razer Game Manager Service 3", "razer_elevation_service", "Razer Chroma SDK Server" }, p.Disable);
		Assert.Equal(new[] { "cpuz165" }, p.Delete);
		Assert.True(p.DisableTask);
		Assert.Equal(new[] { "RazerAppEngine cpuz-installer" }, p.RunOnce);
		Assert.True(p.NeedsAdmin);
		Assert.False(p.IsEmpty);

		// Only the autostart: no administrator part, so no UAC prompt.
		RazerLeftovers onlyAutostart = new RazerLeftovers { Checked = true, Autostart = new List<string> { "RazerAppEngine" }, GmsService = "disabled", UpgradeTask = "disabled" };
		Assert.False(PlanOf(onlyAutostart).NeedsAdmin);
		Assert.False(PlanOf(onlyAutostart).IsEmpty);

		// Not checked yet: nothing planned.
		RazerLeftovers unchecked_ = Found();
		unchecked_.Checked = false;
		Assert.True(PlanOf(unchecked_).IsEmpty);
		Assert.True(PlanOf(null).IsEmpty);
	}

	[Fact]
	public void ScriptDisablesStopsDeletesAndCountsFailures()
	{
		string script = RazerLeftovers.ServicesScript(PlanOf(Found()));
		Assert.StartsWith("$failed = 0\n", script);
		Assert.EndsWith("exit $failed\n", script);
		Assert.Contains("$names = @('Razer Game Manager Service 3', 'razer_elevation_service', 'Razer Chroma SDK Server')", script);
		Assert.Contains("sc.exe config $n start= disabled", script);
		Assert.Contains("sc.exe stop $n", script);
		Assert.Contains("sc.exe delete 'cpuz165'", script);
		Assert.Contains("schtasks.exe /Change /TN 'Razer Upgrade Elevation Service' /DISABLE", script);
		Assert.Contains("/v 'RazerAppEngine cpuz-installer'", script);
		Assert.DoesNotContain("Audiosrv", script);
		Assert.DoesNotContain("Razer Chroma SDK Service", script);
		Assert.DoesNotContain("SomethingElse", script);
		Assert.DoesNotContain("RazerAppEngine'", script.Replace("'RazerAppEngine cpuz-installer'", ""));
		// Disabled before it is stopped: nothing can start it again in between.
		Assert.True(script.IndexOf("start= disabled", StringComparison.Ordinal) < script.IndexOf("sc.exe stop $n", StringComparison.Ordinal));
	}

	[Fact]
	public void ScriptQuotesNamesForPowerShell()
	{
		Assert.Equal("'it''s'", RazerLeftovers.Ps("it's"));
		Assert.Equal("''", RazerLeftovers.Ps(null));
		RazerLeftovers r = new RazerLeftovers { Checked = true, Services = new List<RazerService> { new RazerService { Name = "Razer's Svc", State = "auto" } } };
		string script = RazerLeftovers.ServicesScript(PlanOf(r));
		Assert.Contains("@('Razer''s Svc')", script);
		Assert.DoesNotContain("schtasks", script);
		Assert.DoesNotContain("reg.exe", script);
		Assert.DoesNotContain("sc.exe delete", script);
	}

	[Fact]
	public void OutcomeSaysWhatIsOffAndWhatIsStillOn()
	{
		RazerLeftovers before = Found();
		RazerLeftovers.ServicesPlan p = PlanOf(before);
		RazerLeftovers after = new RazerLeftovers
		{
			Checked = true,
			GmsService = "disabled",
			Services = new List<RazerService>
			{
				new RazerService { Name = "razer_elevation_service", State = "disabled" },
				new RazerService { Name = "Razer Chroma SDK Server", State = "running" },
				new RazerService { Name = "cpuz165", State = "removing", Driver = true }
			},
			UpgradeTask = "disabled"
		};
		string msg = RazerLeftovers.ServicesOutcome(p, before, after, 1, adminRan: true);
		Assert.StartsWith("Turned off: Synapse autostart, Razer Game Manager Service 3, Razer Elevation Service, CPUID driver (cpuz165), upgrade task, RazerAppEngine cpuz-installer (next sign-in).", msg);
		Assert.Contains("Still on: Razer Chroma SDK Server (running).", msg);
		Assert.Contains("uninstall it in Windows Settings", msg);

		// UAC declined: the autostart is off, the services are untouched and the message says so.
		string declined = RazerLeftovers.ServicesOutcome(p, before, before, 0, adminRan: false);
		Assert.Equal("Still on: Synapse autostart. The Razer services were not changed: Windows did not get administrator permission. Razer Synapse may turn them back on: uninstall it in Windows Settings › Apps to keep them off.", declined);
		RazerLeftovers autostartOff = Found();
		autostartOff.Autostart.Clear();
		autostartOff.SynapseInstalled = false;
		before.SynapseInstalled = false;
		Assert.Equal("Turned off: Synapse autostart. The Razer services were not changed: Windows did not get administrator permission.",
			RazerLeftovers.ServicesOutcome(p, before, autostartOff, 0, adminRan: false));

		// Still running after 60 s.
		Assert.EndsWith("The step is still running. Check again in a moment.", RazerLeftovers.ServicesOutcome(p, before, after, -2, adminRan: true));
		Assert.Equal("Nothing was changed.", RazerLeftovers.ServicesOutcome(new RazerLeftovers.ServicesPlan(), before, after, 0, adminRan: false));
	}

	[Theory]
	[InlineData("Razer Chroma SDK Server", "Razer Chroma SDK Server", true)]
	[InlineData("RzChromaSDK", "", true)]
	[InlineData("Razer Central Service", "", true)]
	[InlineData("RazerExperienceService", "Razer Experience", true)]
	[InlineData("Razer Game Manager Service 3", "", true)]
	[InlineData("razer_elevation_service", "Razer Elevation Service", false)]
	[InlineData("cpuz165", "CPUID driver (cpuz165)", false)]
	[InlineData("HapticService", "Razer Sensa Haptics", false)]
	public void ServicesThatReachTheNetworkAreMarked(string name, string label, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsNetworkService(name, label));
	}

	[Theory]
	[InlineData("\\Razer\\Razer Synapse Update", "", true)]
	[InlineData("\\Razer Upgrade Elevation Service", "", true)]
	[InlineData("\\Vendor\\Updater", "C:\\Program Files\\Razer\\RazerAppEngine\\x.exe", true)]
	[InlineData("\\Microsoft\\Windows\\UpdateOrchestrator\\Reboot", "C:\\Windows\\System32\\usoclient.exe", false)]
	[InlineData("", "", false)]
	[InlineData(null, null, false)]
	public void RazerScheduledTasksAreFoundByNameOrProgram(string path, string command, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsRazerTask(path, command));
	}

	[Theory]
	[InlineData("v2.31|Action=Allow|Active=TRUE|Dir=In|App=C:\\Program Files\\Razer\\RazerAppEngine\\RazerAppEngine.exe|Name=Razer|", true)]
	[InlineData("v2.31|Action=Allow|Dir=In|App=C:\\Program Files (x86)\\Razer Chroma SDK\\bin\\RzSDKServer.exe|", true)]
	[InlineData("v2.31|Action=Allow|Dir=In|App=C:\\Program Files\\Steam\\steam.exe|Name=Steam|", false)]
	// Only the program decides, not a rule that merely mentions Razer in its name.
	[InlineData("v2.31|Action=Allow|Dir=In|App=C:\\Games\\game.exe|Name=Razer friendly|", false)]
	[InlineData("", false)]
	[InlineData(null, false)]
	public void FirewallRulesForRazerProgramsAreCounted(string rule, bool expected)
	{
		Assert.Equal(expected, RazerLeftovers.IsRazerFirewallRule(rule));
	}

	[Fact]
	public void TasksAndFirewallRulesAreTurnedOffToo()
	{
		RazerLeftovers r = Found();
		r.Tasks = new List<RazerTask>
		{
			new RazerTask { Path = "\\Razer\\Razer Synapse Update", State = "ready" },
			new RazerTask { Path = "\\Razer\\Already off", State = "disabled" },
			null
		};
		r.FirewallRules = 3;
		RazerLeftovers.ServicesPlan p = PlanOf(r);
		Assert.Equal(new[] { "\\Razer\\Razer Synapse Update" }, p.Tasks);
		Assert.True(p.Firewall);
		string script = RazerLeftovers.ServicesScript(p);
		Assert.Contains("schtasks.exe /Change /TN 'Razer\\Razer Synapse Update' /DISABLE", script);
		Assert.DoesNotContain("Already off", script);
		Assert.Contains("Remove-NetFirewallRule", script);
		// Afterwards: the task is disabled and no rule is left.
		RazerLeftovers after = new RazerLeftovers
		{
			Checked = true,
			GmsService = "disabled",
			UpgradeTask = "disabled",
			Tasks = new List<RazerTask> { new RazerTask { Path = "\\Razer\\Razer Synapse Update", State = "disabled" } },
			FirewallRules = 0
		};
		string msg = RazerLeftovers.ServicesOutcome(p, r, after, 0, adminRan: true);
		Assert.Contains("task Razer\\Razer Synapse Update", msg);
		Assert.Contains("every Razer firewall rule", msg);
		Assert.DoesNotContain("Still on:", msg);
	}

	[Fact]
	public void LogLineListsTheNewFindings()
	{
		string line = Found().ToString();
		Assert.Contains("autostart: RazerAppEngine", line);
		Assert.Contains("services: razer_elevation_service manual, Razer Chroma SDK Server running", line);
		Assert.Contains("upgrade task: ready", line);
		Assert.Contains("RunOnce: RazerAppEngine cpuz-installer", line);
	}
}

public class RazerEnableTests
{
	[Fact]
	public void TurnOnBuildsTheServicesBackUp()
	{
		// Game Manager goes back to automatic, the rest to demand-start, and each is started. The upgrade task
		// is re-enabled. Only a config failure counts.
		string script = RazerLeftovers.EnableScript(
			new[] { RazerLeftovers.GmsServiceName, "RzActionSvc" }, task: true);
		Assert.StartsWith("$failed = 0\n", script);
		Assert.Contains("sc.exe config 'Razer Game Manager Service 3' start= auto", script);
		Assert.Contains("sc.exe config 'RzActionSvc' start= demand", script);
		Assert.Contains("sc.exe start 'Razer Game Manager Service 3'", script);
		Assert.Contains("sc.exe start 'RzActionSvc'", script);
		Assert.Contains("schtasks.exe /Change /TN 'Razer Upgrade Elevation Service' /ENABLE", script);
		Assert.EndsWith("exit $failed\n", script);
		// A service that will not start is not a failure of the restore; only the config line is checked.
		int configChecks = script.Split("if ($LASTEXITCODE -ne 0) { $failed++ }").Length - 1;
		Assert.Equal(3, configChecks); // two services + the task
	}

	[Fact]
	public void TurnOnWithNothingToDoSaysSo()
	{
		Assert.Equal("$failed = 0\nexit $failed\n", RazerLeftovers.EnableScript(null, task: false));
	}

	[Fact]
	public void EnableIsAnAdvertisedActionAndQuotesSafely()
	{
		Assert.Contains("enableServices", RazerLeftovers.Actions);
		// The service name is the only non-literal; it is quoted through Ps(), so an apostrophe cannot break out.
		string script = RazerLeftovers.EnableScript(new[] { "Rz'; sc.exe delete WinDefend; '" }, task: false);
		foreach (string line in script.Split('\n'))
		{
			Assert.Equal(0, CountQuotes(line) % 2);
		}
	}

	private static int CountQuotes(string s)
	{
		int n = 0;
		foreach (char c in s)
		{
			if (c == '\'')
			{
				n++;
			}
		}
		return n;
	}
}
