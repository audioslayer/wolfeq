using System.Reflection;
using WolfEQ.Services;
using WolfEQ.ViewModels;
using Xunit;

namespace WolfEQ.Tests;

/// <summary>Settings › Lights editor and the guided Bluetooth setup states (no device or disk writes).</summary>
public sealed class LightEditorTests : IDisposable
{
    private readonly SystemEqTestRig _rig = new();

    public void Dispose() => _rig.Dispose();

    [Fact]
    public void EditorShowsTheEditedLightsEffectAndColor()
    {
        var model = Model();
        SetZones(model, top: (true, 0x03, 0x00), knob: (true, 0x07, 0x01));

        Assert.True(model.LightZonesDiffer);
        Assert.Null(model.EditorLightEffect);
        Assert.Null(model.EditorLightColor);

        model.LightEditZone = MainViewModel.LightZoneTop;
        Assert.Equal("solid", model.EditorLightEffect!.Key);
        Assert.Equal("Cyan", model.EditorLightColor!.Name);
        Assert.False(model.LightZonesDiffer);

        model.LightEditZone = MainViewModel.LightZoneKnob;
        Assert.Equal("breathing", model.EditorLightEffect!.Key);
        Assert.Equal("Green", model.EditorLightColor!.Name);
        Assert.True(model.KnobLightPreviewBreathing);
    }

    [Fact]
    public void RainbowAndFollowAudioAreEffectsWithoutAColor()
    {
        var model = Model();
        SetZones(model, top: (true, 0x08, 0x01), knob: (true, 0x00, 0x00));

        model.LightEditZone = MainViewModel.LightZoneTop;
        Assert.Equal("rainbow-breathing", model.EditorLightEffect!.Key);
        Assert.Null(model.EditorLightColor);
        Assert.Equal("Rainbow breathing", model.TopLightPreviewText);

        model.LightEditZone = MainViewModel.LightZoneKnob;
        Assert.Equal("audio", model.EditorLightEffect!.Key);
        Assert.DoesNotContain(model.LightColorChoices, option => option.Value is 0x00 or 0x08);
    }

    [Fact]
    public void OffLightsPreviewAsOffAndTheSwitchFollowsTheEditedZone()
    {
        var model = Model();
        SetZones(model, top: (false, 0x01, 0x00), knob: (true, 0x02, 0x00));

        Assert.Same(LedBrushes.Off, model.TopLightPreviewBrush);
        Assert.Equal("Off", model.TopLightPreviewText);
        Assert.True(model.EditorLightOn); // Both: on when either light is on.

        model.LightEditZone = MainViewModel.LightZoneTop;
        Assert.False(model.EditorLightOn);
        Assert.Equal("Off", model.EditorLightOnText);
    }

    [Fact]
    public async Task LightChangesWaitForBluetoothSetupAndLeaveTheEditorUnchanged()
    {
        var model = Model();
        SetZones(model, top: (true, 0x03, 0x00), knob: (true, 0x03, 0x00));
        Assert.True(model.LightsNeedBluetooth);

        await model.SetLightColorAsync(model.LedColorOptions.Single(option => option.Name == "Red"));

        Assert.Equal("Cyan", model.SelectedTopLedColorOption.Name);
        Assert.Contains("Bluetooth", model.Status);
    }

    [Fact]
    public void BluetoothCardShowsFindPromptUntilADeviceIsSaved()
    {
        var unsaved = Model();
        Assert.True(unsaved.ShowBluetoothFindPrompt);
        Assert.False(unsaved.ShowBluetoothSavedDevice);
        Assert.Equal("Not set up", unsaved.BluetoothStatusText);

        var saved = Model(new DeviceBindingSettings { BluetoothAddress = 0xAABBCC112233, BluetoothName = "FiiO K13 R2R" });
        Assert.False(saved.ShowBluetoothFindPrompt);
        Assert.True(saved.ShowBluetoothSavedDevice);
        Assert.False(saved.LightsNeedBluetooth);
        Assert.Equal("Set up", saved.BluetoothStatusText);
        Assert.Equal("FiiO K13 R2R  ·  ends in 112233", saved.SavedBluetoothDisplay);
    }

    private static MainViewModel Model(DeviceBindingSettings? binding = null)
    {
        var model = new MainViewModel();
        // Never use (or overwrite) a connection saved on the developer's machine.
        typeof(MainViewModel).GetField("_binding", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(model, binding ?? new DeviceBindingSettings());
        return model;
    }

    private static void SetZones(MainViewModel model, (bool On, byte Color, byte Mode) top, (bool On, byte Color, byte Mode) knob)
    {
        Set(model, nameof(MainViewModel.TopLedOn), top.On);
        Set(model, nameof(MainViewModel.KnobLedOn), knob.On);
        Set(model, nameof(MainViewModel.SelectedTopLedColorOption), model.LedColorOptions.Single(o => o.Value == top.Color));
        Set(model, nameof(MainViewModel.SelectedKnobLedColorOption), model.LedColorOptions.Single(o => o.Value == knob.Color));
        Set(model, nameof(MainViewModel.SelectedTopLedModeOption), model.LedModeOptions.Single(o => o.Value == top.Mode));
        Set(model, nameof(MainViewModel.SelectedKnobLedModeOption), model.LedModeOptions.Single(o => o.Value == knob.Mode));
    }

    private static void Set(MainViewModel model, string property, object value)
        => typeof(MainViewModel).GetProperty(property)!.SetValue(model, value);
}
