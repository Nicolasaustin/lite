using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Xunit;

namespace LiteSynapse.Tests;

// The sign-in task starts LiteSynapse as administrator, so what it runs has to be exactly LiteSynapse.exe as it
// was when the task was registered - never a file something else put there since.
public class StartupTaskTests
{
	private const string Exe = "C:\\Users\\o'brien\\AppData\\Local\\Programs\\LiteSynapse\\LiteSynapse.exe";

	private const string Hash = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

	private static readonly XNamespace Ns = "http://schemas.microsoft.com/windows/2004/02/mit/task";

	private static string Decode(string arguments)
	{
		string b64 = arguments.Substring(arguments.LastIndexOf(' ') + 1);
		return Encoding.Unicode.GetString(Convert.FromBase64String(b64));
	}

	[Fact]
	public void TheTaskRunsPowerShellHeadlessThroughConhostNotTheExeItself()
	{
		XDocument x = XDocument.Parse(StartupTask.Xml(Exe, Hash, "PC\\me"));
		XElement exec = x.Descendants(Ns + "Exec").Single();
		string command = exec.Element(Ns + "Command").Value;
		string args = exec.Element(Ns + "Arguments").Value;
		// conhost runs the show; LiteSynapse.exe is never the task's own program.
		Assert.EndsWith("conhost.exe", command);
		Assert.DoesNotContain("LiteSynapse.exe", command);
		// Headless, then PowerShell from System32, then the launcher arguments.
		Assert.StartsWith("--headless \"", args);
		Assert.Contains(Path.Combine("WindowsPowerShell", "v1.0", "powershell.exe"), args);
		Assert.EndsWith(StartupTask.Arguments(Exe, Hash), args);
		Assert.Equal(StartupTask.TaskArguments(Exe, Hash), args);
	}

	[Fact]
	public void TheLauncherChecksTheHashAndClearsDotnetVariablesFirst()
	{
		string script = Decode(StartupTask.Arguments(Exe, Hash));
		Assert.StartsWith(RazerLeftovers.Quiet, script);
		// The path is a quoted literal, apostrophe doubled.
		Assert.Contains("$p = 'C:\\Users\\o''brien\\AppData\\Local\\Programs\\LiteSynapse\\LiteSynapse.exe'", script);
		Assert.Contains("'" + Hash + "'", script);
		Assert.Contains("^(DOTNET_|COMPlus_|CORECLR_|COREHOST_|COR_)", script);
		// Variables cleared, planted DLLs refused, the hash checked, then the start - in that order.
		int clear = script.IndexOf("SetEnvironmentVariable", StringComparison.Ordinal);
		int dlls = script.IndexOf("'*.dll'", StringComparison.Ordinal);
		int check = script.IndexOf("ComputeHash", StringComparison.Ordinal);
		int start = script.IndexOf("Process]::Start", StringComparison.Ordinal);
		Assert.True(clear >= 0 && clear < dlls && dlls < check && check < start);
		Assert.Contains("'--tray'", script);
		Assert.Contains("{ exit 3 }", script);
		Assert.Contains("{ exit 4 }", script);
		// No cmdlet PowerShell would load from a module (the module path is the user's to change).
		foreach (string cmdlet in new[] { "Get-FileHash", "Start-Process", "Get-ChildItem", "Remove-Item", "New-Object", "Where-Object", "ForEach-Object" })
		{
			Assert.DoesNotContain(cmdlet, script);
		}
	}

	[Fact]
	public void TheTaskRunsAtSignInWithHighestRightsAtNormalPriority()
	{
		XDocument x = XDocument.Parse(StartupTask.Xml(Exe, Hash, "PC\\me"));
		Assert.Equal("PC\\me", x.Descendants(Ns + "LogonTrigger").Single().Element(Ns + "UserId").Value);
		XElement principal = x.Descendants(Ns + "Principal").Single();
		Assert.Equal("PC\\me", principal.Element(Ns + "UserId").Value);
		Assert.Equal("InteractiveToken", principal.Element(Ns + "LogonType").Value);
		Assert.Equal("HighestAvailable", principal.Element(Ns + "RunLevel").Value);
		XElement settings = x.Descendants(Ns + "Settings").Single();
		Assert.Equal("4", settings.Element(Ns + "Priority").Value);
		Assert.Equal("false", settings.Element(Ns + "AllowStartOnDemand").Value);
		Assert.Equal("PT0S", settings.Element(Ns + "ExecutionTimeLimit").Value);
		Assert.Equal("false", settings.Element(Ns + "DisallowStartIfOnBatteries").Value);
		Assert.Equal("IgnoreNew", settings.Element(Ns + "MultipleInstancesPolicy").Value);
	}

	[Fact]
	public void AnUnusualUserOrPathCannotBreakTheXml()
	{
		string odd = "C:\\Games & Tools\\<LiteSynapse>\\LiteSynapse.exe";
		XDocument x = XDocument.Parse(StartupTask.Xml(odd, Hash, "PC\\a&b"));
		Assert.Equal("PC\\a&b", x.Descendants(Ns + "Principal").Single().Element(Ns + "UserId").Value);
		Assert.Contains("C:\\Games & Tools\\<LiteSynapse>\\LiteSynapse.exe", Decode(x.Descendants(Ns + "Arguments").Single().Value));
	}

	[Fact]
	public void TheFileHashIsWhatGetFileHashPrints()
	{
		string f = Path.GetTempFileName();
		try
		{
			File.WriteAllText(f, "abc");
			// SHA-256("abc"), upper-case hex as Get-FileHash prints it.
			Assert.Equal("BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD", StartupTask.FileHash(f));
		}
		finally
		{
			File.Delete(f);
		}
	}
}
