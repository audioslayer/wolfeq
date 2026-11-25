using WolfEQ.Models;

namespace WolfEQ.Services;

/// <summary>
/// KT Micro register frames (report 0x4B, 11 bytes including the report ID): register, 0 0 0, operation, 0, four value bytes.
/// Layout from devicePEQ (0BSD); registers, limits and the save behaviour follow Contour's hardware measurements.
/// Only reads, the EQ switch, the ten band registers and SAVE can be built or sent. Pure: no I/O.
/// </summary>
public static class KtMicroCodec
{
    public const byte ReportId = 0x4B;
    public const int ReportSize = 11;
    public const int BandCount = 5;
    public const byte FlagsRegister = 0x01;
    public const byte EqRegister = 0x24;
    public const byte FirstBandRegister = 0x26;
    public const byte DigitalGainRegister = 0x66;
    private const byte Read = 0x52;
    private const byte Write = 0x57;
    private const byte Save = 0x53;
    private const byte EqOff = 2;
    private const byte EqOn = 3;
    private const byte WriteAck = 3;
    private const double MaxShelfGainDb = 6;
    private const double MinShelfQ = 0.7;
    private const double MaxShelfQ = 1.0;

    public sealed record RegisterValue(byte Register, byte B0, byte B1, byte B2, byte B3);

    private static byte[] Frame(byte register, byte operation, byte b0 = 0, byte b1 = 0, byte b2 = 0, byte b3 = 0)
        => [ReportId, register, 0, 0, 0, operation, 0, b0, b1, b2, b3];

    public static byte[] ReadRequest(byte register) => Frame(register, Read, register == EqRegister ? (byte)3 : (byte)0);

    public static byte[] WriteRequest(RegisterValue value) => Frame(value.Register, Write, value.B0, value.B1, value.B2, value.B3);

    public static byte[] SaveRequest() => Frame(0, Save);

    /// <summary>The last check before USB: anything but a read, an EQ write or SAVE is refused (0x43 erases the chip's flash).</summary>
    public static bool IsAllowed(byte[] frame)
    {
        if (frame.Length != ReportSize || frame[0] != ReportId || frame[2] != 0 || frame[3] != 0 || frame[4] != 0 || frame[6] != 0) return false;
        return frame[5] switch
        {
            Read => true,
            Write => frame[1] == EqRegister || frame[1] is >= FirstBandRegister and < FirstBandRegister + BandCount * 2,
            Save => frame[1] == 0 && frame.AsSpan(7).IndexOfAnyExcept((byte)0) < 0,
            _ => false
        };
    }

    public static bool IsReadReply(byte[] report, byte register) => IsReply(report, register, Read);

    public static bool IsWriteReply(byte[] report, byte register) => IsReply(report, register, Write);

    private static bool IsReply(byte[] report, byte register, byte operation)
        => report.Length >= ReportSize && report[0] == ReportId && report[1] == register && report[2] == 0 && report[3] == 0
           && report[4] == 0 && report[5] == operation && report[6] == 0;

    public static bool IsAcknowledged(byte[] report) => report[7] == WriteAck && report[8] == 0 && report[9] == 0 && report[10] == 0;

    public static RegisterValue Parse(byte[] report) => new(report[1], report[7], report[8], report[9], report[10]);

    public static bool? ParseEqOn(RegisterValue value) => value.B0 switch { EqOn => true, EqOff => false, _ => null };

    public static RegisterValue EqOnValue() => new(EqRegister, EqOn, 0, 0, 0);

    /// <summary>A digital gain other than zero would change the level of every EQ, so it blocks a save. Never written.</summary>
    public static bool HasZeroDigitalGain(RegisterValue gain, RegisterValue? flags)
        => gain.B0 == 0 && (gain.B1 == 0 || (flags is not null && (flags.B1 & 2) != 0));

    public static IReadOnlyList<RegisterValue> PlanBands(EqPreset preset, FiioDeviceProfile profile)
    {
        var source = preset.Bands.OrderBy(band => band.Number).ToArray();
        if (source.Skip(BandCount).Any(band => band.Enabled && Math.Abs(band.GainDb) >= 0.05))
        {
            throw new InvalidOperationException(
                $"{profile.DisplayName} has {BandCount} bands and this EQ uses more. Turn off or flatten the extra bands, then save again.");
        }

        var registers = new List<RegisterValue>();
        for (var index = 0; index < BandCount; index++)
        {
            var band = index < source.Length && source[index].Enabled ? source[index] : null;
            if (band is null)
            {
                registers.AddRange(Encode(index, 0, 100, 1000, 0));
                continue;
            }

            byte type = band.FilterType switch
            {
                EqFilterType.Peak => 0,
                EqFilterType.LowShelf => 3,
                EqFilterType.HighShelf => 4,
                _ => throw new InvalidOperationException(
                    $"Band {index + 1}: {profile.DisplayName} has no {band.FilterType} filter. Change its type, then save again.")
            };
            var gain = double.IsFinite(band.GainDb) ? band.GainDb : 0;
            var q = double.IsFinite(band.Q) ? band.Q : 1;
            if (type != 0 && (Math.Abs(gain) > MaxShelfGainDb + 0.001 || q is < MinShelfQ - 0.001 or > MaxShelfQ + 0.001))
            {
                throw new InvalidOperationException(
                    $"Band {index + 1}: {profile.DisplayName} plays shelves correctly only up to 6 dB with Q 0.7 to 1. Adjust it, then save again.");
            }

            gain = Math.Clamp(gain, profile.MinGainDb, profile.MaxGainDb);
            q = Math.Clamp(q, profile.MinQ, profile.MaxQ);
            registers.AddRange(Encode(
                index,
                (int)Math.Floor(gain * 10 + 1e-6),
                // The DSP designs at half the 48 kHz stream rate, so the register holds half the frequency.
                (int)Math.Floor(Math.Clamp(band.FrequencyHz, 20, 20000) / 2.0 + 0.5),
                (int)Math.Floor(q * 1000 + 0.5),
                type));
        }

        return registers;
    }

    private static RegisterValue[] Encode(int index, int gainTenths, int frequency, int qThousandths, byte type)
    {
        var register = (byte)(FirstBandRegister + index * 2);
        return
        [
            new(register, (byte)gainTenths, (byte)(gainTenths >> 8), (byte)frequency, (byte)(frequency >> 8)),
            new((byte)(register + 1), (byte)qThousandths, (byte)(qThousandths >> 8), type, 0)
        ];
    }

    public static K13EqBandReadback DecodeBand(int index, RegisterValue gainFrequency, RegisterValue qType)
    {
        var gain = (short)(gainFrequency.B0 | (gainFrequency.B1 << 8)) / 10.0;
        var frequency = (gainFrequency.B2 | (gainFrequency.B3 << 8)) * 2;
        var q = (qType.B0 | (qType.B1 << 8)) / 1000.0;
        var type = qType.B2 switch
        {
            0 => EqFilterType.Peak,
            3 => EqFilterType.LowShelf,
            4 => EqFilterType.HighShelf,
            _ => throw new InvalidOperationException($"Band {index + 1} has an unknown filter type ({qType.B2}).")
        };
        return new(index + 1, Math.Clamp(frequency, 20, 20000), gain, Math.Round(q, 2), (byte)type);
    }
}
