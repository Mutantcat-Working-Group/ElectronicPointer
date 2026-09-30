using System.Runtime.Versioning;

// The service under test reaches user32 through P/Invoke, so the assembly is marked for
// Windows and the tests no-op on a Linux or macOS run instead of failing one.
[assembly: SupportedOSPlatform("windows")]
