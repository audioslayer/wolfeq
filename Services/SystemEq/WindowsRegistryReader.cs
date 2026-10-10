using System.Globalization;
using Microsoft.Win32;

namespace WolfEQ.Services.SystemEq;

/// <summary>
/// Read-only <see cref="IRegistryReader"/> over the 64-bit registry view. Equalizer APO is a
/// 64-bit component, so a 32-bit view would miss its keys. Never throws; failures read as missing.
/// </summary>
public sealed class WindowsRegistryReader : IRegistryReader
{
    public bool KeyExists(RegistryRoot root, string subKey)
    {
        try
        {
            using var key = Open(root, subKey);
            return key is not null;
        }
        catch
        {
            return false;
        }
    }

    public string? GetValue(RegistryRoot root, string subKey, string valueName)
    {
        try
        {
            using var key = Open(root, subKey);
            return key?.GetValue(valueName) switch
            {
                null => null,
                string text => text,
                int dword => dword.ToString(CultureInfo.InvariantCulture),
                long qword => qword.ToString(CultureInfo.InvariantCulture),
                string[] lines => string.Join(Environment.NewLine, lines),
                byte[] => null,
                var other => Convert.ToString(other, CultureInfo.InvariantCulture)
            };
        }
        catch
        {
            return null;
        }
    }

    public IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey)
    {
        try
        {
            using var key = Open(root, subKey);
            return key?.GetValueNames() ?? [];
        }
        catch
        {
            return [];
        }
    }

    private static RegistryKey? Open(RegistryRoot root, string subKey)
    {
        var hive = root == RegistryRoot.ClassesRoot ? RegistryHive.ClassesRoot : RegistryHive.LocalMachine;
        using var baseKey = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        return baseKey.OpenSubKey(subKey, writable: false);
    }
}
