using System.Buffers.Binary;
using System.Security.Cryptography;

namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// A 16-bit PCM WAV turned into FLAC: the same samples, on the same channels, in about a quarter
/// of the bytes. What goes up to the provider instead of the WAV.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it exists.</b> Deepgram ends a prerecorded request at ten minutes, and the upload counts:
/// a 1h42m recording is 395 MB of WAV, which takes about ten minutes to go up over one connection at
/// 650 KB/s and came back <c>504</c>. The same recording as FLAC is 101.5 MB, which puts five hours
/// of that meeting at about 300 MB and 7.7 minutes of upload (ISC-231). The order search and the
/// partitioned residuals are what buy that margin: a single order-2 predictor costs up to a tenth
/// more. Encoding the 1h42m file takes seven seconds.
/// </para>
/// <para>
/// <b>Lossless, so there is no question of what compression costs the transcript.</b> Deepgram
/// decodes exactly the samples it decoded from the WAV. A lossy codec was rejected for what it
/// does at these rates: Windows' AAC encoder goes no higher than 32 kbps for 16 kHz stereo, which is
/// where lossy audio measurably raises an ASR model's word error rate, and single-stream stereo Opus
/// leaked the loopback into the microphone channel at −6 dB, which would put other people's words
/// on the channel that settles who the user is.
/// </para>
/// <para>
/// <b>Written here rather than taken from Windows.</b> Media Foundation's FLAC encoder resamples
/// everything to 44.1 kHz, which makes the file bigger and stops it being the recorded samples.
/// This is the subset of FLAC that compresses speech well enough: fixed predictors of order zero to
/// four, partitioned Rice residuals, every channel coded on its own. The stereo decorrelation FLAC
/// also offers is left out: it is lossless as well, but it is more code for a few percent, and
/// keeping each channel's bits apart makes ISC-232 something a reader can check by eye.
/// </para>
/// <para>
/// Only 16-bit integer PCM is taken, which is what every recording of this application is and what
/// a mix down writes. A single track imported untouched in some other width is sent as the WAV it
/// is, exactly as everything was before this existed.
/// </para>
/// </remarks>
public static class FlacEncoder
{
    /// <summary>The media type a request carrying what this writes is sent under.</summary>
    public const string MediaType = "audio/flac";

    /// <summary>The only width this encodes.</summary>
    private const int BitsPerSample = 16;

    /// <summary>
    /// Samples per channel in one frame: 256 ms at 16 kHz, and one of the sizes FLAC's frame header
    /// spells in four bits.
    /// </summary>
    private const int BlockSize = 4096;

    /// <summary>The highest order of the fixed predictors FLAC defines.</summary>
    private const int MostOrder = 4;

    /// <summary>
    /// How finely a residual may be cut into Rice partitions: 64 of 64 samples at the most.
    /// </summary>
    private const int MostPartitionOrder = 6;

    /// <summary>The largest Rice parameter a four-bit field holds, fifteen being the escape.</summary>
    private const int MostRiceParameter = 14;

    private const int WaveFormatPcm = 1;

    private const int WaveFormatExtensible = 0xFFFE;

