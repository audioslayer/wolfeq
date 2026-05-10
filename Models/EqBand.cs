using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WolfEQ.Models;

public enum EqFilterType
{
    Peak = 0,
    LowShelf = 1,
    HighShelf = 2,
    BandPass = 3,
    LowPass = 4,
    HighPass = 5,
    AllPass = 6
}

public sealed class EqBand : INotifyPropertyChanged
{
    private int _frequencyHz;
    private double _gainDb;
    private double _q;
    private EqFilterType _filterType;
    private bool _enabled = true;
    private int _number;

    public int Number
    {
        get => _number;
        set => SetField(ref _number, value);
    }

    public int FrequencyHz
    {
        get => _frequencyHz;
        set => SetField(ref _frequencyHz, Math.Clamp(value, 20, 20000));
    }

    public double GainDb
    {
        get => _gainDb;
        set => SetField(ref _gainDb, Math.Clamp(Math.Round(value, 1), -24.0, 12.0));
    }

    public double Q
    {
        get => _q;
        set => SetField(ref _q, Math.Clamp(Math.Round(value, 2), 0.10, 10.0));
    }

    public EqFilterType FilterType
    {
        get => _filterType;
        set => SetField(ref _filterType, value);
    }

    public bool Enabled
    {
        get => _enabled;
        set => SetField(ref _enabled, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
