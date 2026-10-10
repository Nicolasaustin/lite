using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;

namespace LiteSynapse;

// "Start LiteSynapse with Windows", as a Task Scheduler task that runs at sign-in with administrator rights.
//
// LiteSynapse always runs as administrator (app.manifest): a game that runs as administrator only takes keys
// from a program with at least its rights, so without it Windows dropped every key a macro or Snap Tap sent
// ("Windows took 0 of 8 key events"). A program that asks for administrator rights cannot start from the Run
// key - Windows skips it at sign-in - so it starts from this task, which needs no UAC prompt.
//
// Task Scheduler is driven through its COM interface (Schedule.Service), late bound: no process to start, and
// reading the state for the Settings page or the tray menu costs a few milliseconds.
//
// The task does not start LiteSynapse.exe directly. It lives in a folder this account can write to, and a task
// that starts it with administrator rights and no prompt would hand those rights to any program that replaced
// it. So the task starts Windows PowerShell (System32, which only an administrator can change) with a short
// command that checks the file's SHA-256 against the one LiteSynapse had when it registered the task, clears
// the .NET variables that could load other code into it (DOTNET_*, COMPlus_*, CORECLR_*), and only then starts
// it. The task itself can only be changed by an administrator. LiteSynapse re-registers it whenever the file
// changes (an update), so the hash is always its own.
internal static class StartupTask
{
	// One task per Windows account ("LiteSynapse-<SID>"): two accounts on the same PC each keep their own, and one
	// account's LiteSynapse never re-points or deletes the other's.
	public static string Name => "LiteSynapse-" + CurrentSid();

	// The task's name, mirrored for the uninstaller's fallback (it removes the task itself if LiteSynapse cannot).
	public const string TaskNameValue = "TaskName";

	// The installer's "Start with Windows" choice, for the app to carry out on its next start (the installer runs
	// without administrator rights, so it cannot register the task itself). "1" or "0"; removed once done.
	public const string RequestKey = "Software\\LiteSynapse";

	public const string RequestValue = "StartupRequest";

	// The current state, mirrored for the installer's checkbox.
	public const string MirrorValue = "StartWithWindows";

	private const int TaskCreateOrUpdate = 6;

	private const int TaskLogonInteractiveToken = 3;

	// The task's state: true when it exists, is enabled and starts this LiteSynapse.exe as it is now; false when
	// it does not exist or is disabled; null when Task Scheduler could not be asked. stale: it is enabled but
	// starts another path or an older file (moved, or updated since), so it needs registering again.
	public static bool? State(string exePath, out bool stale)
	{
		stale = false;
		object service = null;
		object folder = null;
		object task = null;
		try
		{
			service = Connect();
			folder = Call(service, "GetFolder", "\\");
			try
			{
				task = Call(folder, "GetTask", Name);
			}
			catch (TargetInvocationException ex) when (ex.InnerException is COMException || ex.InnerException is System.IO.FileNotFoundException)
			{
				return false;
			}
			if (!(Get(task, "Enabled") is bool enabled) || !enabled)
			{
				return false;
			}
			string xml = Get(task, "Xml") as string ?? "";
			stale = exePath != null && xml.IndexOf(Arguments(exePath, FileHash(exePath)), StringComparison.Ordinal) < 0;
			return !stale;
		}
		catch (Exception ex) when (ex is COMException || ex is TargetInvocationException || ex is MissingMethodException
			|| ex is UnauthorizedAccessException || ex is InvalidCastException || ex is ArgumentException)
		{
			Cfg.Log("start with Windows: Task Scheduler could not be read: " + (ex.InnerException ?? ex).Message);
			return null;
		}
		finally
		{
			Release(task);
			Release(folder);
			Release(service);
		}
	}

	// Registers (or updates) the task for this LiteSynapse.exe, or deletes it. null when it worked, else why not.
	public static string Set(bool on, string exePath)
	{
		object service = null;
		object folder = null;
		object task = null;
		try
		{
			service = Connect();
			folder = Call(service, "GetFolder", "\\");
			if (on)
			{
				task = Call(folder, "RegisterTask", Name, Xml(exePath, FileHash(exePath), CurrentUser()), TaskCreateOrUpdate,
					null, null, TaskLogonInteractiveToken, null);
			}
			else
			{
				try
				{
					Call(folder, "DeleteTask", Name, 0);
				}
				catch (TargetInvocationException ex) when (ex.InnerException is COMException || ex.InnerException is System.IO.FileNotFoundException)
				{
					// Not there: already off.
				}
			}
			return null;
		}
		catch (Exception ex) when (ex is COMException || ex is TargetInvocationException || ex is MissingMethodException
			|| ex is UnauthorizedAccessException || ex is InvalidCastException || ex is ArgumentException || ex is SecurityException
			|| ex is System.IO.IOException)
		{
			string why = (ex.InnerException ?? ex).Message;
			Cfg.Log("start with Windows: the sign-in task could not be " + (on ? "registered: " : "removed: ") + why);
			return why;
		}
		finally
		{
			Release(task);
			Release(folder);
			Release(service);
		}
	}

	private static string CurrentUser()
	{
		using WindowsIdentity me = WindowsIdentity.GetCurrent();
		return me.Name;
	}

	private static string CurrentSid()
	{
		using WindowsIdentity me = WindowsIdentity.GetCurrent();
		return me.User?.Value ?? "user";
	}

	// Windows PowerShell by its full path, never a powershell.exe found some other way.
	internal static string LauncherPath => System.IO.Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");

