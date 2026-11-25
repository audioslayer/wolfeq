using System.ComponentModel;
using System.IO;
using WolfEQ.Models;

namespace WolfEQ.Services;

public sealed partial class FiioK13DeviceService
{
    private sealed record KtMicroState(bool EqOn, bool HasZeroGain, IReadOnlyList<KtMicroCodec.RegisterValue> Bands);

    private async Task<K13EqReadback> ReadKtMicroEqAsync(FiioDeviceProfile profile, CancellationToken cancellationToken)
    {
        var log = new List<string> { $"KT Micro readback: {profile.DisplayName}, read frames only." };
        var (stream, inputLength, outputLength, candidate) =
            await OpenSelectedHidAsync("KT Micro readback", log, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            EnsureKtMicroDevice(profile, inputLength, outputLength);
            var state = await ReadKtMicroStateAsync(stream, log, cancellationToken).ConfigureAwait(false);
            var bands = Enumerable.Range(0, KtMicroCodec.BandCount)
                .Select(index => KtMicroCodec.DecodeBand(index, state.Bands[index * 2], state.Bands[index * 2 + 1]))
                .ToArray();
            return new K13EqReadback(candidate, profile, 0, null, 0, state.EqOn, bands, log);
        }
    }

    private async Task<K13PresetWriteResult> SaveKtMicroPresetAsync(
        FiioDeviceProfile profile,
        EqPreset preset,
        byte presetId,
        CancellationToken cancellationToken)
    {
        // Everything is checked before the device is opened.
        var planned = KtMicroCodec.PlanBands(preset, profile);
        var log = new List<string> { $"KT Micro save: {profile.DisplayName}. This device has no preamp; none is sent." };
        HidDeviceCandidate candidate;
        var (stream, inputLength, outputLength, opened) =
            await OpenSelectedHidAsync("KT Micro save", log, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            candidate = opened;
            EnsureKtMicroDevice(profile, inputLength, outputLength);
            var before = await ReadKtMicroStateAsync(stream, log, cancellationToken).ConfigureAwait(false);
            if (!before.HasZeroGain)
            {
                throw new InvalidOperationException($"{profile.DisplayName} has an unexpected digital gain. Nothing was written.");
            }

            if (!before.EqOn)
            {
                await WriteKtMicroAsync(stream, KtMicroCodec.EqOnValue(), log, cancellationToken).ConfigureAwait(false);
            }

            foreach (var value in planned)
            {
                await WriteKtMicroAsync(stream, value, log, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                await SendKtMicroAsync(stream, KtMicroCodec.SaveRequest(), log, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception)
            {
                // It drops off USB as it saves. Only the read below decides whether the save worked.
                log.Add($"SAVE ended with: {ex.Message}");
            }
        }

        log.Add("SAVE sent. Waiting for the device to come back.");
        await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
        KtMicroState? after = null;
        for (var attempt = 1; attempt <= 6 && after is null; attempt++)
        {
            try
            {
                var (back, backInput, backOutput, _) =
                    await OpenSelectedHidAsync("KT Micro verify", log, cancellationToken).ConfigureAwait(false);
                await using (back.ConfigureAwait(false))
                {
                    EnsureKtMicroDevice(profile, backInput, backOutput);
                    after = await ReadKtMicroStateAsync(back, log, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is IOException or Win32Exception or InvalidOperationException or TimeoutException)
            {
                log.Add($"Check {attempt}: {ex.Message}");
                await Task.Delay(1500, cancellationToken).ConfigureAwait(false);
            }
        }

        if (after is null)
        {
            throw new K13DspVerificationException(
                $"The save was sent, but {profile.DisplayName} did not come back to confirm it. Unplug it, plug it back in and load it to check.", log);
        }

        if (!after.EqOn || !after.Bands.SequenceEqual(planned))
        {
            throw new K13DspVerificationException("The device did not read back as saved. Save again before relying on this EQ.", log);
        }

        log.Add($"Saved {preset.Name} and verified every band after the device restarted.");
        return new K13PresetWriteResult(candidate, profile, presetId, KtMicroCodec.BandCount, 0, log);
    }

    private async Task<K13PresetSelectResult> SelectKtMicroSlotAsync(FiioDeviceProfile profile, byte presetId, CancellationToken cancellationToken)
    {
        // One slot only, so there is nothing to switch. Confirm with a read.
        var snapshot = await ReadKtMicroEqAsync(profile, cancellationToken).ConfigureAwait(false);
        return new K13PresetSelectResult(snapshot.Candidate, profile, 0, presetId, 0, presetId == 0, snapshot.TransportLog);
    }

    private static void EnsureKtMicroDevice(FiioDeviceProfile profile, int inputLength, int outputLength)
    {
        if (inputLength != KtMicroCodec.ReportSize || outputLength != KtMicroCodec.ReportSize)
        {
            throw new InvalidOperationException(
                $"{profile.DisplayName} has unexpected report lengths ({inputLength}/{outputLength}). Nothing was sent.");
        }
    }

    private static async Task<KtMicroState> ReadKtMicroStateAsync(FileStream stream, List<string> log, CancellationToken cancellationToken)
    {
        async Task<KtMicroCodec.RegisterValue> ReadAsync(byte register)
            => KtMicroCodec.Parse(await QueryKtMicroAsync(
                stream, KtMicroCodec.ReadRequest(register), report => KtMicroCodec.IsReadReply(report, register), 2, log, cancellationToken).ConfigureAwait(false));

        var eq = await ReadAsync(KtMicroCodec.EqRegister).ConfigureAwait(false);
        var isOn = KtMicroCodec.ParseEqOn(eq)
            ?? throw new InvalidOperationException($"The device reports an unknown EQ state ({eq.B0}). Nothing was written.");
        var bands = new List<KtMicroCodec.RegisterValue>();
        for (var index = 0; index < KtMicroCodec.BandCount * 2; index++)
        {
            bands.Add(await ReadAsync((byte)(KtMicroCodec.FirstBandRegister + index)).ConfigureAwait(false));
        }

        var gain = await ReadAsync(KtMicroCodec.DigitalGainRegister).ConfigureAwait(false);
        KtMicroCodec.RegisterValue? flags = null;
        try
        {
            flags = await ReadAsync(KtMicroCodec.FlagsRegister).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            log.Add("The channel flags did not answer.");
        }

        return new(isOn, KtMicroCodec.HasZeroDigitalGain(gain, flags), bands);
    }

    private static async Task WriteKtMicroAsync(FileStream stream, KtMicroCodec.RegisterValue value, List<string> log, CancellationToken cancellationToken)
    {
        // A write is never repeated: a missing acknowledgement stops before SAVE.
        var reply = await QueryKtMicroAsync(
            stream, KtMicroCodec.WriteRequest(value), report => KtMicroCodec.IsWriteReply(report, value.Register), 1, log, cancellationToken).ConfigureAwait(false);
        if (!KtMicroCodec.IsAcknowledged(reply))
        {
            throw new InvalidOperationException($"The device did not accept register 0x{value.Register:X2}. Nothing was saved.");
        }
    }

    private static async Task<byte[]> QueryKtMicroAsync(
        FileStream stream,
        byte[] request,
        Func<byte[], bool> isReply,
        int attempts,
        List<string> log,
        CancellationToken cancellationToken)
    {
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            await DrainStaleReportsAsync(stream, KtMicroCodec.ReportSize, log, cancellationToken).ConfigureAwait(false);
            await SendKtMicroAsync(stream, request, log, cancellationToken).ConfigureAwait(false);
            for (var reads = 0; reads < 8; reads++)
            {
                var report = await ReadReportWithTimeoutAsync(
                    stream, KtMicroCodec.ReportSize, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                if (report is null) break;
                if (isReply(report))
                {
                    log.Add($"RX: {FormatHex(report)}");
                    return report;
                }
            }
        }

        throw new TimeoutException("The device did not answer. Unplug it, plug it back in and try again.");
    }

    private static async Task SendKtMicroAsync(FileStream stream, byte[] frame, List<string> log, CancellationToken cancellationToken)
    {
        if (!KtMicroCodec.IsAllowed(frame))
        {
            throw new InvalidOperationException("WolfEQ blocked a command this device should never receive.");
        }

        log.Add($"TX: {FormatHex(frame)}");
        await stream.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
