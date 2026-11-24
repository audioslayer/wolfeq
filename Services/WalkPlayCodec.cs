using WolfEQ.Models;

namespace WolfEQ.Services;

/// <summary>
/// WalkPlay HID frames (report 0x4B, 64 bytes including the report ID). The wire format and biquad layout follow
/// devicePEQ (0BSD); write sequences, shelf Q scale and limits follow Contour's hardware measurements (Apache-2.0).
/// Offsets below index the full report, so byte 0 is the report ID. Pure: no I/O.
/// </summary>
public static class WalkPlayCodec
{
    public const byte ReportId = 0x4B;
    public const int ReportSize = 64;
    public const byte CmdGlobalGain = 0x03;
    public const byte CmdPeq = 0x09;
    private const double Q30 = 1073741824.0;
    private const double DesignRateHz = 96000.0;
    // Register 182/256 is the factory Q on every measured unit.
    private const double FlatQ = 0.7109375;
    private static readonly int[] FlatFrequencies = [31, 62, 125, 250, 500, 1000, 2000, 4000, 8000, 16000];

    public sealed record Registers(int Index, int FrequencyHz, int Q256, int Gain256, byte TypeCode);

    public sealed record Step(byte[] Report, int DelayAfterMs);

    public static byte[] Report(params byte[] payload)
    {
        if (payload.Length > ReportSize - 1)
        {
            throw new ArgumentException("WalkPlay payload is too long.", nameof(payload));
        }

        var report = new byte[ReportSize];
        report[0] = ReportId;
        payload.CopyTo(report, 1);
        return report;
    }

    public static byte[] SlotRequest() => Report(0x80, CmdPeq, 0x00);

    public static byte[] BandRequest(int index) => Report(0x80, CmdPeq, 0x00, 0x00, (byte)index, 0x00);

    public static byte[] PreampRequest() => Report(0x80, CmdGlobalGain, 0x00);

    public static bool IsReply(byte[] report, byte command)
        => report.Length >= 37 && report[0] == ReportId && report[1] == 0x80 && report[2] == command;

    public static bool IsBandReply(byte[] report, int index)
        => IsReply(report, CmdPeq) && report[5] == index;

    public static byte ParseSlot(byte[] report) => report[36];

    public static int ParsePreampDb(byte[] report) => unchecked((sbyte)report[5]);

    public static Registers ParseRegisters(byte[] report)
        => new(
            report[5],
            ReadUInt16(report, 28),
            ReadUInt16(report, 30),
            unchecked((short)ReadUInt16(report, 32)),
            report[34]);

    public static K13EqBandReadback ToBand(Registers registers, WalkPlayOptions options)
    {
        var type = FromTypeCode(registers.TypeCode);
        // Never-written bands read as zeros or 0xFF. They do nothing, so show them as a flat peak.
        if (type is null || registers.FrequencyHz is 0 or 0xFFFF || registers.Q256 is 0 or 0xFFFF)
        {
            return new(registers.Index + 1, FlatFrequency(registers.Index), 0, 0.71, (byte)EqFilterType.Peak);
        }

        return new(
            registers.Index + 1,
            registers.FrequencyHz,
            Math.Round(registers.Gain256 / 256.0, 1),
            Math.Round(registers.Q256 / 256.0 / QScale(type.Value, options), 2),
            (byte)type.Value);
    }

