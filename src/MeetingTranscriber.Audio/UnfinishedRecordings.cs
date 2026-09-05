using System.Diagnostics;

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
    /// <para>
    /// The only thing in this product that removes a recording, and it is reachable only from a
    /// choice somebody made about this one recording. Everything else that looks at a spool reads
    /// it and leaves it — see <see cref="UnfinishedRecordings"/> for what that rule costs and what
    /// it buys.
    /// </para>
    /// <para>
    /// <b>It blocks, and on the failing path it blocks for a while.</b> Throwing a recording away
    /// is a rename that waits out whatever still has a file in the folder open, so this can take up
    /// to <see cref="UnfinishedRecordings.RemovalPatienceMilliseconds"/> — a quarter of a second —
    /// to come back, and it comes back at once when nothing is holding the folder. The one caller
    /// on a window's thread is <c>MeetingsDrawer.Decide</c>, where that ceiling was priced and
    /// accepted: it is under what reads as a hung window, and it is only reached on the path that
    /// ends in a refusal anyway.
    /// </para>
    /// </remarks>
    public void Discard()
    {
        // Both, and in this order, and neither of them is what makes this safe. The move below is
        // the authority and these two are only the sentence — a save that starts between the check
        // and the rename is refused by Windows on the rename, with the folder exactly as it was.
        // They are here because somebody deciding about a meeting is owed a sentence about the
        // meeting rather than one about a rename.
        EnsureThereIsSomethingToDecide();
        UnfinishedRecordings.EnsureRemovable(this);
        UnfinishedRecordings.Remove(this);
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
    /// How long a removal waits out something that still has a file in the folder open, before it
    /// decides the holder is a real reader and says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A quarter of a second, because past that the likelier holder is somebody rather than
    /// something: another window keeping the same recording, a prompt exporting it, a program on
    /// this machine that is not letting go this second. A sentence saying so is worth more to the
    /// person than a longer wait ending in the same sentence. It is also the number
    /// <c>tests/MeetingTranscriber.Testing/Folders.cs</c> measured against this same Windows
    /// refusal.
    /// </para>
    /// <para>
    /// <see cref="HeldMark"/> waits two seconds and the difference is considered rather than
    /// accidental: that one waits out <em>a listing holding a mark for the length of one open</em>
    /// and must not wait out a save or a meeting, which are minutes. This one waits out
    /// <em>whatever opened a file the instant it was closed</em>, which is a millisecond thing, and
    /// must not wait out a person keeping the same recording in another window.
    /// </para>
    /// <para>
    /// Milliseconds as an <c>int</c> rather than a <see cref="TimeSpan"/>, because the loop
    /// compares it against <see cref="Stopwatch.ElapsedMilliseconds"/> and because it is a copy of
    /// <c>Folders</c>, whose budget is an <c>int</c>. Internal because nothing outside this file
    /// has a product reason to read it.
    /// </para>
    /// </remarks>
    internal const int RemovalPatienceMilliseconds = 250;

    /// <summary>
    /// What a removal calls the folder it moves a recording into before it takes it away, in front
    /// of the recording's own folder name.
    /// </summary>
    /// <remarks>
    /// A name and not a <see cref="Guid"/>, deliberately: a unique one would be impossible to find
    /// again, which is what would turn a machine dying inside a discard into rubbish nobody can
    /// identify rather than something a person can see and delete.
    /// </remarks>
    private const string BeingRemoved = ".removing-";

    /// <summary>The two refusals that are somebody else still reading, and not an answer.</summary>
    private const int AccessDenied = unchecked((int)0x80070005);
    private const int SharingViolation = unchecked((int)0x80070020);

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
    /// This is asked for the sentence and not for the safety. What actually removes a recording is
    /// a rename — <see cref="Remove"/> — and a rename either happens or does not, so a save that
    /// starts after this line is refused with the folder exactly as it was. What that refusal
    /// cannot say is <em>which</em> of the things it is; that is what this is for, said while it is
    /// still possible to say it. The one thing here that is not a sentence is the spool opens: a
    /// file named like a spool that is not one is something no rename can tell apart, so this is
    /// still what stands between <c>--discard</c> and the wrong directory.
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
    /// Takes a recording's folder away by moving it out of the way first, so that a refusal leaves
    /// the recording exactly as it was rather than emptied as far as the first held file.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The rename is the authority, and it is the reason this is not a recursive delete.</b> A
    /// recursive delete unlinks in whatever order the directory enumerates in, so a handle nothing
    /// in this engine wrote — another window reading the same recording, a prompt exporting it,
    /// something on the machine — stops it half way: the card and the changes are gone, and what
    /// survives is the file that caused the refusal. A rename either happens or does not.
    /// </para>
    /// <para>
    /// The folder moves one level down, into <c>.removing-&lt;its name&gt;</c> beside where it was.
    /// Beside, because <see cref="Directory.Move(string, string)"/> refuses another volume, so the
    /// temp directory is not reachable. One level down, because <see cref="Found"/> reads any
    /// folder holding a spool as a recording: as a sibling the recording would be offered again
    /// under a name that is not its meeting id for as long as the removal ran, and forever after a
    /// crash inside it. Nested, <see cref="Found"/> looks for the blocks directly in
    /// <c>.removing-&lt;name&gt;</c>, finds none, and returns nothing — so no name rule leaks into
    /// what a recording is.
    /// </para>
    /// <para>
    /// Internal, because a public one beside <see cref="UnfinishedRecording.Discard"/> would be a
    /// second way to remove a recording with neither
    /// <see cref="UnfinishedRecording.EnsureThereIsSomethingToDecide"/> nor
    /// <see cref="EnsureRemovable"/> in front of it, and the sweep that holds this rule greps for
    /// spellings rather than for visibility, so nothing would object.
    /// </para>
    /// <para>
    /// Every way out of it is an <see cref="AudioCaptureException"/>. A raw <see cref="IOException"/>
    /// survives both the screen's and the prompt's reporting, and what either would print is
    /// Windows saying <c>Access to the path '…\spool\.removing-4f3c…' is denied</c> to somebody who
    /// pressed <em>Discard</em> on a meeting.
    /// </para>
    /// </remarks>
    internal static void Remove(UnfinishedRecording recording)
    {
        var folder = recording.Folder;
        var above = folder.Parent
            ?? throw new AudioCaptureException(
                $"There is nothing above '{folder.FullName}', so there is nowhere to move it aside "
                + "to and nothing was removed. A recording is thrown away by moving its folder out "
                + "of the way first.");

        var aside = new DirectoryInfo(Path.Combine(above.FullName, BeingRemoved + folder.Name));

        MakeTheAsideReady(aside, folder);

        try
        {
            MoveWaitingOutWhoeverHasIt(
                folder, new DirectoryInfo(Path.Combine(aside.FullName, folder.Name)));
        }
        catch
        {
            // This call's own and empty, so it goes back rather than being left as a folder every
            // start after a refusal has to have an opinion about.
            EraseIfItIsStillEmpty(aside);
            throw;
        }

        ThrowAwayTheCopy(aside, folder);
    }

    /// <summary>
    /// What in <paramref name="folder"/> says a recording happened there, or nothing when nothing
    /// in it does and it is a folder a press left behind.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The question a sweep asks before it decides anything, and it is here rather than where the
    /// sweeping is because every name in it is written from this project. A folder a recording never
    /// filled holds only files this engine makes before there is anything to record — the two marks,
    /// a spool carrying its header and nothing else, the card, and the note of a channel somebody
    /// moved — and anything else in it is either a recording or somebody's.
    /// </para>
    /// <para>
    /// It says what it found rather than yes or no, because whoever asks has to tell a person which
    /// folder they are looking at and why it is still there.
    /// </para>
    /// </remarks>
    public static string? WhatSaysARecordingHappenedIn(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        var left = NamesAPressLeaves(folder);
        var spools = SpoolsIn(folder).Select(spool => spool.Name).ToArray();

        foreach (var file in folder.EnumerateFiles())
        {
            if (!Array.Exists(left, one => Named(one.Name, file.Name)))
            {
                return $"'{file.Name}' is in it";
            }

            if (Array.Exists(spools, spool => Named(spool, file.Name))
                && file.Length > BlockSpool.HeaderBytes)
            {
                return $"'{file.Name}' holds what a device handed over";
            }
        }

        return folder.EnumerateDirectories().FirstOrDefault() is { } inside
            ? $"'{inside.Name}' is in it"
            : null;
    }

    /// <summary>
    /// Removes a folder nothing was ever recorded into, and refuses whole — having removed nothing
    /// — when anything in it says otherwise.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The second of the two places a folder under <c>spool/</c> goes, and the one that is nobody's
    /// decision: <see cref="UnfinishedRecording.Discard"/> throws away a recording because somebody
    /// said to, and this takes away a folder that never held one. It is here so that both are in the
    /// file that owns what a recording is and what its files are called, and so a third is a change
    /// somebody has to argue for in one place.
    /// </para>
    /// <para>
    /// Named one by one and then the folder by itself, never recursively. A recursive delete unlinks
    /// in whatever order the directory is enumerated in, so a folder that turned out to hold
    /// something would be half emptied before the refusal landed; named, the only thing that can go
    /// is a file this engine wrote before there was anything to record, and the empty delete at the
    /// end is the file system saying there was nothing else.
    /// </para>
    /// <para>
    /// <b>The delete is the authority and the question above it is only the sentence.</b> A capture
    /// that claimed this folder between the two holds <see cref="CaptureMark"/> in a share mode that
    /// forbids unlinking, so the first delete throws and the folder stands with its meeting — which
    /// is why nothing here asks whether a mark is held. What Windows answers cannot be out of date;
    /// what a listing answered a moment ago can.
    /// </para>
    /// <para>
    /// This file holds two different answers to the same hazard and they are not in competition.
    /// <see cref="Remove"/> moves the whole folder because it takes away a <em>recording</em>, where
    /// being told which files went is no consolation, so the guard has to be whole-or-nothing. This
    /// one takes away a folder holding nothing anybody chose to keep, so naming the files is both
    /// the guard and the proof there was nothing else in it.
    /// </para>
    /// </remarks>
    /// <param name="folder">The folder to take away.</param>
    /// <exception cref="AudioCaptureException">Something in it says a recording happened there.</exception>
    public static void EraseWhereNothingWasRecorded(DirectoryInfo folder)
    {
        ArgumentNullException.ThrowIfNull(folder);

        if (WhatSaysARecordingHappenedIn(folder) is { } said)
        {
            throw new AudioCaptureException(
                $"Nothing is taken away from '{folder.FullName}': {said}, and a folder holding "
                + "anything a recording left is a recording somebody still has to decide about.");
        }

        foreach (var file in NamesAPressLeaves(folder))
        {
            file.Delete();
        }

        // Spelled through `Directory` rather than the handle already in hand, and never recursively:
        // this is the spelling `UnfinishedRecordingsTests` greps for, and a folder removal this
        // repository cannot grep for is one nobody is holding to the rule above.
        Directory.Delete(folder.FullName);
    }

    /// <summary>
    /// Makes the folder a removal moves a recording into, or refuses because an earlier removal
    /// left something under that name.
    /// </summary>
    /// <remarks>
    /// Check, never destroy. What licences the recursive delete at the end of a removal is that
    /// everything under this name was put there by that same call an instant earlier — anything
    /// already in it was not. Only a removal ever makes a folder with this name, and only for a
    /// recording somebody said to throw away, so a leftover holding something <em>is</em> that
    /// recording: a machine that died between the move and the delete leaves exactly that, and
    /// nothing offers it any more. Taking it away here would be a second half-emptied removal at a
    /// path nobody was told about, which is the defect this whole shape exists to end. The one
    /// leftover actually reachable is an empty one, from a move that was refused and whose own
    /// cleanup could not run either, and <see cref="DirectoryInfo.Create"/> takes that for free.
    /// </remarks>
    private static void MakeTheAsideReady(DirectoryInfo aside, DirectoryInfo folder)
    {
        try
        {
            aside.Refresh();

            // Every file system entry and not only the files: a leftover from a crash after a
            // successful move holds a directory and no files at all, so a check that looked at
            // files would wave the recording under it through.
            if (aside.Exists && aside.EnumerateFileSystemInfos().Any())
            {
                throw new AudioCaptureException(
                    $"Nothing was removed and '{folder.FullName}' was not touched. There is already "
                    + $"a folder at '{aside.FullName}' from an earlier attempt to throw this "
                    + "recording away and it still has something in it. Nothing here takes that "
                    + "away on its own — what is under that name is a recording somebody already "
                    + "said to throw away, so look at what is in it and remove it by hand.");
            }

            aside.Create();
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            throw new AudioCaptureException(
                $"Nothing was removed and '{folder.FullName}' was not touched: '{aside.FullName}', "
                + "which is where a removal moves a recording before it takes it away, could not be "
                + $"made ready — {refused.Message}",
                refused);
        }
    }

    /// <summary>
    /// Renames <paramref name="from"/> to <paramref name="to"/>, waiting out whatever still has a
    /// file under it open.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The loop <c>tests/MeetingTranscriber.Testing/Folders.cs</c> holds for the same Windows fact,
    /// with this project's patience and its own sentences. The two cannot be one:
    /// <c>MeetingTranscriber.Audio</c> targets a Windows framework and that project targets plain
    /// <c>net10.0</c> and is referenced by the domain's tests, so sharing it either way is a
    /// <c>src/</c> → <c>tests/</c> reference or a Windows target framework under a domain test. DRY
    /// and the project boundary pull apart here and the boundary wins.
    /// </para>
    /// <para>
    /// The wait is not insurance against a rare machine. <see cref="EnsureRemovable"/> opens and
    /// closes every spool roughly a millisecond before this runs, and whatever reads a file the
    /// moment it is closed — a real-time scanner is the usual explanation — is the most likely
    /// holder of all. A build that drops the retry produces a discard that fails intermittently on
    /// real machines and never on a build agent.
    /// </para>
    /// <para>
    /// Catch order is load-bearing: the first filter that passes wins, so a refusal that is
    /// somebody reading never reaches the second clause. The second clause is what makes every
    /// remaining exit a sentence about the recording — a destination already there, a parent that
    /// is not, another volume, a denied ACL, and <see cref="PathTooLongException"/>, which the two
    /// extra levels of path can produce and which nothing was ever going to fix by waiting.
    /// </para>
    /// </remarks>
    private static void MoveWaitingOutWhoeverHasIt(DirectoryInfo from, DirectoryInfo to)
    {
        var clock = Stopwatch.StartNew();
        var refusals = 0;

        while (true)
        {
            try
            {
                Directory.Move(from.FullName, to.FullName);
                return;
            }
            catch (IOException refused) when (SomebodyIsStillReading(refused))
            {
                refusals++;

                if (clock.ElapsedMilliseconds >= RemovalPatienceMilliseconds)
                {
                    throw new AudioCaptureException(
                        $"Nothing was removed from '{from.FullName}': something on this machine "
                        + "still has a file in it open, and it was still refusing after "
                        + $"{refusals} tries over {clock.ElapsedMilliseconds} ms. A recording is "
                        + "thrown away by moving its folder aside first, so a refusal leaves it "
                        + "exactly as it was — every block, the card and the changes beside them "
                        + "are still where they were. Once whatever is reading it lets go, throwing "
                        + "it away is open again.",
                        refused);
                }

                // A sleep rather than a spin, so waiting does not take the core off whoever is
                // reading. How long it actually is, is the platform's timer resolution.
                Thread.Sleep(1);
            }
            catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
            {
                throw new AudioCaptureException(
                    $"'{from.FullName}' was not moved aside, so nothing was removed and the "
                    + $"recording is exactly as it was: {refused.Message}",
                    refused);
            }
        }
    }

    /// <summary>
    /// Takes back the folder a removal made, when the move into it was refused. It does not throw:
    /// it runs while something is already failing, and what the caller has to hear is that refusal.
    /// </summary>
    /// <remarks>
    /// Non-recursive, which is what makes the silence safe — the only thing this can ever take is a
    /// directory that is empty, and a refused move leaves exactly that.
    /// <see cref="BlockSpool.Erase"/> is the same shape for the same reason.
    /// </remarks>
    private static void EraseIfItIsStillEmpty(DirectoryInfo aside)
    {
        try
        {
            Directory.Delete(aside.FullName);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // Swallowed on purpose: see the summary.
        }
    }

    /// <summary>
    /// Removes the moved-aside copy, which is the recording actually going away.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Recursive, and that is right <em>here</em> while
    /// <see cref="EraseWhereNothingWasRecorded"/> two methods down says never: everything under
    /// this folder was put there by this call one instant ago, and it sits at a name nothing in the
    /// product ever looks in. Naming the files instead would mean this method deriving a second
    /// opinion about what a recording's files are called, which is the disagreement
    /// <see cref="WhatSaysARecordingHappenedIn"/> exists to keep to one place.
    /// </para>
    /// <para>
    /// One attempt and no patience budget, unlike the move — the trade is what a refusal costs at
    /// each point. The recording is out of the way by now and nothing offers it any more, so what a
    /// refusal here leaves is bytes nothing points at, said loudly and with the path. A second
    /// budget would double the worst case of a call that runs on a window's thread for a failure
    /// that is already loud and accurate.
    /// </para>
    /// </remarks>
    private static void ThrowAwayTheCopy(DirectoryInfo aside, DirectoryInfo folder)
    {
        try
        {
            Directory.Delete(aside.FullName, recursive: true);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            throw new AudioCaptureException(
                $"The recording that was in '{folder.FullName}' is out of the way and nothing "
                + $"offers it any more, but some of its files are still on disk in "
                + $"'{aside.FullName}': {left.Message} Removing that folder by hand is safe.",
                left);
        }
    }

    /// <summary>
    /// Whether this is a refusal that goes away on its own. Anything else — the destination already
    /// there, a parent that is not, another volume — is the answer and not a delay.
    /// </summary>
    private static bool SomebodyIsStillReading(IOException refused) =>
        refused.HResult is AccessDenied or SharingViolation;

    /// <summary>
    /// Every file a press can leave in a folder it never recorded into, whether or not it is there.
    /// </summary>
    /// <remarks>
    /// One list, read by the question and by the delete, so that the two cannot come to disagree
    /// about what an empty folder is allowed to hold — and so that a third mark is one edit.
    /// </remarks>
    private static FileInfo[] NamesAPressLeaves(DirectoryInfo folder) =>
    [
        .. SpoolsIn(folder),
        SpoolManifest.In(folder),
        SpoolChanges.In(folder),
        new FileInfo(Path.Combine(folder.FullName, CaptureMark.FileName)),
        new FileInfo(Path.Combine(folder.FullName, SavingMark.FileName)),
    ];

    /// <summary>The two spools of a folder, which are the only names carrying a size rule.</summary>
    private static FileInfo[] SpoolsIn(DirectoryInfo folder) =>
    [
        BlockSpool.FileFor(folder, AudioChannel.Loopback),
        BlockSpool.FileFor(folder, AudioChannel.Microphone),
    ];

    /// <summary>
    /// Whether two files in one folder are the same one. Windows decides case for itself, so the
    /// comparison does too rather than trusting how a name was spelled to whoever made it.
    /// </summary>
    private static bool Named(string one, string other) =>
        string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// What is in one folder, or nothing when nothing in it was ever recorded. The card alone is
    /// not a recording — a capture that was refused its devices can leave one — and neither is a
    /// folder somebody made by hand.
    /// </summary>
    /// <remarks>
    /// A spool file is made when its device opens and carries its header from that instant, so its
    /// being there says a device was opened and never that anything was caught. What says something
    /// was caught is a byte past that header, which is what <see cref="UnfinishedSource.Bytes"/> is
    /// for — so a folder whose every spool is its header and nothing else is not a recording, and a
    /// machine killed between the first device opening and its first packet leaves one. It is the
    /// same answer as no spool at all, deliberately: there is nothing in either to keep, to take out
    /// or to play, and offering somebody the choice would be offering them a meeting of nothing.
    /// <para>
    /// Every source that is there stays a source once any of them holds a block. A device that
    /// opened and was taken away before it handed anything over is a channel that recorded silence,
    /// and the meeting is still the other channel's — dropping it would make an hour of one-sided
    /// conversation unrecoverable over the microphone that never worked.
    /// </para>
    /// </remarks>
    private static UnfinishedRecording? Found(DirectoryInfo folder)
    {
        var sources = new[] { AudioChannel.Loopback, AudioChannel.Microphone }
            .Select(channel => (Channel: channel, Blocks: BlockSpool.FileFor(folder, channel)))
            .Where(source => source.Blocks.Exists)
            .Select(source => new UnfinishedSource(source.Channel, source.Blocks, source.Blocks.Length))
            .ToArray();

        if (Array.TrueForAll(sources, source => source.Bytes <= BlockSpool.HeaderBytes))
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
