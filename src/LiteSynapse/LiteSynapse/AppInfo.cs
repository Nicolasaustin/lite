using System.Linq;
using System.Reflection;

namespace LiteSynapse;

// The version and test build number, for the window title, the Settings page and the first line of each
// session in the log - so a screenshot or a pasted log always says which build it came from.
internal static class AppInfo
{
	public static readonly string Version =
		typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";

	public static readonly string Build = typeof(AppInfo).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
		.FirstOrDefault((AssemblyMetadataAttribute a) => a.Key == "Build")?.Value ?? "";

	// "1.2.6 · build 17".
	public static string Text => (Build.Length > 0) ? (Version + " · build " + Build) : Version;
}
