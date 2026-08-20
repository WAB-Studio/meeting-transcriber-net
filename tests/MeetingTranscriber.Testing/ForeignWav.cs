using System.Text;

namespace MeetingTranscriber.Testing;

/// <summary>
/// A WAV built byte by byte, standing in for a file this application did not write.
/// </summary>
/// <remarks>
/// <para>
/// <b>It writes the header itself, and that is the whole point of it.</b> A fixture built with the
/// audio engine's own writer proves that this build can read its own dependency back, which is not
/// what a suite about audio from somewhere else needs to know. Nothing here references the audio
/// engine or NAudio, so a file it makes cannot be shaped by the code under test — and the failures
/// worth testing are the ones no writer produces at all: a data chunk that declares more than the
/// file holds, a chunk length that is not a whole number of frames, a rate of zero.
/// </para>
/// <para>
/// A declared length that outruns the file is not an invented case. It is what a copy interrupted
/// half way leaves, what a download that stopped leaves, and what a recorder whose battery went
/// leaves — the length lives in a field written before the audio and never corrected. A build that
/// believes it rather than counting files a meeting whose stored length is a number nothing on the
/// disk supports, and every citation into that meeting is checked against it.
/// </para>
/// <para>
/// It lives here rather than in one suite because three of them need it and none may hold the
/// copy. It costs this project no reference at all, which is the bar: what stops
/// <c>MeetingTranscriber.Testing</c> holding an audio fixture is a path from a corpus test to the
/// engine, and there is none here to make.
/// </para>
/// </remarks>
public static class ForeignWav
{
    /// <summary>Sixteen bits, the only width this writes.</summary>
    private const int BitsPerSample = 16;

    /// <summary>
    /// A whole, well-formed PCM WAV holding a steady level on each channel.
    /// </summary>
    /// <remarks>
    /// A different level per channel rather than one everywhere: a mix down that dropped a side
    /// instead of averaging the two produces a number, and a number is something a test can catch.
    /// </remarks>
    /// <param name="levels">What each channel holds, full scale at one. Its length is the channel count.</param>
    public static FileInfo Steady(FileInfo file, int rate, int frames, params float[] levels)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(levels);

        var frame = new byte[levels.Length * (BitsPerSample / 8)];
        for (var channel = 0; channel < levels.Length; channel++)
        {
            var value = (short)(levels[channel] * short.MaxValue);
            frame[channel * 2] = (byte)(value & 0xFF);
            frame[(channel * 2) + 1] = (byte)((value >> 8) & 0xFF);
        }

        var audio = new byte[frame.Length * frames];
        for (var at = 0; at < audio.Length; at += frame.Length)
        {
            frame.CopyTo(audio, at);
        }

        return Write(file, rate, levels.Length, BitsPerSample, audio.Length, audio);
    }

    /// <summary>
    /// A PCM WAV whose data chunk says one thing and whose file holds another.
    /// </summary>
    /// <param name="declared">What the data chunk claims, in bytes.</param>
    /// <param name="present">What is really written after the header, in bytes.</param>
    public static FileInfo Truncated(
        FileInfo file, int rate, int channels, int declared, int present) =>
        Write(file, rate, channels, BitsPerSample, declared, Quiet(present));

    /// <summary>A whole PCM WAV at a rate no audio could be read at.</summary>
    public static FileInfo AtNoRate(FileInfo file, int channels, int bytes) =>
        Write(file, 0, channels, BitsPerSample, bytes, Quiet(bytes));

    /// <summary>A PCM WAV of a width this build has no reader for.</summary>
    public static FileInfo Wide(FileInfo file, int rate, int channels, int frames)
    {
        const int wide = 24;
        var bytes = frames * channels * (wide / 8);
        return Write(file, rate, channels, wide, bytes, Quiet(bytes));
    }

    /// <summary>
    /// The file, with every field of the two chunks written from what was asked for rather than
    /// from what was produced — which is what lets a caller ask for a header that disagrees with
    /// the audio behind it.
    /// </summary>
    private static FileInfo Write(
        FileInfo file, int rate, int channels, int bitsPerSample, int declared, byte[] audio)
    {
        var blockAlign = channels * (bitsPerSample / 8);

        using (var stream = file.Open(FileMode.Create, FileAccess.Write, FileShare.None))
        using (var wav = new BinaryWriter(stream, Encoding.ASCII))
        {
            wav.Write("RIFF"u8);
            wav.Write(36 + declared);
            wav.Write("WAVE"u8);

            wav.Write("fmt "u8);
            wav.Write(16);
            wav.Write((short)1);
            wav.Write((short)channels);
            wav.Write(rate);
            wav.Write(rate * blockAlign);
            wav.Write((short)blockAlign);
            wav.Write((short)bitsPerSample);

            wav.Write("data"u8);
            wav.Write(declared);
            wav.Write(audio);
        }

        file.Refresh();
        return file;
    }

    /// <summary>
    /// Audio at a steady quiet level rather than zeros, so a build that read the wrong number of
    /// bytes cannot pass by producing silence that reads as silence.
    /// </summary>
    private static byte[] Quiet(int bytes)
    {
        var audio = new byte[bytes];
        for (var index = 0; index + 1 < audio.Length; index += 2)
        {
            audio[index + 1] = 0x10;
        }

        return audio;
    }
}
