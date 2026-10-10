using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace LiteSynapse.Tests;

// Keeps the tests out of the real per-user data folder: Cfg.Dir (and everything built on it - the log, onboard
// return-slot, backups) is redirected to a folder under the test output. Runs before Cfg's static Init, since a
// module initializer runs before any type in the assembly is first used.
internal static class TestSetup
{
	[ModuleInitializer]
	internal static void Init()
	{
		AppContext.SetData("LiteSynapse.DataDir", Path.Combine(AppContext.BaseDirectory, "test-data"));
	}
}
