using System.ComponentModel;

namespace WolfEQ.ViewModels;

/// <summary>
/// One-line summaries shown under each collapsed section title in the Settings panel,
/// so the panel tells the user the current state without opening every section.
/// Each summary is derived from existing view-model properties and re-raised when
/// one of those inputs changes.
/// </summary>
public sealed partial class MainViewModel
{
    private const string SummarySeparator = " · ";

    private static readonly Dictionary<string, string[]> SettingsSummaryDependencies = BuildSettingsSummaryDependencies();

    public string DeviceSectionSummary
    {
        get
        {
            if (IsSystemEqSelected)
            {
                var status = SystemEqStatusText?.Trim().TrimEnd('.') ?? string.Empty;
                // "Software EQ on Headphones" already names the mode; don't repeat it.
                return status.Length == 0
                    ? "Software EQ"
                    : status.StartsWith("Software EQ", StringComparison.OrdinalIgnoreCase)
                        ? status
                        : $"Software EQ{SummarySeparator}{status}";
            }

            var name = string.IsNullOrWhiteSpace(_binding.UsbPath) || string.IsNullOrWhiteSpace(_binding.Name)
                ? SelectedDeviceProfile.DisplayName
                : _binding.Name.Trim();
            return $"{name}{SummarySeparator}{(IsDeviceConnected ? "Connected" : "Disconnected")}";
        }
    }

    public string LightsSectionSummary
    {
        get
        {
            if (IsSystemEqSelected || !SelectedDeviceSupportsBleLighting)
            {
                return "Not available for this device";
            }

            if (!TopLedOn && !KnobLedOn)
            {
                return "Lights off";
            }

            return $"Top {DescribeLed(TopLedOn, SelectedTopLedColorOption, SelectedTopLedModeOption)}" +
                   $"{SummarySeparator}Knob {DescribeLed(KnobLedOn, SelectedKnobLedColorOption, SelectedKnobLedModeOption)}";
        }
    }

    /// <summary>Collapsed summary for Settings › Sound (controls that live on the device).</summary>
    public string SoundSectionSummary
        => SelectedDeviceSupportsBleInput
            ? $"{SelectedDeviceInputSource.DisplayName} input{SummarySeparator}volume {DeviceVolume:0}"
            : $"Volume {DeviceVolume:0}";

    /// <summary>
    /// Names that identify the selected device's Windows playback output, e.g. "Speakers (FIIO K13 R2R)"
    /// matches "K13 R2R". Used by Sound › Volume, which sets the Windows output volume.
    /// </summary>
    internal IReadOnlyList<string> WindowsVolumeEndpointNames
    {
        get
        {
            var profile = SelectedDeviceProfile;
            var names = new List<string>(profile.ProductNameAliases ?? []) { profile.DisplayName };
            foreach (var brand in new[] { "FiiO ", "Snowsky " })
            {
                if (profile.DisplayName.StartsWith(brand, StringComparison.OrdinalIgnoreCase))
                    names.Add(profile.DisplayName[brand.Length..]);
            }

            return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }

    /// <summary>"?" help text for the model picker: replaces the spec fine print that used to sit in the panel.</summary>
    public string DeviceModelHelpText => IsK13R2RSelected
        ? $"{DeviceProfileCapabilityText}. Plays up to 192 kHz / 24-bit. MQA turns off while EQ is on, and switching EQ can briefly reconnect USB."
        : DeviceProfileCapabilityText;

    public string WindowsAudioSectionSummary
    {
        get
        {
            var format = WindowsAudioCurrentFormat?.Trim();
            return string.IsNullOrEmpty(format) ||
                   string.Equals(format, "Not checked", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(format, "Unavailable", StringComparison.OrdinalIgnoreCase)
                ? "Default output format and spatial sound"
                : $"Default output{SummarySeparator}{format}";
        }
    }

    public string IntegrationsSectionSummary
    {
        get
        {
            if (IsSystemEqSelected)
            {
                return "Bypass and preset shortcuts";
            }

            return CanCopyStreamDeckPowerShell
                ? $"Stream Deck{SummarySeparator}USER {IntegrationSlot} ready"
                : "Stream Deck shortcuts";
        }
    }

    public string AppearanceSectionSummary
    {
        get
        {
            var mode = string.IsNullOrWhiteSpace(SelectedBackgroundMode) ? "Dark" : SelectedBackgroundMode;
            return SelectedAccentColorOption is { } accent
                ? $"{mode}{SummarySeparator}{accent.Name}"
                : mode;
        }
    }

    public string AboutSectionSummary
    {
        get
        {
            var update = ShortUpdateStatus(UpdateStatus);
            return string.IsNullOrEmpty(update)
                ? $"WolfEQ {AppVersionText}"
                : $"WolfEQ {AppVersionText}{SummarySeparator}{update}";
        }
    }

    /// <summary>Hooks the view model's own change notifications to keep the summaries live.</summary>
    private void InitializeSettingsSummaries()
    {
        PropertyChanged += OnSettingsSummaryInputChanged;
    }

    private void OnSettingsSummaryInputChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName))
        {
            return;
        }

