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
                return status.Length == 0
                    ? "Windows System EQ"
                    : $"Windows System EQ{SummarySeparator}{status}";
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
