using System.Security.Cryptography;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// A FLAC decoder for what <c>FlacEncoder</c> writes and nothing wider: STREAMINFO, then frames of
/// independently coded 16-bit channels whose subframes are constant, verbatim or fixed-predictor
/// with Rice residuals.
/// </summary>
/// <remarks>
/// It checks every CRC and the STREAMINFO MD5 rather than trusting them, so an encoder that wrote
/// the right samples behind a wrong checksum still fails here — which is what a real decoder at the
/// provider would do with it. It shares no code with the encoder. What it cannot catch is a
/// misreading of the format the two would share; the 1h42m recording decoded with ffmpeg on
/// 2026-10-20 is the evidence that reaches that.
/// </remarks>
internal static class FlacReader
{
    /// <summary>Every channel's samples, and what STREAMINFO said about them.</summary>
    internal sealed record Decoded(int SampleRate, int[][] Channels, long DeclaredSamples);

    public static Decoded Read(byte[] flac)
    {
        var bits = new BitReader(flac);
        if (bits.Read(32) != 0x664C6143)
        {
            throw new InvalidDataException("No fLaC marker.");
        }

        var last = bits.Read(1);
        var type = bits.Read(7);
        var length = bits.Read(24);
        if (last != 1 || type != 0 || length != 34)
        {
            throw new InvalidDataException("STREAMINFO is not the one and last metadata block.");
        }

        bits.Read(16);
        var maxBlock = (int)bits.Read(16);
        bits.Read(24);
        bits.Read(24);
        var rate = (int)bits.Read(20);
        var channelCount = (int)bits.Read(3) + 1;
        var bitsPerSample = (int)bits.Read(5) + 1;
        var total = (long)bits.Read(36);
        var md5 = new byte[16];
        for (var at = 0; at < 16; at++)
        {
            md5[at] = (byte)bits.Read(8);
        }

        if (bitsPerSample != 16)
        {
            throw new InvalidDataException($"{bitsPerSample} bits per sample.");
        }

        var channels = Enumerable.Range(0, channelCount).Select(_ => new List<int>()).ToArray();
        long expectedFrame = 0;

        while (bits.BytePosition < flac.Length)
        {
            var frameStart = bits.BytePosition;
            if (bits.Read(14) != 0b11111111111110 || bits.Read(1) != 0 || bits.Read(1) != 0)
            {
                throw new InvalidDataException($"No frame sync at byte {frameStart}.");
            }

            var sizeCode = bits.Read(4);
            var rateCode = bits.Read(4);
            var assignment = (int)bits.Read(4);
            var sampleSizeCode = bits.Read(3);
            bits.Read(1);
            var number = ReadUtf8(bits);
            if (number != expectedFrame++)
            {
                throw new InvalidDataException($"Frame {number} where {expectedFrame - 1} was due.");
            }

            var blockSize = sizeCode switch
            {
                0b1100 => 4096,
                0b0111 => (int)bits.Read(16) + 1,
                _ => throw new InvalidDataException($"Block size code {sizeCode}."),
            };

            var frameRate = rateCode switch
            {
                0b0100 => 8_000,
                0b0101 => 16_000,
                0b0110 => 22_050,
                0b0111 => 24_000,
                0b1000 => 32_000,
                0b1001 => 44_100,
                0b1010 => 48_000,
                0b1011 => 96_000,
                0b1100 => (int)bits.Read(8) * 1000,
                0b1101 => (int)bits.Read(16),
                0b1110 => (int)bits.Read(16) * 10,
                _ => throw new InvalidDataException(
                    $"Rate code {rateCode}, which leaves the streamable subset or is reserved."),
            };

            if (frameRate != rate || sampleSizeCode != 0b100 || assignment != channelCount - 1 || blockSize > maxBlock)
            {
                throw new InvalidDataException("A frame header this reader was not written for.");
            }

            var crc8 = (byte)bits.Read(8);
            if (crc8 != Crc8(flac.AsSpan(frameStart, bits.BytePosition - frameStart - 1)))
            {
                throw new InvalidDataException($"Header CRC-8 wrong in frame {number}.");
            }

            foreach (var channel in channels)
            {
                channel.AddRange(Subframe(bits, blockSize));
            }

            bits.AlignToByte();
            var crcAt = bits.BytePosition;
            var crc16 = (ushort)bits.Read(16);
            if (crc16 != Crc16(flac.AsSpan(frameStart, crcAt - frameStart)))
            {
                throw new InvalidDataException($"Frame CRC-16 wrong in frame {number}.");
            }
        }

        var decoded = channels.Select(channel => channel.ToArray()).ToArray();
        var pcm = new byte[decoded[0].Length * channelCount * 2];
        for (var at = 0; at < decoded[0].Length; at++)
        {
            for (var channel = 0; channel < channelCount; channel++)
            {
                var sample = (short)decoded[channel][at];
                pcm[((at * channelCount) + channel) * 2] = (byte)sample;
                pcm[(((at * channelCount) + channel) * 2) + 1] = (byte)(sample >> 8);
            }
        }

        if (!MD5.HashData(pcm).SequenceEqual(md5))
        {
            throw new InvalidDataException("STREAMINFO's MD5 is not the MD5 of the decoded samples.");
        }

        return new Decoded(rate, decoded, total);
    }