    /// <summary>All bands the device holds: the preset's, then factory-flat fillers. Disabled bands become flat peaks.</summary>
    public static IReadOnlyList<K13EqBandReadback> PlanBands(EqPreset preset, FiioDeviceProfile profile)
    {
        var source = preset.Bands.OrderBy(band => band.Number).Take(profile.BandCount).ToArray();
        var planned = new List<K13EqBandReadback>();
        for (var index = 0; index < profile.BandCount; index++)
        {
            var band = index < source.Length ? source[index] : null;
            if (band is null || !band.Enabled)
            {
                planned.Add(new(index + 1, FlatFrequency(index), 0, FlatQ, (byte)EqFilterType.Peak));
                continue;
            }

            if (!profile.SupportsFilter(band.FilterType))
            {
                throw new InvalidOperationException(
                    $"Band {index + 1}: {profile.DisplayName} has no {band.FilterType} filter. Change its type, then save again.");
            }

            var isPass = band.FilterType is EqFilterType.LowPass or EqFilterType.HighPass;
            // The Q register tops out at 10, and a shelf may store Q x sqrt 2.
            var maxQ = profile.MaxQ / QScale(band.FilterType, profile.WalkPlay!);
            planned.Add(new(
                index + 1,
                Math.Clamp(band.FrequencyHz, 20, 20000),
                isPass ? 0 : Math.Clamp(Math.Round(double.IsFinite(band.GainDb) ? band.GainDb : 0, 1), profile.MinGainDb, profile.MaxGainDb),
                Math.Clamp(Math.Round(double.IsFinite(band.Q) ? band.Q : 1, 2), profile.MinQ, maxQ),
                (byte)band.FilterType));
        }

        return planned;
    }

    public static Registers ToRegisters(K13EqBandReadback band, WalkPlayOptions options)
    {
        var type = (EqFilterType)band.FilterType;
        return new(
            band.Number - 1,
            band.FrequencyHz,
            (int)Math.Floor(band.Q * QScale(type, options) * 256 + 0.5),
            (int)Math.Floor(band.GainDb * 256 + 0.5),
            ToTypeCode(type));
    }

    public static byte[] BandWrite(K13EqBandReadback band, byte slot, WalkPlayOptions options)
    {
        var registers = ToRegisters(band, options);
        var payload = new byte[37];
        payload[0] = 0x01;
        payload[1] = CmdPeq;
        payload[2] = options.BandLength;
        payload[4] = (byte)registers.Index;
        EncodeBiquad(band, options).CopyTo(payload, 7);
        WriteUInt16(payload, 27, registers.FrequencyHz);
        WriteUInt16(payload, 29, registers.Q256);
        WriteUInt16(payload, 31, registers.Gain256);
        payload[33] = registers.TypeCode;
        payload[35] = slot;
        return Report(payload);
    }

    public static byte[] PreampWrite(double preampDb)
        => Report(0x01, CmdGlobalGain, 0x02, 0x00, unchecked((byte)(sbyte)Math.Floor(preampDb + 1e-6)));

    /// <summary>Builds every frame up front, so an unsupported band fails before anything is sent.</summary>
    public static IReadOnlyList<Step> WriteSequence(
        IReadOnlyList<K13EqBandReadback> bands,
        double preampDb,
        byte slot,
        WalkPlayOptions options)
    {
        var steps = new List<Step>();
        var tempWrite = Report(0x01, 0x0A, 0x04, 0x00, 0x00, 0xFF, 0xFF);
        for (var index = 0; index < bands.Count; index++)
        {
            var delay = options.TrnSequence ? 25 : index == bands.Count - 1 ? 120 : 20;
            steps.Add(new(BandWrite(bands[index], slot, options), delay));
        }

        if (options.TrnSequence)
        {
            steps.Add(new(tempWrite, 50));
            steps.Add(new(PreampWrite(preampDb), 50));
            steps.Add(new(Report(0x01, 0x01, 0x00), 200));
            steps.Add(new(Report(0x01, 0x04, 0x00), 200));
        }
        else
        {
            steps.Add(new(PreampWrite(preampDb), 50));
            steps.Add(new(Report(0x01, 0x05, 0x00), 20));
            steps.Add(new(Report(0x01, 0x17, 0x00), 20));
            steps.Add(new(tempWrite, 50));
            steps.Add(new(Report(0x01, 0x01, 0x01, 0x00), 200));
        }

        return steps;
    }

