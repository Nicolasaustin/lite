using System;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace LiteSynapse;

// The few facts about this PC that change which side of a setting helps.
//
// A dual-chiplet Ryzen X3D (7950X3D, 7900X3D, 9950X3D, 9900X3D) has its extra cache on one chiplet only. AMD's
// 3D V-Cache driver keeps a game on that chiplet by parking the other one while the game runs, and it does that
// through Windows' core parking - which it only gets in the Balanced plan, with Game Mode on, once Xbox Game Bar
// has recognised the program as a game. A "maximum performance" plan with core parking off lets the game spread
// onto the chiplet without the cache, which costs frames: the opposite of what that tweak promises everywhere else.
internal static class Hardware
{
	public const string BalancedPlan = "381b4222-f694-41f0-9685-ff5bb260df2e";

	public const string HighPerformancePlan = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";

	public const string UltimatePlan = "e9a42b02-d5df-448d-aa00-03f14749eb61";

	private static string cpu;

	// The processor's marketing name, e.g. "AMD Ryzen 9 7950X3D 16-Core Processor". "" when it cannot be read.
	public static string CpuName
	{
		get
		{
			if (cpu == null)
			{
				string name = "";
				try
				{
					using RegistryKey k = Registry.LocalMachine.OpenSubKey("HARDWARE\\DESCRIPTION\\System\\CentralProcessor\\0");
					name = (k?.GetValue("ProcessorNameString") as string)?.Trim() ?? "";
				}
				catch (Exception ex) when (ex is System.Security.SecurityException || ex is UnauthorizedAccessException
					|| ex is System.IO.IOException || ex is PlatformNotSupportedException)
				{
				}
				cpu = name;
			}
			return cpu;
		}
	}

	public static bool DualCcdX3D => IsDualCcdX3D(CpuName);

	// The X3D parts with two chiplets: model numbers ending in 900 or 950 (7900X3D, 7950X3D, 9900X3D,
	// 9950X3D). The single-chiplet ones (5800X3D, 7800X3D, 9800X3D and the like) have the cache on every core.
	internal static bool IsDualCcdX3D(string name)
	{
		Match m = Regex.Match(name ?? "", "\\b(\\d{4,5})X3D", RegexOptions.IgnoreCase);
		if (!m.Success || !int.TryParse(m.Groups[1].Value, out int model))
		{
			return false;
		}
		int tail = model % 1000;
		return tail == 900 || tail == 950;
	}
}
