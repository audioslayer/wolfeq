using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WolfEQ.ViewModels;

/// <summary>One row in the Software EQ endpoint picker.</summary>
public sealed class SystemEqEndpointOption : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isApoActive;
    private bool _enhancementsDisabled;
    private string _statusText = string.Empty;

    public SystemEqEndpointOption(string id, string guid, string name, string description, bool isDefault, bool isFiio)
    {
        Id = id;
        Guid = guid;
        Name = name;
        Description = description;
        IsDefault = isDefault;
        IsFiio = isFiio;
    }

    public string Id { get; }
    public string Guid { get; }
    public string Name { get; }
    public string Description { get; }
    public bool IsDefault { get; }
    public bool IsFiio { get; }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }

    public bool IsApoActive
    {
        get => _isApoActive;
        set => SetField(ref _isApoActive, value);
    }

    public bool EnhancementsDisabled
    {
        get => _enhancementsDisabled;
        set => SetField(ref _enhancementsDisabled, value);
    }

    public string StatusText
    {
        get => _statusText;
        set => SetField(ref _statusText, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return;
        }

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
