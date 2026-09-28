using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("macos")]

// The P/Invoke targets below are only called once OperatingSystem.IsMacOS() is true. The
// macOS build validates them; on any other TFM they are just declarations that never run.
