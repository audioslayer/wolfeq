using System.Collections.Concurrent;
using System.IO;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;
using Windows.Storage.Streams;

namespace WolfEQ.Services;

public sealed class FiioK13BleLightService
{
    private static readonly Guid ServiceUuid = Guid.Parse("00001100-04a5-1000-1000-40ed981a04a5");
    private static readonly Guid WriteUuid = Guid.Parse("00001101-04a5-1000-1000-40ed981a04a5");
    private static readonly Guid NotifyUuid = Guid.Parse("00001102-04a5-1000-1000-40ed981a04a5");
    private static readonly string[] SupportedAdvertisementNameFragments =
    [
        "K13",
        "FIIO",
        "SNOWSKY",
        "JadeAudio"
    ];
    private static readonly string AddressCachePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WolfEQ",
        "k13-ble-address.txt");
    private static readonly BleVolumeCommand[] VolumeCommands =
    [
        new("device-settings reference", 0x02, 0x01, 0x01),
        new("legacy probe", 0x00, 0x02, 0x02)
    ];
    private static readonly BleDeviceSettingCommand VolumeLimitCommand = new("volume limit", 0x02, 0x03, 0x01);
    private static readonly BleDeviceSettingCommand GainModeCommand = new("gain mode", 0x02, 0x02, 0x01);
    private static readonly BleDeviceSettingCommand ChannelBalanceCommand = new("channel balance", 0x02, 0x06, 0x01);
    private static readonly BleDeviceSettingCommand? DreCommand = null;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private ulong? _cachedAddress;

    public FiioK13BleLightService()
    {
        _cachedAddress = LoadCachedAddress();
    }

    public async Task<K13BleLightSnapshot> ReadLightsAsync(CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string> { "BLE light readback started." };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(session, transportLog, cancellationToken).ConfigureAwait(false);

            transportLog.Add($"Decoded lights: top={snapshot.Top}, knob={snapshot.Knob}.");
            return snapshot with { TransportLog = transportLog };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE light readback failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE light readback failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleLightSnapshot> SetBothLightsAsync(
        byte color,
        byte mode,
        bool on,
        CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string>
        {
            $"BLE light write requested: on={on}, mode={GetModeName(mode)}, color={GetColorName(color)}."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            foreach (var zone in new[] { LightZone.Top, LightZone.Knob })
            {
                await SendSetAsync(session, BuildSetPacket(0x01, zone, on ? (byte)1 : (byte)0), transportLog, cancellationToken)
                    .ConfigureAwait(false);
                await SendSetAsync(session, BuildSetPacket(0x02, zone, mode), transportLog, cancellationToken)
                    .ConfigureAwait(false);
                await SendSetAsync(session, BuildSetPacket(0x03, zone, color), transportLog, cancellationToken)
                    .ConfigureAwait(false);
            }

            var snapshot = await ReadSnapshotAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            transportLog.Add($"Decoded lights after write: top={snapshot.Top}, knob={snapshot.Knob}.");
            return snapshot with { TransportLog = transportLog };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE light write failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE light write failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleLightSnapshot> SetLightAsync(
        byte zone,
        byte color,
        byte mode,
        bool on,
        CancellationToken cancellationToken = default)
    {
        ValidateLightZone(zone);
        ValidateLightColor(color);
        ValidateLightMode(mode);

        var zoneName = GetZoneName(zone);
        var transportLog = new List<string>
        {
            $"BLE {zoneName} light write requested: on={on}, mode={GetModeName(mode)}, color={GetColorName(color)}."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            await SendSetAsync(session, BuildSetPacket(0x01, zone, on ? (byte)1 : (byte)0), transportLog, cancellationToken)
                .ConfigureAwait(false);
            await SendSetAsync(session, BuildSetPacket(0x02, zone, mode), transportLog, cancellationToken)
                .ConfigureAwait(false);
            await SendSetAsync(session, BuildSetPacket(0x03, zone, color), transportLog, cancellationToken)
                .ConfigureAwait(false);

            var snapshot = await ReadSnapshotAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            transportLog.Add($"Decoded lights after {zoneName} write: top={snapshot.Top}, knob={snapshot.Knob}.");
            return snapshot with { TransportLog = transportLog };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE {zoneName} light write failed internally: {ex.Message}");
            throw new K13BleOperationException($"BLE {zoneName} light write failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleLightSnapshot> SetSplitLightsAsync(
        byte topColor,
        byte topMode,
        bool topOn,
        byte knobColor,
        byte knobMode,
        bool knobOn,
        CancellationToken cancellationToken = default)
    {
        ValidateLightColor(topColor);
        ValidateLightColor(knobColor);
        ValidateLightMode(topMode);
        ValidateLightMode(knobMode);

        var transportLog = new List<string>
        {
            $"BLE split light write requested: top={topOn}/{GetModeName(topMode)}/{GetColorName(topColor)}, knob={knobOn}/{GetModeName(knobMode)}/{GetColorName(knobColor)}."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            await WriteLightStateAsync(session, LightZone.Top, topColor, topMode, topOn, transportLog, cancellationToken)
                .ConfigureAwait(false);
            await WriteLightStateAsync(session, LightZone.Knob, knobColor, knobMode, knobOn, transportLog, cancellationToken)
                .ConfigureAwait(false);

            var snapshot = await ReadSnapshotAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            transportLog.Add($"Decoded lights after split write: top={snapshot.Top}, knob={snapshot.Knob}.");
            return snapshot with { TransportLog = transportLog };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE split light write failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE split light write failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleLightSnapshot> SetBothLightPowerAsync(
        bool on,
        CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string>
        {
            $"BLE light power requested: on={on}."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            foreach (var zone in new[] { LightZone.Top, LightZone.Knob })
            {
                await SendSetAsync(session, BuildSetPacket(0x01, zone, on ? (byte)1 : (byte)0), transportLog, cancellationToken)
                    .ConfigureAwait(false);
            }

            var snapshot = await ReadSnapshotAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            transportLog.Add($"Decoded lights after power change: top={snapshot.Top}, knob={snapshot.Knob}.");
            return snapshot with { TransportLog = transportLog };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE light power change failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE light power change failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleLightSnapshot> SetLightPowerAsync(
        byte zone,
        bool on,
        CancellationToken cancellationToken = default)
    {
        ValidateLightZone(zone);

        var zoneName = GetZoneName(zone);
        var transportLog = new List<string>
        {
            $"BLE {zoneName} light power requested: on={on}."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            await SendSetAsync(session, BuildSetPacket(0x01, zone, on ? (byte)1 : (byte)0), transportLog, cancellationToken)
                .ConfigureAwait(false);

            var snapshot = await ReadSnapshotAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            transportLog.Add($"Decoded lights after {zoneName} power change: top={snapshot.Top}, knob={snapshot.Knob}.");
            return snapshot with { TransportLog = transportLog };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE {zoneName} light power change failed internally: {ex.Message}");
            throw new K13BleOperationException($"BLE {zoneName} light power change failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleInputSourceSnapshot> ReadInputSourceAsync(CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string> { "BLE input source readback started." };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);
            var source = await ReadInputSourceCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);

            transportLog.Add($"Decoded input source: {GetInputSourceName(source)}.");
            return new K13BleInputSourceSnapshot(source, source, source, true, transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE input source readback failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE input source readback failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleInputSourceSnapshot> SetInputSourceAsync(
        byte source,
        CancellationToken cancellationToken = default)
    {
        if (source is not 0x01 and not 0x04)
        {
            throw new InvalidOperationException("Input source writes are currently limited to verified USB and COAX values.");
        }

        var transportLog = new List<string>
        {
            $"BLE input source write requested: {GetInputSourceName(source)}."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);
            var before = await ReadInputSourceCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);

            await SendSetAsync(
                session,
                BuildSetInputSourcePacket(source),
                transportLog,
                cancellationToken).ConfigureAwait(false);

            var after = await ReadInputSourceCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            var confirmed = after == source;

            transportLog.Add(
                $"Decoded input after write: {GetInputSourceName(before)} -> {GetInputSourceName(after)}; confirmed={confirmed}.");

            return new K13BleInputSourceSnapshot(before, source, after, confirmed, transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE input source write failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE input source write failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleVolumeSnapshot> ReadVolumeAsync(CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string> { "BLE volume readback started." };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);
            var readback = await ReadVolumeCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);

            transportLog.Add($"Decoded volume: {readback.Level}/99 via {readback.Command.Name}.");
            return new K13BleVolumeSnapshot(readback.Level, readback.Level, false, readback.Command.Name, transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE volume readback failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE volume readback failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleVolumeSnapshot> ChangeVolumeByOneAsync(
        int direction,
        CancellationToken cancellationToken = default)
    {
        if (direction is not -1 and not 1)
        {
            throw new ArgumentOutOfRangeException(nameof(direction), "Volume test only allows one step up or down.");
        }

        var transportLog = new List<string>
        {
            $"BLE guarded volume change requested: {(direction > 0 ? "+1" : "-1")} step."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);
            var beforeReadback = await ReadVolumeCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            var before = beforeReadback.Level;
            var target = (byte)Math.Clamp(before + direction, 0, 99);

            if (target == before)
            {
                transportLog.Add($"Volume already at limit {before}/99; SET skipped.");
                return new K13BleVolumeSnapshot(before, before, false, beforeReadback.Command.Name, transportLog);
            }

            await SendSetAsync(
                session,
                BuildSetPacket(beforeReadback.Command, target),
                transportLog,
                cancellationToken).ConfigureAwait(false);

            var afterReadback = await ReadVolumeCoreAsync(
                session,
                transportLog,
                cancellationToken,
                beforeReadback.Command).ConfigureAwait(false);
            var after = afterReadback.Level;
            var changed = after != before;

            if (changed)
            {
                transportLog.Add($"Decoded volume after guarded change: {before}/99 -> {after}/99.");
            }
            else
            {
                transportLog.Add($"Volume SET ACKed, but readback stayed at {after}/99. Treating volume write as unverified.");
            }

            return new K13BleVolumeSnapshot(before, after, changed, afterReadback.Command.Name, transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE volume change failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE volume change failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleVolumeSnapshot> SetVolumeAsync(
        byte level,
        CancellationToken cancellationToken = default)
    {
        if (level > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(level), "Volume must be between 0 and 99.");
        }

        var transportLog = new List<string>
        {
            $"BLE guarded volume set requested: {level}/99."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);
            var beforeReadback = await ReadVolumeCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            var before = beforeReadback.Level;

            await SendSetAsync(
                session,
                BuildSetPacket(beforeReadback.Command, level),
                transportLog,
                cancellationToken).ConfigureAwait(false);

            var afterReadback = await ReadVolumeCoreAsync(
                session,
                transportLog,
                cancellationToken,
                beforeReadback.Command).ConfigureAwait(false);
            var after = afterReadback.Level;
            var confirmed = after == level;

            transportLog.Add(
                confirmed
                    ? $"Volume set confirmed: {before}/99 -> {after}/99."
                    : $"Volume set unverified. Requested {level}/99; readback returned {after}/99.");

            return new K13BleVolumeSnapshot(before, after, confirmed, afterReadback.Command.Name, transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE volume set failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE volume set failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleDeviceControlsSnapshot> ReadDeviceControlsAsync(
        CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string>
        {
            "BLE device controls readback started. Candidate FiiO Control commands only."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            var volume = await ReadVolumeCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            var volumeLimit = await TryReadSettingByteAsync(session, VolumeLimitCommand, transportLog, cancellationToken).ConfigureAwait(false);
            var gain = await TryReadSettingByteAsync(session, GainModeCommand, transportLog, cancellationToken).ConfigureAwait(false);
            var balance = await TryReadBalanceAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            var dre = DreCommand is null
                ? null
                : await TryReadSettingByteAsync(session, DreCommand, transportLog, cancellationToken).ConfigureAwait(false);
            if (DreCommand is null)
            {
                transportLog.Add("BLE DRE command is not mapped yet; DRE readback skipped.");
            }

            return new K13BleDeviceControlsSnapshot(
                volume.Level,
                volumeLimit,
                gain,
                balance,
                dre is null ? null : dre != 0,
                transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE device controls readback failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE device controls readback failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleDeviceControlsSnapshot> ApplyDeviceControlsAsync(
        byte volume,
        byte volumeLimit,
        sbyte channelBalance,
        bool highGain,
        bool dreEnabled,
        CancellationToken cancellationToken = default)
    {
        if (volume > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(volume), "Volume must be between 0 and 99.");
        }

        if (volumeLimit > 99)
        {
            throw new ArgumentOutOfRangeException(nameof(volumeLimit), "Volume limit must be between 0 and 99.");
        }

        if (channelBalance is < -10 or > 10)
        {
            throw new ArgumentOutOfRangeException(nameof(channelBalance), "Channel balance must be between L10 and R10.");
        }

        var transportLog = new List<string>
        {
            "BLE device controls write requested. These FiiO Control commands are experimental and verified by readback."
        };

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            var volumeReadback = await ReadVolumeCoreAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            await SendSetAsync(
                session,
                BuildSetPacket(volumeReadback.Command, volume),
                transportLog,
                cancellationToken).ConfigureAwait(false);

            await TryWriteSettingAsync(session, VolumeLimitCommand, [volumeLimit], transportLog, cancellationToken).ConfigureAwait(false);
            await TryWriteSettingAsync(session, GainModeCommand, [highGain ? (byte)1 : (byte)0], transportLog, cancellationToken).ConfigureAwait(false);
            await TryWriteBalanceAsync(session, channelBalance, transportLog, cancellationToken).ConfigureAwait(false);
            if (DreCommand is null)
            {
                transportLog.Add("BLE DRE command is not mapped yet; DRE write skipped.");
            }
            else
            {
                await TryWriteSettingAsync(session, DreCommand, [dreEnabled ? (byte)1 : (byte)0], transportLog, cancellationToken).ConfigureAwait(false);
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);

            var afterVolume = await ReadVolumeCoreAsync(
                session,
                transportLog,
                cancellationToken,
                volumeReadback.Command).ConfigureAwait(false);
            var afterLimit = await TryReadSettingByteAsync(session, VolumeLimitCommand, transportLog, cancellationToken).ConfigureAwait(false);
            var afterGain = await TryReadSettingByteAsync(session, GainModeCommand, transportLog, cancellationToken).ConfigureAwait(false);
            var afterBalance = await TryReadBalanceAsync(session, transportLog, cancellationToken).ConfigureAwait(false);
            var afterDre = DreCommand is null
                ? null
                : await TryReadSettingByteAsync(session, DreCommand, transportLog, cancellationToken).ConfigureAwait(false);

            transportLog.Add(
                $"Device control readback: volume {afterVolume.Level}/99, limit {(afterLimit?.ToString() ?? "?")}, " +
                $"gain {(afterGain is null ? "?" : afterGain == 0 ? "low" : "high")}, balance {(afterBalance?.ToString() ?? "?")}, DRE {(afterDre is null ? "?" : afterDre != 0 ? "on" : "off")}.");

            return new K13BleDeviceControlsSnapshot(
                afterVolume.Level,
                afterLimit,
                afterGain,
                afterBalance,
                afterDre is null ? null : afterDre != 0,
                transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE device controls write failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE device controls write failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleProbeSnapshot> ProbeVolume85Async(CancellationToken cancellationToken = default)
    {
        const byte expectedLevel = 85;
        var transportLog = new List<string>
        {
            "BLE volume probe started. GET packets only; no volume SET commands will be sent.",
            $"Looking for decimal {expectedLevel} / hex 0x{expectedLevel:X2} in response data."
        };

        var matches = 0;
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);

            foreach (var candidate in BuildVolumeProbeCandidates())
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var response = await SendProbeGetAsync(
                        session,
                        candidate,
                        transportLog,
                        cancellationToken).ConfigureAwait(false);

                    var data = ExtractResponseData(response);
                    if (data.Contains(expectedLevel))
                    {
                        matches++;
                        transportLog.Add($"*** Possible volume match: {candidate.Name} returned data {FormatHex(data)}.");
                    }
                }
                catch (TimeoutException)
                {
                    transportLog.Add($"Probe no response: {candidate.Name} [{FormatHex(candidate.GetTriplet)}].");
                }

                await Task.Delay(80, cancellationToken).ConfigureAwait(false);
            }

            transportLog.Add($"Volume probe complete. Possible matches: {matches}.");
            return new K13BleProbeSnapshot(matches, transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE volume probe failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE volume probe failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<K13BleProbeSnapshot> ListenForVolumeNotificationsAsync(
        TimeSpan duration,
        CancellationToken cancellationToken = default)
    {
        var transportLog = new List<string>
        {
            $"BLE passive notification capture started for {duration.TotalSeconds:F0} seconds.",
            "No GET or SET packets will be sent. Turn the K13 volume knob during this window."
        };

        var notifications = 0;
        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            transportLog.Add("BLE operation lock acquired.");
            await using var session = await ConnectAsync(transportLog, cancellationToken).ConfigureAwait(false);
            session.Drain();

            var deadline = DateTimeOffset.UtcNow + duration;
            while (DateTimeOffset.UtcNow < deadline)
            {
                var remaining = deadline - DateTimeOffset.UtcNow;
                var wait = remaining < TimeSpan.FromSeconds(1)
                    ? remaining
                    : TimeSpan.FromSeconds(1);

                if (wait <= TimeSpan.Zero)
                {
                    break;
                }

                try
                {
                    var notification = await session.ReadNotificationAsync(wait, cancellationToken)
                        .ConfigureAwait(false);

                    notifications++;
                    transportLog.Add($"BLE NOTIFY RX: {FormatHex(notification)}");
                }
                catch (TimeoutException)
                {
                    // Quiet one-second polling; final count is logged below.
                }
            }

            transportLog.Add($"Passive notification capture complete. Notifications received: {notifications}.");
            return new K13BleProbeSnapshot(notifications, transportLog);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            transportLog.Add($"BLE passive notification capture failed internally: {ex.Message}");
            throw new K13BleOperationException("BLE passive notification capture failed.", transportLog, ex);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    private async Task<BleSession> ConnectAsync(List<string> transportLog, CancellationToken cancellationToken)
    {
        if (_cachedAddress is ulong cachedAddress)
        {
            transportLog.Add($"Trying cached K13 BLE address {cachedAddress:X12} before scanning.");
            var cachedSession = await TryCreateSessionAsync(cachedAddress, transportLog, cancellationToken)
                .ConfigureAwait(false);

            if (cachedSession is not null)
            {
                return cachedSession;
            }

            transportLog.Add("Cached K13 BLE address did not open cleanly; falling back to advertisement scan.");
            _cachedAddress = null;
        }

        var address = await FindK13AddressAsync(transportLog, cancellationToken).ConfigureAwait(false);
        _cachedAddress = address;
        SaveCachedAddress(address, transportLog);

        var session = await TryCreateSessionAsync(address, transportLog, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            _cachedAddress = null;
            throw new InvalidOperationException("K13 BLE device was found, but the light service could not be opened.");
        }

        return session;
    }

    private static ulong? LoadCachedAddress()
    {
        try
        {
            if (!File.Exists(AddressCachePath))
            {
                return null;
            }

            var text = File.ReadAllText(AddressCachePath).Trim();
            return ulong.TryParse(text, System.Globalization.NumberStyles.HexNumber, null, out var address)
                ? address
                : null;
        }
        catch
        {
            return null;
        }
    }

    private static void SaveCachedAddress(ulong address, List<string> transportLog)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AddressCachePath)!);
            File.WriteAllText(AddressCachePath, address.ToString("X12"));
            transportLog.Add($"Cached K13 BLE address for future app launches: {address:X12}.");
        }
        catch (Exception ex)
        {
            transportLog.Add($"Could not save K13 BLE address cache: {ex.Message}");
        }
    }

    private static async Task<BleSession?> TryCreateSessionAsync(
        ulong address,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        transportLog.Add($"Connecting to BLE address {address:X12}.");

        var device = await BluetoothLEDevice.FromBluetoothAddressAsync(address);
        if (device is null)
        {
            transportLog.Add("Windows could not open the K13 BLE device object.");
            return null;
        }

        transportLog.Add($"BLE device object: {device.Name}, status={device.ConnectionStatus}.");

        var service = await GetServiceAsync(device, transportLog, cancellationToken).ConfigureAwait(false);
        if (service is null)
        {
            device.Dispose();
            return null;
        }

        var writeCharacteristic = await GetCharacteristicAsync(
            service,
            WriteUuid,
            "write",
            transportLog,
            cancellationToken).ConfigureAwait(false);

        var notifyCharacteristic = await GetCharacteristicAsync(
            service,
            NotifyUuid,
            "notify",
            transportLog,
            cancellationToken).ConfigureAwait(false);

        if (writeCharacteristic is null || notifyCharacteristic is null)
        {
            service.Dispose();
            device.Dispose();
            return null;
        }

        var session = new BleSession(device, service, writeCharacteristic, notifyCharacteristic);
        session.NotifyCharacteristic.ValueChanged += session.OnValueChanged;

        var subscribeStatus = await session.NotifyCharacteristic
            .WriteClientCharacteristicConfigurationDescriptorAsync(GattClientCharacteristicConfigurationDescriptorValue.Notify);

        if (subscribeStatus != GattCommunicationStatus.Success)
        {
            transportLog.Add($"K13 BLE notification subscribe failed: {subscribeStatus}.");
            await session.DisposeAsync().ConfigureAwait(false);
            return null;
        }

        transportLog.Add("Subscribed to K13 BLE notifications.");
        return session;
    }

    private static async Task<GattDeviceService?> GetServiceAsync(
        BluetoothLEDevice device,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        foreach (var attempt in new[] { 1, 2, 3 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cacheMode = attempt == 1 ? BluetoothCacheMode.Uncached : BluetoothCacheMode.Cached;
            var services = await device.GetGattServicesForUuidAsync(ServiceUuid, cacheMode);

            transportLog.Add(
                $"BLE service lookup attempt {attempt} ({cacheMode}): {services.Status}, count={services.Services.Count}.");

            if (services.Status == GattCommunicationStatus.Success && services.Services.Count > 0)
            {
                return services.Services[0];
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<GattCharacteristic?> GetCharacteristicAsync(
        GattDeviceService service,
        Guid characteristicUuid,
        string displayName,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        foreach (var attempt in new[] { 1, 2, 3 })
        {
            cancellationToken.ThrowIfCancellationRequested();
            var cacheMode = attempt == 1 ? BluetoothCacheMode.Uncached : BluetoothCacheMode.Cached;
            var result = await service.GetCharacteristicsForUuidAsync(characteristicUuid, cacheMode);

            transportLog.Add(
                $"BLE {displayName} characteristic lookup attempt {attempt} ({cacheMode}): {result.Status}, count={result.Characteristics.Count}.");

            if (result.Status == GattCommunicationStatus.Success && result.Characteristics.Count > 0)
            {
                return result.Characteristics[0];
            }

            await Task.Delay(250, cancellationToken).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<ulong> FindK13AddressAsync(List<string> transportLog, CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<ulong>(TaskCreationOptions.RunContinuationsAsynchronously);
        var observed = new ConcurrentDictionary<string, BleAdvertisementSample>();
        var watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };

        watcher.Received += (_, args) =>
        {
            var name = args.Advertisement.LocalName ?? string.Empty;
            var hasService = args.Advertisement.ServiceUuids.Contains(ServiceUuid);
            TrackAdvertisementSample(observed, name, args.RawSignalStrengthInDBm, hasService);
            var isSupportedDevice = IsSupportedAdvertisement(name, hasService);

            if (!isSupportedDevice)
            {
                return;
            }

            transportLog.Add(
                $"BLE advertisement match: {args.BluetoothAddress:X12}, name='{name}', rssi={args.RawSignalStrengthInDBm}, service={hasService}.");
            completion.TrySetResult(args.BluetoothAddress);
        };

        using var stopRegistration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));

        watcher.Start();
        transportLog.Add("Scanning for supported FiiO/SNOWSKY BLE advertisement...");

        try
        {
            var finished = await Task.WhenAny(completion.Task, Task.Delay(TimeSpan.FromSeconds(12), cancellationToken))
                .ConfigureAwait(false);

            if (finished != completion.Task)
            {
                AppendAdvertisementSummary(transportLog, observed.Values);
                throw new TimeoutException("Supported FiiO/SNOWSKY BLE advertisement was not found within 12 seconds.");
            }

            return await completion.Task.ConfigureAwait(false);
        }
        finally
        {
            watcher.Stop();
        }
    }

    private static bool IsSupportedAdvertisement(string name, bool hasService)
        => hasService || SupportedAdvertisementNameFragments.Any(fragment =>
            name.Contains(fragment, StringComparison.OrdinalIgnoreCase));

    private static void TrackAdvertisementSample(
        ConcurrentDictionary<string, BleAdvertisementSample> observed,
        string name,
        short rssi,
        bool hasService)
    {
        var displayName = string.IsNullOrWhiteSpace(name) ? "<unnamed>" : name.Trim();
        var key = hasService ? $"{displayName}|service" : displayName;

        if (observed.Count >= 64 && !observed.ContainsKey(key))
        {
            return;
        }

        observed.AddOrUpdate(
            key,
            _ => new BleAdvertisementSample(displayName, rssi, hasService, 1),
            (_, current) => current with
            {
                Count = current.Count + 1,
                HasService = current.HasService || hasService,
                Rssi = Math.Max(current.Rssi, rssi)
            });
    }

    private static void AppendAdvertisementSummary(
        List<string> transportLog,
        IEnumerable<BleAdvertisementSample> observed)
    {
        var samples = observed
            .OrderByDescending(sample => sample.HasService)
            .ThenByDescending(sample => sample.Rssi)
            .Take(8)
            .Select(sample =>
                $"{sample.Name} rssi={sample.Rssi}{(sample.HasService ? " expected-service" : string.Empty)} x{sample.Count}")
            .ToList();

        transportLog.Add(samples.Count == 0
            ? "No BLE advertisements were observed by Windows during the 12-second scan."
            : $"BLE advertisements observed during scan: {string.Join("; ", samples)}.");
    }

    private static async Task<K13BleLightSnapshot> ReadSnapshotAsync(
        BleSession session,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var top = await ReadLightStateAsync(session, LightZone.Top, transportLog, cancellationToken).ConfigureAwait(false);
        var knob = await ReadLightStateAsync(session, LightZone.Knob, transportLog, cancellationToken).ConfigureAwait(false);
        return new K13BleLightSnapshot(top, knob, transportLog);
    }

    private static async Task<K13BleLightState> ReadLightStateAsync(
        BleSession session,
        byte zone,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var on = await SendGetSingleByteAsync(session, BuildGetPacket(0x01, zone), transportLog, cancellationToken)
            .ConfigureAwait(false);
        var mode = await SendGetSingleByteAsync(session, BuildGetPacket(0x02, zone), transportLog, cancellationToken)
            .ConfigureAwait(false);
        var color = await SendGetSingleByteAsync(session, BuildGetPacket(0x03, zone), transportLog, cancellationToken)
            .ConfigureAwait(false);

        return new K13BleLightState(zone, on != 0, mode, color);
    }

    private static async Task<byte> ReadInputSourceCoreAsync(
        BleSession session,
        List<string> transportLog,
        CancellationToken cancellationToken)
        => await SendGetSingleByteAsync(
            session,
            BuildGetInputSourcePacket(),
            transportLog,
            cancellationToken).ConfigureAwait(false);

    private static async Task<K13BleVolumeReadback> ReadVolumeCoreAsync(
        BleSession session,
        List<string> transportLog,
        CancellationToken cancellationToken,
        BleVolumeCommand? preferredCommand = null)
    {
        var commands = preferredCommand is null
            ? VolumeCommands
            : new[] { preferredCommand }.Concat(VolumeCommands.Where(command => command != preferredCommand)).ToArray();

        foreach (var command in commands)
        {
            try
            {
                transportLog.Add($"Trying volume GET '{command.Name}': {FormatHex(command.GetTriplet)}.");
                var level = await SendGetSingleByteAsync(
                    session,
                    BuildGetPacket(command),
                    transportLog,
                    cancellationToken).ConfigureAwait(false);

                if (level > 99)
                {
                    transportLog.Add($"Volume GET '{command.Name}' returned out-of-range value {level}; trying next command.");
                    continue;
                }

                return new K13BleVolumeReadback(level, command);
            }
            catch (TimeoutException ex)
            {
                transportLog.Add($"Volume GET '{command.Name}' timed out: {ex.Message}");
                session.Drain();
            }
            catch (InvalidOperationException ex)
            {
                transportLog.Add($"Volume GET '{command.Name}' failed: {ex.Message}");
                session.Drain();
            }
        }

        throw new TimeoutException("No volume GET command returned a valid 0-99 response.");
    }

    private static async Task<byte?> TryReadSettingByteAsync(
        BleSession session,
        BleDeviceSettingCommand command,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAndReceiveAsync(
                session,
                BuildGetPacket(command.Category, command.Command, command.Target),
                transportLog,
                cancellationToken).ConfigureAwait(false);
            var data = ExtractResponseData(response);
            var value = data.Length > 0 ? data[0] : (byte?)null;
            transportLog.Add($"BLE {command.Name} readback: {(value is null ? "(blank)" : $"0x{value:X2}")}.");
            return value;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
        {
            transportLog.Add($"BLE {command.Name} readback unavailable: {ex.Message}");
            return null;
        }
    }

    private static async Task<sbyte?> TryReadBalanceAsync(
        BleSession session,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await SendAndReceiveAsync(
                session,
                BuildGetPacket(ChannelBalanceCommand.Category, ChannelBalanceCommand.Command, ChannelBalanceCommand.Target),
                transportLog,
                cancellationToken).ConfigureAwait(false);
            var data = ExtractResponseData(response);
            if (data.Length == 0)
            {
                return null;
            }

            var balance = DecodeBalance(data);
            transportLog.Add($"BLE channel balance readback: {balance:+0;-0;0}.");
            return balance;
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
        {
            transportLog.Add($"BLE channel balance readback unavailable: {ex.Message}");
            return null;
        }
    }

    private static async Task TryWriteSettingAsync(
        BleSession session,
        BleDeviceSettingCommand command,
        byte[] payload,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        try
        {
            await SendSetAsync(
                session,
                BuildSetPacket(command.Category, command.Command, command.Target, payload),
                transportLog,
                cancellationToken).ConfigureAwait(false);
            transportLog.Add($"BLE {command.Name} write sent: {FormatHex(payload)}.");
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidOperationException or IOException)
        {
            transportLog.Add($"BLE {command.Name} write unavailable: {ex.Message}");
        }
    }

    private static async Task TryWriteBalanceAsync(
        BleSession session,
        sbyte channelBalance,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var direction = channelBalance < 0 ? (byte)0x01 : channelBalance > 0 ? (byte)0x02 : (byte)0x00;
        var magnitude = (byte)Math.Abs(channelBalance);
        await TryWriteSettingAsync(
            session,
            ChannelBalanceCommand,
            [direction, magnitude],
            transportLog,
            cancellationToken).ConfigureAwait(false);
    }

    private static sbyte DecodeBalance(byte[] data)
    {
        if (data.Length >= 2)
        {
            var magnitude = (sbyte)Math.Clamp((int)data[1], 0, 10);
            return data[0] switch
            {
                0x01 => (sbyte)-magnitude,
                0x02 => magnitude,
                _ => 0
            };
        }

        return data[0] <= 10
            ? (sbyte)data[0]
            : unchecked((sbyte)data[0]);
    }

    private static async Task<byte> SendGetSingleByteAsync(
        BleSession session,
        byte[] packet,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        var response = await SendAndReceiveAsync(session, packet, transportLog, cancellationToken).ConfigureAwait(false);
        if (response.Length < 9)
        {
            throw new InvalidOperationException($"BLE response was too short: {FormatHex(response)}");
        }

        return response[7];
    }

    private static async Task<byte[]> SendAndReceiveAsync(
        BleSession session,
        byte[] packet,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        session.Drain();

        transportLog.Add($"BLE TX: {FormatHex(packet)}");
        await WritePacketAsync(session, packet).ConfigureAwait(false);

        var expectedCommand = packet.Skip(4).Take(3).ToArray();
        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var response = await session.ReadNotificationAsync(TimeSpan.FromSeconds(5), cancellationToken)
                .ConfigureAwait(false);

            if (response.Length >= 7 && response.Skip(4).Take(3).SequenceEqual(expectedCommand))
            {
                transportLog.Add($"BLE RX: {FormatHex(response)}");
                return response;
            }

            transportLog.Add($"BLE ignored non-matching RX attempt {attempt}: {FormatHex(response)}");
        }

        throw new TimeoutException($"No matching BLE response for {FormatHex(expectedCommand)}.");
    }

    private static async Task<byte[]> SendProbeGetAsync(
        BleSession session,
        BleProbeCommand candidate,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        session.Drain();

        var packet = BuildPacket(candidate.GetTriplet, []);
        transportLog.Add($"BLE PROBE TX {candidate.Name}: {FormatHex(packet)}");
        await WritePacketAsync(session, packet).ConfigureAwait(false);

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            var response = await session.ReadNotificationAsync(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);

            if (response.Length >= 7 && response.Skip(4).Take(3).SequenceEqual(candidate.GetTriplet))
            {
                transportLog.Add($"BLE PROBE RX {candidate.Name}: {FormatHex(response)}");
                return response;
            }

            transportLog.Add($"BLE probe ignored non-matching RX attempt {attempt}: {FormatHex(response)}");
        }

        throw new TimeoutException("BLE probe notification timeout.");
    }

    private static async Task SendSetAsync(
        BleSession session,
        byte[] packet,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        session.Drain();

        transportLog.Add($"BLE SET TX: {FormatHex(packet)}");
        await WritePacketAsync(session, packet).ConfigureAwait(false);

        try
        {
            var response = await session.ReadNotificationAsync(TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);
            transportLog.Add($"BLE SET RX: {FormatHex(response)}");
        }
        catch (TimeoutException)
        {
            transportLog.Add("BLE SET did not return an ACK before timeout; continuing to read back state.");
        }

        await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        session.Drain();
    }

    private static async Task WriteLightStateAsync(
        BleSession session,
        byte zone,
        byte color,
        byte mode,
        bool on,
        List<string> transportLog,
        CancellationToken cancellationToken)
    {
        await SendSetAsync(session, BuildSetPacket(0x01, zone, on ? (byte)1 : (byte)0), transportLog, cancellationToken)
            .ConfigureAwait(false);
        await SendSetAsync(session, BuildSetPacket(0x02, zone, mode), transportLog, cancellationToken)
            .ConfigureAwait(false);
        await SendSetAsync(session, BuildSetPacket(0x03, zone, color), transportLog, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task WritePacketAsync(BleSession session, byte[] packet)
    {
        var writer = new DataWriter();
        writer.WriteBytes(packet);

        var status = await session.WriteCharacteristic.WriteValueAsync(
            writer.DetachBuffer(),
            GattWriteOption.WriteWithResponse);

        if (status != GattCommunicationStatus.Success)
        {
            throw new InvalidOperationException($"BLE write failed: {status}.");
        }
    }

    private static byte[] BuildGetPacket(byte command, byte zone)
        => BuildPacket([0x05, command, zone], []);

    private static byte[] BuildSetPacket(byte command, byte zone, byte value)
        => BuildPacket([0x15, command, zone], [value]);

    private static byte[] BuildGetInputSourcePacket()
        => BuildPacket([0x09, 0x02, 0x01], []);

    private static byte[] BuildSetInputSourcePacket(byte source)
        => BuildPacket([0x19, 0x02, 0x01], [source]);

    private static byte[] BuildGetPacket(byte category, byte command, byte target)
        => BuildPacket([category, command, target], []);

    private static byte[] BuildSetPacket(byte category, byte command, byte target, byte value)
        => BuildPacket([(byte)(category | 0x10), command, target], [value]);

    private static byte[] BuildSetPacket(byte category, byte command, byte target, byte[] data)
        => BuildPacket([(byte)(category | 0x10), command, target], data);

    private static byte[] BuildGetPacket(BleVolumeCommand command)
        => BuildPacket(command.GetTriplet, []);

    private static byte[] BuildSetPacket(BleVolumeCommand command, byte value)
        => BuildPacket(command.SetTriplet, [value]);

    private static byte[] BuildPacket(byte[] command, byte[] data)
    {
        var totalLength = 3 + 1 + 3 + data.Length + 1;
        var packet = new byte[totalLength];
        packet[0] = 0xF1;
        packet[1] = 0x10;
        packet[2] = 0x00;
        packet[3] = (byte)totalLength;
        command.CopyTo(packet.AsSpan(4));
        data.CopyTo(packet.AsSpan(7));
        packet[^1] = 0xFF;
        return packet;
    }

    private static byte[] ReadBytes(IBuffer buffer)
    {
        var reader = DataReader.FromBuffer(buffer);
        var bytes = new byte[(int)buffer.Length];
        reader.ReadBytes(bytes);
        return bytes;
    }

    private static byte[] ExtractResponseData(byte[] response)
    {
        if (response.Length <= 7)
        {
            return [];
        }

        var end = response[^1] == 0xFF ? response.Length - 1 : response.Length;
        return response[7..end];
    }

    private static string FormatHex(IEnumerable<byte> bytes)
        => string.Join(" ", bytes.Select(value => value.ToString("X2")));

    public static string GetModeName(byte mode)
        => mode switch
        {
            0x00 => "Always On",
            0x01 => "Breathe",
            _ => $"Unknown(0x{mode:X2})"
        };

    public static string GetColorName(byte color)
        => color switch
        {
            0x00 => "Follow Audio",
            0x01 => "Red",
            0x02 => "Blue",
            0x03 => "Turquoise",
            0x04 => "Purple",
            0x05 => "Yellow",
            0x06 => "White",
            0x07 => "Green",
            0x08 => "Cycle",
            _ => $"Unknown(0x{color:X2})"
        };

    public static string GetZoneName(byte zone)
        => zone switch
        {
            LightZone.Top => "Top",
            LightZone.Knob => "Knob",
            _ => $"Zone 0x{zone:X2}"
        };

    public static string GetInputSourceName(byte source)
        => source switch
        {
            0x01 => "USB",
            0x04 => "COAX",
            0x08 => "OPTICAL",
            0x20 => "BLUETOOTH",
            _ => $"Unknown(0x{source:X2})"
        };

    private static class LightZone
    {
        public const byte Top = 0x02;
        public const byte Knob = 0x03;
    }

    private static void ValidateLightZone(byte zone)
    {
        if (zone is not LightZone.Top and not LightZone.Knob)
        {
            throw new InvalidOperationException("Light writes are limited to verified top and knob LED zones.");
        }
    }

    private static void ValidateLightColor(byte color)
    {
        if (color > 0x08)
        {
            throw new InvalidOperationException("Light color writes are limited to verified K13 color values.");
        }
    }

    private static void ValidateLightMode(byte mode)
    {
        if (mode is not 0x00 and not 0x01)
        {
            throw new InvalidOperationException("Light mode writes are limited to Always On and Breathe.");
        }
    }

    private static IEnumerable<BleProbeCommand> BuildVolumeProbeCandidates()
    {
        yield return new BleProbeCommand("reference volume 02-01-01", [0x02, 0x01, 0x01]);
        yield return new BleProbeCommand("legacy volume label 00-02-02", [0x00, 0x02, 0x02]);
        yield return new BleProbeCommand("legacy vol max 00-02-04", [0x00, 0x02, 0x04]);
        yield return new BleProbeCommand("legacy vol output 00-02-05", [0x00, 0x02, 0x05]);

        for (byte target = 0x00; target <= 0x10; target++)
        {
            yield return new BleProbeCommand($"legacy device 00-02-{target:X2}", [0x00, 0x02, target]);
        }

        for (byte command = 0x00; command <= 0x10; command++)
        {
            yield return new BleProbeCommand($"device settings 02-{command:X2}-01", [0x02, command, 0x01]);
            yield return new BleProbeCommand($"device settings 02-{command:X2}-02", [0x02, command, 0x02]);
        }
    }

    private sealed record BleVolumeCommand(string Name, byte Category, byte Command, byte Target)
    {
        public byte[] GetTriplet => [Category, Command, Target];
        public byte[] SetTriplet => [(byte)(Category | 0x10), Command, Target];
    }

    private sealed record BleDeviceSettingCommand(string Name, byte Category, byte Command, byte Target);

    private sealed record BleAdvertisementSample(string Name, short Rssi, bool HasService, int Count);

    private sealed record K13BleVolumeReadback(byte Level, BleVolumeCommand Command);

    private sealed record BleProbeCommand(string Name, byte[] GetTriplet);

    private sealed class BleSession(
        BluetoothLEDevice device,
        GattDeviceService service,
        GattCharacteristic writeCharacteristic,
        GattCharacteristic notifyCharacteristic) : IAsyncDisposable
    {
        private readonly ConcurrentQueue<byte[]> _notifications = new();
        private readonly SemaphoreSlim _notificationSignal = new(0);

        public BluetoothLEDevice Device { get; } = device;
        public GattDeviceService Service { get; } = service;
        public GattCharacteristic WriteCharacteristic { get; } = writeCharacteristic;
        public GattCharacteristic NotifyCharacteristic { get; } = notifyCharacteristic;

        public void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            _notifications.Enqueue(ReadBytes(args.CharacteristicValue));
            _notificationSignal.Release();
        }

        public void Drain()
        {
            while (_notifications.TryDequeue(out _))
            {
            }

            while (_notificationSignal.Wait(0))
            {
            }
        }

        public async Task<byte[]> ReadNotificationAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeout);

            try
            {
                await _notificationSignal.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("BLE notification timeout.");
            }

            if (_notifications.TryDequeue(out var notification))
            {
                return notification;
            }

            throw new TimeoutException("BLE notification signal had no queued data.");
        }

        public async ValueTask DisposeAsync()
        {
            NotifyCharacteristic.ValueChanged -= OnValueChanged;

            try
            {
                await NotifyCharacteristic.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.None);
            }
            catch
            {
                // Best-effort BLE cleanup.
            }

            Device.Dispose();
            Service.Dispose();
            _notificationSignal.Dispose();
        }
    }
}

public sealed record K13BleLightSnapshot(
    K13BleLightState Top,
    K13BleLightState Knob,
    IReadOnlyList<string> TransportLog);

public sealed record K13BleVolumeSnapshot(
    byte Before,
    byte After,
    bool Changed,
    string CommandName,
    IReadOnlyList<string> TransportLog)
{
    public string Display => Changed
        ? $"{Before}/99 -> {After}/99"
        : $"{After}/99";
}

public sealed record K13BleDeviceControlsSnapshot(
    byte? Volume,
    byte? VolumeLimit,
    byte? GainMode,
    sbyte? ChannelBalance,
    bool? DreEnabled,
    IReadOnlyList<string> TransportLog);

public sealed record K13BleInputSourceSnapshot(
    byte Before,
    byte Requested,
    byte After,
    bool Confirmed,
    IReadOnlyList<string> TransportLog)
{
    public string BeforeName => FiioK13BleLightService.GetInputSourceName(Before);
    public string RequestedName => FiioK13BleLightService.GetInputSourceName(Requested);
    public string AfterName => FiioK13BleLightService.GetInputSourceName(After);
}

public sealed record K13BleProbeSnapshot(
    int MatchCount,
    IReadOnlyList<string> TransportLog);

public sealed class K13BleOperationException(
    string message,
    IReadOnlyList<string> transportLog,
    Exception innerException) : Exception(message, innerException)
{
    public IReadOnlyList<string> TransportLog { get; } = transportLog;
}

public sealed record K13BleLightState(byte Zone, bool On, byte Mode, byte Color)
{
    public string ZoneName => Zone switch
    {
        0x02 => "Top",
        0x03 => "Knob",
        _ => $"Zone 0x{Zone:X2}"
    };

    public string ModeName => FiioK13BleLightService.GetModeName(Mode);
    public string ColorName => FiioK13BleLightService.GetColorName(Color);

    public override string ToString()
        => $"{ZoneName}: {(On ? "On" : "Off")}, {ModeName}, {ColorName}";
}
