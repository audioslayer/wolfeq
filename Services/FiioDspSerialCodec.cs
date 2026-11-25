using WolfEQ.Models;

namespace WolfEQ.Services;

/// <summary>
/// Frames for the DSP in the FiiO K17, K17 R2R and K19, as FiiO's own web control sends them over the rear RS232 USB port
/// (57600 baud): FC FA, length (LE, payload + 4), command, module, address, payload, checksum. A write is acknowledged
/// with a byte 0xBB. Values are indexes into fixed tables. Unrelated to the AA 0A / BB 0B HID protocol. Pure: no I/O.
/// </summary>
public static class FiioDspSerialCodec
{
    public const int BaudRate = 57600;
    public const int BandCount = 31;
    public const byte Ack = 0xBB;
    private const byte Start = 0xFC;
    private const byte Id = 0xFA;
    private const byte Write = 0xA7;
    private const byte Read = 0xA0;
    private const byte SystemModule = 0;
    private const byte EqModule = 1;
    private const byte SaveAddress = 1;
    private const byte PresetAddress = 2;
    private const byte UploadAddress = 16;
    private const byte StateAddress = 48;

    public sealed record BandRecord(int Type, int FrequencyIndex, int GainIndex, int QIndex, int SlopeIndex);

    public static byte[] Frame(byte command, byte module, byte address, params byte[] data)
    {
        var frame = new byte[data.Length + 8];
        frame[0] = Start;
        frame[1] = Id;
        frame[2] = (byte)((data.Length + 4) & 0xFF);
        frame[3] = (byte)((data.Length + 4) >> 8);
        frame[4] = command;
        frame[5] = module;
        frame[6] = address;
        data.CopyTo(frame, 7);
        frame[^1] = Checksum(command, module, address, data);
        return frame;
    }

    private static byte Checksum(byte command, byte module, byte address, byte[] data)
        => (byte)(((Start + command + module + address + data.Sum(value => value)) & 0xFF) ^ Id);

    public static byte[] Connect() => Frame(Write, SystemModule, StateAddress, 17, 0);

    public static byte[] Disconnect() => Frame(Write, SystemModule, StateAddress, 170, 0);

    public static byte[] ReadPreset() => Frame(Read, SystemModule, PresetAddress);

    public static byte[] SelectPreset(byte presetId) => Frame(Write, SystemModule, PresetAddress, presetId, 0);

    public static byte[] ReadBands() => Frame(Read, SystemModule, UploadAddress, EqModule, 0);

    public static byte[] SaveToPreset(byte presetId) => Frame(Write, SystemModule, SaveAddress, presetId, 0);

    public static byte[] WriteBand(int index, BandRecord band)
        => Frame(Write, EqModule, 0, [.. Word(index), .. Word(band.Type), .. Word(band.FrequencyIndex), .. Word(band.GainIndex), .. Word(band.QIndex), .. Word(band.SlopeIndex)]);

    /// <summary>The first complete frame in a receive buffer, or null while bytes are still missing.</summary>
    public static byte[]? TryTakeFrame(ReadOnlySpan<byte> buffer)
    {
        for (var start = 0; start + 4 <= buffer.Length; start++)
        {
            if (buffer[start] != Start || buffer[start + 1] != Id) continue;
            var total = 4 + (buffer[start + 2] | (buffer[start + 3] << 8));
            return total >= 8 && start + total <= buffer.Length ? buffer.Slice(start, total).ToArray() : null;
        }

        return null;
    }

    /// <summary>Null unless this is the device's own reply: an echo of the request is never accepted.</summary>
    public static int? ParsePreset(byte[] frame)
        => frame.Length >= 10 && frame[5] == SystemModule && frame[6] == PresetAddress ? frame[7] | (frame[8] << 8) : null;

    public static IReadOnlyList<BandRecord>? ParseBands(byte[] frame)
    {
        // FC FA len len cmd module address, index, pass, then 31 x 5 words and a checksum.
        const int first = 11;
        if (frame[5] != EqModule || frame.Length < first + BandCount * 10 + 1) return null;
        var bands = new List<BandRecord>();
        for (var offset = first; bands.Count < BandCount; offset += 10)
        {
            int At(int index) => frame[offset + index * 2] | (frame[offset + index * 2 + 1] << 8);
            bands.Add(new(At(0), At(1), At(2), At(3), At(4)));
        }

        return bands;
    }

