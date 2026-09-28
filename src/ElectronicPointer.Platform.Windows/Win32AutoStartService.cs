using Mutantcat.ElectronicPointer.Platform.Startup;
using System.Text;
using System.ComponentModel;

namespace Mutantcat.ElectronicPointer.Platform.Windows;

/// <summary>
/// "Start when I log in" on Windows, which means one value under
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Run. The registry is reached through
/// advapi32 directly so this project does not need the Windows Compatibility pack.
/// </summary>
public sealed class Win32AutoStartService : IAutoStartService
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "ElectronicPointer";

    private readonly string? _executable;

    public Win32AutoStartService()
    {
        var path = Environment.ProcessPath;
        _executable = string.IsNullOrEmpty(path) ? null : $"\"{path}\"";
    }

    public bool IsSupported => _executable is not null;

    public bool IsEnabled => ReadValue() is not null;

    public void SetEnabled(bool enabled)
    {
        if (_executable is null)
            return;

        var status = NativeMethods.RegOpenKeyEx(
            NativeMethods.HKeyCurrentUser,
            RunSubKey,
            0,
            NativeMethods.KeyAllAccess | NativeMethods.KeyWow64_64,
            out var key);

        if (status != NativeMethods.ErrorSuccess)
            return;

        try
        {
            if (enabled)
            {
                // The trailing zero is part of an REG_SZ value, hence length + 1.
                status = NativeMethods.RegSetValueEx(
                    key,
                    ValueName,
                    0,
                    NativeMethods.RegSz,
                    _executable,
                    (_executable.Length + 1) * sizeof(char));
            }
            else
            {
                status = NativeMethods.RegDeleteValue(key, ValueName);
            }

            if (status != NativeMethods.ErrorSuccess)
                throw new Win32Exception(status);
        }
        finally
        {
            _ = NativeMethods.RegCloseKey(key);
        }
    }

    private static string? ReadValue()
    {
        var buffer = new StringBuilder(1024);
        var size = buffer.Capacity;
        var status = NativeMethods.RegGetValue(
            NativeMethods.HKeyCurrentUser,
            RunSubKey,
            ValueName,
            NativeMethods.RrfRtRegSz,
            out _,
            buffer,
            ref size);

        return status == NativeMethods.ErrorSuccess ? buffer.ToString() : null;
    }
}
