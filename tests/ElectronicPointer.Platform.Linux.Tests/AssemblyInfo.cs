using System.Runtime.Versioning;

// Everything under test here is pure mapping logic; the attribute keeps the platform
// analyzer quiet when the tests reference the Linux bundle from another operating system.
[assembly: SupportedOSPlatform("linux")]
