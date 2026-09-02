using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Audio;

/// <summary>One source of a recording, as the folder shows it before anything is read.</summary>
/// <param name="Channel">Which of the two channels it fed.</param>
/// <param name="Blocks">The file its blocks are in.</param>
/// <param name="Bytes">What that file occupies, which is what says a source recorded anything at all.</param>
public sealed record UnfinishedSource(AudioChannel Channel, FileInfo Blocks, long Bytes);

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
/// A recording sitting in the folder recordings are written into, and the three things that may
/// happen to it.
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
/// Keeping it produces nothing here, and that is not an omission: the blocks already are the
/// recording, whole up to the packet the machine died in, and <see cref="Keep"/> is the reading
/// that says what survived. Turning those blocks into the meeting they are is
/// <see cref="MeetingAudio"/>'s, made beside them once somebody has kept them — which is why it
/// takes both sources and this takes whatever is there.
/// </para>
/// <para>
/// A recording that is still being written is one of these too, and so is one whose save is
/// running; both are marked rather than hidden — a meeting somebody is in the middle of, or has
/// just stopped, is the last thing to leave off a list. What neither is is something to decide
/// about: all three outcomes refuse them, because two of them read a file that is still growing or
/// already being read, and the third would throw away a meeting that is still happening or the
/// blocks a finish is halfway through.
/// <see cref="EnsureThereIsSomethingToDecide"/> is where all three refuse it and the only place
/// that says so, so that a caller reaches the answer about the meeting rather than the answer
/// about a block file that would not open. That covers the three outcomes and nothing wider:
/// <see cref="MeetingAudio.Materialise"/> takes the same folder without passing through here, so
/// whoever reaches the blocks that way still asks.
/// </para>
/// </remarks>
/// <param name="Folder">Where the recording is.</param>
/// <param name="Card">What it says about itself, or nothing when it never said.</param>
/// <param name="Unreadable">
/// Why what this folder says about itself could not be read — the card or the changes beside it —
/// or nothing when it read, or when there was none. A recording whose card was torn in half is
/// still a recording, so this is said rather than thrown: one damaged folder that stopped the
/// others being offered would be the crash winning twice.
/// </param>
/// <param name="Running">Whether a capture still holds these files, which on this machine means a meeting in progress.</param>
/// <param name="BeingSaved">
/// Whether a save of this recording is running right now — a finish somewhere holding the mark in
/// the folder. It is the one thing about a spool the files themselves cannot show: a finish reads
/// the blocks the way any reader does, so nothing here is held the way a capture holds it, and the
/// meeting has no length until the save's last write. See <see cref="SavingMark"/>.
/// </param>
/// <param name="Sources">What is on disk for each channel, in channel order.</param>
/// <param name="Changed">
/// What somebody moved while it was recording, in the order it happened, which is nothing for
/// almost every recording. The card says what a channel started on and this says what it ended on,
/// so a folder whose channel 0 was moved to the whole machine says so rather than still naming the
/// program it opened against.
/// </param>
public sealed record UnfinishedRecording(
    DirectoryInfo Folder,
    SpoolCard? Card,
    string? Unreadable,
    bool Running,
    bool BeingSaved,
    IReadOnlyList<UnfinishedSource> Sources,
    IReadOnlyList<SourceChanged> Changed)
{
    /// <summary>
    /// Why there is nothing to decide about this recording yet, or nothing when there is.
    /// </summary>
    /// <remarks>
    /// Two cases and they are asked in this order: a meeting whose save is running, then one a
    /// capture is still writing. The save first because it is the later half of the same meeting —
    /// stopping lets the devices go and then reads what they wrote, so a folder that is both is one
    /// the devices have not finished closing on, and what somebody stopped four seconds ago is
    /// being saved rather than still being recorded.
    /// <para>
    /// It is here, beside the three outcomes and the two handles that answer it, because everything
    /// the rule is made of is here. The reason on its own, in the middle of a sentence: whoever
    /// shows it says what it means for them, and <see cref="EnsureThereIsSomethingToDecide"/> is
    /// the one that says it for a refusal.
    /// </para>
    /// </remarks>
    public string? NothingToDecideYet =>
        BeingSaved ? "its save is running"
        : Running ? "it is still being recorded"
        : null;

    /// <summary>
    /// Throws unless this recording is one somebody may decide about, which every one of the three
    /// outcomes asks before it does anything.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The words, once, where all three reach them. A capture holding the blocks would stop each
    /// of the three anyway — the file system is what protects them, because
    /// <see cref="SpoolWriter"/> opens without <see cref="FileShare.Delete"/> and Windows will not
    /// unlink a file somebody holds that way — but what comes back from a handle is a sentence
    /// about a file that would not open. The person is deciding about a meeting, so the refusal is
    /// about the meeting.
    /// </para>
    /// <para>
    /// It says what was observed first and what it means second, so that the one case this cannot
    /// tell apart — something else on the machine holding a spool open — reads as what was seen
    /// rather than as an assertion about a meeting nobody is in.
    /// </para>
    /// <para>
    /// A save gets its own sentence rather than the capture's, because it is a different thing to
    /// have done and a different thing to wait for: nobody is speaking, the meeting is being
    /// written down, and what ends the wait is the save finishing or the process running it going
    /// away — not somebody stopping a meeting. It is also the case an answer typed at a second
    /// prompt would otherwise land on, over blocks a finish is reading at that moment.
    /// </para>
    /// </remarks>
    public void EnsureThereIsSomethingToDecide()
    {
        if (NothingToDecideYet is null)
        {
            return;
        }

        // Which case it is was settled above, so this chooses the sentence and never the answer.
        throw new AudioCaptureException(BeingSaved
            ? $"A save of the recording in '{Folder.FullName}' is running, and it is reading these "
                + "blocks into the meeting right now. A recording being written down is not one to "
                + "decide about yet. Once the save is over — or once whatever is running it is "
                + "gone — keeping it, taking it out and throwing it away are all open again."
            : $"Something is holding the blocks in '{Folder.FullName}' open, which on this machine "
                + $"is a capture writing them: {NothingToDecideYet}, and a meeting that is still "
                + "happening is not one to decide about yet. Once nothing is holding them, keeping "
                + "it, taking it out and throwing it away are all open.");
    }

    /// <summary>
    /// Reads every source through and says what survived, changing nothing. The recording is still
    /// there afterwards, which is what keeping it means.
    /// </summary>
    public IReadOnlyList<SurvivingSource> Keep()
    {
        EnsureThereIsSomethingToDecide();

        return [.. Sources.Select(Survived)];
    }

    /// <summary>
    /// Writes each source into <paramref name="into"/> as a file somebody can play, in the format
    /// its device handed over, and leaves the recording where it is.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One file per source, unaligned and unresampled — what a person plays to hear what each
    /// device caught. It is not the meeting's audio: two sources become one pair of channels on
    /// the shared timeline, and that file is made when a recording is finished rather than when it
    /// is taken out.
    /// </para>
    /// <para>
    /// Every destination is claimed from the file system before any audio is poured, and anything
    /// that goes wrong afterwards takes back what this call made. Half of a recording somebody
    /// asked for is worse than a refusal — worse still because the half that landed is what makes
    /// the second attempt refuse the folder.
    /// </para>
    /// </remarks>
    public IReadOnlyList<ExportedSource> Export(DirectoryInfo into)
    {
        ArgumentNullException.ThrowIfNull(into);
        EnsureThereIsSomethingToDecide();

        into.Create();
        var claimed = new List<FileInfo>();
        try
        {
            foreach (var source in Sources)
            {
                var wav = new FileInfo(Path.Combine(
                    into.FullName, BlockSpool.PlaybackFor(source.Blocks).Name));

                Claim(wav);
                claimed.Add(wav);
            }

            return
            [
                .. Sources.Zip(claimed, (source, wav) =>
                {
                    var replayed = BlockSpool.ToWav(source.Blocks, wav);

                    // The handle answered whether the file was there before it held anything, and
                    // that answer is the one a caller would read off what came back.
                    wav.Refresh();
                    return new ExportedSource(source.Channel, wav, replayed.Blocks, replayed.Discarded);
                }),
            ];
        }
        catch
        {
            foreach (var wav in claimed)
            {
                BlockSpool.Erase(wav);
            }

            throw;
        }
    }

    /// <summary>
    /// Throws the recording away: the blocks, the card and the folder holding them.
    /// </summary>
    /// <remarks>
    /// The only thing in this product that removes a recording, and it is reachable only from a
    /// choice somebody made about this one recording. Everything else that looks at a spool reads
    /// it and leaves it — see <see cref="UnfinishedRecordings"/> for what that rule costs and what
    /// it buys.
    /// </remarks>
    public void Discard()
    {
        // Both, and in this order. The first is what a person is told; the second is asked again
        // against the file system, because the first is an answer read a moment ago. Neither holds
        // anything across the delete below — what stops it half way is that `SpoolWriter` and
        // `SavingMark` both open without `FileShare.Delete`, so Windows refuses to unlink a block
        // file a capture holds or the mark a save holds. This is the check that fails whole
        // instead, before one source has gone and the other has not.
        EnsureThereIsSomethingToDecide();
        UnfinishedRecordings.EnsureRemovable(this);
        Folder.Delete(recursive: true);
    }

    /// <summary>
    /// Claims a name from the file system rather than asking whether it is free. The two are not
    /// the same answer: between a question and a write there is a window in which the file that
    /// appears is somebody else's, and what this is guarding is that nothing here writes over it.
    /// </summary>
    private static void Claim(FileInfo wav)
    {
        try
        {
            using var claim = new FileStream(
                wav.FullName, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException taken) when (File.Exists(wav.FullName))
        {
            throw new AudioCaptureException(
                $"'{wav.FullName}' is already there. A recording taken out of the application is "
                + "not written over another one — name a folder of its own.", taken);
        }
    }

    private SurvivingSource Survived(UnfinishedSource source)
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
/// The recordings sitting in the folder recordings are written into — what a start that follows a
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
/// Every folder holding a spool is one of these, and it says which of them are still being
/// recorded rather than leaving them out. A folder the recording was already made in is offered
/// all the same: <see cref="MeetingAudio.FileName"/> beside the blocks says the meeting was
/// finished, not that anybody has done anything with it, and nothing yet files a spool folder into
/// the corpus. Until that exists, a recording somebody stopped and one the machine died in the
/// middle of are the same folder, and the honest thing is to offer both rather than to claim a
/// difference on the strength of a file keeping one makes.
/// </para>
/// <para>
/// A folder with no card is still a recording, and so is one whose card cannot be read. Each spool
/// declares its own format, so the blocks are readable without one; dropping a folder for want of
/// a readable card would be exactly the silent discard this whole path exists to make impossible.
/// </para>
/// </remarks>
public static class UnfinishedRecordings
{
    /// <summary>
    /// Every recording sitting in <paramref name="root"/>, in the order their folders are named.
    /// A root that is not there holds none, which is a machine that has never recorded and not a
    /// failure.
    /// </summary>
    public static IReadOnlyList<UnfinishedRecording> In(DirectoryInfo root)
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
                .OfType<UnfinishedRecording>(),
        ];
    }

    /// <summary>
    /// The recording in one folder, named directly rather than found by looking. A folder holding
    /// no spool is refused: what somebody typed is then a folder, and acting on it as a recording
    /// is how the wrong directory gets thrown away.
    /// </summary>
    public static UnfinishedRecording At(DirectoryInfo folder)
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
    /// Throws unless every one of this recording's files is a spool nobody is writing and nothing
    /// is saving it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is what stands between <c>--discard</c> and a folder it should never have removed. It
    /// opens each source the way reading one does, so both of the ways a folder can fail to be
    /// what it looks like stop the delete before anything goes: a file named like a spool that is
    /// not one, and a spool a capture is still writing, which on this machine is a meeting still
    /// happening.
    /// </para>
    /// <para>
    /// The save is asked here as well as on the recording, and asked again against the file system
    /// rather than off what was listed: a folder is listed once and answered about seconds later,
    /// and a save can start in between. It narrows that window rather than closing it, and saying
    /// so is the point — every check here is asked before a delete that then runs file by file, so
    /// a save starting after this line still ends in a folder emptied as far as the first thing
    /// something is holding. Closing it properly is moving the folder aside and removing the copy,
    /// which is a change to the one thing that removes a recording and not a check in front of it.
    /// </para>
    /// </remarks>
    internal static void EnsureRemovable(UnfinishedRecording recording)
    {
        if (SavingMark.IsHeldIn(recording.Folder))
        {
            throw new AudioCaptureException(
                $"A save of the recording in '{recording.Folder.FullName}' is running, and the "
                + "blocks it is reading are not something to throw away while it does. Once the "
                + "save is over — or once the process running it is gone — throwing it away is "
                + "open again.");
        }

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
    private static UnfinishedRecording? Found(DirectoryInfo folder)
    {
        var sources = new[] { AudioChannel.Loopback, AudioChannel.Microphone }
            .Select(channel => (Channel: channel, Blocks: BlockSpool.FileFor(folder, channel)))
            .Where(source => source.Blocks.Exists)
            .Select(source => new UnfinishedSource(source.Channel, source.Blocks, source.Blocks.Length))
            .ToArray();

        if (sources.Length == 0)
        {
            return null;
        }

        SpoolCard? card = null;
        IReadOnlyList<SourceChanged> changed = [];
        string? unreadable = null;
        try
        {
            card = SpoolManifest.Find(folder);
            changed = SpoolChanges.Find(folder);
        }
        catch (AudioCaptureException torn)
        {
            unreadable = torn.Message;
        }

        return new UnfinishedRecording(
            folder,
            card,
            unreadable,
            Array.Exists(sources, source => BlockSpool.IsStillBeingWritten(source.Blocks)),
            SavingMark.IsHeldIn(folder),
            sources,
            changed);
    }
}
