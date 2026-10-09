using System;
using Microsoft.Win32;

namespace LiteSynapse;

// Task Manager's "Startup apps" switch. HKCU ...\Explorer\StartupApproved\Run holds one 12-byte value per HKCU Run
// value: an odd first byte means "disabled" (the Run value stays, Windows skips it at sign-in), no entry or an even
// first byte means "enabled". Bytes 4-11 are the time it was turned off (FILETIME, UTC).
internal static class StartupApproved
{
	public const string RunKey = "Software\\Microsoft\\Windows\\CurrentVersion\\Run";

	public const string Key = "Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\StartupApproved\\Run";

	internal static bool IsDisabled(byte[] data)
	{
		return data != null && data.Length > 0 && (data[0] & 1) == 1;
	}

	// The value Task Manager writes when an app is turned off.
	internal static byte[] DisabledValue(DateTime utc)
	{
		byte[] b = new byte[12];
		b[0] = 3;
		BitConverter.GetBytes(utc.ToFileTimeUtc()).CopyTo(b, 4);
		return b;
	}

	// absent | enabled | disabled for an HKCU Run value.
	public static string State(string runValue)
	{
		using RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKey);
		if (run?.GetValue(runValue) == null)
		{
			return "absent";
		}
		using RegistryKey approved = Registry.CurrentUser.OpenSubKey(Key);
		return IsDisabled(approved?.GetValue(runValue) as byte[]) ? "disabled" : "enabled";
	}

	public static void Disable(string runValue)
	{
		using RegistryKey approved = Registry.CurrentUser.CreateSubKey(Key);
		approved.SetValue(runValue, DisabledValue(DateTime.UtcNow), RegistryValueKind.Binary);
	}

	// No entry means enabled, like an app Task Manager never touched.
	public static void Enable(string runValue)
	{
		using RegistryKey approved = Registry.CurrentUser.OpenSubKey(Key, writable: true);
		approved?.DeleteValue(runValue, throwOnMissingValue: false);
	}
}
