using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Permissions;

[assembly: AssemblyCompany("LiteSynapse")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyFileVersion("1.2.16.0")]
[assembly: AssemblyInformationalVersion("1.2.16")]
// The test build number shown next to the version in the window and the log (releases/...-test<Build>.exe).
[assembly: AssemblyMetadata("Build", "27")]
[assembly: AssemblyProduct("LiteSynapse")]
[assembly: AssemblyTitle("LiteSynapse")]
[assembly: TargetPlatform("Windows7.0")]
[assembly: SupportedOSPlatform("Windows7.0")]
[assembly: AssemblyVersion("1.2.16.0")]
// Running as administrator: every DllImport is a Windows library, loaded from System32 - never a DLL of the same
// name placed next to LiteSynapse.exe.
[assembly: System.Runtime.InteropServices.DefaultDllImportSearchPaths(System.Runtime.InteropServices.DllImportSearchPath.System32)]
