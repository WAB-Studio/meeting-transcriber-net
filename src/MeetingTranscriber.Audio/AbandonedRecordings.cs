using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>One source of a recording nobody stopped, as the folder shows it before anything is read.</summary>
/// <param name="Channel">Which of the two channels it fed.</param>
/// <param name="Blocks">The file its blocks are in.</param>
/// <param name="Bytes">What that file occupies, which is what says a source recorded anything at all.</param>
public sealed record AbandonedSource(AudioChannel Channel, FileInfo Blocks, long Bytes);

/// <summary>What one source turned out to be worth once its blocks were read through.</summary>
/// <param name="Channel">Which of the two channels it fed.</param>
/// <param name="Format">What its device handed over, as its own file says.</param>
/// <param name="Blocks">Whole blocks, every one of them as its device reported it.</param>
/// <param name="Covers">The stretch of the meeting those blocks' positions span.</param>
/// <param name="Lost">How much of that stretch the device counted and never handed over.</param>
/// <param name="Discarded">
/// Bytes at the end that were not a whole block. Non-zero is the recording having been cut off,
/// and what it cost is the last packet rather than the meeting.
/// </param>
public sealed record SurvivingSource(
    AudioChannel Channel,
    StreamFormat Format,
    int Blocks,
    Duration Covers,
    Duration Lost,
    long Discarded);

/// <summary>One source's audio, written out where somebody asked for it.</summary>
/// <param name="Channel">Which of the two channels it fed.</param>
/// <param name="Wav">The file it was written to.</param>
/// <param name="Blocks">Whole blocks poured into it.</param>
/// <param name="Discarded">Bytes at the end of the spool that were not a whole block.</param>
public sealed record ExportedSource(AudioChannel Channel, FileInfo Wav, int Blocks, long Discarded);

/// <summary>
/// A recording the application never got to stop, and the three things that may happen to it.
/// </summary>
/// <remarks>
/// <para>
/// Three, named, and each one somebody's choice: the recording is kept as it stands, its audio is
/// taken out to a folder they name, or it is thrown away. Nothing else here removes anything —
/// that is the whole point of the type. A spool may be the only copy of a meeting that happened,
/// and the failure this exists to prevent is not a crash losing it but the next start tidying it
/// away.
/// </para>
/// <para>
/// Keeping it does not produce a file, and that is not an omission: the blocks already are the
/// recording, whole up to the packet the machine died in. What the choice settles is that
/// somebody has seen it and it stays.
/// </para>
/// </remarks>
public sealed record AbandonedRecording(
    DirectoryInfo Folder,
    SpoolCard? Card,
    IReadOnlyList<AbandonedSource> Sources)
{
    /// <summary>
    /// Reads every source through and says what survived, changing nothing. The recording is still
    /// there afterwards, which is what keeping it means.
    /// </summary>
    public IReadOnlyList<SurvivingSource> Keep() => [.. Sources.Select(Survived)];

    /// <summary>
    /// Writes each source into <paramref name="into"/> as a file somebody can play, in the format
    /// its device handed over, and leaves the recording where it is.
    /// </summary>
    /// <remarks>
    /// One file per source, unaligned and unresampled — what a person plays to hear what each
    /// device caught. It is not the meeting's audio: two sources become one pair of channels on
    /// the shared timeline, and that file is made when a recording is finished rather than when it
    /// is taken out.
    /// </remarks>
    public IReadOnlyList<ExportedSource> Export(DirectoryInfo into)
    {
        ArgumentNullException.ThrowIfNull(into);

        into.Create();
        var wavs = Sources.ToDictionary(
            source => source.Channel,
            source => new FileInfo(Path.Combine(into.FullName, BlockSpool.PlaybackFor(source.Blocks).Name)));

        // Every destination before the first one is written: a source that lands and a second that
        // is refused would leave somebody holding half of a recording they asked to take out.
        foreach (var wav in wavs.Values.Where(wav => wav.Exists))
        {
            throw new AudioCaptureException(
                $"'{wav.FullName}' is already there. A recording taken out of the application is "
                + "not written over another one — name a folder of its own.");
        }

        return
        [
            .. Sources.Select(source =>
            {
                var wav = wavs[source.Channel];
                var replayed = BlockSpool.ToWav(source.Blocks, wav);

                // The handle asked whether it was there before it was written, and the answer it
                // cached is the one a caller would read off what came back.
                wav.Refresh();
                return new ExportedSource(source.Channel, wav, replayed.Blocks, replayed.Discarded);
            }),
        ];
    }

    /// <summary>
    /// Throws the recording away: the blocks, the card and the folder holding them.
    /// </summary>
    /// <remarks>
    /// The only thing in this product that removes a recording, and it is reachable only from a
    /// choice somebody made about this one recording. Everything else that looks at a spool reads
    /// it and leaves it — see <see cref="AbandonedRecordings"/> for what that rule costs and what
    /// it buys.
    /// </remarks>
    public void Discard()
    {
        AbandonedRecordings.EnsureNothingIsRecordingInto(this);
        Folder.Delete(recursive: true);
    }

    private SurvivingSource Survived(AbandonedSource source)
    {
        using var spool = SpoolReader.Open(source.Blocks);
        var tally = new PacketTally(spool.Format);
        var blocks = 0;

        foreach (var packet in spool.Packets())
        {
            tally.Add(packet);
            blocks++;
        }

        return new SurvivingSource(
            spool.Channel, spool.Format, blocks, tally.Covers, tally.Lost, spool.Discarded);
    }
}

