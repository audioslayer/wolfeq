using System.Windows.Media;
using WolfEQ.Services;

namespace WolfEQ.ViewModels;

/// <summary>A built-in K13 light effect: a mode plus, for rainbow and follow audio, a fixed color value.</summary>
public sealed record LightEffectOption(string Key, string Name, string Description, byte Mode, byte? FixedColor)
{
    public bool UsesColor => FixedColor is null;
}

/// <summary>
/// The Settings › Lights editor: one set of controls that edits the top light, the knob, or both,
/// plus the live preview. Every change is written over Bluetooth and only kept if the K13 accepts it.
/// </summary>
public sealed partial class MainViewModel
{
    public const string LightZoneBoth = "Both";
    public const string LightZoneTop = "Top";
    public const string LightZoneKnob = "Knob";

    private string _lightEditZone = LightZoneBoth;
    private byte _lastTopPlainColor = 0x07;
    private byte _lastKnobPlainColor = 0x03;

    /// <summary>Every look the K13 can show by itself. It has two modes (solid, breathe) and two special colors.</summary>
    public IReadOnlyList<LightEffectOption> LightEffectOptions { get; } =
    [
        new("solid", "Solid", "A steady light in the color you pick.", 0x00, null),
        new("breathing", "Breathing", "Slowly fades in and out in the color you pick.", 0x01, null),
        new("rainbow", "Rainbow", "Cycles through every color.", 0x00, 0x08),
        new("rainbow-breathing", "Rainbow breathing", "Cycles through every color while fading in and out.", 0x01, 0x08),
        new("audio", "Follow audio", "The K13's own audio-reactive mode.", 0x00, 0x00)
    ];

    /// <summary>The plain colors (rainbow and follow audio are effects, not colors).</summary>
    public IEnumerable<LedColorOption> LightColorChoices => LedColorOptions.Where(option => IsPlainLightColor(option.Value));

    /// <summary>Which light the editor changes: "Both", "Top" or "Knob".</summary>
    public string LightEditZone
    {
        get => _lightEditZone;
        set
        {
            var zone = value is LightZoneTop or LightZoneKnob ? value : LightZoneBoth;
            if (SetField(ref _lightEditZone, zone)) RaiseLightEditorChanged();
        }
    }

    public bool IsBluetoothSaved => _binding.BluetoothAddress is not null;
    public bool LightsNeedBluetooth => SelectedDeviceSupportsBleLighting && !IsBluetoothSaved;
    public string LightsEditingTitle => $"Lights for {SelectedDeviceUserPreset?.DeviceName ?? "this slot"}";

    /// <summary>The selected effect, or null when editing both lights and they differ.</summary>
    public LightEffectOption? EditorLightEffect => EditedZoneValue(zone => EffectFor(zone.Color, zone.Mode));

    /// <summary>The selected plain color, or null for rainbow/follow audio or when both lights differ.</summary>
    public LedColorOption? EditorLightColor
        => EditedZoneValue(zone => IsPlainLightColor(zone.Color) ? LedColorOptions.First(option => option.Value == zone.Color) : null);

    /// <summary>On when any edited light is on. Setting it writes to the K13 (the switch in Settings binds here).</summary>
    public bool EditorLightOn
    {
        get => EditedZones().Any(zone => zone.On);
        set
        {
            if (value == EditorLightOn) return;
            _ = SetEditedLightPowerAsync(value);
            // If the write was refused before changing anything, snap the switch back once
            // the binding update finishes (WPF ignores change notifications raised mid-update).
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(RaiseLightEditorChanged);
        }
    }
    public string EditorLightOnText => EditorLightOn ? "On" : "Off";

    public bool LightZonesDiffer => LightEditZone == LightZoneBoth
        && (TopLedOn != KnobLedOn || SelectedTopLedColorOption.Value != SelectedKnobLedColorOption.Value
            || SelectedTopLedModeOption.Value != SelectedKnobLedModeOption.Value);

    public Brush TopLightPreviewBrush => TopLedOn ? LedBrushes.For(SelectedTopLedColorOption.Value) : LedBrushes.Off;
    public Brush KnobLightPreviewBrush => KnobLedOn ? LedBrushes.For(SelectedKnobLedColorOption.Value) : LedBrushes.Off;
    public bool TopLightPreviewBreathing => TopLedOn && SelectedTopLedModeOption.Value == 0x01;
    public bool KnobLightPreviewBreathing => KnobLedOn && SelectedKnobLedModeOption.Value == 0x01;
    public string TopLightPreviewText => DescribeZone(TopLedOn, SelectedTopLedColorOption.Value, SelectedTopLedModeOption.Value);
    public string KnobLightPreviewText => DescribeZone(KnobLedOn, SelectedKnobLedColorOption.Value, SelectedKnobLedModeOption.Value);

    public async Task SetLightEffectAsync(LightEffectOption effect)
    {
        await WriteEditedZonesAsync(effect.Name, zone => zone with
        {
            On = true,
            Mode = effect.Mode,
            Color = effect.FixedColor ?? (IsPlainLightColor(zone.Color) ? zone.Color : zone.LastPlainColor)
        });
    }

    public async Task SetLightColorAsync(LedColorOption color)
    {
        // Picking a color from rainbow/follow audio switches to the matching plain effect.
        await WriteEditedZonesAsync(color.Name, zone => zone with { On = true, Color = color.Value });
    }

    public async Task SetEditedLightPowerAsync(bool on)
    {
        await WriteEditedZonesAsync(on ? "On" : "Off", zone => zone with { On = on });
    }

