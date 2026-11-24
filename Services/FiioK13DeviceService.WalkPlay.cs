using System.IO;
using WolfEQ.Models;

namespace WolfEQ.Services;

public sealed partial class FiioK13DeviceService
{
    /// <summary>WalkPlay DACs never receive a FiiO frame: every FiiO send path checks this first.</summary>
    private void EnsureFiioProtocol()
    {
        if (SelectedProfile.WalkPlay is not null)
        {
            throw new InvalidOperationException($"{SelectedProfile.DisplayName} does not support this control.");
        }
    }

    private async Task<K13EqReadback> ReadWalkPlayEqAsync(FiioDeviceProfile profile, CancellationToken cancellationToken)
    {
        var log = new List<string> { $"WalkPlay readback: {profile.DisplayName}, read frames only." };
        var (stream, inputLength, outputLength, candidate) =
            await OpenSelectedHidAsync("WalkPlay readback", log, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            EnsureWalkPlayDevice(profile, inputLength, outputLength);
            var (slot, preamp, registers) = await ReadWalkPlayStateAsync(stream, profile, log, cancellationToken).ConfigureAwait(false);
            return new K13EqReadback(
                candidate,
                profile,
                slot,
                null,
                preamp,
                true,
                registers.Select(band => WalkPlayCodec.ToBand(band, profile.WalkPlay!)).ToArray(),
                log);
        }
    }

    private async Task<K13PresetWriteResult> SaveWalkPlayPresetAsync(
        FiioDeviceProfile profile,
        EqPreset preset,
        byte presetId,
        CancellationToken cancellationToken)
    {
        var options = profile.WalkPlay!;
        var preamp = profile.NormalizePreamp(preset.PreampDb);
        var bands = WalkPlayCodec.PlanBands(preset, profile);
        var log = new List<string> { $"WalkPlay save: {profile.DisplayName}, {bands.Count} band(s), preamp {preamp:F0} dB." };
        var (stream, inputLength, outputLength, candidate) =
            await OpenSelectedHidAsync("WalkPlay save", log, cancellationToken).ConfigureAwait(false);
        await using (stream.ConfigureAwait(false))
        {
            EnsureWalkPlayDevice(profile, inputLength, outputLength);

            // A full read first proves this is the expected layout before anything is written.
            var (slot, _, _) = await ReadWalkPlayStateAsync(stream, profile, log, cancellationToken).ConfigureAwait(false);
            if (slot != presetId)
            {
                throw new InvalidOperationException($"{profile.DisplayName} reports an unexpected slot ({slot}). Nothing was written.");
            }

            var steps = WalkPlayCodec.WriteSequence(bands, preamp, slot, options);
            foreach (var step in steps)
            {
                await SendWalkPlayAsync(stream, step.Report, log, cancellationToken).ConfigureAwait(false);
                await Task.Delay(step.DelayAfterMs, cancellationToken).ConfigureAwait(false);
            }

            var (slotAfter, preampAfter, after) = await ReadWalkPlayStateAsync(stream, profile, log, cancellationToken).ConfigureAwait(false);
            for (var index = 0; index < bands.Count; index++)
            {
                if (after[index] != WalkPlayCodec.ToRegisters(bands[index], options))
                {
                    throw new InvalidOperationException($"Band {index + 1} did not read back as saved. Save again before relying on this EQ.");
                }
            }

            if (slotAfter != slot || preampAfter != (int)preamp)
            {
                throw new InvalidOperationException($"The preamp reads back {preampAfter} dB instead of {preamp:F0} dB. Save again before relying on this EQ.");
            }

            log.Add($"Saved {preset.Name} and verified every band and the preamp.");
            return new K13PresetWriteResult(candidate, profile, presetId, bands.Count, preamp, log);
        }
    }

    private async Task<K13PresetSelectResult> SelectWalkPlaySlotAsync(
        FiioDeviceProfile profile,
        byte presetId,
        CancellationToken cancellationToken)
    {
        // One slot only, so there is nothing to switch. Confirm with a read.
        var snapshot = await ReadWalkPlayEqAsync(profile, cancellationToken).ConfigureAwait(false);
        return new K13PresetSelectResult(
            snapshot.Candidate, profile, snapshot.PresetId, presetId, snapshot.PresetId, snapshot.PresetId == presetId, snapshot.TransportLog);
    }

    private static void EnsureWalkPlayDevice(FiioDeviceProfile profile, int inputLength, int outputLength)
    {
        if (inputLength != WalkPlayCodec.ReportSize || outputLength != WalkPlayCodec.ReportSize)
        {
            throw new InvalidOperationException(
                $"{profile.DisplayName} has unexpected report lengths ({inputLength}/{outputLength}). Nothing was sent.");
        }
    }

    private static async Task<(byte Slot, int PreampDb, IReadOnlyList<WalkPlayCodec.Registers> Bands)> ReadWalkPlayStateAsync(
        FileStream stream,
        FiioDeviceProfile profile,
        List<string> log,
        CancellationToken cancellationToken)
    {
        var slotReply = await QueryWalkPlayAsync(
            stream, WalkPlayCodec.SlotRequest(), report => WalkPlayCodec.IsReply(report, WalkPlayCodec.CmdPeq), log, cancellationToken).ConfigureAwait(false);
        var bands = new List<WalkPlayCodec.Registers>();
        for (var index = 0; index < profile.BandCount; index++)
        {
            var current = index;
            var reply = await QueryWalkPlayAsync(
                stream, WalkPlayCodec.BandRequest(index), report => WalkPlayCodec.IsBandReply(report, current), log, cancellationToken).ConfigureAwait(false);
            bands.Add(WalkPlayCodec.ParseRegisters(reply));
        }

        var preampReply = await QueryWalkPlayAsync(
            stream, WalkPlayCodec.PreampRequest(), report => WalkPlayCodec.IsReply(report, WalkPlayCodec.CmdGlobalGain), log, cancellationToken).ConfigureAwait(false);
        return (WalkPlayCodec.ParseSlot(slotReply), WalkPlayCodec.ParsePreampDb(preampReply), bands);
    }

    private static async Task<byte[]> QueryWalkPlayAsync(
        FileStream stream,
        byte[] request,
        Func<byte[], bool> isReply,
        List<string> log,
        CancellationToken cancellationToken)
    {
        // A reply can go missing once, so each request is retried after draining stale input.
        for (var attempt = 1; attempt <= 2; attempt++)
        {
            await DrainStaleReportsAsync(stream, WalkPlayCodec.ReportSize, log, cancellationToken).ConfigureAwait(false);
            await SendWalkPlayAsync(stream, request, log, cancellationToken).ConfigureAwait(false);
            // Button presses also arrive here; ignore anything that is not the matching reply.
            for (var reads = 0; reads < 8; reads++)
            {
                var report = await ReadReportWithTimeoutAsync(
                    stream, WalkPlayCodec.ReportSize, TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
                if (report is null)
                {
                    break;
                }

                if (isReply(report))
                {
                    log.Add($"RX: {FormatHex(report)}");
                    return report;
                }
            }
        }

        throw new TimeoutException("The DAC did not answer. Unplug it, plug it back in and try again.");
    }

    private static async Task SendWalkPlayAsync(FileStream stream, byte[] report, List<string> log, CancellationToken cancellationToken)
    {
        log.Add($"TX: {FormatHex(report)}");
        await stream.WriteAsync(report, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}
