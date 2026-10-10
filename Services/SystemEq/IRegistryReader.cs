namespace WolfEQ.Services.SystemEq;

public enum RegistryRoot
{
    LocalMachine,
    ClassesRoot
}

/// <summary>Read-only registry access (64-bit view) so Software EQ detection can be faked in tests.</summary>
public interface IRegistryReader
{
    bool KeyExists(RegistryRoot root, string subKey);

    /// <summary>Returns the value as a string (REG_SZ as-is, DWORD as decimal text), or null when missing.</summary>
    string? GetValue(RegistryRoot root, string subKey, string valueName);

    IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey);
}
