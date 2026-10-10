using System.IO;
using WolfEQ.Services.SystemEq;
using Xunit;

namespace WolfEQ.Tests;

public sealed class EqualizerApoInstallationTests
{
    private const string InstallPath = @"C:\Program Files\EqualizerAPO";
    private const string ConfigPath = @"C:\Program Files\EqualizerAPO\config";
    private const string EndpointGuid = "{6a1f2c3d-0000-4000-8000-0123456789ab}";
    private const string FxKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render\" + EndpointGuid + @"\FxProperties";
    private const string FxClsidPrefix = "{d04e05a6-594b-4fb6-a80d-01af5eed7d1d}";
    private const string DisableEnhancements = "{1da5d803-d492-4edd-8c23-e0c0ffee7f0e},5";

    [Fact]
    public void LeftoverRegistryKeyWithoutDllOrClsid_IsNotInstalled()
    {
        var registry = new FakeRegistry().WithApoKey();
        var installation = new EqualizerApoInstallation(registry, fileExists: _ => false, readText: _ => null, canWriteDirectory: _ => true);

        var status = installation.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.Contains("not installed", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DllWithoutRegisteredClsid_IsNotInstalled()
    {
        var registry = new FakeRegistry().WithApoKey();
        var installation = Create(registry);

        Assert.False(installation.GetStatus().IsInstalled);
    }

    [Fact]
    public void ClsidWithoutDll_IsNotInstalled()
    {
        var registry = new FakeRegistry().WithApoKey().WithClsid(EqualizerApoInstallation.PreMixClsid);
        var installation = new EqualizerApoInstallation(registry, fileExists: _ => false, readText: _ => null, canWriteDirectory: _ => true);

        Assert.False(installation.GetStatus().IsInstalled);
    }

    [Fact]
    public void MissingRegistryKey_IsNotInstalled()
    {
        var installation = Create(new FakeRegistry());

        var status = installation.GetStatus();

        Assert.False(status.IsInstalled);
        Assert.Null(installation.DeviceSelectorPath);
    }

    [Fact]
    public void FullInstall_IsInstalledWithResolvedPaths()
    {
        var registry = new FakeRegistry().WithApoKey().WithClsid(EqualizerApoInstallation.PostMixClsid);
        var installation = Create(registry);

        var status = installation.GetStatus();

        Assert.True(status.IsInstalled);
        Assert.Equal(InstallPath, status.InstallDirectory);
        Assert.Equal(ConfigPath, status.ConfigDirectory);
        Assert.True(status.IsConfigWritable);
        Assert.False(status.IsPeaceIncludeActive);
        Assert.Equal(ConfigPath, installation.ConfigDirectory);
        Assert.Equal(Path.Combine(InstallPath, "DeviceSelector.exe"), installation.DeviceSelectorPath);
    }

    [Fact]
    public void FullInstall_ReadOnlyConfigFolder_ReportsNotWritable()
    {
        var registry = new FakeRegistry().WithApoKey().WithClsid(EqualizerApoInstallation.PreMixClsid);
        var installation = Create(registry, canWrite: false);

        var status = installation.GetStatus();

        Assert.True(status.IsInstalled);
        Assert.False(status.IsConfigWritable);
        Assert.Contains("can't write", status.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FullInstall_WithPeaceInclude_DetectsPeace()
    {
        var registry = new FakeRegistry().WithApoKey().WithClsid(EqualizerApoInstallation.PreMixClsid);
        var installation = Create(registry, configText: "Preamp: -3 dB\r\nInclude: peace.txt\r\n");

        Assert.True(installation.GetStatus().IsPeaceIncludeActive);
    }

    [Theory]
    [InlineData("Include: peace.txt", true)]
    [InlineData("  include :   PEACE.TXT  ", true)]
    [InlineData("Preamp: -6 dB\nInclude: peace.txt\nInclude: WolfEQ.txt", true)]
    [InlineData(@"Include: C:\Program Files\EqualizerAPO\config\peace.txt", true)]
    [InlineData("# Include: peace.txt", false)]
    [InlineData("#Include: peace.txt", false)]
    [InlineData("Include: peacekeeper.txt", false)]
    [InlineData("Include: WolfEQ.txt", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void PeaceInclude_OnlyUncommentedLinesCount(string? configText, bool expected)
    {
        Assert.Equal(expected, EqualizerApoInstallation.IsPeaceIncludeActive(configText));
    }

    [Theory]
    [InlineData(EqualizerApoInstallation.PreMixClsid)]
    [InlineData("{ec1cc9ce-faed-4822-828a-82a81a6f018f}")]
    public void EndpointWithApoClsid_IsApoActive(string clsid)
    {
        var registry = new FakeRegistry()
            .WithValue(RegistryRoot.LocalMachine, FxKey, FxClsidPrefix + ",1", "{62dc1a93-ae24-464c-a43e-452f824c4250}")
            .WithValue(RegistryRoot.LocalMachine, FxKey, FxClsidPrefix + ",5", clsid);
        var installation = Create(registry);

        var status = installation.GetEndpointStatus(EndpointGuid);

        Assert.True(status.IsApoActive);
        Assert.False(status.EnhancementsDisabled);
    }

    [Fact]
    public void EndpointWithDifferentClsid_IsNotApoActive()
    {
        var registry = new FakeRegistry()
            .WithValue(RegistryRoot.LocalMachine, FxKey, FxClsidPrefix + ",5", "{62dc1a93-ae24-464c-a43e-452f824c4250}")
            // An APO CLSID under an unrelated property key does not count.
            .WithValue(RegistryRoot.LocalMachine, FxKey, "{11111111-2222-3333-4444-555555555555},5", EqualizerApoInstallation.PreMixClsid);
        var installation = Create(registry);

        Assert.False(installation.GetEndpointStatus(EndpointGuid).IsApoActive);
    }

    [Fact]
    public void EndpointWithEnhancementsDisabled_ReportsIt()
    {
        var registry = new FakeRegistry()
            .WithValue(RegistryRoot.LocalMachine, FxKey, FxClsidPrefix + ",5", EqualizerApoInstallation.PreMixClsid)
            .WithValue(RegistryRoot.LocalMachine, FxKey, DisableEnhancements, "1");
        var installation = Create(registry);

        var status = installation.GetEndpointStatus(EndpointGuid);

        Assert.True(status.IsApoActive);
        Assert.True(status.EnhancementsDisabled);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-guid")]
    [InlineData(@"..\..\Capture")]
    public void MalformedEndpointGuid_ReturnsInactiveWithoutThrowing(string guid)
    {
        var installation = Create(new FakeRegistry());

        var status = installation.GetEndpointStatus(guid);

        Assert.False(status.IsApoActive);
        Assert.False(status.EnhancementsDisabled);
    }

    [Fact]
    public void ThrowingDelegates_AreTreatedAsMissing()
    {
        var registry = new FakeRegistry().WithApoKey().WithClsid(EqualizerApoInstallation.PreMixClsid);
        var installation = new EqualizerApoInstallation(
            registry,
            fileExists: _ => throw new UnauthorizedAccessException(),
            readText: _ => throw new IOException(),
            canWriteDirectory: _ => throw new IOException());

        Assert.False(installation.GetStatus().IsInstalled);
    }

    [Theory]
    [InlineData("{0.0.0.00000000}.{6A1F2C3D-0000-4000-8000-0123456789AB}", "{6a1f2c3d-0000-4000-8000-0123456789ab}")]
    [InlineData("{0.0.0.00000000}.{6a1f2c3d-0000-4000-8000-0123456789ab}", "{6a1f2c3d-0000-4000-8000-0123456789ab}")]
    public void EndpointId_ParsesTrailingGuid(string id, string expected)
    {
        Assert.True(WindowsAudioEndpointCatalog.TryParseEndpointGuid(id, out var guid));
        Assert.Equal(expected, guid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{0.0.0.00000000}")]
    [InlineData("{0.0.0.00000000}.{not-a-guid}")]
    [InlineData("{0.0.1.00000000}.6a1f2c3d-0000-4000-8000-0123456789ab")]
    [InlineData("SWD\\MMDEVAPI\\something")]
    public void MalformedEndpointId_IsSkipped(string? id)
    {
        Assert.False(WindowsAudioEndpointCatalog.TryParseEndpointGuid(id, out var guid));
        Assert.Equal(string.Empty, guid);
    }

    private static EqualizerApoInstallation Create(FakeRegistry registry, bool canWrite = true, string? configText = null)
        => new(
            registry,
            fileExists: path => string.Equals(path, Path.Combine(InstallPath, "EqualizerAPO.dll"), StringComparison.OrdinalIgnoreCase),
            readText: path => string.Equals(path, Path.Combine(ConfigPath, "config.txt"), StringComparison.OrdinalIgnoreCase) ? configText : null,
            canWriteDirectory: _ => canWrite);

    private sealed class FakeRegistry : IRegistryReader
    {
        private readonly Dictionary<(RegistryRoot Root, string Key), Dictionary<string, string>> _keys = new();

        public FakeRegistry WithApoKey()
            => WithValue(RegistryRoot.LocalMachine, @"SOFTWARE\EqualizerAPO", "InstallPath", InstallPath)
                .WithValue(RegistryRoot.LocalMachine, @"SOFTWARE\EqualizerAPO", "ConfigPath", ConfigPath);

        public FakeRegistry WithClsid(string clsid)
        {
            Key(RegistryRoot.ClassesRoot, $@"CLSID\{clsid}");
            return this;
        }

        public FakeRegistry WithValue(RegistryRoot root, string subKey, string name, string value)
        {
            Key(root, subKey)[name] = value;
            return this;
        }

        public bool KeyExists(RegistryRoot root, string subKey) => _keys.ContainsKey((root, subKey.ToUpperInvariant()));

        public string? GetValue(RegistryRoot root, string subKey, string valueName)
            => _keys.TryGetValue((root, subKey.ToUpperInvariant()), out var values) && values.TryGetValue(valueName, out var value)
                ? value
                : null;

        public IReadOnlyList<string> GetValueNames(RegistryRoot root, string subKey)
            => _keys.TryGetValue((root, subKey.ToUpperInvariant()), out var values) ? values.Keys.ToArray() : [];

        private Dictionary<string, string> Key(RegistryRoot root, string subKey)
        {
            var id = (root, subKey.ToUpperInvariant());
            if (!_keys.TryGetValue(id, out var values))
            {
                values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                _keys[id] = values;
            }

            return values;
        }
    }
}