    private static int[] Subframe(BitReader bits, int blockSize)
    {
        if (bits.Read(1) != 0)
        {
            throw new InvalidDataException("Subframe padding bit set.");
        }

        var type = (int)bits.Read(6);
        if (bits.Read(1) != 0)
        {
            throw new InvalidDataException("Wasted bits, which the encoder never writes.");
        }

        var samples = new int[blockSize];
        if (type == 0)
        {
            Array.Fill(samples, bits.ReadSigned(16));
            return samples;
        }

        if (type == 1)
        {
            for (var at = 0; at < blockSize; at++)
            {
                samples[at] = bits.ReadSigned(16);
            }

            return samples;
        }

        if (type is < 0b001000 or > 0b001100)
        {
            throw new InvalidDataException($"Subframe type {type}.");
        }

        var order = type & 0b111;
        for (var at = 0; at < order; at++)
        {
            samples[at] = bits.ReadSigned(16);
        }

        if (bits.Read(2) != 0)
        {
            throw new InvalidDataException("A residual coding other than 4-bit Rice.");
        }

        var partitionOrder = (int)bits.Read(4);
        var per = blockSize >> partitionOrder;
        var position = order;
        for (var partition = 0; partition < 1 << partitionOrder; partition++)
        {
            var k = (int)bits.Read(4);
            var end = (partition + 1) * per;
            for (; position < end; position++)
            {
                long residual;
                if (k == 15)
                {
                    throw new InvalidDataException("An escaped partition, which the encoder never writes.");
                }

                long quotient = 0;
                while (bits.Read(1) == 0)
                {
                    quotient++;
                }

                var folded = (quotient << k) | (long)(k > 0 ? bits.Read(k) : 0);
                residual = (folded >> 1) ^ -(folded & 1);
                samples[position] = (int)(order switch
                {
                    0 => residual,
                    1 => residual + samples[position - 1],
                    2 => residual + (2L * samples[position - 1]) - samples[position - 2],
                    3 => residual + (3L * samples[position - 1]) - (3L * samples[position - 2]) + samples[position - 3],
                    _ => residual + (4L * samples[position - 1]) - (6L * samples[position - 2]) + (4L * samples[position - 3]) - samples[position - 4],
                });
            }
        }

        return samples;
    }

    private static long ReadUtf8(BitReader bits)
    {
        var first = (int)bits.Read(8);
        if ((first & 0x80) == 0)
        {
            return first;
        }

        var continuation = 0;
        for (var mask = 0x40; (first & mask) != 0; mask >>= 1)
        {
            continuation++;
        }

        long value = first & (0x3F >> continuation);
        for (var at = 0; at < continuation; at++)
        {
            value = (value << 6) | (long)(bits.Read(8) & 0x3F);
        }

        return value;
    }

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

    private sealed class BitReader(byte[] data)
    {
        private long position;

        public int BytePosition => (int)(position / 8);

        public ulong Read(int count)
        {
            ulong value = 0;
            for (var at = 0; at < count; at++)
            {
                if (position / 8 >= data.Length)
                {
                    throw new InvalidDataException("Ran off the end of the file.");
                }

                var bit = (data[position / 8] >> (7 - (int)(position % 8))) & 1;
                value = (value << 1) | (uint)bit;
                position++;
            }

            return value;
        }

        public int ReadSigned(int count) => (int)((long)(Read(count) << (64 - count)) >> (64 - count));

        public void AlignToByte() => position = (position + 7) / 8 * 8;
    }
}