    private static byte[] EncodeBiquad(K13EqBandReadback band, WalkPlayOptions options)
    {
        var type = (EqFilterType)band.FilterType;
        var a = Math.Pow(10, band.GainDb / 40);
        var w = 2 * Math.PI * band.FrequencyHz / DesignRateHz;
        var c = Math.Cos(w);
        var alpha = Math.Sin(w) / (2 * band.Q);
        var k = 2 * Math.Sqrt(a) * alpha;
        double b0, b1, b2, a0, a1, a2;
        switch (type)
        {
            case EqFilterType.Peak:
                (b0, b1, b2) = (1 + alpha * a, -2 * c, 1 - alpha * a);
                (a0, a1, a2) = (1 + alpha / a, -2 * c, 1 - alpha / a);
                break;
            case EqFilterType.LowShelf:
                (b0, b1, b2) = (a * (a + 1 - (a - 1) * c + k), 2 * a * (a - 1 - (a + 1) * c), a * (a + 1 - (a - 1) * c - k));
                (a0, a1, a2) = (a + 1 + (a - 1) * c + k, -2 * (a - 1 + (a + 1) * c), a + 1 + (a - 1) * c - k);
                break;
            case EqFilterType.HighShelf:
                (b0, b1, b2) = (a * (a + 1 + (a - 1) * c + k), -2 * a * (a - 1 + (a + 1) * c), a * (a + 1 + (a - 1) * c - k));
                (a0, a1, a2) = (a + 1 - (a - 1) * c + k, 2 * (a - 1 - (a + 1) * c), a + 1 - (a - 1) * c - k);
                break;
            case EqFilterType.LowPass:
                (b0, b1, b2) = ((1 - c) / 2, 1 - c, (1 - c) / 2);
                (a0, a1, a2) = (1 + alpha, -2 * c, 1 - alpha);
                break;
            case EqFilterType.HighPass:
                (b0, b1, b2) = ((1 + c) / 2, -(1 + c), (1 + c) / 2);
                (a0, a1, a2) = (1 + alpha, -2 * c, 1 - alpha);
                break;
            default:
                throw new InvalidOperationException($"WalkPlay devices have no {type} filter.");
        }

        var bytes = new byte[20];
        double[] values = [b0 / a0, b1 / a0, b2 / a0, -a1 / a0, -a2 / a0];
        for (var index = 0; index < values.Length; index++)
        {
            var scaled = Math.Round(values[index] * Q30);
            if (!double.IsFinite(scaled) || (!options.WrapCoefficients && scaled is < int.MinValue or > int.MaxValue))
            {
                throw new InvalidOperationException(
                    $"Band {band.Number} boosts more than this DAC can store. Lower its gain or frequency, then save again.");
            }

            BitConverter.TryWriteBytes(bytes.AsSpan(index * 4), unchecked((int)(long)scaled));
        }

        return bytes;
    }

    private static double QScale(EqFilterType type, WalkPlayOptions options)
        => type is EqFilterType.LowShelf or EqFilterType.HighShelf ? options.ShelfQScale : 1.0;

    private static int FlatFrequency(int index) => FlatFrequencies[Math.Clamp(index, 0, FlatFrequencies.Length - 1)];

    private static byte ToTypeCode(EqFilterType type) => type switch
    {
        EqFilterType.LowShelf => 1,
        EqFilterType.Peak => 2,
        EqFilterType.HighShelf => 3,
        EqFilterType.LowPass => 4,
        EqFilterType.HighPass => 5,
        _ => throw new InvalidOperationException($"WalkPlay devices have no {type} filter.")
    };

    private static EqFilterType? FromTypeCode(byte code) => code switch
    {
        1 => EqFilterType.LowShelf,
        2 => EqFilterType.Peak,
        3 => EqFilterType.HighShelf,
        4 => EqFilterType.LowPass,
        5 => EqFilterType.HighPass,
        _ => null
    };

    private static int ReadUInt16(byte[] report, int offset) => report[offset] | (report[offset + 1] << 8);

    private static void WriteUInt16(byte[] payload, int offset, int value)
    {
        payload[offset] = (byte)(value & 0xFF);
        payload[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
