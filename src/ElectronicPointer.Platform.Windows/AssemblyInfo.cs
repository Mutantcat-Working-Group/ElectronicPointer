using System.Runtime.Versioning;

// The assembly only does anything on Windows, but it still targets net9.0 so it can be
// referenced by the shared app project. The attribute makes every call site warn unless it
// is guarded by an OperatingSystem.IsWindows() check.
[assembly: SupportedOSPlatform("windows")]

[assembly: SupportedOSPlatform("windows6.0.6000")]
