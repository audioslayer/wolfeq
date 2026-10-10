using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using WolfEQ.Models;

namespace WolfEQ.ViewModels;

/// <summary>
/// State for the split library drawer: which tab is showing (My Library, Favorites, Discover
/// online), the online source filter, and the preset the right-hand detail pane previews.
/// The underlying library and online search logic is unchanged.
/// </summary>
public sealed partial class MainViewModel
{
    public const string LibraryTabMine = "Mine";
    public const string LibraryTabFavorites = "Favorites";
    public const string LibraryTabOnline = "Online";

    private string _libraryTab = LibraryTabMine;
    private string _onlineSourceFilter = "All";
    private ListCollectionView? _filteredGitHubProfiles;

    private static readonly HashSet<string> LibraryDetailSources = new(StringComparer.Ordinal)
    {
        nameof(LibrarySelection), nameof(GitHubPreviewPreset), nameof(SelectedGitHubProfile), nameof(LibraryTab)
    };

    public string LibraryTab
    {
        get => _libraryTab;
        set
        {
            var tab = value is LibraryTabFavorites or LibraryTabOnline ? value : LibraryTabMine;
            if (!SetField(ref _libraryTab, tab))
            {
                return;
            }

            if (tab != LibraryTabOnline)
            {
                SelectedProfileCategory = tab == LibraryTabFavorites ? "Favorites" : "All";
            }

            OnPropertyChanged(nameof(IsOnlineLibraryTab));
            OnPropertyChanged(nameof(IsLocalLibraryTab));
        }
    }

    public bool IsOnlineLibraryTab => LibraryTab == LibraryTabOnline;

    /// <summary>Category chips for My Library; Favorites has its own tab.</summary>
    public IReadOnlyList<string> LibraryCategoryChips => ProfileCategories.Where(c => c != "Favorites").ToArray();
    public bool IsLocalLibraryTab => !IsOnlineLibraryTab;

    /// <summary>"All", "AutoEQ" or "OPRA"; filters the online results list.</summary>
    public string OnlineSourceFilter
    {
        get => _onlineSourceFilter;
        set
        {
            if (SetField(ref _onlineSourceFilter, string.IsNullOrWhiteSpace(value) ? "All" : value))
            {
                FilteredGitHubProfiles.Refresh();
            }
        }
    }

    public ICollectionView FilteredGitHubProfiles
    {
        get
        {
            if (_filteredGitHubProfiles is null)
            {
                _filteredGitHubProfiles = new ListCollectionView(GitHubProfiles) { Filter = FilterGitHubProfile };
                GitHubProfiles.CollectionChanged += (_, _) => OnPropertyChanged(nameof(FilteredGitHubProfileCount));
            }

            return _filteredGitHubProfiles;
        }
    }

    private bool FilterGitHubProfile(object item)
        => item is AutoEqProfileIndexEntry entry
           && (OnlineSourceFilter == "All" || string.Equals(entry.Provider, OnlineSourceFilter, StringComparison.OrdinalIgnoreCase));

    /// <summary>The preset the detail pane shows: the online preview or the selected library preset.</summary>
    public EqPreset? LibraryDetailPreset => IsOnlineLibraryTab ? GitHubPreviewPreset : LibrarySelection;

    public string LibraryDetailTitle => IsOnlineLibraryTab
        ? SelectedGitHubProfile?.Name ?? "Search for your headphones"
        : LibrarySelection?.Name ?? "Select a preset";

    public string LibraryDetailSubtitle
    {
        get
        {
            if (IsOnlineLibraryTab)
            {
                return SelectedGitHubProfile is { } entry
                    ? $"{entry.SourceSummary} · {entry.Provider}"
                    : "Results come from AutoEq and OPRA. Pick one to preview it here.";
            }

            if (LibrarySelection is not { } preset)
            {
                return "Pick a preset on the left to preview it.";
            }

            return preset.IsFavorite ? $"{preset.Category} · Favorite" : preset.Category;
        }
    }

    public bool HasLibraryDetail => LibraryDetailPreset is not null;

    private System.Windows.Input.ICommand? _toggleLibraryFavoriteCommand;

    /// <summary>Stars or un-stars a library preset (the row's preset, or the selection when no parameter).</summary>
    public System.Windows.Input.ICommand ToggleLibraryFavoriteCommand
        => _toggleLibraryFavoriteCommand ??= new AsyncParameterRelayCommand(parameter =>
        {
            ToggleLibraryFavorite(parameter as EqPreset ?? LibrarySelection);
            return Task.CompletedTask;
        });

    /// <summary>True when the saved preset in the detail pane is a favorite (drives the header star).</summary>
    public bool LibraryDetailIsFavorite => IsLocalLibraryTab && LibrarySelection?.IsFavorite == true;

    internal void ToggleLibraryFavorite(EqPreset? preset)
    {
        if (preset is null || !Presets.Contains(preset))
        {
            return;
        }

        preset.IsFavorite = !preset.IsFavorite;
        Status = preset.IsFavorite ? $"Added {preset.Name} to Favorites." : $"Removed {preset.Name} from Favorites.";
        AddLog(Status);

        NotifyLibraryPresetChanged(preset);
    }

    /// <summary>
    /// After a saved preset changes in place (favorite, rename): rebuild the rows (EqPreset has no
    /// change notifications), keep the selection, refresh the preview, and write the library to disk.
    /// </summary>
    private void NotifyLibraryPresetChanged(EqPreset preset)
    {
        var selection = LibrarySelection;
        RefreshProfileLibrary();
        if (selection is not null && FilteredPresets.Cast<EqPreset>().Contains(selection))
        {
            LibrarySelection = selection;
        }

        if (ReferenceEquals(preset, SelectedPreset))
        {
            OnPropertyChanged(nameof(SelectedPreset));
        }

        OnPropertyChanged(nameof(Presets));
        RaiseLibraryDetailChanged();
        if (!TrySaveProfileLibraryQuietly(out var saveError))
        {
            Status = $"Changed {preset.Name}, but the library could not be saved: {saveError}";
            AddLog(Status);
        }
    }

    public string LibraryDetailFilterCount => LibraryDetailPreset is { } p
        ? p.Bands.Count(band => band.Enabled).ToString(CultureInfo.CurrentCulture)
        : "—";

    public string LibraryDetailFitText => LibraryDetailPreset is not { } preset
        ? "—"
        : preset.Bands.Count <= SelectedDeviceProfile.BandCount ? "Yes" : "Needs trim";

    public bool LibraryDetailFits => LibraryDetailPreset is { } preset && preset.Bands.Count <= SelectedDeviceProfile.BandCount;

    private void InitializeLibraryBrowser()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not null && LibraryDetailSources.Contains(e.PropertyName))
            {
                RaiseLibraryDetailChanged();
            }
        };
    }

    public int FilteredGitHubProfileCount => FilteredGitHubProfiles.Cast<object>().Count();

    private void RaiseLibraryDetailChanged()
    {
        OnPropertyChanged(nameof(LibraryDetailPreset));
        OnPropertyChanged(nameof(LibraryDetailTitle));
        OnPropertyChanged(nameof(LibraryDetailSubtitle));
        OnPropertyChanged(nameof(HasLibraryDetail));
        OnPropertyChanged(nameof(LibraryDetailIsFavorite));
        OnPropertyChanged(nameof(LibraryDetailFilterCount));
        OnPropertyChanged(nameof(LibraryDetailFitText));
        OnPropertyChanged(nameof(LibraryDetailFits));
    }
}