    /// <summary>All 31 bands: the preset's, then flat fillers. Disabled bands are sent flat.</summary>
    public static IReadOnlyList<BandRecord> PlanBands(EqPreset preset, FiioDeviceProfile profile)
    {
        var source = preset.Bands.OrderBy(band => band.Number).Take(BandCount).ToArray();
        if (preset.Bands.Count > BandCount)
        {
            throw new InvalidOperationException($"{profile.DisplayName} holds {BandCount} bands; this EQ has {preset.Bands.Count}.");
        }

        var planned = new List<BandRecord>();
        for (var index = 0; index < BandCount; index++)
        {
            var band = index < source.Length ? source[index] : null;
            if (band is null || !band.Enabled)
            {
                planned.Add(ToRecord(EqFilterType.Peak, band?.FrequencyHz ?? FlatFrequencies[index], 0, 1));
                continue;
            }

            if (!profile.SupportsFilter(band.FilterType))
            {
                throw new InvalidOperationException(
                    $"Band {index + 1}: {profile.DisplayName} has no {band.FilterType} filter. Change its type, then save again.");
            }

            planned.Add(ToRecord(band.FilterType, band.FrequencyHz, Math.Clamp(band.GainDb, profile.MinGainDb, profile.MaxGainDb), band.Q));
        }

        return planned;
    }

    private static BandRecord ToRecord(EqFilterType type, double frequency, double gain, double q)
    {
        var isPeak = type == EqFilterType.Peak;
        // A shelf's width is its slope; the unused value stays at 1, as FiiO's app sends it.
        return new((int)type, Nearest(Frequencies, frequency), Nearest(Gains, gain), Nearest(Widths, isPeak ? q : 1), Nearest(Widths, isPeak ? 1 : q));
    }

    public static K13EqBandReadback ToBand(int index, BandRecord record)
    {
        var isPeak = record.Type == (int)EqFilterType.Peak;
        return new(
            index + 1,
            Math.Clamp((int)Math.Round(At(Frequencies, record.FrequencyIndex)), 20, 20000),
            At(Gains, record.GainIndex),
            At(Widths, isPeak ? record.QIndex : record.SlopeIndex),
            (byte)(record.Type is >= 0 and <= 2 ? record.Type : 0));
    }

    private static double At(double[] table, int index) => table[Math.Clamp(index, 0, table.Length - 1)];

    private static int Nearest(double[] table, double value)
    {
        var best = 0;
        for (var index = 1; index < table.Length; index++)
        {
            if (Math.Abs(table[index] - value) < Math.Abs(table[best] - value)) best = index;
        }

        return best;
    }

    private static byte[] Word(int value) => [(byte)(value & 0xFF), (byte)((value >> 8) & 0xFF)];

    private static readonly double[] FlatFrequencies =
        [20.1, 24.8, 31.3, 40.1, 49.6, 62.5, 80.3, 101, 125, 161, 202, 250, 315, 397, 500, 630, 794, 1000, 1260, 1590, 2000, 2520, 3170, 4000, 5040, 6350, 8000, 10100, 12500, 16000, 20000];

