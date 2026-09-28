using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// The assembly targets net9.0 like every other project, but everything inside it only does
// anything on Linux. The attribute makes each call site warn unless it sits behind an
// OperatingSystem.IsLinux() check, which is exactly how the app entry point guards it.
[assembly: SupportedOSPlatform("linux")]

// The Linux key mapping and the session probes are pure logic with no X dependency, so the
// unit tests for them run on any machine the build agent happens to be.
[assembly: InternalsVisibleTo("ElectronicPointer.Platform.Linux.Tests")]
