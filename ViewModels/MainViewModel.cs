using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WolfEQ.Models;
using WolfEQ.Services;

namespace WolfEQ.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly FiioK13DeviceService _deviceService = new();
    private readonly FiioK13BleLightService _bleLightService = new();
    private readonly AutoEqGitHubProfileService _githubProfileService = new();
    private readonly LocalPresetLibraryService _localLibraryService = new();
    private readonly WindowsAudioFormatService _windowsAudioFormatService = new();
    private readonly SlotLightingProfileService _slotLightingProfileService = new();
    private readonly AppLogService _log = new();
    private readonly DispatcherTimer _profileAutosaveTimer;
    private readonly DispatcherTimer _liveDeviceEqSyncTimer;
    private readonly HashSet<int> _pendingLiveBandNumbers = [];
    private string _status = "Connect your K13 or edit profiles.";
    private string _liveDeviceEqSyncStatus = "Live sync on - connect K13 to save edits";
    private string _deviceProfileName = "ANANDA";
    private string _deviceVolumeDisplay = "Vol --";
    private string _deviceInputDisplay = "Input --";
    private string _githubProfileSearchText = "Ananda";
    private string _githubProfileStatus = "Online source ready";
    private string _connectionText = "Disconnected";
    private string _updateStatus = "Updates are checked from GitHub Releases when you ask.";
    private bool _isDeviceConnected;
    private bool _topLedOn = true;
    private bool _knobLedOn = true;
    private double _preampDb;
    private EqPreset _selectedPreset;
    private DeviceUserPresetOption _selectedDeviceUserPreset;
    private DeviceInputSourceOption _selectedDeviceInputSource;
    private LedColorOption _selectedTopLedColorOption;
    private LedColorOption _selectedKnobLedColorOption;
    private LedModeOption _selectedTopLedModeOption;
    private LedModeOption _selectedKnobLedModeOption;
    private WindowsAudioFormatOption? _selectedWindowsAudioFormat;
    private AutoEqProfileIndexEntry? _selectedGitHubProfile;
    private AutoEqProfileIndexEntry? _githubPreviewProfile;
    private EqPreset? _githubPreviewPreset;
    private CancellationTokenSource? _githubPreviewCts;
    private string _profileSearchText = string.Empty;
    private string _selectedProfileCategory = "All";
    private string _compareStatus = "A/B slot empty";
    private AccentColorOption _selectedAccentColorOption;
    private bool _favoritesOnly;
    private EqPreset? _comparePreset;
    private byte? _currentDevicePresetId;
    private string _windowsAudioFormatStatus = "Select the default playback format Windows reports for this device.";
    private bool _isLoadingWindowsAudioFormats;
    private bool _isApplyingWindowsAudioFormat;
    private bool _isLoadingPreset;
    private bool _liveDeviceEqSyncEnabled = true;
    private bool _pendingLivePreampSync;
    private bool _isSyncingLiveDeviceEq;
    private bool _isLoadingSlotLightingProfile;
    private Dictionary<int, SlotLightingProfileData> _slotLightingProfiles = [];

    public MainViewModel()
    {
        Presets = new ObservableCollection<EqPreset>(LoadStartupPresets());
        _profileAutosaveTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(650)
        };
        _profileAutosaveTimer.Tick += ProfileAutosaveTimerOnTick;
        _liveDeviceEqSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(550)
        };
        _liveDeviceEqSyncTimer.Tick += LiveDeviceEqSyncTimerOnTick;

        _selectedPreset = Presets[0];
        FilteredPresets = CollectionViewSource.GetDefaultView(Presets);
        FilteredPresets.Filter = FilterPreset;
        Presets.CollectionChanged += (_, _) =>
        {
            RefreshProfileLibrary();
            QueueProfileAutosave();
        };
        ProfileCategories = new ObservableCollection<string> { "All", "Listening", "Gaming", "Imported", "Online", "Reference", "Favorites", "K13 Ready" };
        AccentColorOptions = new ObservableCollection<AccentColorOption>
        {
            new("AmpUp Green", "#00E676"),
            new("Wolf Cyan", "#06B6D4"),
            new("FiiO Red", "#C8102E"),
            new("Gold", "#FFB800"),
            new("Rose", "#FF4D8D"),
            new("Violet", "#9B5CFF"),
            new("Ice Blue", "#5ED7FF")
        };
        _selectedAccentColorOption = AccentColorOptions[0];
        LedColorOptions = new ObservableCollection<LedColorOption>
        {
            new("Follow device", 0x00, "#8A8F98"),
            new("Red", 0x01, "#FF4F5E"),
            new("Blue", 0x02, "#2F80FF"),
            new("Cyan", 0x03, "#00D9FF"),
            new("Purple", 0x04, "#B25CFF"),
            new("Yellow", 0x05, "#FFCC33"),
            new("White", 0x06, "#F2F5F7"),
            new("Green", 0x07, "#00E676"),
            new("Rainbow", 0x08, "#FF7A18")
        };
        LedModeOptions = new ObservableCollection<LedModeOption>
        {
            new("Solid", 0x00),
            new("Pulse", 0x01)
        };
        LedSceneOptions = new ObservableCollection<LedSceneOption>
        {
            new("Neon Split", 0x03, 0x07, 0x00),
            new("Soft Pulse", 0x04, 0x05, 0x01),
            new("Audio Glow", 0x00, 0x00, 0x00),
            new("Cycle Pulse", 0x08, 0x08, 0x01)
        };
        _selectedTopLedColorOption = LedColorOptions.First(option => option.Value == 0x07);
        _selectedKnobLedColorOption = LedColorOptions.First(option => option.Value == 0x03);
        _selectedTopLedModeOption = LedModeOptions[0];
        _selectedKnobLedModeOption = LedModeOptions[0];
        LoadSlotLightingProfiles();
        Bands = _selectedPreset.Bands;
        WatchBands(Bands);
        _preampDb = _selectedPreset.PreampDb;
        DeviceUserPresets = new ObservableCollection<DeviceUserPresetOption>(
            Enumerable.Range(1, 10)
                .Select(slot => new DeviceUserPresetOption(slot, (byte)(0x9F + slot))));
        _selectedDeviceUserPreset = DeviceUserPresets[0];
        LoadSelectedSlotLightingProfileIntoUi();
        DeviceInputSources =
        [
            new DeviceInputSourceOption("USB", 0x01),
            new DeviceInputSourceOption("COAX", 0x04)
        ];
        _selectedDeviceInputSource = DeviceInputSources[0];
        K13FeatureRows =
        [
            new K13FeatureRow("Save to Device", "PEQ edits save through live sync", "Ready"),
            new K13FeatureRow("Live Device Sync", "Preamp and band edits write to K13", "Ready"),
            new K13FeatureRow("Output Mode", "Headphones and line out controls", "Next"),
            new K13FeatureRow("Gain Mode", "Low and high gain controls", "Next"),
            new K13FeatureRow("Sampling Mode", "OS / NOS switching", "Next"),
            new K13FeatureRow("DSD Mode", "DSD playback behavior", "Next"),
            new K13FeatureRow("Balance", "Left and right level balance", "Next"),
            new K13FeatureRow("Light Brightness", "Color and mode work now; brightness comes next", "Next")
        ];
        WindowsAudioFormats = [];
        LoadWindowsAudioFormats();

        GitHubProfiles = [];
        DeviceLog = [];
        AddLog("Ready. Device reads, USER switching, input, lighting, EQ on/off, live preamp, and single-band EQ writes are guarded.");
        AddLog($"File log: {_log.LogFilePath}");

        ConnectCommand = new AsyncRelayCommand(DetectDeviceAsync);
        ReadDeviceCommand = new AsyncRelayCommand(ReadDeviceEqAsync);
        RenameUserSlotCommand = new AsyncRelayCommand(RenameCurrentUserSlotAsync);
        SelectUserPresetCommand = new AsyncRelayCommand(SelectUserPresetAsync);
        ReadLightsCommand = new AsyncRelayCommand(ReadLightsAsync);
        LedOnCommand = new AsyncRelayCommand(SetLightsOnAsync);
        LedOffCommand = new AsyncRelayCommand(SetLightsOffAsync);
        LedGreenCommand = new AsyncRelayCommand(SetLightsGreenAsync);
        LedCycleCommand = new AsyncRelayCommand(SetLightsCycleAsync);
        ReadInputSourceCommand = new AsyncRelayCommand(ReadInputSourceAsync);
        SetInputSourceCommand = new AsyncRelayCommand(SetInputSourceAsync);
        ReadVolumeCommand = new AsyncRelayCommand(ReadVolumeAsync);
        ProbeVolumeCommand = new AsyncRelayCommand(ProbeVolume85Async);
        ListenVolumeCommand = new AsyncRelayCommand(ListenVolumeAsync);
        VolumeDownCommand = new AsyncRelayCommand(VolumeDownAsync, () => false);
        VolumeUpCommand = new AsyncRelayCommand(VolumeUpAsync, () => false);
        SaveCommand = new RelayCommand(async () => await SaveAsync(), () => CanWriteToHardware);
        ResetCommand = new RelayCommand(LoadSelectedPreset);
        ImportApoFromFileCommand = new RelayCommand(ImportApoFromFile);
        ExportApoToFileCommand = new RelayCommand(ExportApoToFile, () => SelectedPreset is not null);
        ImportApoFromClipboardCommand = new RelayCommand(ImportApoFromClipboard);
        ExportApoToClipboardCommand = new RelayCommand(ExportApoToClipboard, () => SelectedPreset is not null);
        ImportFiioXmlFromFileCommand = new RelayCommand(ImportFiioXmlFromFile);
        ExportFiioXmlToFileCommand = new RelayCommand(ExportFiioXmlToFile, () => SelectedPreset is not null);
        ToggleFavoriteCommand = new RelayCommand(ToggleSelectedFavorite, () => SelectedPreset is not null);
        SaveProfileLibraryCommand = new RelayCommand(SaveProfileLibrary, () => SelectedPreset is not null);
        ImportJsonFromFileCommand = new RelayCommand(ImportJsonFromFile);
        ExportJsonToFileCommand = new RelayCommand(ExportJsonToFile, () => SelectedPreset is not null);
        ImportLibraryJsonCommand = new RelayCommand(ImportLibraryJsonFromFile);
        ExportLibraryJsonCommand = new RelayCommand(ExportLibraryJsonToFile, () => Presets.Count > 0);
        CopyTuningReportCommand = new RelayCommand(CopyTuningReportToClipboard, () => SelectedPreset is not null);
        SearchGitHubProfilesCommand = new AsyncRelayCommand(SearchGitHubProfilesAsync);
        ImportGitHubProfileCommand = new AsyncRelayCommand(ImportGitHubProfileAsync);
        ApplyAutoHeadroomCommand = new RelayCommand(ApplyAutoHeadroom);
        CaptureCompareCommand = new RelayCommand(CaptureComparePreset);
        SwapCompareCommand = new RelayCommand(SwapComparePreset);
        ClearCompareCommand = new RelayCommand(ClearComparePreset);
        DuplicatePresetCommand = new RelayCommand(DuplicateSelectedPreset);
        CreateFlatPresetCommand = new RelayCommand(CreateFlatPreset);
        EnableAllBandsCommand = new RelayCommand(EnableAllBands);
        BypassAllBandsCommand = new RelayCommand(BypassAllBands);
        ZeroAllGainsCommand = new RelayCommand(ZeroAllGains);
        SortBandsByFrequencyCommand = new RelayCommand(SortBandsByFrequency);
        PrepareK13PresetCommand = new RelayCommand(PrepareK13Preset);
        SmoothK13PresetCommand = new RelayCommand(SmoothK13Preset);
        AddWarmTiltCommand = new RelayCommand(AddWarmTiltPreset);
        AddTrebleTamerCommand = new RelayCommand(AddTrebleTamerPreset);
        AddGamingClarityCommand = new RelayCommand(AddGamingClarityPreset);
        OpenWindowsSoundSettingsCommand = new RelayCommand(OpenWindowsSoundSettings);
        OpenFiioSupportCommand = new RelayCommand(OpenFiioSupport);
        CheckForUpdatesCommand = new AsyncRelayCommand(CheckForUpdatesAsync);
        OpenReleasesCommand = new RelayCommand(() => OpenUrl("https://github.com/audioslayer/wolfeq/releases", "WolfEQ releases"));
        OpenBuyMeCoffeeCommand = new RelayCommand(() => OpenUrl("https://www.buymeacoffee.com/audioslayer", "Buy Me a Coffee"));
    }

    public ObservableCollection<EqPreset> Presets { get; }
    public ICollectionView FilteredPresets { get; }
    public ObservableCollection<string> ProfileCategories { get; }
    public ObservableCollection<AccentColorOption> AccentColorOptions { get; }
    public ObservableCollection<LedColorOption> LedColorOptions { get; }
    public ObservableCollection<LedModeOption> LedModeOptions { get; }
    public ObservableCollection<LedSceneOption> LedSceneOptions { get; }
    public ObservableCollection<DeviceUserPresetOption> DeviceUserPresets { get; }
    public ObservableCollection<DeviceInputSourceOption> DeviceInputSources { get; }
    public ObservableCollection<K13FeatureRow> K13FeatureRows { get; }
    public ObservableCollection<WindowsAudioFormatOption> WindowsAudioFormats { get; }
    public ObservableCollection<EqBand> Bands { get; private set; }
    public ObservableCollection<AutoEqProfileIndexEntry> GitHubProfiles { get; }
    public ObservableCollection<string> DeviceLog { get; }
    public string LogFilePath => _log.LogFilePath;
    public int EnabledBandCount => Bands.Count(band => band.Enabled);
    public double MaxEnabledBoostDb => Bands.Count == 0 ? 0 : Bands.Where(band => band.Enabled).Select(band => band.GainDb).DefaultIfEmpty(0).Max();
    public double RecommendedPreampDb => Math.Min(0, -MaxEnabledBoostDb);
    public string PresetEditSummaryText => $"{EnabledBandCount}/{Bands.Count} on · headroom {RecommendedPreampDb:F1} dB";
    public string LiveDeviceEqSyncStatus
    {
        get => _liveDeviceEqSyncStatus;
        private set => SetField(ref _liveDeviceEqSyncStatus, value);
    }

    public string BandInspectorText => BuildBandInspectorText();
    public string BandInspectorHint => BuildBandInspectorHint();
    public int FilteredPresetCount => FilteredPresets.Cast<EqPreset>().Count();
    public int GitHubProfileCount => GitHubProfiles.Count;
    public int FavoritePresetCount => Presets.Count(preset => preset.IsFavorite);
    public int K13StageablePresetCount => Presets.Count(IsK13StageablePreset);
    public string ProfileLibrarySummaryText => $"{FilteredPresetCount}/{Presets.Count} shown · {FavoritePresetCount} favorite(s) · {K13StageablePresetCount} K13-stageable";
    public string ProfileLibraryHint => BuildProfileLibraryHint();
    public string ClippingHeadroomText => PreampDb + MaxEnabledBoostDb > 0
        ? $"Clipping risk: preamp + max boost = {PreampDb + MaxEnabledBoostDb:F1} dB. Suggested preamp {RecommendedPreampDb:F1} dB."
        : $"Headroom OK: preamp + max boost = {PreampDb + MaxEnabledBoostDb:F1} dB.";
    public double BassAverageDb => AverageGainForRange(20, 250);
    public double MidAverageDb => AverageGainForRange(250, 2000);
    public double PresenceAverageDb => AverageGainForRange(2000, 6000);
    public double AirAverageDb => AverageGainForRange(6000, 20000);
    public string TonalSnapshotText =>
        $"Bass {BassAverageDb:+0.0;-0.0;0.0} dB · mids {MidAverageDb:+0.0;-0.0;0.0} dB · presence {PresenceAverageDb:+0.0;-0.0;0.0} dB · air {AirAverageDb:+0.0;-0.0;0.0} dB";
    public string TonalBalanceHint => BuildTonalBalanceHint();
    public string K13ReadinessText => BuildK13ReadinessText();
    public string K13ReadinessHint => BuildK13ReadinessHint();
    public int TuningConfidenceScore => BuildTuningConfidenceScore();
    public string TuningConfidenceText => $"Ready score {TuningConfidenceScore}/100";
    public string TuningConfidenceHint => BuildTuningConfidenceHint();
    public string CompareDeltaText => BuildCompareDeltaText();
    public string CompareDeltaHint => BuildCompareDeltaHint();
    public string AppVersionText => $"v{AppUpdateService.CurrentVersion}";

    public string UpdateStatus
    {
        get => _updateStatus;
        private set => SetField(ref _updateStatus, value);
    }

    public string ConnectionText
    {
        get => _connectionText;
        private set => SetField(ref _connectionText, value);
    }

    public bool IsDeviceConnected
    {
        get => _isDeviceConnected;
        private set => SetField(ref _isDeviceConnected, value);
    }

    public EqPreset? ComparePreset
    {
        get => _comparePreset;
        private set
        {
            if (SetField(ref _comparePreset, value))
            {
                OnPropertyChanged(nameof(CompareDeltaText));
                OnPropertyChanged(nameof(CompareDeltaHint));
            }
        }
    }

    public string CompareStatus
    {
        get => _compareStatus;
        private set => SetField(ref _compareStatus, value);
    }

    public AccentColorOption SelectedAccentColorOption
    {
        get => _selectedAccentColorOption;
        set
        {
            if (value is null)
            {
                return;
            }

            if (SetField(ref _selectedAccentColorOption, value))
            {
                ApplyAccentColor(value.Hex);
                Status = $"App accent changed to {value.Name}.";
            }
        }
    }

    public LedColorOption SelectedTopLedColorOption
    {
        get => _selectedTopLedColorOption;
        private set
        {
            if (SetField(ref _selectedTopLedColorOption, value))
            {
                OnPropertyChanged(nameof(TopLedSummary));
                OnPropertyChanged(nameof(SlotLightingSummary));
            }
        }
    }

    public LedColorOption SelectedKnobLedColorOption
    {
        get => _selectedKnobLedColorOption;
        private set
        {
            if (SetField(ref _selectedKnobLedColorOption, value))
            {
                OnPropertyChanged(nameof(KnobLedSummary));
                OnPropertyChanged(nameof(SlotLightingSummary));
            }
        }
    }

    public LedModeOption SelectedTopLedModeOption
    {
        get => _selectedTopLedModeOption;
        private set
        {
            if (SetField(ref _selectedTopLedModeOption, value))
            {
                OnPropertyChanged(nameof(TopLedSummary));
                OnPropertyChanged(nameof(SlotLightingSummary));
            }
        }
    }

    public LedModeOption SelectedKnobLedModeOption
    {
        get => _selectedKnobLedModeOption;
        private set
        {
            if (SetField(ref _selectedKnobLedModeOption, value))
            {
                OnPropertyChanged(nameof(KnobLedSummary));
                OnPropertyChanged(nameof(SlotLightingSummary));
            }
        }
    }

    public bool TopLedOn
    {
        get => _topLedOn;
        private set
        {
            if (SetField(ref _topLedOn, value))
            {
                OnPropertyChanged(nameof(TopLedSummary));
                OnPropertyChanged(nameof(SlotLightingSummary));
            }
        }
    }

    public bool KnobLedOn
    {
        get => _knobLedOn;
        private set
        {
            if (SetField(ref _knobLedOn, value))
            {
                OnPropertyChanged(nameof(KnobLedSummary));
                OnPropertyChanged(nameof(SlotLightingSummary));
            }
        }
    }

    public string TopLedSummary => BuildLedSummary("Top", TopLedOn, SelectedTopLedModeOption, SelectedTopLedColorOption);
    public string KnobLedSummary => BuildLedSummary("Knob", KnobLedOn, SelectedKnobLedModeOption, SelectedKnobLedColorOption);
    public string SlotLightingTitle => $"{SelectedDeviceUserPreset.DisplayName} Lighting";
    public string SlotLightingSummary => $"{TopLedSummary} / {KnobLedSummary}";

    public EqPreset SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            if (SetField(ref _selectedPreset, value)) LoadSelectedPreset();
        }
    }

    public double PreampDb
    {
        get => _preampDb;
        set
        {
            if (SetField(ref _preampDb, Math.Clamp(Math.Round(value, 1), -24.0, 12.0)))
            {
                SelectedPreset.PreampDb = _preampDb;
                RefreshHeadroomProperties();
                QueueProfileAutosave();
                QueueLiveDevicePreampSync();
            }
        }
    }

    public bool LiveDeviceEqSyncEnabled
    {
        get => true;
        set
        {
            if (!value)
            {
                return;
            }

            if (SetField(ref _liveDeviceEqSyncEnabled, true))
            {
                LiveDeviceEqSyncStatus = value
                    ? "Live sync on · writes save to the active K13 profile"
                    : "Off";
                AddLog(value
                    ? "Live device EQ sync enabled. Preamp and band edits will use guarded USB writes."
                    : "Live device EQ sync disabled.");
            }
        }
    }

    public string Status
    {
        get => _status;
        set => SetField(ref _status, value);
    }

    public string DeviceProfileName
    {
        get => _deviceProfileName;
        set => SetField(ref _deviceProfileName, SanitizeProfileName(value));
    }

    public string DeviceVolumeDisplay
    {
        get => _deviceVolumeDisplay;
        set => SetField(ref _deviceVolumeDisplay, value);
    }

    public string DeviceInputDisplay
    {
        get => _deviceInputDisplay;
        set => SetField(ref _deviceInputDisplay, value);
    }

    public string GitHubProfileSearchText
    {
        get => _githubProfileSearchText;
        set => SetField(ref _githubProfileSearchText, value);
    }

    public string GitHubProfileStatus
    {
        get => _githubProfileStatus;
        set => SetField(ref _githubProfileStatus, value);
    }

    public DeviceUserPresetOption SelectedDeviceUserPreset
    {
        get => _selectedDeviceUserPreset;
        set
        {
            if (SetField(ref _selectedDeviceUserPreset, value))
            {
                LoadSelectedSlotLightingProfileIntoUi();
                OnPropertyChanged(nameof(SlotLightingTitle));
                OnPropertyChanged(nameof(SlotLightingSummary));
            }
        }
    }

    public DeviceInputSourceOption SelectedDeviceInputSource
    {
        get => _selectedDeviceInputSource;
        set => SetField(ref _selectedDeviceInputSource, value);
    }

    public WindowsAudioFormatOption? SelectedWindowsAudioFormat
    {
        get => _selectedWindowsAudioFormat;
        set
        {
            if (value is not null && SetField(ref _selectedWindowsAudioFormat, value) && !_isLoadingWindowsAudioFormats)
            {
                ApplySelectedWindowsAudioFormat();
            }
        }
    }

    public string WindowsAudioFormatStatus
    {
        get => _windowsAudioFormatStatus;
        private set => SetField(ref _windowsAudioFormatStatus, value);
    }

    public AutoEqProfileIndexEntry? SelectedGitHubProfile
    {
        get => _selectedGitHubProfile;
        set
        {
            if (SetField(ref _selectedGitHubProfile, value))
            {
                _ = LoadGitHubProfilePreviewAsync(value);
            }
        }
    }

    public EqPreset? GitHubPreviewPreset
    {
        get => _githubPreviewPreset;
        private set => SetField(ref _githubPreviewPreset, value);
    }

    public string ProfileSearchText
    {
        get => _profileSearchText;
        set
        {
            if (SetField(ref _profileSearchText, value))
            {
                RefreshProfileLibrary();
            }
        }
    }

    public string SelectedProfileCategory
    {
        get => _selectedProfileCategory;
        set
        {
            if (SetField(ref _selectedProfileCategory, value))
            {
                FavoritesOnly = string.Equals(value, "Favorites", StringComparison.OrdinalIgnoreCase);
                RefreshProfileLibrary();
            }
        }
    }

    public bool FavoritesOnly
    {
        get => _favoritesOnly;
        set
        {
            if (SetField(ref _favoritesOnly, value))
            {
                RefreshProfileLibrary();
            }
        }
    }

    public ICommand ConnectCommand { get; }
    public ICommand ReadDeviceCommand { get; }
    public ICommand RenameUserSlotCommand { get; }
    public ICommand SelectUserPresetCommand { get; }
    public ICommand ReadLightsCommand { get; }
    public ICommand LedOnCommand { get; }
    public ICommand LedOffCommand { get; }
    public ICommand LedGreenCommand { get; }
    public ICommand LedCycleCommand { get; }
    public ICommand ReadInputSourceCommand { get; }
    public ICommand SetInputSourceCommand { get; }
    public ICommand ReadVolumeCommand { get; }
    public ICommand ProbeVolumeCommand { get; }
    public ICommand ListenVolumeCommand { get; }
    public ICommand VolumeDownCommand { get; }
    public ICommand VolumeUpCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand ResetCommand { get; }
    public ICommand ImportApoFromFileCommand { get; }
    public ICommand ExportApoToFileCommand { get; }
    public ICommand ImportApoFromClipboardCommand { get; }
    public ICommand ExportApoToClipboardCommand { get; }
    public ICommand ImportFiioXmlFromFileCommand { get; }
    public ICommand ExportFiioXmlToFileCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand SaveProfileLibraryCommand { get; }
    public ICommand ImportJsonFromFileCommand { get; }
    public ICommand ExportJsonToFileCommand { get; }
    public ICommand ImportLibraryJsonCommand { get; }
    public ICommand ExportLibraryJsonCommand { get; }
    public ICommand CopyTuningReportCommand { get; }
    public ICommand SearchGitHubProfilesCommand { get; }
    public ICommand ImportGitHubProfileCommand { get; }
    public ICommand ApplyAutoHeadroomCommand { get; }
    public ICommand CaptureCompareCommand { get; }
    public ICommand SwapCompareCommand { get; }
    public ICommand ClearCompareCommand { get; }
    public ICommand DuplicatePresetCommand { get; }
    public ICommand CreateFlatPresetCommand { get; }
    public ICommand EnableAllBandsCommand { get; }
    public ICommand BypassAllBandsCommand { get; }
    public ICommand ZeroAllGainsCommand { get; }
    public ICommand SortBandsByFrequencyCommand { get; }
    public ICommand PrepareK13PresetCommand { get; }
    public ICommand SmoothK13PresetCommand { get; }
    public ICommand AddWarmTiltCommand { get; }
    public ICommand AddTrebleTamerCommand { get; }
    public ICommand AddGamingClarityCommand { get; }
    public ICommand OpenWindowsSoundSettingsCommand { get; }
    public ICommand OpenFiioSupportCommand { get; }
    public ICommand CheckForUpdatesCommand { get; }
    public ICommand OpenReleasesCommand { get; }
    public ICommand OpenBuyMeCoffeeCommand { get; }
    public bool CanWriteToHardware => false;

    public event PropertyChangedEventHandler? PropertyChanged;

    private async Task DetectDeviceAsync()
    {
        Status = "Scanning Windows HID interfaces for FiiO VID_2972...";
        DeviceLog.Clear();
        AddLog("Read-only scan started. No output, feature, or SET packets will be sent.");

        try
        {
            var result = await _deviceService.DetectUsbAsync();
            Status = result.StatusMessage;
            SetConnectionState(result.IsDetected);

            AddLog($"Scanned {result.ScannedDeviceCount} HID interface(s).");

            if (result.Candidates.Count == 0)
            {
                AddLog("No VID_2972 HID candidates found.");
            }

            foreach (var candidate in result.Candidates)
            {
                AddLog(FormatCandidate(candidate));

                if (!string.IsNullOrWhiteSpace(candidate.ReadbackError))
                {
                    AddLog($"  Metadata readback issue: {candidate.ReadbackError}");
                }

                AddLog($"  Path: {candidate.DevicePath}");
            }

            AddLog("Detection complete. Hardware writes remain disabled.");
        }
        catch (OperationCanceledException)
        {
            Status = "USB HID detection was canceled.";
            SetConnectionState(false);
            AddLog("Detection canceled.");
        }
        catch (Exception ex)
        {
            Status = $"USB HID detection failed: {ex.Message}";
            SetConnectionState(false);
            AddLog(Status);
        }
    }

    private async Task ReadDeviceEqAsync()
    {
        Status = "Reading K13 EQ with read-only GET packets...";
        AddLog("Device EQ readback started. GET packets only; no SET/save commands will be sent.");

        try
        {
            var snapshot = await _deviceService.ReadCurrentEqAsync();
            SetConnectionState(true);
            _currentDevicePresetId = snapshot.PresetId;
            SelectDeviceUserPresetOption(snapshot.PresetId);

            foreach (var line in snapshot.TransportLog)
            {
                AddLog($"  {line}");
            }

            var readbackPreset = UpsertReadbackPreset(snapshot);
            SelectedPreset = readbackPreset;

            Status = $"Read {snapshot.Bands.Count} band(s) from {snapshot.PresetDisplayName}.";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "K13 EQ readback was canceled.";
            AddLog("Device EQ readback canceled.");
        }
        catch (Exception ex)
        {
            Status = $"K13 EQ readback failed: {ex.Message}";
            SetConnectionState(false);
            AddLog(Status);
        }
    }

    private async Task SelectUserPresetAsync()
    {
        var option = SelectedDeviceUserPreset;
        Status = $"Switching K13 to {option.DisplayName}...";
        AddLog($"Guarded USER preset switch requested: {option.DisplayName} (0x{option.PresetId:X2}).");

        try
        {
            var result = await _deviceService.SelectUserPresetAsync(option.PresetId);
            SetConnectionState(true);
            _currentDevicePresetId = result.AfterPresetId;
            SelectDeviceUserPresetOption(result.AfterPresetId);

            foreach (var line in result.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = result.Confirmed
                ? $"Preset switched: {result.BeforeDisplay} -> {result.AfterDisplay}"
                : $"Preset switch unconfirmed. Requested {result.RequestedDisplay}, read back {result.AfterDisplay}.";
            AddLog(Status);

            await ApplySelectedSlotLightingAsync();

            AddLog("Auto-reading EQ after USER slot switch.");
            await ReadDeviceEqAsync();
        }
        catch (OperationCanceledException)
        {
            Status = "Preset switch was canceled.";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"Preset switch failed: {ex.Message}";
            SetConnectionState(false);
            AddLog(Status);
        }
    }

    private async Task RenameCurrentUserSlotAsync()
    {
        if (_currentDevicePresetId is not byte presetId)
        {
            Status = "Read the device EQ first so WolfEQ knows which USER slot is active.";
            AddLog(Status);
            return;
        }

        if (presetId is < 160 or > 169)
        {
            Status = $"Current preset {K13EqReadback.GetPresetDisplayName(presetId)} is not a USER slot. Rename skipped.";
            AddLog(Status);
            return;
        }

        Status = $"Renaming USER {presetId - 159} to {DeviceProfileName}...";
        AddLog($"Guarded preset rename requested for USER {presetId - 159}: '{DeviceProfileName}'.");

        try
        {
            var result = await _deviceService.RenameUserPresetAsync(presetId, DeviceProfileName);

            foreach (var line in result.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = $"Rename sent for {result.UserSlotDisplay}: {result.RequestedName}. Device readback: {result.ReadBackName ?? "(blank)"}";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "Preset rename was canceled.";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"Preset rename failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task ReadLightsAsync()
    {
        Status = "Reading K13 lights over BLE...";
        AddLog("BLE light readback requested.");

        try
        {
            var snapshot = await _bleLightService.ReadLightsAsync();
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            Status = $"Lights: {snapshot.Top}; {snapshot.Knob}";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE light readback was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE light readback failed: {ex.Message}";
            AddLog(Status);
        }
    }

    public async Task SetTopLedColorAsync(LedColorOption option)
    {
        SelectedTopLedColorOption = option;
        await SetLedZoneAsync(0x02, SelectedTopLedColorOption, SelectedTopLedModeOption, TopLedOn, "Top");
    }

    public async Task SetKnobLedColorAsync(LedColorOption option)
    {
        SelectedKnobLedColorOption = option;
        await SetLedZoneAsync(0x03, SelectedKnobLedColorOption, SelectedKnobLedModeOption, KnobLedOn, "Knob");
    }

    public async Task SetTopLedModeAsync(LedModeOption option)
    {
        SelectedTopLedModeOption = option;
        await SetLedZoneAsync(0x02, SelectedTopLedColorOption, SelectedTopLedModeOption, TopLedOn, "Top");
    }

    public async Task SetKnobLedModeAsync(LedModeOption option)
    {
        SelectedKnobLedModeOption = option;
        await SetLedZoneAsync(0x03, SelectedKnobLedColorOption, SelectedKnobLedModeOption, KnobLedOn, "Knob");
    }

    public async Task SetTopLedPowerAsync(bool on)
    {
        TopLedOn = on;
        await SetLedPowerAsync(0x02, on, "Top");
    }

    public async Task SetKnobLedPowerAsync(bool on)
    {
        KnobLedOn = on;
        await SetLedPowerAsync(0x03, on, "Knob");
    }

    public async Task ApplyLedSceneAsync(LedSceneOption scene)
    {
        SelectedTopLedColorOption = SelectLedColorOption(scene.TopColor);
        SelectedKnobLedColorOption = SelectLedColorOption(scene.KnobColor);
        SelectedTopLedModeOption = SelectLedModeOption(scene.Mode);
        SelectedKnobLedModeOption = SelectLedModeOption(scene.Mode);
        TopLedOn = true;
        KnobLedOn = true;

        Status = $"Applying {scene.Name} lighting...";
        AddLog($"BLE split light scene requested: {scene.Name}.");

        try
        {
            var snapshot = await _bleLightService.SetSplitLightsAsync(
                scene.TopColor,
                scene.Mode,
                topOn: true,
                scene.KnobColor,
                scene.Mode,
                knobOn: true);
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            Status = $"Lighting scene applied: {scene.Name}";
            SaveSelectedSlotLightingProfile();
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE light scene was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            LogBleException(ex);
            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE light scene failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task SetLightsGreenAsync()
    {
        await SetLightsAsync(color: 0x07, mode: 0x00, on: true, label: "Green / Solid");
    }

    private async Task SetLightsCycleAsync()
    {
        await SetLightsAsync(color: 0x08, mode: 0x01, on: true, label: "Rainbow / Pulse");
    }

    private async Task SetLedZoneAsync(
        byte zone,
        LedColorOption color,
        LedModeOption mode,
        bool on,
        string zoneName)
    {
        Status = $"Setting {zoneName} LED to {color.Name} / {mode.Name}...";
        AddLog($"BLE {zoneName} LED write requested: {color.Name} / {mode.Name}.");

        try
        {
            var snapshot = await _bleLightService.SetLightAsync(zone, color.Value, mode.Value, on);
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            Status = $"{zoneName} LED set: {color.Name} / {mode.Name}";
            SaveSelectedSlotLightingProfile();
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE LED write was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            LogBleException(ex);
            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE LED write failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task SetLedPowerAsync(byte zone, bool on, string zoneName)
    {
        Status = $"Turning {zoneName} LED {(on ? "on" : "off")}...";
        AddLog($"BLE {zoneName} LED power requested: {(on ? "On" : "Off")}.");

        try
        {
            var snapshot = await _bleLightService.SetLightPowerAsync(zone, on);
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            Status = $"{zoneName} LED {(on ? "on" : "off")}: {snapshot.Top}; {snapshot.Knob}";
            SaveSelectedSlotLightingProfile();
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE LED power change was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            LogBleException(ex);
            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE LED power change failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task ReadInputSourceAsync()
    {
        Status = "Reading K13 input source over BLE...";
        AddLog("BLE input source readback requested.");

        try
        {
            var snapshot = await _bleLightService.ReadInputSourceAsync();
            LogBleSnapshot(snapshot);
            SelectInputSourceOption(snapshot.After);
            DeviceInputDisplay = $"Input {snapshot.AfterName}";
            Status = $"Input source: {snapshot.AfterName}";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE input source readback was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE input source readback failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task SetInputSourceAsync()
    {
        var option = SelectedDeviceInputSource;
        Status = $"Setting K13 input to {option.DisplayName} over BLE...";
        AddLog($"BLE input source write requested: {option.DisplayName}.");

        try
        {
            var snapshot = await _bleLightService.SetInputSourceAsync(option.Value);
            LogBleSnapshot(snapshot);
            SelectInputSourceOption(snapshot.After);
            DeviceInputDisplay = $"Input {snapshot.AfterName}";
            Status = snapshot.Confirmed
                ? $"Input switched: {snapshot.BeforeName} -> {snapshot.AfterName}"
                : $"Input switch unconfirmed. Requested {snapshot.RequestedName}, read back {snapshot.AfterName}.";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE input source write was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE input source write failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void LoadWindowsAudioFormats()
    {
        _isLoadingWindowsAudioFormats = true;
        WindowsAudioFormats.Clear();

        try
        {
            var catalog = _windowsAudioFormatService.GetDefaultRenderFormats();
            foreach (var option in catalog.Options)
            {
                WindowsAudioFormats.Add(option);
            }

            _selectedWindowsAudioFormat = catalog.Current ?? WindowsAudioFormats.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedWindowsAudioFormat));
            WindowsAudioFormatStatus = WindowsAudioFormats.Count == 0
                ? "No playback quality options were found for the current device."
                : $"Current quality: {SelectedWindowsAudioFormat?.DisplayName}.";
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or UnauthorizedAccessException)
        {
            WindowsAudioFormatStatus = $"Playback quality could not be loaded: {ex.Message}";
            AddLog(WindowsAudioFormatStatus);
        }
        finally
        {
            _isLoadingWindowsAudioFormats = false;
        }
    }

    private void ApplySelectedWindowsAudioFormat()
    {
        if (_isApplyingWindowsAudioFormat)
        {
            return;
        }

        if (SelectedWindowsAudioFormat is null)
        {
            LoadWindowsAudioFormats();
            if (SelectedWindowsAudioFormat is null)
            {
                WindowsAudioFormatStatus = "No playback quality options were found for the current device.";
                Status = WindowsAudioFormatStatus;
                AddLog(WindowsAudioFormatStatus);
                return;
            }
        }

        try
        {
            _isApplyingWindowsAudioFormat = true;
            var result = _windowsAudioFormatService.SetDefaultRenderFormat(SelectedWindowsAudioFormat);

            WindowsAudioFormatStatus = $"Playback quality saved: {result.DisplayText}.";
            Status = WindowsAudioFormatStatus;
            AddLog($"Windows audio format applied: {result.DisplayText}");
        }
        catch (Exception ex) when (ex is COMException or ArgumentOutOfRangeException or InvalidOperationException or UnauthorizedAccessException)
        {
            WindowsAudioFormatStatus = $"Playback quality was not accepted: {ex.Message}";
            Status = WindowsAudioFormatStatus;
            AddLog(WindowsAudioFormatStatus);
        }
        finally
        {
            _isApplyingWindowsAudioFormat = false;
        }
    }

    private void OpenWindowsSoundSettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "ms-settings:sound",
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            WindowsAudioFormatStatus = $"Windows Sound settings could not be opened: {ex.Message}";
            Status = WindowsAudioFormatStatus;
            AddLog(WindowsAudioFormatStatus);
        }
    }

    private void OpenFiioSupport()
    {
        OpenUrl("https://www.fiio.com/supports", "FiiO Support");
    }

    private async Task CheckForUpdatesAsync()
    {
        UpdateStatus = "Checking GitHub Releases...";
        Status = UpdateStatus;
        AddLog(UpdateStatus);

        try
        {
            var update = await AppUpdateService.CheckForUpdateAsync();
            if (update is null)
            {
                UpdateStatus = "WolfEQ is up to date.";
                Status = UpdateStatus;
                AddLog(UpdateStatus);
                return;
            }

            UpdateStatus = $"WolfEQ {update.Tag} is available.";
            Status = UpdateStatus;
            AddLog(UpdateStatus);

            var result = MessageBox.Show(
                $"WolfEQ {update.Tag} is available. Download and install it now?",
                "WolfEQ Update",
                MessageBoxButton.YesNo,
                MessageBoxImage.Information);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }

            UpdateStatus = "Downloading update...";
            await AppUpdateService.DownloadAndInstallAsync(update, progress =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateStatus = $"Downloading... {progress}%";
                });
            });
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Update check failed: {ex.Message}";
            Status = UpdateStatus;
            AddLog(Status);
        }
    }

    private void OpenUrl(string url, string label)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Status = $"{label} could not be opened: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task ReadVolumeAsync()
    {
        Status = "Reading K13 volume over BLE...";
        AddLog("BLE volume readback requested.");

        try
        {
            var snapshot = await _bleLightService.ReadVolumeAsync();
            LogBleSnapshot(snapshot);
            var isTrusted = !snapshot.CommandName.Contains("legacy", StringComparison.OrdinalIgnoreCase);
            DeviceVolumeDisplay = isTrusted ? $"Vol {snapshot.After}/99" : "Vol ?";
            Status = isTrusted
                ? $"Volume: {snapshot.After}/99"
                : $"Unverified volume candidate: {snapshot.After}/99 via {snapshot.CommandName}.";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE volume readback was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE volume readback failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task VolumeDownAsync()
    {
        await ChangeVolumeAsync(direction: -1, label: "down one step");
    }

    private async Task VolumeUpAsync()
    {
        await ChangeVolumeAsync(direction: 1, label: "up one step");
    }

    private async Task ProbeVolume85Async()
    {
        Status = "Probing K13 volume readback candidates over BLE...";
        AddLog("BLE volume probe requested. Set the K13 front panel to 85 first. GET packets only.");

        try
        {
            var snapshot = await _bleLightService.ProbeVolume85Async();
            LogBleSnapshot(snapshot);
            Status = $"Volume probe complete. Possible 85 matches: {snapshot.MatchCount}.";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE volume probe was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE volume probe failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task ListenVolumeAsync()
    {
        Status = "Listening for K13 volume notifications over BLE...";
        AddLog("BLE passive volume capture requested. Turn the K13 volume knob now; no GET/SET packets will be sent.");

        try
        {
            var snapshot = await _bleLightService.ListenForVolumeNotificationsAsync(TimeSpan.FromSeconds(12));
            LogBleSnapshot(snapshot);
            Status = $"Passive volume capture complete. Notifications: {snapshot.MatchCount}.";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE passive volume capture was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE passive volume capture failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task SetLightsOnAsync()
    {
        await SetLightPowerAsync(on: true, label: "On");
    }

    private async Task SetLightsOffAsync()
    {
        await SetLightPowerAsync(on: false, label: "Off");
    }

    private async Task SetLightsAsync(byte color, byte mode, bool on, string label)
    {
        Status = $"Setting K13 lights to {label} over BLE...";
        AddLog($"BLE light write requested: {label}.");

        try
        {
            var snapshot = await _bleLightService.SetBothLightsAsync(color, mode, on);
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            Status = $"Lights set: {snapshot.Top}; {snapshot.Knob}";
            SaveSelectedSlotLightingProfile();
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE light write was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE light write failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task SetLightPowerAsync(bool on, string label)
    {
        Status = $"Turning K13 lights {label.ToLowerInvariant()} over BLE...";
        AddLog($"BLE light power requested: {label}.");

        try
        {
            var snapshot = await _bleLightService.SetBothLightPowerAsync(on);
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            Status = $"Lights {label.ToLowerInvariant()}: {snapshot.Top}; {snapshot.Knob}";
            SaveSelectedSlotLightingProfile();
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE light power change was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE light power change failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task ChangeVolumeAsync(int direction, string label)
    {
        Status = $"Turning K13 volume {label} over BLE...";
        AddLog($"BLE guarded volume change requested: {label}.");

        try
        {
            var snapshot = await _bleLightService.ChangeVolumeByOneAsync(direction);
            LogBleSnapshot(snapshot);
            DeviceVolumeDisplay = $"Vol {snapshot.After}/99";
            Status = snapshot.Changed
                ? $"Volume changed: {snapshot.Before}/99 -> {snapshot.After}/99"
                : $"Volume write unverified. Readback stayed {snapshot.After}/99.";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "BLE volume change was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            foreach (var line in ex.TransportLog)
            {
                AddLog($"  {line}");
            }

            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"BLE volume change failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private async Task SearchGitHubProfilesAsync()
    {
        var query = GitHubProfileSearchText.Trim();
        Status = string.IsNullOrWhiteSpace(query)
            ? "Loading online AutoEq profile index..."
            : $"Searching online AutoEq profiles for '{query}'...";
        GitHubProfileStatus = "Searching online AutoEq profiles...";
        AddLog(Status);

        try
        {
            var results = await _githubProfileService.SearchAsync(query);
            GitHubProfiles.Clear();
            foreach (var result in results)
            {
                GitHubProfiles.Add(result);
            }

            SelectedGitHubProfile = GitHubProfiles.FirstOrDefault();
            OnPropertyChanged(nameof(GitHubProfileCount));

            GitHubProfileStatus = $"{GitHubProfileCount} online profile(s) shown";
            Status = GitHubProfileCount == 0
                ? "No online AutoEq profiles matched the search."
                : $"Found {GitHubProfileCount} online AutoEq profile(s).";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException)
        {
            GitHubProfileStatus = $"Online search failed: {ex.Message}";
            Status = GitHubProfileStatus;
            AddLog(Status);
        }
    }

    private async Task ImportGitHubProfileAsync()
    {
        if (SelectedGitHubProfile is not AutoEqProfileIndexEntry profile)
        {
            Status = "Select an online headphone profile first.";
            AddLog(Status);
            return;
        }

        Status = $"Downloading online AutoEq profile: {profile.Name}...";
        GitHubProfileStatus = $"Downloading {profile.Name}...";
        AddLog($"Online profile download requested: {profile.Name} ({profile.SourceSummary}).");

        try
        {
            var preset = _githubPreviewProfile == profile && GitHubPreviewPreset is not null
                ? ClonePreset(GitHubPreviewPreset, GitHubPreviewPreset.Name)
                : await _githubProfileService.ImportParametricEqAsync(profile);
            AddPresetAndSelect(preset, "Online");
            var saved = TrySaveProfileLibraryQuietly(out var saveError);
            GitHubProfileStatus = saved
                ? $"Downloaded and saved {preset.Name}"
                : $"Downloaded {preset.Name}; save failed";
            Status = saved
                ? $"Downloaded and saved AutoEq profile: {profile.Name}."
                : $"Downloaded AutoEq profile: {profile.Name}, but local save failed: {saveError}";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or FileNotFoundException)
        {
            GitHubProfileStatus = $"Online download failed: {ex.Message}";
            Status = GitHubProfileStatus;
            AddLog(Status);
        }
    }

    private async Task LoadGitHubProfilePreviewAsync(AutoEqProfileIndexEntry? profile)
    {
        _githubPreviewCts?.Cancel();
        _githubPreviewCts?.Dispose();

        if (profile is null)
        {
            _githubPreviewCts = null;
            _githubPreviewProfile = null;
            GitHubPreviewPreset = null;
            return;
        }

        var cts = new CancellationTokenSource();
        _githubPreviewCts = cts;
        GitHubPreviewPreset = null;
        GitHubProfileStatus = $"Loading curve for {profile.Name}...";

        try
        {
            var preset = await _githubProfileService.ImportParametricEqAsync(profile, cts.Token);
            if (!ReferenceEquals(_githubPreviewCts, cts) || !EqualityComparer<AutoEqProfileIndexEntry>.Default.Equals(SelectedGitHubProfile, profile))
            {
                return;
            }

            _githubPreviewProfile = profile;
            GitHubPreviewPreset = preset;
            GitHubProfileStatus = $"Previewing {profile.Name}";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or FormatException or FileNotFoundException)
        {
            if (ReferenceEquals(_githubPreviewCts, cts))
            {
                _githubPreviewProfile = null;
                GitHubPreviewPreset = null;
                GitHubProfileStatus = $"Preview unavailable: {ex.Message}";
            }
        }
        finally
        {
            if (ReferenceEquals(_githubPreviewCts, cts))
            {
                cts.Dispose();
                _githubPreviewCts = null;
            }
        }
    }

    private void RefreshProfileLibrary()
    {
        FilteredPresets.Refresh();
        OnPropertyChanged(nameof(FilteredPresetCount));
        OnPropertyChanged(nameof(GitHubProfileCount));
        OnPropertyChanged(nameof(FavoritePresetCount));
        OnPropertyChanged(nameof(K13StageablePresetCount));
        OnPropertyChanged(nameof(ProfileLibrarySummaryText));
        OnPropertyChanged(nameof(ProfileLibraryHint));
    }

    private string BuildProfileLibraryHint()
    {
        if (FavoritesOnly)
        {
            return FavoritePresetCount == 0
                ? "Mark strong profiles with ★ Favorite to build a short audition list."
                : "Favorites view is active: good for quickly cycling the strongest offline candidates.";
        }

        if (string.Equals(SelectedProfileCategory, "K13 Ready", StringComparison.OrdinalIgnoreCase))
        {
            return FilteredPresetCount == 0
                ? "No currently stageable presets in this view. Use Prepare K13 Copy and Auto Headroom to create one."
                : "K13 Ready view shows offline presets that already match the 10-band shape and safe headroom checks.";
        }

        if (!string.IsNullOrWhiteSpace(ProfileSearchText))
        {
            return "Search checks name, category, source, and notes. Import AutoEQ/Peace text, then narrow by headphone or target.";
        }

        if (K13StageablePresetCount < Presets.Count)
        {
            return "Some presets need Prepare K13 Copy or Auto Headroom before future device staging.";
        }

        return "Library is ready for editing, A/B checks, and exports.";
    }

    private static bool IsK13StageablePreset(EqPreset preset)
    {
        var bands = preset.Bands;
        if (bands.Count != 10 || preset.PreampDb is < -24 or > 12)
        {
            return false;
        }

        foreach (var band in bands)
        {
            if (band.FrequencyHz is < 20 or > 20000 || band.GainDb is < -24 or > 12 || band.Q is < 0.10 or > 10.0)
            {
                return false;
            }
        }

        var maxBoost = bands.Where(band => band.Enabled).Select(band => band.GainDb).DefaultIfEmpty(0).Max();
        return preset.PreampDb + maxBoost <= 0;
    }

    private bool FilterPreset(object item)
    {
        if (item is not EqPreset preset)
        {
            return false;
        }

        if (FavoritesOnly && !preset.IsFavorite)
        {
            return false;
        }

        if (string.Equals(SelectedProfileCategory, "K13 Ready", StringComparison.OrdinalIgnoreCase)
            && !IsK13StageablePreset(preset))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(SelectedProfileCategory)
            && !string.Equals(SelectedProfileCategory, "All", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(SelectedProfileCategory, "Favorites", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(SelectedProfileCategory, "K13 Ready", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(preset.Category, SelectedProfileCategory, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(ProfileSearchText))
        {
            return true;
        }

        var query = ProfileSearchText.Trim();
        return preset.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
               || preset.Category.Contains(query, StringComparison.OrdinalIgnoreCase)
               || preset.SourceName.Contains(query, StringComparison.OrdinalIgnoreCase)
               || preset.Description.Contains(query, StringComparison.OrdinalIgnoreCase);
    }

    private IReadOnlyList<EqPreset> LoadStartupPresets()
    {
        try
        {
            var saved = _localLibraryService.Load();
            if (saved.Count > 0)
            {
                var cleaned = RemoveLegacyReadbackPresets(saved);
                if (cleaned.Count != saved.Count)
                {
                    _localLibraryService.Save(cleaned);
                }

                if (cleaned.Count > 0)
                {
                    _status = cleaned.Count == saved.Count
                        ? $"Loaded {cleaned.Count} saved profile(s)."
                        : $"Loaded {cleaned.Count} saved profile(s); removed {saved.Count - cleaned.Count} old readback preset(s).";
                    return cleaned;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or JsonException)
        {
            _status = $"Saved profile library could not be loaded: {ex.Message}";
        }

        return
        [
            EqPreset.AnandaMusic(),
            EqPreset.AnandaGamingAtmos(),
            EqPreset.AnandaHarmanStarter(),
            EqPreset.AnandaOratoryStarter(),
            EqPreset.AnandaBassFun()
        ];
    }

    private static IReadOnlyList<EqPreset> RemoveLegacyReadbackPresets(IReadOnlyList<EqPreset> presets)
        => presets
            .Where(preset => !preset.Name.StartsWith("K13 Readback -", StringComparison.Ordinal))
            .ToList();

    private void SaveProfileLibrary()
    {
        try
        {
            _localLibraryService.Save(Presets);
            Status = $"Saved {Presets.Count} profile(s) to WolfEQ.";
            AddLog($"Saved local profile library: {_localLibraryService.LibraryPath}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = $"Profile save failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private bool TrySaveProfileLibraryQuietly(out string? errorMessage)
    {
        try
        {
            _localLibraryService.Save(Presets);
            AddLog($"Saved local profile library: {_localLibraryService.LibraryPath}");
            errorMessage = null;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            errorMessage = ex.Message;
            AddLog($"Profile save failed: {ex.Message}");
            return false;
        }
    }

    private void AddPresetAndSelect(EqPreset preset, string selectedCategory)
    {
        Presets.Add(preset);
        ProfileSearchText = string.Empty;
        SelectedProfileCategory = "All";
        RefreshProfileLibrary();

        if (!FilteredPresets.Contains(preset))
        {
            SelectedProfileCategory = "All";
            RefreshProfileLibrary();
        }

        SelectedPreset = preset;
        FilteredPresets.MoveCurrentTo(preset);
    }

    private void ImportApoFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import Equalizer APO / Peace preset",
            Filter = "Equalizer APO text (*.txt;*.cfg)|*.txt;*.cfg|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            ImportApoText(File.ReadAllText(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException)
        {
            Status = $"APO import failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ImportApoFromClipboard()
    {
        if (!Clipboard.ContainsText())
        {
            Status = "Clipboard does not contain text to import.";
            AddLog(Status);
            return;
        }

        try
        {
            ImportApoText(Clipboard.GetText(), "Clipboard APO preset");
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException)
        {
            Status = $"APO clipboard import failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ImportApoText(string text, string name)
    {
        var preset = EqualizerApoPresetCodec.Parse(text, name);
        var imported = new EqPreset
        {
            Name = preset.Name,
            Category = "Imported",
            SourceName = "Equalizer APO / Peace",
            Description = "Imported into the WolfEQ library.",
            PreampDb = preset.PreampDb,
            Bands = preset.Bands
        };

        AddPresetAndSelect(imported, "Imported");
        Status = TrySaveProfileLibraryQuietly(out var saveError)
            ? $"Imported and saved {imported.Bands.Count} APO filter(s)."
            : $"Imported {imported.Bands.Count} APO filter(s), but local save failed: {saveError}";
        AddLog(Status);
    }

    private void ExportApoToFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Equalizer APO / Peace preset",
            Filter = "Equalizer APO text (*.txt)|*.txt|All files (*.*)|*.*",
            FileName = MakeSafeFileName(SelectedPreset.Name) + ".txt",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, EqualizerApoPresetCodec.Export(SelectedPreset));
            Status = "Exported APO text.";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = $"APO export failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ExportApoToClipboard()
    {
        Clipboard.SetText(EqualizerApoPresetCodec.Export(SelectedPreset));
        Status = "Copied APO/Peace preset text.";
        AddLog(Status);
    }

    private void ImportFiioXmlFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import FiiO Control XML profile",
            Filter = "FiiO DSP XML (*.xml)|*.xml|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var preset = FiioDspXmlPresetCodec.Import(File.ReadAllText(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName));
            AddPresetAndSelect(preset, "Imported");
            Status = TrySaveProfileLibraryQuietly(out var saveError)
                ? $"Imported and saved FiiO XML profile: {preset.Name}."
                : $"Imported FiiO XML profile: {preset.Name}, but local save failed: {saveError}";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or System.Xml.XmlException)
        {
            Status = $"FiiO XML import failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ExportFiioXmlToFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export FiiO Control XML profile",
            Filter = "FiiO DSP XML (*.xml)|*.xml|All files (*.*)|*.*",
            FileName = MakeSafeFileName(SelectedPreset.Name) + "-fiio.xml",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, FiioDspXmlPresetCodec.Export(SelectedPreset));
            Status = "Exported FiiO XML profile.";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = $"FiiO XML export failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ImportJsonFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import WolfEQ JSON preset",
            Filter = "WolfEQ preset JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var preset = WolfEqPresetJsonCodec.Import(File.ReadAllText(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName));
            AddPresetAndSelect(preset, string.IsNullOrWhiteSpace(preset.Category) ? "Imported" : preset.Category);
            Status = TrySaveProfileLibraryQuietly(out var saveError)
                ? $"Imported and saved WolfEQ profile: {preset.Name}."
                : $"Imported WolfEQ profile: {preset.Name}, but local save failed: {saveError}";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            Status = $"WolfEQ JSON import failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ExportJsonToFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export WolfEQ JSON preset",
            Filter = "WolfEQ preset JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = MakeSafeFileName(SelectedPreset.Name) + ".json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, WolfEqPresetJsonCodec.Export(SelectedPreset));
            Status = "Exported WolfEQ profile.";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = $"WolfEQ JSON export failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ImportLibraryJsonFromFile()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import WolfEQ preset library JSON",
            Filter = "WolfEQ library JSON (*.json)|*.json|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            var imported = WolfEqPresetJsonCodec.ImportLibrary(File.ReadAllText(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName));
            foreach (var preset in imported)
            {
                Presets.Add(preset);
            }

            SelectedProfileCategory = "Imported";
            RefreshProfileLibrary();
            SelectedPreset = imported[0];
            FilteredPresets.MoveCurrentTo(imported[0]);
            Status = TrySaveProfileLibraryQuietly(out var saveError)
                ? $"Imported and saved {imported.Count} WolfEQ library profile(s)."
                : $"Imported {imported.Count} WolfEQ library profile(s), but local save failed: {saveError}";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or FormatException or System.Text.Json.JsonException)
        {
            Status = $"WolfEQ library import failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void ExportLibraryJsonToFile()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export WolfEQ preset library JSON",
            Filter = "WolfEQ library JSON (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"wolfeq-library-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            OverwritePrompt = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            File.WriteAllText(dialog.FileName, WolfEqPresetJsonCodec.ExportLibrary(Presets));
            Status = $"Exported {Presets.Count} WolfEQ library profile(s).";
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = $"WolfEQ library export failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void CopyTuningReportToClipboard()
    {
        Clipboard.SetText(BuildTuningReport());
        Status = "Copied WolfEQ tuning report.";
        AddLog(Status);
    }

    private string BuildTuningReport()
    {
        var builder = new StringBuilder();
        builder.AppendLine($"WolfEQ tuning report - {SelectedPreset.Name}");
        builder.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        builder.AppendLine($"Category: {SelectedPreset.Category}");
        builder.AppendLine($"Source: {SelectedPreset.SourceName}");
        builder.AppendLine($"Description: {SelectedPreset.Description}");
        builder.AppendLine($"Preamp: {PreampDb:F1} dB");
        builder.AppendLine(PresetEditSummaryText);
        builder.AppendLine(BandInspectorText);
        builder.AppendLine(BandInspectorHint);
        builder.AppendLine(TonalSnapshotText);
        builder.AppendLine(TonalBalanceHint);
        builder.AppendLine(K13ReadinessText);
        builder.AppendLine(K13ReadinessHint);
        builder.AppendLine(TuningConfidenceText);
        builder.AppendLine(TuningConfidenceHint);

        if (ComparePreset is not null)
        {
            builder.AppendLine();
            builder.AppendLine($"A/B reference: {ComparePreset.Name}");
            builder.AppendLine(CompareDeltaText);
            builder.AppendLine(CompareDeltaHint);
        }

        builder.AppendLine();
        builder.AppendLine("Bands:");
        foreach (var band in Bands.OrderBy(band => band.Number))
        {
            builder.AppendLine($"{band.Number:00}. {(band.Enabled ? "ON " : "OFF")} {band.FilterType} Fc {band.FrequencyHz} Hz Gain {band.GainDb:F1} dB Q {band.Q:F2}");
        }

        builder.AppendLine();
        builder.AppendLine("Saved from WolfEQ profile editor.");
        return builder.ToString();
    }

    private void ToggleSelectedFavorite()
    {
        SelectedPreset.IsFavorite = !SelectedPreset.IsFavorite;
        Status = SelectedPreset.IsFavorite
            ? $"Favorited {SelectedPreset.Name}."
            : $"Removed favorite from {SelectedPreset.Name}.";
        AddLog(Status);
        RefreshProfileLibrary();
        OnPropertyChanged(nameof(SelectedPreset));
        QueueProfileAutosave();
    }

    private void ApplyAutoHeadroom()
    {
        PreampDb = RecommendedPreampDb;
        Status = $"Applied headroom: {PreampDb:F1} dB preamp.";
        AddLog(Status);
    }

    private void DuplicateSelectedPreset()
    {
        var copy = ClonePreset(SelectedPreset, $"Copy - {SelectedPreset.Name}");
        copy.IsFavorite = false;
        Presets.Add(copy);
        RefreshProfileLibrary();
        SelectedPreset = copy;
        Status = $"Duplicated profile: {copy.Name}.";
        AddLog(Status);
    }

    private void EnableAllBands()
    {
        foreach (var band in Bands)
        {
            band.Enabled = true;
        }

        Status = "Enabled all bands.";
        AddLog(Status);
        RefreshHeadroomProperties();
    }

    private void BypassAllBands()
    {
        foreach (var band in Bands)
        {
            band.Enabled = false;
        }

        Status = "Bypassed all bands.";
        AddLog(Status);
        RefreshHeadroomProperties();
    }

    private void ZeroAllGains()
    {
        foreach (var band in Bands)
        {
            band.GainDb = 0;
        }

        PreampDb = 0;
        Status = "Reset band gains and preamp.";
        AddLog(Status);
        RefreshHeadroomProperties();
    }

    public void MoveBand(EqBand sourceBand, EqBand targetBand, bool insertAfter)
    {
        var sourceIndex = Bands.IndexOf(sourceBand);
        var targetIndex = Bands.IndexOf(targetBand);

        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var sourceLabel = $"B{sourceBand.Number}";
        var targetLabel = $"B{targetBand.Number}";
        var insertIndex = targetIndex;

        Bands.RemoveAt(sourceIndex);
        if (sourceIndex < targetIndex)
        {
            insertIndex--;
        }

        if (insertAfter)
        {
            insertIndex++;
        }

        insertIndex = Math.Clamp(insertIndex, 0, Bands.Count);
        Bands.Insert(insertIndex, sourceBand);
        RenumberBandsByOrder();

        Status = $"Moved {sourceLabel} {(insertAfter ? "after" : "before")} {targetLabel}.";
        AddLog(Status);
        RefreshHeadroomProperties();
    }

    private void SortBandsByFrequency()
    {
        var sortedBands = Bands
            .OrderBy(band => band.FrequencyHz)
            .Select((band, index) => new EqBand
            {
                Number = index + 1,
                Enabled = band.Enabled,
                FilterType = band.FilterType,
                FrequencyHz = band.FrequencyHz,
                GainDb = band.GainDb,
                Q = band.Q
            })
            .ToList();

        Bands.CollectionChanged -= BandsOnCollectionChanged;
        foreach (var band in Bands)
        {
            band.PropertyChanged -= BandOnPropertyChanged;
        }

        Bands.Clear();
        foreach (var band in sortedBands)
        {
            Bands.Add(band);
        }

        Bands.CollectionChanged += BandsOnCollectionChanged;
        foreach (var band in Bands)
        {
            band.PropertyChanged += BandOnPropertyChanged;
        }

        Status = "Sorted bands by frequency.";
        AddLog(Status);
        RefreshHeadroomProperties();
    }

    private void RenumberBandsByOrder()
    {
        for (var index = 0; index < Bands.Count; index++)
        {
            Bands[index].Number = index + 1;
        }
    }

    private void CreateFlatPreset()
    {
        var preset = CreateFlatPresetModel();
        Presets.Add(preset);
        RefreshProfileLibrary();
        SelectedProfileCategory = "Reference";
        SelectedPreset = preset;
        Status = "Created flat 10-band profile.";
        AddLog(Status);
    }

    private void PrepareK13Preset()
    {
        var sourceBands = Bands
            .OrderBy(band => band.FrequencyHz)
            .Take(10)
            .Select((band, index) => new EqBand
            {
                Number = index + 1,
                Enabled = band.Enabled,
                FilterType = band.FilterType,
                FrequencyHz = band.FrequencyHz,
                GainDb = band.GainDb,
                Q = band.Q
            })
            .ToList();

        for (var i = sourceBands.Count; i < 10; i++)
        {
            sourceBands.Add(new EqBand
            {
                Number = i + 1,
                Enabled = false,
                FilterType = EqFilterType.Peak,
                FrequencyHz = 1000,
                GainDb = 0,
                Q = 1
            });
        }

        var maxBoost = sourceBands.Where(band => band.Enabled).Select(band => band.GainDb).DefaultIfEmpty(0).Max();
        var safePreamp = Math.Round(Math.Min(0, Math.Min(PreampDb, -maxBoost)), 1);
        var prepared = new EqPreset
        {
            Name = $"K13 Staged - {SelectedPreset.Name}",
            Category = "Reference",
            SourceName = "WolfEQ K13 staging",
            Description = "10-band K13-ready copy with sorted bands and safe headroom.",
            PreampDb = safePreamp,
            Bands = new ObservableCollection<EqBand>(sourceBands)
        };

        Presets.Add(prepared);
        RefreshProfileLibrary();
        SelectedProfileCategory = "Reference";
        SelectedPreset = prepared;
        Status = "Prepared a K13-ready 10-band copy.";
        AddLog(Status);
    }

    private void SmoothK13Preset()
    {
        var smoothedBands = Bands
            .OrderBy(band => band.FrequencyHz)
            .Take(10)
            .Select((band, index) => new EqBand
            {
                Number = index + 1,
                Enabled = band.Enabled,
                FilterType = band.FilterType is EqFilterType.LowPass or EqFilterType.HighPass or EqFilterType.AllPass
                    ? EqFilterType.Peak
                    : band.FilterType,
                FrequencyHz = band.FrequencyHz,
                GainDb = Math.Clamp(Math.Round(band.GainDb, 1), -8.0, 8.0),
                Q = Math.Clamp(Math.Round(band.Q, 2), 0.25, 4.0)
            })
            .ToList();

        for (var i = smoothedBands.Count; i < 10; i++)
        {
            smoothedBands.Add(new EqBand
            {
                Number = i + 1,
                Enabled = false,
                FilterType = EqFilterType.Peak,
                FrequencyHz = 1000,
                GainDb = 0,
                Q = 1
            });
        }

        var maxBoost = smoothedBands.Where(band => band.Enabled).Select(band => band.GainDb).DefaultIfEmpty(0).Max();
        var safePreamp = Math.Round(Math.Min(0, Math.Min(PreampDb, -maxBoost)), 1);
        var preset = new EqPreset
        {
            Name = $"Smooth K13 - {SelectedPreset.Name}",
            Category = "Reference",
            SourceName = "WolfEQ safety smoothing",
            Description = "Smoothed 10-band K13-ready copy with safer gains, Q, and headroom.",
            PreampDb = safePreamp,
            Bands = new ObservableCollection<EqBand>(smoothedBands)
        };

        Presets.Add(preset);
        RefreshProfileLibrary();
        SelectedProfileCategory = "Reference";
        SelectedPreset = preset;
        Status = "Created a smoothed K13-ready copy.";
        AddLog(Status);
    }

    private void AddWarmTiltPreset() => CreateToneShapePreset(
        "Warm Tilt",
        "Adds a gentle low-end lift and softens upper-treble bite for relaxed music listening.",
        band => band.FrequencyHz switch
        {
            <= 90 => 1.6,
            <= 180 => 1.0,
            >= 5500 and <= 9500 => -1.2,
            >= 10000 => -0.7,
            _ => 0
        });

    private void AddTrebleTamerPreset() => CreateToneShapePreset(
        "Treble Tamer",
        "Reduces presence/air hot spots for brighter recordings while preserving the existing bass contour.",
        band => band.FrequencyHz switch
        {
            >= 2500 and < 5000 => -0.7,
            >= 5000 and < 9000 => -1.8,
            >= 9000 => -1.0,
            _ => 0
        });

    private void AddGamingClarityPreset() => CreateToneShapePreset(
        "Gaming Clarity",
        "Keeps bass controlled, nudges footsteps/dialog presence, and trims sharp treble for Atmos gaming.",
        band => band.FrequencyHz switch
        {
            <= 90 => -0.8,
            >= 120 and <= 250 => -0.4,
            >= 1800 and <= 3500 => 0.9,
            >= 5000 and <= 8500 => -1.1,
            _ => 0
        });

    private void CreateToneShapePreset(string toneName, string description, Func<EqBand, double> gainDeltaForBand)
    {
        var shapedBands = SelectedPreset.Bands.Select(band => new EqBand
        {
            Number = band.Number,
            Enabled = true,
            FilterType = band.FilterType,
            FrequencyHz = band.FrequencyHz,
            GainDb = Math.Clamp(Math.Round(band.GainDb + gainDeltaForBand(band), 1), -12.0, 12.0),
            Q = band.Q
        }).ToList();

        var safePreamp = Math.Min(SelectedPreset.PreampDb, -shapedBands.Where(band => band.Enabled).Select(band => band.GainDb).DefaultIfEmpty(0).Max());
        var preset = new EqPreset
        {
            Name = $"{SelectedPreset.Name} + {toneName}",
            Category = "Listening",
            SourceName = "WolfEQ tone macro",
            Description = $"{description} Based on '{SelectedPreset.Name}'.",
            IsFavorite = false,
            PreampDb = Math.Round(Math.Min(0, safePreamp), 1),
            Bands = new ObservableCollection<EqBand>(shapedBands)
        };

        Presets.Add(preset);
        RefreshProfileLibrary();
        SelectedProfileCategory = "Listening";
        SelectedPreset = preset;
        Status = $"Created {toneName} tone-shape profile.";
        AddLog(Status);
    }

    private void CaptureComparePreset()
    {
        ComparePreset = ClonePreset(SelectedPreset, $"A/B copy - {SelectedPreset.Name}");
        CompareStatus = $"A/B captured: {SelectedPreset.Name}";
        Status = "Captured current profile to A/B slot.";
        AddLog(Status);
        CommandManager.InvalidateRequerySuggested();
    }

    private void SwapComparePreset()
    {
        if (ComparePreset is null)
        {
            return;
        }

        var current = ClonePreset(SelectedPreset, $"A/B copy - {SelectedPreset.Name}");
        var next = ClonePreset(ComparePreset, ComparePreset.Name.Replace("A/B copy - ", string.Empty));
        if (!Presets.Contains(next))
        {
            Presets.Add(next);
        }

        ComparePreset = current;
        SelectedPreset = next;
        RefreshProfileLibrary();
        CompareStatus = $"A/B swapped. Slot now holds {current.Name.Replace("A/B copy - ", string.Empty)}";
        Status = "Swapped A/B profile.";
        AddLog(Status);
        CommandManager.InvalidateRequerySuggested();
    }

    private void ClearComparePreset()
    {
        ComparePreset = null;
        CompareStatus = "A/B slot empty";
        Status = "Cleared A/B slot.";
        AddLog(Status);
        CommandManager.InvalidateRequerySuggested();
    }

    private async Task SaveAsync()
    {
        try
        {
            await _deviceService.SavePresetAsync(SelectedPreset, slot: 1);
            Status = $"Saved {SelectedPreset.Name} to USER 1";
        }
        catch (Exception ex)
        {
            Status = ex.Message;
        }
    }

    private void LoadSelectedPreset()
    {
        _isLoadingPreset = true;
        UnwatchBands(Bands);
        Bands = SelectedPreset.Bands;
        WatchBands(Bands);
        PreampDb = SelectedPreset.PreampDb;
        _isLoadingPreset = false;
        OnPropertyChanged(nameof(Bands));
        RefreshHeadroomProperties();
        Status = $"Loaded preset: {SelectedPreset.Name}";
    }

    private void WatchBands(ObservableCollection<EqBand> bands)
    {
        bands.CollectionChanged += BandsOnCollectionChanged;
        foreach (var band in bands)
        {
            band.PropertyChanged += BandOnPropertyChanged;
        }
    }

    private void UnwatchBands(ObservableCollection<EqBand> bands)
    {
        bands.CollectionChanged -= BandsOnCollectionChanged;
        foreach (var band in bands)
        {
            band.PropertyChanged -= BandOnPropertyChanged;
        }
    }

    private void BandsOnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (EqBand band in e.OldItems)
            {
                band.PropertyChanged -= BandOnPropertyChanged;
            }
        }

        if (e.NewItems != null)
        {
            foreach (EqBand band in e.NewItems)
            {
                band.PropertyChanged += BandOnPropertyChanged;
            }
        }

        RefreshHeadroomProperties();
        QueueProfileAutosave();
    }

    private void BandOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RefreshHeadroomProperties();
        QueueProfileAutosave();
        if (sender is EqBand band
            && e.PropertyName is nameof(EqBand.Enabled) or nameof(EqBand.FrequencyHz) or nameof(EqBand.GainDb) or nameof(EqBand.Q) or nameof(EqBand.FilterType))
        {
            QueueLiveDeviceBandSync(band.Number);
        }
    }

    private void QueueProfileAutosave()
    {
        if (_isLoadingPreset)
        {
            return;
        }

        _profileAutosaveTimer.Stop();
        _profileAutosaveTimer.Start();
    }

    private void ProfileAutosaveTimerOnTick(object? sender, EventArgs e)
    {
        _profileAutosaveTimer.Stop();
        if (TrySaveProfileLibraryQuietly(out var saveError))
        {
            AddLog($"Auto-saved profile: {SelectedPreset.Name}");
            return;
        }

        Status = $"Profile auto-save failed: {saveError}";
    }

    private void QueueLiveDevicePreampSync()
    {
        if (_isLoadingPreset || !LiveDeviceEqSyncEnabled)
        {
            return;
        }

        if (!IsDeviceConnected)
        {
            LiveDeviceEqSyncStatus = "Live sync on - connect K13 to save edits";
            return;
        }

        _pendingLivePreampSync = true;
        QueueLiveDeviceEqSync();
    }

    private void QueueLiveDeviceBandSync(int bandNumber)
    {
        if (_isLoadingPreset || !LiveDeviceEqSyncEnabled || bandNumber is < 1 or > 10)
        {
            return;
        }

        if (!IsDeviceConnected)
        {
            LiveDeviceEqSyncStatus = "Live sync on - connect K13 to save edits";
            return;
        }

        _pendingLiveBandNumbers.Add(bandNumber);
        QueueLiveDeviceEqSync();
    }

    private void QueueLiveDeviceEqSync()
    {
        if (_isSyncingLiveDeviceEq)
        {
            return;
        }

        LiveDeviceEqSyncStatus = "Sync queued";
        _liveDeviceEqSyncTimer.Stop();
        _liveDeviceEqSyncTimer.Start();
    }

    private async void LiveDeviceEqSyncTimerOnTick(object? sender, EventArgs e)
    {
        _liveDeviceEqSyncTimer.Stop();
        await FlushLiveDeviceEqSyncAsync();
    }

    private async Task FlushLiveDeviceEqSyncAsync()
    {
        if (!LiveDeviceEqSyncEnabled || _isSyncingLiveDeviceEq)
        {
            return;
        }

        var syncPreamp = _pendingLivePreampSync;
        var bandNumbers = _pendingLiveBandNumbers.Order().ToArray();
        _pendingLivePreampSync = false;
        _pendingLiveBandNumbers.Clear();

        if (!syncPreamp && bandNumbers.Length == 0)
        {
            return;
        }

        _isSyncingLiveDeviceEq = true;
        LiveDeviceEqSyncStatus = "Syncing to device...";

        try
        {
            if (syncPreamp)
            {
                var preampResult = await _deviceService.SetGlobalGainAsync(PreampDb);
                foreach (var line in preampResult.TransportLog)
                {
                    AddLog($"  {line}");
                }

                if (!preampResult.Confirmed)
                {
                    throw new InvalidOperationException($"Preamp readback stayed {preampResult.AfterGainDb:F1} dB.");
                }
            }

            var syncedBands = 0;
            foreach (var bandNumber in bandNumbers)
            {
                var band = Bands.FirstOrDefault(candidate => candidate.Number == bandNumber);
                if (band is null)
                {
                    continue;
                }

                var bandResult = await _deviceService.SetBandAsync(ToDeviceBand(band));
                foreach (var line in bandResult.TransportLog)
                {
                    AddLog($"  {line}");
                }

                if (!bandResult.Confirmed)
                {
                    throw new InvalidOperationException($"Band {bandNumber} readback did not match.");
                }

                syncedBands++;
            }

            LiveDeviceEqSyncStatus = BuildLiveDeviceEqSyncSuccessText(syncPreamp, syncedBands);
            Status = LiveDeviceEqSyncStatus;
            AddLog(Status);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or TimeoutException)
        {
            LiveDeviceEqSyncStatus = $"Sync failed: {ex.Message}";
            Status = LiveDeviceEqSyncStatus;
            AddLog(Status);
        }
        finally
        {
            _isSyncingLiveDeviceEq = false;
            if (LiveDeviceEqSyncEnabled && (_pendingLivePreampSync || _pendingLiveBandNumbers.Count > 0))
            {
                QueueLiveDeviceEqSync();
            }
        }
    }

    private static K13EqBandReadback ToDeviceBand(EqBand band)
        => new(
            band.Number,
            band.FrequencyHz,
            band.Enabled ? band.GainDb : 0,
            band.Q,
            (byte)band.FilterType);

    private static string BuildLiveDeviceEqSyncSuccessText(bool syncedPreamp, int syncedBands)
        => (syncedPreamp, syncedBands) switch
        {
            (true, > 0) => $"Saved preamp and {syncedBands} band(s) to device",
            (true, 0) => "Saved preamp to device",
            (false, > 0) => $"Saved {syncedBands} band(s) to device",
            _ => "Live sync ready"
        };

    private void RefreshHeadroomProperties()
    {
        OnPropertyChanged(nameof(EnabledBandCount));
        OnPropertyChanged(nameof(MaxEnabledBoostDb));
        OnPropertyChanged(nameof(RecommendedPreampDb));
        OnPropertyChanged(nameof(PresetEditSummaryText));
        OnPropertyChanged(nameof(BandInspectorText));
        OnPropertyChanged(nameof(BandInspectorHint));
        OnPropertyChanged(nameof(ClippingHeadroomText));
        OnPropertyChanged(nameof(K13StageablePresetCount));
        OnPropertyChanged(nameof(ProfileLibrarySummaryText));
        OnPropertyChanged(nameof(ProfileLibraryHint));
        OnPropertyChanged(nameof(BassAverageDb));
        OnPropertyChanged(nameof(MidAverageDb));
        OnPropertyChanged(nameof(PresenceAverageDb));
        OnPropertyChanged(nameof(AirAverageDb));
        OnPropertyChanged(nameof(TonalSnapshotText));
        OnPropertyChanged(nameof(TonalBalanceHint));
        OnPropertyChanged(nameof(K13ReadinessText));
        OnPropertyChanged(nameof(K13ReadinessHint));
        OnPropertyChanged(nameof(TuningConfidenceScore));
        OnPropertyChanged(nameof(TuningConfidenceText));
        OnPropertyChanged(nameof(TuningConfidenceHint));
        OnPropertyChanged(nameof(CompareDeltaText));
        OnPropertyChanged(nameof(CompareDeltaHint));
    }

    private int BuildTuningConfidenceScore()
    {
        var score = 100;
        score -= GetK13ReadinessIssues().Count() * 14;

        var clippingOver = PreampDb + MaxEnabledBoostDb;
        if (clippingOver > 0)
        {
            score -= (int)Math.Ceiling(clippingOver * 10);
        }

        var enabledBands = Bands.Where(band => band.Enabled).ToList();
        score -= enabledBands.Count(band => Math.Abs(band.GainDb) >= 8) * 5;
        score -= enabledBands.Count(band => band.Q >= 6) * 4;
        score -= enabledBands.Count(band => band.FilterType is EqFilterType.LowPass or EqFilterType.HighPass or EqFilterType.AllPass) * 8;

        return Math.Clamp(score, 0, 100);
    }

    private string BuildTuningConfidenceHint()
    {
        var score = TuningConfidenceScore;
        if (score >= 90)
        {
            return "Clean profile: safe headroom, 10-band device shape, and no extreme bands.";
        }

        if (score >= 70)
        {
            return "Good draft. Review any high-Q or large-gain moves before long listening sessions.";
        }

        if (score >= 45)
        {
            return "Needs a careful pass: use Auto Headroom, A/B, and the graph before saving.";
        }

        return "Aggressive curve: clean up readiness issues and sharp filters before long listening sessions.";
    }

    private string BuildBandInspectorText()
    {
        var enabled = Bands.Where(band => band.Enabled).ToList();
        if (enabled.Count == 0)
        {
            return "No enabled bands · curve is bypassed.";
        }

        var strongestBoost = enabled.OrderByDescending(band => band.GainDb).First();
        var strongestCut = enabled.OrderBy(band => band.GainDb).First();
        var narrowest = enabled.OrderByDescending(band => band.Q).First();
        return $"Boost B{strongestBoost.Number} {strongestBoost.GainDb:+0.0;-0.0;0.0} dB @ {strongestBoost.FrequencyHz} Hz · cut B{strongestCut.Number} {strongestCut.GainDb:+0.0;-0.0;0.0} dB @ {strongestCut.FrequencyHz} Hz · narrowest Q B{narrowest.Number} {narrowest.Q:F2}";
    }

    private string BuildBandInspectorHint()
    {
        var warnings = new List<string>();
        foreach (var band in Bands.Where(band => band.Enabled))
        {
            if (Math.Abs(band.GainDb) >= 8)
            {
                warnings.Add($"B{band.Number} large {band.GainDb:+0.0;-0.0;0.0} dB move");
            }

            if (band.Q >= 6)
            {
                warnings.Add($"B{band.Number} very narrow Q {band.Q:F1}");
            }

            if (band.FilterType is EqFilterType.LowPass or EqFilterType.HighPass or EqFilterType.AllPass)
            {
                warnings.Add($"B{band.Number} uses aggressive {band.FilterType}");
            }
        }

        if (warnings.Count == 0)
        {
            return "Band moves look controlled. Use Smooth Copy if an imported profile feels too sharp.";
        }

        return string.Join(" · ", warnings.Take(3));
    }

    private string BuildCompareDeltaText()
    {
        if (ComparePreset is null)
        {
            return "Capture an A/B preset to show live delta stats.";
        }

        var deltas = GetCompareGainDeltas().ToList();
        if (deltas.Count == 0)
        {
            return $"A/B active · preamp delta {PreampDb - ComparePreset.PreampDb:+0.0;-0.0;0.0} dB";
        }

        var average = deltas.Average();
        var max = deltas.OrderByDescending(delta => Math.Abs(delta)).First();
        return $"A/B delta · avg {average:+0.0;-0.0;0.0} dB · max {max:+0.0;-0.0;0.0} dB · preamp {PreampDb - ComparePreset.PreampDb:+0.0;-0.0;0.0} dB";
    }

    private string BuildCompareDeltaHint()
    {
        if (ComparePreset is null)
        {
            return "Capture A/B, then tweak sliders or apply a quick tuning preset to compare curves.";
        }

        var maxAbs = GetCompareGainDeltas().Select(Math.Abs).DefaultIfEmpty(0).Max();
        if (maxAbs < 0.5 && Math.Abs(PreampDb - ComparePreset.PreampDb) < 0.5)
        {
            return "Very small change from the captured reference.";
        }

        if (maxAbs <= 2.0)
        {
            return "Moderate tuning move: good for controlled A/B listening.";
        }

        return "Large EQ move: check headroom and listen for tonal side-effects before saving/exporting.";
    }

    private IEnumerable<double> GetCompareGainDeltas()
    {
        if (ComparePreset is null)
        {
            yield break;
        }

        var compareByNumber = ComparePreset.Bands.ToDictionary(band => band.Number);
        foreach (var band in Bands)
        {
            if (compareByNumber.TryGetValue(band.Number, out var compareBand))
            {
                yield return band.GainDb - compareBand.GainDb;
            }
        }
    }

    private string BuildK13ReadinessText()
    {
        var issues = GetK13ReadinessIssues().ToList();
        return issues.Count == 0
            ? $"Device-ready · {Bands.Count} bands · {EnabledBandCount} on · headroom {PreampDb:F1} dB"
            : $"Needs cleanup · {issues.Count} change(s)";
    }

    private string BuildK13ReadinessHint()
    {
        var issues = GetK13ReadinessIssues().ToList();
        if (issues.Count == 0)
        {
            return "Fits the current 10-band device profile.";
        }

        return string.Join(" · ", issues.Take(3));
    }

    private IEnumerable<string> GetK13ReadinessIssues()
    {
        if (Bands.Count != 10)
        {
            yield return $"Device profiles use 10 bands; current profile has {Bands.Count}.";
        }

        if (PreampDb is < -24 or > 12)
        {
            yield return "Preamp is outside the editor range (-24 to +12 dB).";
        }

        foreach (var band in Bands)
        {
            if (band.FrequencyHz is < 20 or > 20000)
            {
                yield return $"Band {band.Number} frequency is outside 20 Hz - 20 kHz.";
            }

            if (band.GainDb is < -24 or > 12)
            {
                yield return $"Band {band.Number} gain is outside -24 to +12 dB.";
            }

            if (band.Q is < 0.10 or > 10.0)
            {
                yield return $"Band {band.Number} Q is outside 0.10 - 10.0.";
            }
        }

        if (PreampDb + MaxEnabledBoostDb > 0)
        {
            yield return "Apply Auto Headroom before any future hardware staging to avoid clipping.";
        }
    }

    private double AverageGainForRange(int minHz, int maxHz)
    {
        var gains = Bands
            .Where(band => band.Enabled && band.FrequencyHz >= minHz && band.FrequencyHz < maxHz)
            .Select(band => band.GainDb)
            .ToList();

        return gains.Count == 0 ? 0 : Math.Round(gains.Average(), 1);
    }

    private string BuildTonalBalanceHint()
    {
        var bassLift = BassAverageDb - MidAverageDb;
        var trebleDelta = Math.Max(PresenceAverageDb, AirAverageDb) - MidAverageDb;

        if (bassLift >= 2.5 && trebleDelta <= -0.5)
        {
            return "Warm / relaxed tilt: likely good for music fatigue control.";
        }

        if (trebleDelta >= 2.0 && bassLift <= 0.5)
        {
            return "Bright / detail-forward tilt: watch for fatigue on Ananda-style planars.";
        }

        if (bassLift >= 2.0 && trebleDelta >= 1.0)
        {
            return "V-shaped tilt: fun profile, but keep headroom conservative.";
        }

        if (Math.Abs(bassLift) <= 1.0 && Math.Abs(trebleDelta) <= 1.0)
        {
            return "Balanced tilt: good reference or profile-staging baseline.";
        }

        return "Custom tilt: use A/B and the response graph before exporting or copying to device later.";
    }

    private void LoadSlotLightingProfiles()
    {
        try
        {
            _slotLightingProfiles = _slotLightingProfileService.Load().ToDictionary(
                pair => pair.Key,
                pair => pair.Value);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            _slotLightingProfiles = [];
            _status = $"Slot lighting cues could not be loaded: {ex.Message}";
        }
    }

    private void LoadSelectedSlotLightingProfileIntoUi()
    {
        if (SelectedDeviceUserPreset is null)
        {
            return;
        }

        _isLoadingSlotLightingProfile = true;
        try
        {
            var profile = GetSlotLightingProfile(SelectedDeviceUserPreset.Slot);
            TopLedOn = profile.TopOn;
            KnobLedOn = profile.KnobOn;
            SelectedTopLedColorOption = SelectLedColorOption(profile.TopColor);
            SelectedKnobLedColorOption = SelectLedColorOption(profile.KnobColor);
            SelectedTopLedModeOption = SelectLedModeOption(profile.TopMode);
            SelectedKnobLedModeOption = SelectLedModeOption(profile.KnobMode);
        }
        finally
        {
            _isLoadingSlotLightingProfile = false;
        }
    }

    private async Task ApplySelectedSlotLightingAsync()
    {
        var profile = GetSlotLightingProfile(SelectedDeviceUserPreset.Slot);
        LoadSelectedSlotLightingProfileIntoUi();
        Status = $"Applying {SelectedDeviceUserPreset.DisplayName} lighting cue...";
        AddLog($"BLE slot lighting cue requested: {SelectedDeviceUserPreset.DisplayName}.");

        try
        {
            var snapshot = await _bleLightService.SetSplitLightsAsync(
                profile.TopColor,
                profile.TopMode,
                profile.TopOn,
                profile.KnobColor,
                profile.KnobMode,
                profile.KnobOn);
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            Status = $"{SelectedDeviceUserPreset.DisplayName} lighting cue applied.";
            AddLog(Status);
        }
        catch (OperationCanceledException)
        {
            Status = "Slot lighting cue was canceled.";
            AddLog(Status);
        }
        catch (K13BleOperationException ex)
        {
            LogBleException(ex);
            Status = ex.InnerException is null
                ? ex.Message
                : $"{ex.Message} {ex.InnerException.Message}";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            Status = $"Slot lighting cue failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private void SaveSelectedSlotLightingProfile()
    {
        if (_isLoadingSlotLightingProfile)
        {
            return;
        }

        _slotLightingProfiles[SelectedDeviceUserPreset.Slot] = new SlotLightingProfileData
        {
            Slot = SelectedDeviceUserPreset.Slot,
            TopOn = TopLedOn,
            TopColor = SelectedTopLedColorOption.Value,
            TopMode = SelectedTopLedModeOption.Value,
            KnobOn = KnobLedOn,
            KnobColor = SelectedKnobLedColorOption.Value,
            KnobMode = SelectedKnobLedModeOption.Value
        };

        try
        {
            _slotLightingProfileService.Save(_slotLightingProfiles.Values);
            OnPropertyChanged(nameof(SlotLightingSummary));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status = $"Slot lighting save failed: {ex.Message}";
            AddLog(Status);
        }
    }

    private SlotLightingProfileData GetSlotLightingProfile(int slot)
    {
        if (_slotLightingProfiles.TryGetValue(slot, out var profile))
        {
            return profile;
        }

        profile = CreateDefaultSlotLightingProfile(slot);
        _slotLightingProfiles[slot] = profile;
        return profile;
    }

    private static SlotLightingProfileData CreateDefaultSlotLightingProfile(int slot)
    {
        var color = (byte)(slot switch
        {
            1 => 0x02,
            2 => 0x01,
            3 => 0x07,
            4 => 0x04,
            5 => 0x05,
            6 => 0x03,
            7 => 0x06,
            8 => 0x02,
            9 => 0x01,
            10 => 0x08,
            _ => 0x07
        });

        var knobColor = (byte)(slot switch
        {
            8 => 0x05,
            9 => 0x06,
            10 => 0x07,
            _ => color
        });

        return new SlotLightingProfileData
        {
            Slot = slot,
            TopOn = true,
            TopColor = color,
            TopMode = 0x00,
            KnobOn = true,
            KnobColor = knobColor,
            KnobMode = 0x00
        };
    }

    private static string BuildLedSummary(string zoneName, bool on, LedModeOption mode, LedColorOption color)
        => on ? $"{zoneName}: {color.Name} / {mode.Name}" : $"{zoneName}: Off";

    private void AddLog(string message)
    {
        DeviceLog.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
        _log.WriteLine(message);
    }

    private void LogBleSnapshot(K13BleLightSnapshot snapshot)
    {
        foreach (var line in snapshot.TransportLog)
        {
            AddLog($"  {line}");
        }
    }

    private void LogBleException(K13BleOperationException ex)
    {
        foreach (var line in ex.TransportLog)
        {
            AddLog($"  {line}");
        }
    }

    private void ApplyLightSnapshot(K13BleLightSnapshot snapshot)
    {
        TopLedOn = snapshot.Top.On;
        KnobLedOn = snapshot.Knob.On;
        SelectedTopLedModeOption = SelectLedModeOption(snapshot.Top.Mode);
        SelectedKnobLedModeOption = SelectLedModeOption(snapshot.Knob.Mode);
        SelectedTopLedColorOption = SelectLedColorOption(snapshot.Top.Color);
        SelectedKnobLedColorOption = SelectLedColorOption(snapshot.Knob.Color);
    }

    private LedColorOption SelectLedColorOption(byte value)
        => LedColorOptions.FirstOrDefault(option => option.Value == value) ?? LedColorOptions[0];

    private LedModeOption SelectLedModeOption(byte value)
        => LedModeOptions.FirstOrDefault(option => option.Value == value) ?? LedModeOptions[0];

    private void LogBleSnapshot(K13BleVolumeSnapshot snapshot)
    {
        foreach (var line in snapshot.TransportLog)
        {
            AddLog($"  {line}");
        }
    }

    private void LogBleSnapshot(K13BleInputSourceSnapshot snapshot)
    {
        foreach (var line in snapshot.TransportLog)
        {
            AddLog($"  {line}");
        }
    }

    private void LogBleSnapshot(K13BleProbeSnapshot snapshot)
    {
        foreach (var line in snapshot.TransportLog)
        {
            AddLog($"  {line}");
        }
    }

    private EqPreset UpsertReadbackPreset(K13EqReadback snapshot)
    {
        var stableName = BuildReadbackPresetName(snapshot);
        var legacyPrefix = $"K13 Readback - {snapshot.PresetDisplayName}";
        var matches = Presets
            .Where(preset => preset.Name == stableName ||
                             preset.Name.StartsWith(legacyPrefix, StringComparison.Ordinal))
            .ToList();
        var target = matches.FirstOrDefault(preset => preset.Name == stableName);

        if (target is null)
        {
            target = CreateReadbackPreset(snapshot);
            Presets.Add(target);
        }
        else
        {
            RefreshReadbackPreset(target, snapshot);
        }

        foreach (var duplicate in matches.Where(preset => !ReferenceEquals(preset, target)).ToList())
        {
            Presets.Remove(duplicate);
        }

        return target;
    }

    private static void RefreshReadbackPreset(EqPreset preset, K13EqReadback snapshot)
    {
        preset.PreampDb = snapshot.GlobalGainDb;
        preset.Bands.Clear();
        foreach (var band in CreateReadbackBands(snapshot))
        {
            preset.Bands.Add(band);
        }
    }

    private static EqPreset CreateReadbackPreset(K13EqReadback snapshot)
        => new()
        {
            Name = BuildReadbackPresetName(snapshot),
            Category = "Device",
            SourceName = "K13",
            Description = snapshot.EqEnabled
                ? "Current EQ read from the active K13 USER slot."
                : "Current K13 USER slot readback. Device EQ bypass appears off, but band values are preserved here.",
            PreampDb = snapshot.GlobalGainDb,
            Bands = new ObservableCollection<EqBand>(CreateReadbackBands(snapshot))
        };

    private static string BuildReadbackPresetName(K13EqReadback snapshot)
        => $"Device EQ - {snapshot.PresetDisplayName}";

    private static IEnumerable<EqBand> CreateReadbackBands(K13EqReadback snapshot)
    {
        return snapshot.Bands.Select(band => new EqBand
            {
                Number = band.Number,
                FilterType = ToEqFilterType(band.FilterType),
                FrequencyHz = band.FrequencyHz,
                GainDb = band.GainDb,
                Q = band.Q,
                Enabled = true
            });
    }

    private static EqPreset CreateFlatPresetModel() => new()
    {
        Name = $"Flat Reference - {DateTime.Now:HHmmss}",
        Category = "Reference",
        SourceName = "WolfEQ",
        Description = "Flat 10-band reference profile for manual tuning.",
        PreampDb = 0,
        Bands = new ObservableCollection<EqBand>(new[] { 31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000 }
            .Select((frequency, index) => new EqBand
            {
                Number = index + 1,
                Enabled = true,
                FilterType = EqFilterType.Peak,
                FrequencyHz = frequency,
                GainDb = 0,
                Q = 1
            }))
    };

    private static EqPreset ClonePreset(EqPreset preset, string? name = null) => new()
    {
        Name = string.IsNullOrWhiteSpace(name) ? preset.Name : name,
        Category = preset.Category,
        SourceName = preset.SourceName,
        Description = preset.Description,
        IsFavorite = preset.IsFavorite,
        PreampDb = preset.PreampDb,
        Bands = new ObservableCollection<EqBand>(preset.Bands.Select(band => new EqBand
        {
            Number = band.Number,
            Enabled = band.Enabled,
            FilterType = band.FilterType,
            FrequencyHz = band.FrequencyHz,
            GainDb = band.GainDb,
            Q = band.Q
        }))
    };

    private static EqFilterType ToEqFilterType(byte filterType)
        => Enum.IsDefined(typeof(EqFilterType), (int)filterType)
            ? (EqFilterType)filterType
            : EqFilterType.Peak;

    private static string FormatCandidate(HidDeviceCandidate candidate)
    {
        var expectedMarker = candidate.IsExpectedInterface ? "target interface" : "candidate";
        var version = candidate.VersionNumber is ushort versionNumber ? $" v0x{versionNumber:X4}" : string.Empty;
        var serial = string.IsNullOrWhiteSpace(candidate.SerialNumber) ? string.Empty : $" serial {candidate.SerialNumber}";

        return $"{expectedMarker}: {candidate.VendorProductDisplay} {candidate.InterfaceDisplay}{version} " +
               $"{candidate.UsageDisplay}, {candidate.ReportLengthDisplay}, {candidate.DisplayName}{serial}";
    }

    private void SelectDeviceUserPresetOption(byte presetId)
    {
        var match = DeviceUserPresets.FirstOrDefault(option => option.PresetId == presetId);
        if (match is not null)
        {
            SelectedDeviceUserPreset = match;
        }
    }

    private void SelectInputSourceOption(byte source)
    {
        var match = DeviceInputSources.FirstOrDefault(option => option.Value == source);
        if (match is not null)
        {
            SelectedDeviceInputSource = match;
        }
    }

    private static string SanitizeProfileName(string value)
        => new(value
            .Where(character => character is >= ' ' and <= '~')
            .Take(8)
            .ToArray());

    private static string MakeSafeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string(value.Select(character => invalid.Contains(character) ? '-' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(safe) ? "WolfEQ-preset" : safe;
    }

    private static void ApplyAccentColor(string hex)
    {
        if (ColorConverter.ConvertFromString(hex) is not Color color)
        {
            return;
        }

        var dim = Darken(color, 0.35);
        var hover = Color.FromArgb(0x1A, color.R, color.G, color.B);
        var resources = Application.Current.Resources;

        resources["WolfGreen"] = color;
        resources["WolfCyan"] = color;
        resources["WolfCyanDim"] = dim;
        resources["WolfAccentHover"] = hover;

        SetBrush(resources, "WolfGreenBrush", color);
        SetBrush(resources, "WolfCyanBrush", color);
        SetBrush(resources, "WolfCyanDimBrush", dim);
        SetBrush(resources, "WolfAccentHoverBrush", hover);
    }

    private void SetConnectionState(bool connected)
    {
        IsDeviceConnected = connected;
        ConnectionText = connected ? "Connected" : "Disconnected";
        LiveDeviceEqSyncStatus = connected
            ? "Live sync on - edits save to the active K13 profile"
            : "Live sync on - connect K13 to save edits";
    }

    private static void SetBrush(ResourceDictionary resources, string key, Color color)
    {
        if (resources[key] is SolidColorBrush brush && !brush.IsFrozen)
        {
            brush.Color = color;
            return;
        }

        resources[key] = new SolidColorBrush(color);
    }

    private static Color Darken(Color color, double amount)
        => Color.FromRgb(
            (byte)Math.Round(color.R * (1 - amount)),
            (byte)Math.Round(color.G * (1 - amount)),
            (byte)Math.Round(color.B * (1 - amount)));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed record DeviceUserPresetOption(int Slot, byte PresetId)
{
    public string DisplayName => $"USER {Slot}";
}

public sealed record DeviceInputSourceOption(string DisplayName, byte Value);

public sealed record K13FeatureRow(string Name, string Detail, string State);

public sealed record AccentColorOption(string Name, string Hex)
{
    public Brush Brush
    {
        get
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex));
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }
}

public sealed record LedColorOption(string Name, byte Value, string Hex)
{
    public Brush Brush
    {
        get
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(Hex));
            }
            catch
            {
                return Brushes.Transparent;
            }
        }
    }
}

public sealed record LedModeOption(string Name, byte Value);

public sealed record LedSceneOption(string Name, byte TopColor, byte KnobColor, byte Mode);