	// SHA-256 of the file, upper-case hex like Get-FileHash.
	internal static string FileHash(string path)
	{
		using System.IO.FileStream f = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read,
			System.IO.FileShare.Read | System.IO.FileShare.Delete);
		return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(f));
	}

	// What the task asks PowerShell to do (see the top of this file). Only the language and .NET calls - no
	// cmdlet that PowerShell would load from a module, since the module path comes from this account's environment.
	// The path is quoted through Ps(), and the hash is hex, so nothing in here can end a literal.
	internal static string LauncherScript(string exePath, string sha256)
	{
		return "$p = " + RazerLeftovers.Ps(exePath) + "\n" +
			// Variables that could load other code into a .NET program (runtime location, startup hooks, profilers,
			// host tracing to a chosen file).
			"foreach ($k in @([Environment]::GetEnvironmentVariables().Keys)) { " +
			"if ($k -match '^(DOTNET_|COMPlus_|CORECLR_|COREHOST_|COR_)') { [Environment]::SetEnvironmentVariable($k, $null) } }\n" +
			"$d = [IO.Path]::GetDirectoryName($p)\n" +
			// LiteSynapse.exe is one file: a DLL beside it can only be something planted to load into it.
			"if ([IO.Directory]::GetFiles($d, '*.dll').Length -gt 0) { exit 4 }\n" +
			"$f = [IO.File]::OpenRead($p)\n" +
			"try { $h = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($f)).Replace('-', '') } " +
			"finally { $f.Dispose() }\n" +
			"if ($h -ne '" + sha256 + "') { exit 3 }\n" +
			"$i = [Diagnostics.ProcessStartInfo]::new($p, '--tray')\n" +
			"$i.UseShellExecute = $false\n" +
			"$i.WorkingDirectory = $d\n" +
			"[void][Diagnostics.Process]::Start($i)\n";
	}

	internal static string Arguments(string exePath, string sha256)
	{
		string encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(
			RazerLeftovers.Quiet + LauncherScript(exePath, sha256)));
		return "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " + encoded;
	}

	// The task definition. Priority 4: normal CPU, I/O and memory priority (Task Scheduler's default of 7 would start
	// LiteSynapse below normal, and 5 lowers its memory priority). No time limit, not stopped on battery, one copy
	// at a time, and only at sign-in - not on demand.
	internal static string Xml(string exePath, string sha256, string user)
	{
		return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
			"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
			"  <RegistrationInfo>\n" +
			"    <Author>LiteSynapse</Author>\n" +
			"    <Description>Starts LiteSynapse at sign-in with administrator rights, so its macros and Snap Tap reach games that run as administrator. It checks LiteSynapse.exe's SHA-256 first and starts nothing if the file was changed.</Description>\n" +
			"  </RegistrationInfo>\n" +
			"  <Triggers>\n" +
			"    <LogonTrigger>\n" +
			"      <Enabled>true</Enabled>\n" +
			"      <UserId>" + Escape(user) + "</UserId>\n" +
			"    </LogonTrigger>\n" +
			"  </Triggers>\n" +
			"  <Principals>\n" +
			"    <Principal id=\"Author\">\n" +
			"      <UserId>" + Escape(user) + "</UserId>\n" +
			"      <LogonType>InteractiveToken</LogonType>\n" +
			"      <RunLevel>HighestAvailable</RunLevel>\n" +
			"    </Principal>\n" +
			"  </Principals>\n" +
			"  <Settings>\n" +
			"    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n" +
			"    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n" +
			"    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n" +
			"    <AllowHardTerminate>true</AllowHardTerminate>\n" +
			"    <StartWhenAvailable>false</StartWhenAvailable>\n" +
			"    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n" +
			"    <IdleSettings>\n" +
			"      <StopOnIdleEnd>false</StopOnIdleEnd>\n" +
			"      <RestartOnIdle>false</RestartOnIdle>\n" +
			"    </IdleSettings>\n" +
			"    <AllowStartOnDemand>false</AllowStartOnDemand>\n" +
			"    <Enabled>true</Enabled>\n" +
			"    <Hidden>false</Hidden>\n" +
			"    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n" +
			"    <WakeToRun>false</WakeToRun>\n" +
			"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n" +
			"    <Priority>4</Priority>\n" +
			"  </Settings>\n" +
			"  <Actions Context=\"Author\">\n" +
			"    <Exec>\n" +
			"      <Command>" + Escape(LauncherPath) + "</Command>\n" +
			"      <Arguments>" + Escape(Arguments(exePath, sha256)) + "</Arguments>\n" +
			"    </Exec>\n" +
			"  </Actions>\n" +
			"</Task>\n";
	}

	private static string Escape(string s)
	{
		return SecurityElement.Escape(s ?? "") ?? "";
	}

	private static object Connect()
	{
		Type t = Type.GetTypeFromProgID("Schedule.Service", throwOnError: true);
		object service = Activator.CreateInstance(t);
		Call(service, "Connect");
		return service;
	}

	private static object Call(object target, string method, params object[] args)
	{
		return target.GetType().InvokeMember(method, BindingFlags.InvokeMethod, null, target, args);
	}

	private static object Get(object target, string property)
	{
		return target.GetType().InvokeMember(property, BindingFlags.GetProperty, null, target, null);
	}

	private static void Release(object com)
	{
		if (com != null && Marshal.IsComObject(com))
		{
			Marshal.FinalReleaseComObject(com);
		}
	}
}
