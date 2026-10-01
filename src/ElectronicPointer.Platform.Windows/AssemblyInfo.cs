using System.Runtime.CompilerServices;
using System.Runtime.Versioning;

// The assembly only does anything on Windows, but it still targets net9.0 so it can be
// referenced by the shared app project. The attribute makes every call site warn unless it
// is guarded by an OperatingSystem.IsWindows() check.
[assembly: SupportedOSPlatform("windows")]

[assembly: SupportedOSPlatform("windows6.0.6000")]

// The corner shape is a plain arithmetic function over the window's own scale, and the
// numbers matter on every desk: a radius that drifts by a pixel shows up as one corner
// disagreeing with the other three. Letting the test assembly in keeps that arithmetic
// assertable instead of inspectable only by eye on a scaled monitor.
[assembly: InternalsVisibleTo("ElectronicPointer.Platform.Windows.Tests")]