    /// <summary>
    /// Writes <paramref name="wav"/> into <paramref name="into"/> as FLAC, or writes nothing and
    /// answers false when it is not a 16-bit PCM WAV this can encode.
    /// </summary>
    /// <param name="wav">Read from where it stands; it is the corpus's audio and is only read.</param>
    /// <param name="into">
    /// Must be seekable: how many samples there were, and their MD5, are only known at the end and
    /// go in the header at the start.
    /// </param>
    /// <param name="stopping">Checked between frames.</param>
    /// <exception cref="IOException">Reading the WAV or writing the FLAC failed.</exception>
    public static bool TryEncode(Stream wav, Stream into, CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(wav);
        ArgumentNullException.ThrowIfNull(into);

        if (Pcm16(wav) is not { } format)
        {
            return false;
        }

        into.Write("fLaC"u8);
        var streamInfoAt = into.Position;
        into.Write(StreamInfo(format, totalSamples: 0, md5: new byte[16]));

        var frameBytes = format.Channels * 2;
        var raw = new byte[BlockSize * frameBytes];
        var channels = new int[format.Channels][];
        for (var channel = 0; channel < channels.Length; channel++)
        {
            channels[channel] = new int[BlockSize];
        }

        using var md5 = IncrementalHash.CreateHash(HashAlgorithmName.MD5);
        var frame = new BitWriter();
        long left = format.DataBytes / frameBytes;
        long total = 0;
        long number = 0;

        while (left > 0)
        {
            stopping.ThrowIfCancellationRequested();

            var wanted = (int)Math.Min(BlockSize, left) * frameBytes;
            var read = ReadUpTo(wav, raw.AsSpan(0, wanted));
            var samples = read / frameBytes;
            if (samples == 0)
            {
                // A data chunk that declares more than the file holds ends where the file does,
                // and only on a whole frame: half a frame is not a sample of either channel.
                break;
            }

            var bytes = raw.AsSpan(0, samples * frameBytes);
            md5.AppendData(bytes);

            for (var at = 0; at < samples; at++)
            {
                for (var channel = 0; channel < channels.Length; channel++)
                {
                    channels[channel][at] = BinaryPrimitives.ReadInt16LittleEndian(
                        bytes[(((at * channels.Length) + channel) * 2)..]);
                }
            }

            WriteFrame(frame, channels, samples, format.SampleRate, number, into);
            number++;
            total += samples;
            left -= samples;

            if (samples * frameBytes < wanted)
            {
                break;
            }
        }

        var end = into.Position;
        into.Position = streamInfoAt;
        into.Write(StreamInfo(format, total, md5.GetHashAndReset()));
        into.Position = end;
        into.Flush();
        return true;
    }

    /// <summary>What a WAV's own chunks say, read as far as the first sample.</summary>
    private sealed record WavFormat(int SampleRate, int Channels, long DataBytes);