    public async Task ApplyLightLookAsync(LedSceneOption look)
    {
        LightEditZone = LightZoneBoth;
        await WriteLightStatesAsync(
            new LightZoneState(true, look.TopColor, look.Mode, _lastTopPlainColor),
            new LightZoneState(true, look.KnobColor, look.Mode, _lastKnobPlainColor),
            look.Name);
    }

    private sealed record LightZoneState(bool On, byte Color, byte Mode, byte LastPlainColor);

    private LightZoneState TopZone => new(TopLedOn, SelectedTopLedColorOption.Value, SelectedTopLedModeOption.Value, _lastTopPlainColor);
    private LightZoneState KnobZone => new(KnobLedOn, SelectedKnobLedColorOption.Value, SelectedKnobLedModeOption.Value, _lastKnobPlainColor);

    private IEnumerable<LightZoneState> EditedZones()
    {
        if (LightEditZone != LightZoneKnob) yield return TopZone;
        if (LightEditZone != LightZoneTop) yield return KnobZone;
    }

    private T? EditedZoneValue<T>(Func<LightZoneState, T?> select) where T : class
    {
        var values = EditedZones().Select(select).Distinct().ToArray();
        return values.Length == 1 ? values[0] : null;
    }

    private LightEffectOption EffectFor(byte color, byte mode)
        => LightEffectOptions.First(effect => color switch
        {
            0x00 => effect.Key == "audio",
            0x08 => effect.Key == (mode == 0x01 ? "rainbow-breathing" : "rainbow"),
            _ => effect.Key == (mode == 0x01 ? "breathing" : "solid")
        });

    private string DescribeZone(bool on, byte color, byte mode)
    {
        if (!on) return "Off";
        var effect = EffectFor(color, mode);
        return effect.UsesColor ? $"{SelectLedColorOption(color).Name} · {effect.Name.ToLowerInvariant()}" : effect.Name;
    }

    private static bool IsPlainLightColor(byte value) => value is >= 0x01 and <= 0x07;

    private Task WriteEditedZonesAsync(string label, Func<LightZoneState, LightZoneState> change)
    {
        var top = LightEditZone == LightZoneKnob ? TopZone : change(TopZone);
        var knob = LightEditZone == LightZoneTop ? KnobZone : change(KnobZone);
        return WriteLightStatesAsync(top, knob, label);
    }

    /// <summary>
    /// Shows the new state straight away, writes only the light(s) that changed, then shows what the
    /// K13 reports. If the write fails, the editor goes back to the previous state instead of showing
    /// colors the device never received.
    /// </summary>
    private async Task WriteLightStatesAsync(LightZoneState top, LightZoneState knob, string label)
    {
        var previousTop = TopZone;
        var previousKnob = KnobZone;
        var topChanged = previousTop with { LastPlainColor = 0 } != top with { LastPlainColor = 0 };
        var knobChanged = previousKnob with { LastPlainColor = 0 } != knob with { LastPlainColor = 0 };
        if (!topChanged && !knobChanged) return;

        if (!IsBluetoothSaved)
        {
            Status = "Set up Bluetooth in Settings › Your device to change the lights.";
            return;
        }

        ShowLightStates(top, knob);
        var target = topChanged && knobChanged ? "lights" : topChanged ? "top light" : "knob light";
        Status = $"Setting {target}: {label}...";
        AddLog($"BLE light editor write requested: {target} → {label}.");

        try
        {
            var snapshot = topChanged && knobChanged
                ? await _bleLightService.SetSplitLightsAsync(top.Color, top.Mode, top.On, knob.Color, knob.Mode, knob.On)
                : topChanged
                    ? await _bleLightService.SetLightAsync(0x02, top.Color, top.Mode, top.On)
                    : await _bleLightService.SetLightAsync(0x03, knob.Color, knob.Mode, knob.On);
            ApplyLightSnapshot(snapshot);
            LogBleSnapshot(snapshot);
            SaveSelectedSlotLightingProfile();
            Status = $"Lights: top {TopLightPreviewText.ToLowerInvariant()}, knob {KnobLightPreviewText.ToLowerInvariant()}.";
            AddLog(Status);
        }
        catch (Exception ex)
        {
            ShowLightStates(previousTop, previousKnob);
            if (ex is K13BleOperationException bleError) LogBleException(bleError);
            Status = ex is OperationCanceledException
                ? "Light change was canceled."
                : $"Couldn't change the lights: {(ex.InnerException ?? ex).Message}";
            AddLog(Status);
        }
    }

    private void ShowLightStates(LightZoneState top, LightZoneState knob)
    {
        TopLedOn = top.On;
        KnobLedOn = knob.On;
        SelectedTopLedColorOption = SelectLedColorOption(top.Color);
        SelectedKnobLedColorOption = SelectLedColorOption(knob.Color);
        SelectedTopLedModeOption = SelectLedModeOption(top.Mode);
        SelectedKnobLedModeOption = SelectLedModeOption(knob.Mode);
    }

    /// <summary>Editor properties derived from the zone state; kept live via the summary dependency map.</summary>
    internal static string[] LightEditorProperties() =>
    [
        nameof(EditorLightEffect), nameof(EditorLightColor), nameof(EditorLightOn), nameof(EditorLightOnText),
        nameof(LightZonesDiffer), nameof(TopLightPreviewBrush), nameof(KnobLightPreviewBrush),
        nameof(TopLightPreviewBreathing), nameof(KnobLightPreviewBreathing),
        nameof(TopLightPreviewText), nameof(KnobLightPreviewText)
    ];

    private void RaiseLightEditorChanged()
    {
        foreach (var name in LightEditorProperties()) OnPropertyChanged(name);
    }
}