    // The device's value tables (index = wire value).
    internal static readonly double[] Gains = Enumerable.Range(0, 181).Select(step => Math.Round(-24 + step * 0.2, 1)).ToArray();
    internal static readonly double[] Frequencies = [
        19.7, 20.1, 20.5, 20.9, 21.3, 21.7, 22.1, 22.5, 23, 23.4, 23.9, 24.3, 24.8, 25.3, 25.8, 26.3,
        26.8, 27.3, 27.8, 28.4, 28.9, 29.5, 30.1, 30.7, 31.3, 31.9, 32.5, 33.1, 33.8, 34.4, 35.1, 35.8,
        36.5, 37.2, 37.9, 38.6, 39.4, 40.1, 40.9, 41.7, 42.5, 43.4, 44.2, 45.1, 45.9, 46.8, 47.7, 48.7,
        49.6, 50.6, 51.6, 52.6, 53.6, 54.6, 55.7, 56.8, 57.9, 59, 60.1, 61.3, 62.5, 63.7, 65, 66.2,
        67.5, 68.8, 70.2, 71.5, 72.9, 74.3, 75.8, 77.2, 78.7, 80.3, 81.8, 83.4, 85, 86.7, 88.4, 90.1,
        91.9, 94, 95, 97, 99, 101, 103, 105, 107, 109, 111, 114, 116, 118, 120, 123,
        125, 127, 130, 132, 135, 138, 140, 143, 146, 149, 152, 154, 157, 161, 164, 167,
        170, 173, 177, 180, 184, 187, 191, 195, 198, 202, 206, 210, 214, 218, 223, 227,
        231, 236, 241, 245, 250, 255, 260, 265, 270, 275, 281, 286, 292, 297, 303, 309,
        315, 321, 327, 334, 340, 347, 354, 360, 367, 375, 382, 389, 397, 405, 412, 420,
        429, 437, 445, 454, 463, 472, 481, 490, 500, 510, 520, 530, 540, 551, 561, 572,
        583, 595, 606, 618, 630, 642, 655, 667, 680, 694, 707, 721, 735, 749, 764, 779,
        794, 809, 825, 841, 857, 874, 891, 908, 926, 944, 962, 981, 1000, 1020, 1040, 1060,
        1080, 1100, 1120, 1140, 1170, 1190, 1210, 1240, 1260, 1280, 1310, 1330, 1360, 1390, 1410, 1440,
        1470, 1500, 1530, 1560, 1590, 1620, 1650, 1680, 1710, 1750, 1780, 1820, 1850, 1890, 1920, 1960,
        2000, 2040, 2080, 2120, 2160, 2200, 2240, 2290, 2330, 2380, 2420, 2470, 2520, 2570, 2620, 2670,
        2720, 2770, 2830, 2880, 2940, 3000, 3050, 3110, 3170, 3240, 3300, 3360, 3430, 3500, 3560, 3630,
        3700, 3780, 3850, 3920, 4000, 4080, 4160, 4240, 4320, 4400, 4490, 4580, 4670, 4760, 4850, 4940,
        5040, 5140, 5240, 5340, 5440, 5550, 5660, 5770, 5880, 5990, 6110, 6230, 6350, 6470, 6600, 6730,
        6860, 6990, 7130, 7270, 7410, 7550, 7700, 7850, 8000, 8160, 8310, 8480, 8640, 8810, 8980, 9150,
        9330, 9510, 9700, 9890, 10100, 10300, 10500, 10700, 10900, 11100, 11300, 11500, 11800, 12000, 12200, 12500,
        12700, 12900, 13200, 13500, 13700, 14000, 14300, 14500, 14800, 15100, 15400, 15700, 16000, 16300, 16600, 17000,
        17300, 17600, 18000, 18300, 18700, 19000, 19400, 19800, 20200, 20600
    ];
    internal static readonly double[] Widths = [
        0.4, 0.42, 0.45, 0.47, 0.5, 0.53, 0.56, 0.59, 0.63, 0.67, 0.71, 0.75, 0.79, 0.84, 0.89, 0.94,
        1, 1.06, 1.12, 1.19, 1.26, 1.3, 1.4, 1.5, 1.6, 1.7, 1.8, 1.9, 2, 2.1, 2.2, 2.4,
        2.5, 2.7, 2.8, 3, 3.2, 3.4, 3.6, 3.8, 4, 4.2, 4.5, 4.8, 5, 5.3, 5.7, 6,
        6.3, 6.7, 7.1, 7.6, 8, 8.5, 9, 9.5, 10.1, 10.7, 11.3, 12, 12.7, 13.5, 14, 15,
        16, 17, 18, 19, 20, 21, 23, 24, 25, 27, 29, 30, 32, 34, 36, 38,
        40, 43, 45, 48, 51, 54, 57, 60, 64, 68, 72, 76, 81, 85, 91, 96,
        102, 108, 114, 121, 128
    ];
}
