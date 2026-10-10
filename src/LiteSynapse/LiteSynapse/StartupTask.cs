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
internal static class StartupTask
{
	public const string Name = "LiteSynapse";

	// The installer's "Start with Windows" choice, for the app to carry out on its next start (the installer runs
	// without administrator rights, so it cannot register the task itself). "1" or "0"; removed once done.
	public const string RequestKey = "Software\\LiteSynapse";

	public const string RequestValue = "StartupRequest";

	// The current state, mirrored for the installer's checkbox.
	public const string MirrorValue = "StartWithWindows";

	private const int TaskCreateOrUpdate = 6;

	private const int TaskLogonInteractiveToken = 3;

	// The task's state: true when it exists, is enabled and starts this LiteSynapse.exe; false when it does not
	// exist or starts something else; null when Task Scheduler could not be asked.
	public static bool? State(string exePath, out bool otherPath)
	{
		otherPath = false;
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
			otherPath = exePath != null && xml.IndexOf(Escape(exePath), StringComparison.OrdinalIgnoreCase) < 0;
			return !otherPath;
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
				task = Call(folder, "RegisterTask", Name, Xml(exePath, CurrentUser()), TaskCreateOrUpdate, null, null,
					TaskLogonInteractiveToken, null);
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
			|| ex is UnauthorizedAccessException || ex is InvalidCastException || ex is ArgumentException || ex is SecurityException)
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

	// The task definition. Normal priority (Task Scheduler's default of 7 would start LiteSynapse below normal),
	// no time limit, not stopped on battery, one copy at a time.
	internal static string Xml(string exePath, string user)
	{
		return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
			"<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
			"  <RegistrationInfo>\n" +
			"    <Author>LiteSynapse</Author>\n" +
			"    <Description>Starts LiteSynapse at sign-in with administrator rights, so its macros and Snap Tap reach games that run as administrator.</Description>\n" +
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
			"    <AllowStartOnDemand>true</AllowStartOnDemand>\n" +
			"    <Enabled>true</Enabled>\n" +
			"    <Hidden>false</Hidden>\n" +
			"    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n" +
			"    <WakeToRun>false</WakeToRun>\n" +
			"    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n" +
			"    <Priority>5</Priority>\n" +
			"  </Settings>\n" +
			"  <Actions Context=\"Author\">\n" +
			"    <Exec>\n" +
			"      <Command>\"" + Escape(exePath) + "\"</Command>\n" +
			"      <Arguments>--tray</Arguments>\n" +
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