/// <summary>
/// The recordings waiting in the folder recordings are written into — what a start that follows a
/// crash has to offer somebody before anything else happens.
/// </summary>
/// <remarks>
/// <para>
/// It reads the card and the size of each spool, and never the blocks themselves. Two hours of a
/// meeting is a few hundred megabytes per source, and a list that read through all of it would be
/// one nobody waits for on a start — so what is offered is what a person decides on: which meeting
/// it was, when, on which devices, and how much is there. Reading a recording through is what
/// keeping or taking one out does, to one recording, because somebody asked.
/// </para>
/// <para>
/// A folder with no card is still a recording. Each spool declares its own format, so the blocks
/// are readable without one, and dropping a folder for want of a card would be exactly the silent
/// discard this whole path exists to make impossible.
/// </para>
/// </remarks>
public static class AbandonedRecordings
{
    /// <summary>
    /// Every recording sitting in <paramref name="root"/>, in the order their folders are named.
    /// A root that is not there holds none, which is a machine that has never recorded and not a
    /// failure.
    /// </summary>
    public static IReadOnlyList<AbandonedRecording> In(DirectoryInfo root)
    {
        ArgumentNullException.ThrowIfNull(root);

        root.Refresh();
        if (!root.Exists)
        {
            return [];
        }

        return
        [
            .. root.EnumerateDirectories()
                .OrderBy(folder => folder.Name, StringComparer.Ordinal)
                .Select(Found)
                .OfType<AbandonedRecording>(),
        ];
    }

    /// <summary>
    /// The recording in one folder, named directly rather than found by looking. A folder holding
    /// no spool is refused: what somebody typed is then a folder, and acting on it as a recording
    /// is how the wrong directory gets thrown away.
    /// </summary>
    public static AbandonedRecording At(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        folder.Refresh();
        if (!folder.Exists)
        {
            throw new AudioCaptureException($"There is no folder at '{folder.FullName}'.");
        }

        return Found(folder)
            ?? throw new AudioCaptureException(
                $"'{folder.FullName}' holds no spool, so there is no recording in it to decide about.");
    }

    /// <summary>
    /// Throws when a capture still holds any of this recording's spools, which on this machine
    /// means a meeting that is still being recorded.
    /// </summary>
    /// <remarks>
    /// Asked before a recording is thrown away rather than left to the delete: what the file
    /// system says when a handle is open is that a file is in use, and somebody who has just
    /// discarded a recording still going on deserves to be told that is what happened.
    /// </remarks>
    internal static void EnsureNothingIsRecordingInto(AbandonedRecording recording)
    {
        foreach (var source in recording.Sources)
        {
            SpoolReader.Open(source.Blocks).Dispose();
        }
    }

    /// <summary>
    /// What is in one folder, or nothing when no source of a recording is. The card alone is not a
    /// recording — a capture that was refused its devices can leave one — and neither is a folder
    /// somebody made by hand.
    /// </summary>
    private static AbandonedRecording? Found(DirectoryInfo folder)
    {
        var sources = new[] { AudioChannel.Loopback, AudioChannel.Microphone }
            .Select(channel => (Channel: channel, Blocks: BlockSpool.FileFor(folder, channel)))
            .Where(source => source.Blocks.Exists)
            .Select(source => new AbandonedSource(source.Channel, source.Blocks, source.Blocks.Length))
            .ToArray();

        return sources.Length == 0
            ? null
            : new AbandonedRecording(folder, SpoolManifest.Find(folder), sources);
    }
}