        if (SettingsSummaryDependencies.TryGetValue(e.PropertyName, out var summaries))
        {
            foreach (var summary in summaries)
            {
                OnPropertyChanged(summary);
            }
        }
    }

    private static Dictionary<string, string[]> BuildSettingsSummaryDependencies()
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        void Add(string summary, params string[] inputs)
        {
            foreach (var input in inputs)
            {
                if (!map.TryGetValue(input, out var set))
                {
                    map[input] = set = [];
                }

                set.Add(summary);
            }
        }

        Add(nameof(DeviceSectionSummary),
            nameof(IsSystemEqSelected), nameof(SelectedDeviceProfile), nameof(SystemEqStatusText),
            nameof(IsDeviceConnected), nameof(ConnectionText), nameof(DeviceBindingSummary));
        Add(nameof(LightsSectionSummary),
            nameof(IsSystemEqSelected), nameof(SelectedDeviceProfile), nameof(SelectedDeviceSupportsBleLighting),
            nameof(TopLedOn), nameof(KnobLedOn), nameof(TopLedSummary), nameof(KnobLedSummary),
            nameof(SelectedTopLedColorOption), nameof(SelectedKnobLedColorOption),
            nameof(SelectedTopLedModeOption), nameof(SelectedKnobLedModeOption));
        foreach (var editorProperty in LightEditorProperties())
        {
            Add(editorProperty,
                nameof(TopLedOn), nameof(KnobLedOn), nameof(SelectedTopLedColorOption), nameof(SelectedKnobLedColorOption),
                nameof(SelectedTopLedModeOption), nameof(SelectedKnobLedModeOption));
        }
        Add(nameof(LightsEditingTitle), nameof(SelectedDeviceUserPreset));
        Add(nameof(StreamDeckKeys), nameof(SlotLightingSummary), nameof(IntegrationSlots), nameof(DeviceUserPresets));
        Add(nameof(SelectedStreamDeckKey), nameof(SlotLightingSummary), nameof(IntegrationSlots), nameof(DeviceUserPresets));
        Add(nameof(SoundSectionSummary), nameof(SelectedDeviceInputSource), nameof(DeviceVolume), nameof(SelectedDeviceProfile));
        Add(nameof(DeviceModelHelpText), nameof(SelectedDeviceProfile), nameof(DeviceProfileCapabilityText), nameof(IsK13R2RSelected));
        Add(nameof(LightsNeedBluetooth), nameof(SelectedDeviceProfile), nameof(SelectedDeviceSupportsBleLighting), nameof(IsBluetoothSaved));
        Add(nameof(WindowsAudioSectionSummary), nameof(WindowsAudioCurrentFormat));
        Add(nameof(IntegrationsSectionSummary),
            nameof(IsSystemEqSelected), nameof(SelectedDeviceProfile), nameof(IntegrationSlot),
            nameof(CanCopyStreamDeckPowerShell), nameof(StreamDeckSetupNote));
        Add(nameof(AppearanceSectionSummary), nameof(SelectedBackgroundMode), nameof(SelectedAccentColorOption));
        Add(nameof(AboutSectionSummary), nameof(UpdateStatus));

        return map.ToDictionary(pair => pair.Key, pair => pair.Value.ToArray(), StringComparer.Ordinal);
    }

    private static string DescribeLed(bool on, LedColorOption? color, LedModeOption? mode)
    {
        if (!on)
        {
            return "off";
        }

        var colorName = color?.Name ?? "on";
        return mode is { Value: not 0x00 } ? $"{colorName} {mode.Name.ToLowerInvariant()}" : colorName;
    }

    private static string ShortUpdateStatus(string? status)
    {
        var text = status?.Trim() ?? string.Empty;
        if (text.Length == 0 || text.StartsWith("Updates are checked", StringComparison.OrdinalIgnoreCase))
        {
            return string.Empty;
        }

        if (text.Contains("up to date", StringComparison.OrdinalIgnoreCase))
        {
            return "Up to date";
        }

        if (text.StartsWith("WolfEQ ", StringComparison.Ordinal))
        {
            text = text["WolfEQ ".Length..];
        }

        return text.TrimEnd('.');
    }
}