    /// <summary>
    /// Reads the chunks up to <c>data</c>, leaving <paramref name="wav"/> at the first sample, and
    /// answers what they say when that is 16-bit integer PCM of one to eight channels.
    /// </summary>
    /// <remarks>
    /// The chunks are walked rather than the header assumed to be 44 bytes: this application's own
    /// recordings carry an 18-byte <c>fmt </c> chunk, so their audio starts at byte 46, and reading
    /// from 44 shifts every sample by one.
    /// </remarks>
    private static WavFormat? Pcm16(Stream wav)
    {
        Span<byte> header = stackalloc byte[12];
        if (ReadUpTo(wav, header) < 12 || !header[..4].SequenceEqual("RIFF"u8) || !header[8..].SequenceEqual("WAVE"u8))
        {
            return null;
        }

        Span<byte> chunk = stackalloc byte[8];
        WavFormat? format = null;

        while (ReadUpTo(wav, chunk) == 8)
        {
            var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);

            if (chunk[..4].SequenceEqual("data"u8))
            {
                return format is null ? null : format with { DataBytes = size };
            }

            // A fmt chunk is 16, 18 or 40 bytes; one claiming more is not a WAV this reads, and
            // allocating what it claims would be the failure.
            if (chunk[..4].SequenceEqual("fmt "u8) && size is >= 16 and <= 64)
            {
                var fmt = new byte[size];
                if (ReadUpTo(wav, fmt) < size)
                {
                    return null;
                }

                var tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt);
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(2));
                var rate = BinaryPrimitives.ReadInt32LittleEndian(fmt.AsSpan(4));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(14));

                // WAVE_FORMAT_EXTENSIBLE carries the real format as the first two bytes of the
                // sub-format GUID, at offset 24.
                if (tag == WaveFormatExtensible && size >= 26)
                {
                    tag = BinaryPrimitives.ReadUInt16LittleEndian(fmt.AsSpan(24));
                }

                format = tag == WaveFormatPcm && bits == BitsPerSample && channels is >= 1 and <= 8 && rate is > 0 and < (1 << 20)
                    ? new WavFormat(rate, channels, 0)
                    : null;

                if (format is null)
                {
                    return null;
                }

                SkipPad(wav, size);
                continue;
            }

            wav.Seek(size + (size & 1), SeekOrigin.Current);
        }

        return null;
    }

    private static void SkipPad(Stream wav, uint size)
    {
        if ((size & 1) == 1)
        {
            wav.Seek(1, SeekOrigin.Current);
        }
    }

    private static int ReadUpTo(Stream from, Span<byte> into)
    {
        var read = 0;
        while (read < into.Length)
        {
            var got = from.Read(into[read..]);
            if (got == 0)
            {
                break;
            }

            read += got;
        }

        return read;
    }

    /// <summary>The STREAMINFO block, with its block header, marked as the last metadata block.</summary>
    private static byte[] StreamInfo(WavFormat format, long totalSamples, byte[] md5)
    {
        var bits = new BitWriter();
        bits.Write(1, 1);
        bits.Write(0, 7);
        bits.Write(34, 24);
        bits.Write(BlockSize, 16);
        bits.Write(BlockSize, 16);
        bits.Write(0, 24);
        bits.Write(0, 24);
        bits.Write((ulong)format.SampleRate, 20);
        bits.Write((ulong)(format.Channels - 1), 3);
        bits.Write(BitsPerSample - 1, 5);
        bits.Write((ulong)totalSamples, 36);
        foreach (var b in md5)
        {
            bits.Write(b, 8);
        }

        return bits.ToArray();
    }

    /// <summary>
    /// The frame header's four-bit rate code, and the value that follows the header when the code
    /// says one does.
    /// </summary>
    /// <remarks>
    /// Spelled in every frame rather than left as <c>0000</c>, "read it from STREAMINFO": that code
    /// is outside FLAC's streamable subset (RFC 9639 §7), which is the part of the format every
    /// decoder is bound to take, and what decodes the file at Deepgram is not ours to test.
    /// </remarks>
    private static (ulong Code, ulong Tail, int TailBits) RateCode(int rate) => rate switch
    {
        8_000 => (0b0100, 0, 0),
        16_000 => (0b0101, 0, 0),
        22_050 => (0b0110, 0, 0),
        24_000 => (0b0111, 0, 0),
        32_000 => (0b1000, 0, 0),
        44_100 => (0b1001, 0, 0),
        48_000 => (0b1010, 0, 0),
        96_000 => (0b1011, 0, 0),
        _ when rate % 1000 == 0 && rate / 1000 <= 255 => (0b1100, (ulong)(rate / 1000), 8),
        _ when rate <= 65_535 => (0b1101, (ulong)rate, 16),
        _ when rate % 10 == 0 && rate / 10 <= 65_535 => (0b1110, (ulong)(rate / 10), 16),
        _ => (0b0000, 0, 0),
    };

    private static void WriteFrame(
        BitWriter frame, int[][] channels, int samples, int sampleRate, long number, Stream into)
    {
        frame.Clear();

        frame.Write(0b11111111111110, 14);
        frame.Write(0, 1);
        frame.Write(0, 1);

        // 1100 is 4096 samples; 0111 says the size follows the frame number as sixteen bits,
        // which is what the last, shorter frame needs.
        frame.Write(samples == BlockSize ? 0b1100UL : 0b0111UL, 4);
        var (rateCode, rateTail, rateTailBits) = RateCode(sampleRate);
        frame.Write(rateCode, 4);
        frame.Write((ulong)(channels.Length - 1), 4);
        frame.Write(0b100, 3);
        frame.Write(0, 1);
        WriteUtf8(frame, number);
        if (samples != BlockSize)
        {
            frame.Write((ulong)(samples - 1), 16);
        }

        frame.Write(rateTail, rateTailBits);

        frame.Write(Crc8(frame.Bytes), 8);

        foreach (var channel in channels)
        {
            WriteSubframe(frame, channel.AsSpan(0, samples));
        }

        frame.PadToByte();
        frame.Write(Crc16(frame.Bytes), 16);
        into.Write(frame.Bytes);
    }

    /// <summary>A frame number in FLAC's UTF-8-like coding.</summary>
    private static void WriteUtf8(BitWriter bits, long value)
    {
        if (value < 0x80)
        {
            bits.Write((ulong)value, 8);
            return;
        }

        var continuation = value switch
        {
            < 0x800 => 1,
            < 0x10000 => 2,
            < 0x200000 => 3,
            < 0x4000000 => 4,
            < 0x80000000 => 5,
            _ => 6,
        };

        var lead = (0xFF00 >> (continuation + 1)) & 0xFF;
        bits.Write((ulong)(lead | (int)(value >> (6 * continuation))), 8);
        for (var shift = 6 * (continuation - 1); shift >= 0; shift -= 6)
        {
            bits.Write((ulong)(0x80 | ((value >> shift) & 0x3F)), 8);
        }
    }

    private static void WriteSubframe(BitWriter bits, ReadOnlySpan<int> samples)
    {
        if (!samples.ContainsAnyExcept(samples[0]))
        {
            bits.Write(0, 1);
            bits.Write(0b000000, 6);
            bits.Write(0, 1);
            bits.Write((ulong)(samples[0] & 0xFFFF), BitsPerSample);
            return;
        }

        var best = Plan.Verbatim(samples.Length);
        var residual = new long[samples.Length];

        for (var order = 0; order <= MostOrder && order < samples.Length; order++)
        {
            Residual(samples, order, residual);
            var plan = Partitioned(residual.AsSpan(0, samples.Length), order);
            if (plan.Bits < best.Bits)
            {
                best = plan;
            }
        }

        bits.Write(0, 1);
        if (best.Order < 0)
        {
            bits.Write(0b000001, 6);
            bits.Write(0, 1);
            foreach (var sample in samples)
            {
                bits.Write((ulong)(sample & 0xFFFF), BitsPerSample);
            }

            return;
        }

        bits.Write((ulong)(0b001000 | best.Order), 6);
        bits.Write(0, 1);
        for (var at = 0; at < best.Order; at++)
        {
            bits.Write((ulong)(samples[at] & 0xFFFF), BitsPerSample);
        }

        Residual(samples, best.Order, residual);
        bits.Write(0b00, 2);
        bits.Write((ulong)best.PartitionOrder, 4);

        var partitions = 1 << best.PartitionOrder;
        var per = samples.Length >> best.PartitionOrder;
        var at2 = best.Order;
        for (var partition = 0; partition < partitions; partition++)
        {
            var k = best.Parameters[partition];
            var end = (partition + 1) * per;
            bits.Write((ulong)k, 4);
            for (; at2 < end; at2++)
            {
                bits.WriteRice(ZigZag(residual[at2]), k);
            }
        }
    }

    /// <summary>
    /// What a subframe will cost one way: the predictor order (−1 is verbatim), how its residual is
    /// partitioned, each partition's Rice parameter, and the bits all of that comes to.
    /// </summary>
    private sealed record Plan(int Order, int PartitionOrder, int[] Parameters, long Bits)
    {
        public static Plan Verbatim(int samples) => new(-1, 0, [], 8 + ((long)samples * BitsPerSample));
    }

    /// <summary>
    /// The fixed predictor's residual of order <paramref name="order"/>, from sample
    /// <paramref name="order"/> on; the samples before it are the warm-up and carry none.
    /// </summary>
    private static void Residual(ReadOnlySpan<int> x, int order, long[] into)
    {
        for (var i = order; i < x.Length; i++)
        {
            into[i] = order switch
            {
                0 => x[i],
                1 => (long)x[i] - x[i - 1],
                2 => (long)x[i] - (2L * x[i - 1]) + x[i - 2],
                3 => (long)x[i] - (3L * x[i - 1]) + (3L * x[i - 2]) - x[i - 3],
                _ => (long)x[i] - (4L * x[i - 1]) + (6L * x[i - 2]) - (4L * x[i - 3]) + x[i - 4],
            };
        }
    }

    /// <summary>
    /// The cheapest way to partition one order's residual, estimated from each partition's sum.
    /// </summary>
    /// <remarks>
    /// The estimate takes a partition of <c>n</c> values summing to <c>s</c> as costing
    /// <c>n·(k+1) + s/2^k</c> bits under parameter <c>k</c>. Each value's quotient is floored on its
    /// own when written, so the estimate overcounts by less than one bit a value. Choosing on it
    /// rather than counting exactly is what keeps 1h42m at seven seconds.
    /// </remarks>
    private static Plan Partitioned(ReadOnlySpan<long> residual, int order)
    {
        var n = residual.Length;
        var most = 0;
        while (most < MostPartitionOrder && n % (1 << (most + 1)) == 0 && (n >> (most + 1)) > order)
        {
            most++;
        }

        var finest = 1 << most;
        var sums = new ulong[finest];
        var per = n >> most;
        for (var i = order; i < n; i++)
        {
            sums[i / per] += ZigZag(residual[i]);
        }

        Plan? best = null;
        for (var partitionOrder = most; partitionOrder >= 0; partitionOrder--)
        {
            var partitions = 1 << partitionOrder;
            var parameters = new int[partitions];
            long bits = 8 + (BitsPerSample * order) + 2 + 4;
            var size = n >> partitionOrder;

            for (var partition = 0; partition < partitions; partition++)
            {
                var count = size - (partition == 0 ? order : 0);
                var (k, cost) = Rice(sums[partition], count);
                parameters[partition] = k;
                bits += 4 + cost;
            }

            if (best is null || bits < best.Bits)
            {
                best = new Plan(order, partitionOrder, parameters, bits);
            }

            // The next coarser order merges neighbouring partitions.
            for (var partition = 0; partition < partitions / 2; partition++)
            {
                sums[partition] = sums[2 * partition] + sums[(2 * partition) + 1];
            }
        }

        return best!;
    }

    private static (int K, long Bits) Rice(ulong sum, int count)
    {
        var best = (K: 0, Bits: long.MaxValue);
        for (var k = 0; k <= MostRiceParameter; k++)
        {
            var bits = ((long)count * (k + 1)) + (long)(sum >> k);
            if (bits < best.Bits)
            {
                best = (k, bits);
            }
        }

        return best;
    }

    private static ulong ZigZag(long value) => (ulong)((value << 1) ^ (value >> 63));

    private static byte Crc8(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (var b in data)
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (byte)((crc & 0x80) != 0 ? (crc << 1) ^ 0x07 : crc << 1);
            }
        }

        return crc;
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort crc = 0;
        foreach (var b in data)
        {
            crc ^= (ushort)(b << 8);
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (ushort)((crc & 0x8000) != 0 ? (crc << 1) ^ 0x8005 : crc << 1);
            }
        }

        return crc;
    }

    /// <summary>Bits written most significant first, the order FLAC reads them in.</summary>
    private sealed class BitWriter
    {
        private byte[] buffer = new byte[1 << 16];
        private int length;
        private ulong pending;
        private int pendingBits;

        /// <summary>Every whole byte written so far.</summary>
        public ReadOnlySpan<byte> Bytes => buffer.AsSpan(0, length);

        public void Clear()
        {
            length = 0;
            pending = 0;
            pendingBits = 0;
        }

        public void Write(ulong value, int count)
        {
            for (var left = count; left > 0;)
            {
                var take = Math.Min(left, 32);
                left -= take;
                var part = (value >> left) & ((1UL << take) - 1);
                pending = (pending << take) | part;
                pendingBits += take;
                while (pendingBits >= 8)
                {
                    pendingBits -= 8;
                    Put((byte)(pending >> pendingBits));
                }
            }
        }

        /// <summary>
        /// <paramref name="value"/>'s quotient by 2^<paramref name="k"/> in unary — that many
        /// zeros and a one — then its remainder in <paramref name="k"/> bits.
        /// </summary>
        public void WriteRice(ulong value, int k)
        {
            for (var zeros = value >> k; zeros > 0;)
            {
                var run = (int)Math.Min(zeros, 32);
                Write(0, run);
                zeros -= (ulong)run;
            }

            Write(1, 1);
            if (k > 0)
            {
                Write(value & ((1UL << k) - 1), k);
            }
        }

        public void PadToByte()
        {
            if (pendingBits > 0)
            {
                Write(0, 8 - pendingBits);
            }
        }

        public byte[] ToArray()
        {
            PadToByte();
            return Bytes.ToArray();
        }

        private void Put(byte value)
        {
            if (length == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }

            buffer[length++] = value;
        }
    }
}
