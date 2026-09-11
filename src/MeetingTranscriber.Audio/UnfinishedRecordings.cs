using System.Diagnostics;

using MeetingTranscriber.Domain.Artifacts;
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

/// <summary>One source whose audio has no single playable file, and why it has none.</summary>
/// <param name="Channel">Which of the two channels it fed.</param>
/// <param name="Why">
/// What <see cref="NoSinglePlaybackException"/> said: which two formats the source came to hold,
/// that both stretches are still in its blocks, and what holds them as one recording.
/// </param>
public sealed record SourceWithNoPlayback(AudioChannel Channel, string Why);

/// <summary>One source whose blocks would not read through, and what stopped the read.</summary>
/// <param name="Channel">Which of the two channels it fed.</param>
/// <param name="Why">
/// What the engine said: a spool that would not open at all, a header that is not a header, a
/// block whose hash is not its bytes. Nothing here is fine, which is what makes it a list of its
/// own rather than another <see cref="SourceWithNoPlayback"/>.
/// </param>
public sealed record SourceThatWouldNotRead(AudioChannel Channel, string Why);

/// <summary>What taking a recording's audio out came to: what landed, and what could not.</summary>
/// <remarks>
/// Three lists and not two, because the two ways a source fails to become a file are not the same
/// thing to whoever reads them. A source in <see cref="NotMade"/> is entirely fine and its audio is
/// all still there; a source in <see cref="WouldNotRead"/> is damaged, and folding the two together
/// would have the report say a corrupt header is "a source that is entirely fine".
/// </remarks>
/// <param name="Exported">One per source that poured, in channel order.</param>
/// <param name="NotMade">
/// One per source that changed to a device handing over another format, in channel order. A source
/// here is a source that is entirely fine — a WAV is one format all the way down and this one is
/// two — so it is reported beside what landed rather than instead of it.
/// </param>
/// <param name="WouldNotRead">
/// One per source whose blocks could not be read through, in channel order. It costs its own file
/// and nothing else: every source that poured is still poured, and this is how the export says
/// which one it could not read.
/// </param>
public sealed record TakenOut(
    IReadOnlyList<ExportedSource> Exported,
    IReadOnlyList<SourceWithNoPlayback> NotMade,
    IReadOnlyList<SourceThatWouldNotRead> WouldNotRead)
{
    /// <summary>
    /// Whether every source this recording has came out.
    /// </summary>
    /// <remarks>
    /// Answered here and asked by the reporters, because it is one judgement about an export and
    /// every surface that carries an export has to reach the same one. Both command-line reporters
    /// turn it into an exit code and a screen would turn it into a sentence; deciding it at each of
    /// them is how the prompt comes to say an export went as asked while a window says it did not.
    /// <para>
    /// <see cref="NotMade"/> does not count against it. A source that changed device mid-meeting has
    /// every block it ever had, and what does not exist is only the single-format file poured for
    /// convenience — so an export that says so is an export that did what it could and said what it
    /// could not, which is a different thing from one that could not read a source's audio.
    /// </para>
    /// </remarks>
    public bool Whole => WouldNotRead.Count == 0;
}

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
    /// Reads every source through and says what survived. Every block is still there afterwards,
    /// which is what keeping it means — what this does write is the mark saying it is being read.
    /// </summary>
    public IReadOnlyList<SurvivingSource> Keep()
    {
        // Asked before the mark, so that a folder whose save is running or whose capture is still
        // writing does not get a file dropped into it by something that is about to be refused
        // anyway. Both refusals reach their own sentence either way; what the order buys is that
        // nothing is left behind on the way to one.
        EnsureThereIsSomethingToDecide();
        using var reading = ReadingMark.Take(Folder);

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
    /// that goes wrong afterwards <em>and is not one source failing</em> takes back what this call
    /// made — the poured files, and the destination folder itself along with every level of it this
    /// call had to make on the way. That is the export itself failing: a name already taken, a
    /// destination that cannot be made, a disk that will not take the bytes. Half of a recording
    /// somebody asked for is worse than a refusal, worse still because the half that landed is what
    /// makes the second attempt refuse the folder. Both halves of that undo are best effort and
    /// silent, so what it promises is that nothing here leaves a folder behind on purpose, not that
    /// a disk holding one open will let go of it.
    /// </para>
    /// <para>
    /// What bounds the undo is the word <em>made</em>: a destination that was already there is left
    /// exactly as it was, empty or not, because deleting a folder somebody had is worse than
    /// leaving behind one this call made. That bound is the reason the folder is remembered on the
    /// way in rather than judged on the way out, and it is why a successful call can end by asking
    /// the file system to remove its own destination and being refused — which is what a folder
    /// with a recording in it does.
    /// </para>
    /// <para>
    /// <b>One source failing costs its own file and nothing else.</b> A source that came to hold
    /// two formats is the one thing that is not something going wrong, so it is said in
    /// <see cref="TakenOut.NotMade"/>; a source whose blocks will not read through is something
    /// going wrong, and it is said in <see cref="TakenOut.WouldNotRead"/>. Either way what landed
    /// for the other sources stays. A spool with a corrupt header used to take the whole export
    /// back, which threw away a <c>loopback.wav</c> that had poured completely and was not damaged
    /// — and a recording nobody stopped is often the only copy of a meeting there will ever be, so
    /// the half that can still be heard is worth more than the tidiness of an all-or-nothing call.
    /// </para>
    /// <para>
    /// Nothing in this method leaves a refused source's half-poured file standing:
    /// <see cref="BlockSpool.ToWav"/> takes back what it poured on its way out, a source whose
    /// spool would not open at all never reached the pour, and the zero-byte name this method
    /// claimed is erased here. That is checked rather than trusted, because
    /// <see cref="BlockSpool.Erase"/> is best effort and a truncated or empty WAV standing beside a
    /// line saying the source was not made is the one outcome worse than refusing the whole call.
    /// A destination this leaves partly full is a destination the next attempt refuses by name,
    /// which is the paragraph above and is why somebody asking again names a folder of their own.
    /// </para>
    /// <para>
    /// The family caught per source is <see cref="AudioCaptureException"/> and nothing wider, and
    /// the line is where the fault is: that family means this machine will not hand over
    /// <em>this source's</em> audio, which is a fact about one channel. Anything else — a full
    /// disk, a destination that will not take the bytes — is the export failing rather than a
    /// source, and an export that cannot write is not an export that leaves half of one behind.
    /// The one thing inside that family that is still refused whole is a
    /// <see cref="SpoolInUseException"/>: on this machine something holding a spool is a capture
    /// writing it, so it is a fact about the meeting and not about the channel, and all three
    /// outcomes refuse a meeting that has not stopped.
    /// </para>
    /// <para>
    /// When <em>every</em> source has failed this still comes back rather than throwing — one
    /// source failing is a fact about that source, and so are two, and the call is how those facts
    /// reach whoever asked — and what it leaves behind is nothing this call made. A destination
    /// holding no file is the same empty folder a refusal would have left, and it is the only trace
    /// an attempt that made nothing has, so it goes back the same way. That needs no rule of its
    /// own: the undo runs on every way out and asks only whether the folder is empty, which after
    /// every source failed it is.
    /// </para>
    /// </remarks>
    public TakenOut Export(DirectoryInfo into)
    {
        ArgumentNullException.ThrowIfNull(into);
        EnsureThereIsSomethingToDecide();

        // Before the destination is made, because the one thing a claim still refuses is the
        // recording having been thrown away since this was found, and that refusal must not leave
        // an empty destination folder behind. Outside the `try`, so the catch that erases what was
        // claimed runs before the mark is let go of.
        using var reading = ReadingMark.Take(Folder);

        // What this made is remembered rather than derived afterwards, because `Create` does not
        // tell a folder it made from one it found, and only what it made is ever taken back.
        var made = UnfinishedRecordings.MakeTheDestination(into);
        var claimed = new List<FileInfo>();
        try
        {
            var exported = new List<ExportedSource>();
            var notMade = new List<SourceWithNoPlayback>();
            var wouldNotRead = new List<SourceThatWouldNotRead>();

            foreach (var source in Sources)
            {
                var wav = new FileInfo(Path.Combine(
                    into.FullName, BlockSpool.PlaybackFor(source.Blocks).Name));

                Claim(wav);
                claimed.Add(wav);
            }

            foreach (var (source, wav) in Sources.Zip(claimed))
            {
                try
                {
                    var replayed = BlockSpool.ToWav(source.Blocks, wav);

                    // The handle answered whether the file was there before it held anything, and
                    // that answer is the one a caller would read off what came back.
                    wav.Refresh();
                    exported.Add(
                        new ExportedSource(source.Channel, wav, replayed.Blocks, replayed.Discarded));
                }
                catch (NoSinglePlaybackException cannot)
                {
                    // First, because it is the narrower type and the one that is not a failure.
                    wav.Refresh();
                    if (wav.Exists)
                    {
                        throw new AudioCaptureException(
                            $"The pour into '{wav.FullName}' stopped where the source changed "
                            + "format, and what it had written is still there. A part of a source "
                            + "under the name of the whole one is worse than no file at all, so "
                            + "nothing of this export is left standing.",
                            cannot);
                    }

                    notMade.Add(new SourceWithNoPlayback(source.Channel, cannot.Message));
                }
                // A spool something is holding is not a damaged source: on this machine a handle on
                // blocks is a capture writing them, so the meeting has not stopped, and a meeting
                // that is still happening is refused whole by all three outcomes rather than
                // exported in part. `EnsureThereIsSomethingToDecide` asks that on the way in; a
                // capture that took hold since the recording was found lands here instead, and goes
                // past this clause to the outer catch, which takes the whole export back.
                // What comes out is `SpoolReader`'s sentence about a block file rather than the one
                // about a meeting — the shape `EnsureThereIsSomethingToDecide` exists to avoid — and
                // that is unchanged from before this clause existed and is what
                // `None_of_the_three_outcomes_lands_on_a_meeting_that_is_still_being_recorded`
                // pins. Asking the question here instead, by opening the file again, would ask it a
                // second time and get a second answer: a capture that stopped in between makes an
                // intact source read as damaged, and one that started throws away a `loopback.wav`
                // that poured completely, which is the loss this clause exists to stop.
                catch (AudioCaptureException unreadable) when (unreadable is not SpoolInUseException)
                {
                    // The claim is erased here and not by `ToWav`, which never ran for a spool
                    // that would not open at all — so what stands under this name is the zero-byte
                    // file `Claim` left, and a file under a source's name holding nothing is a file
                    // somebody would play and hear silence from.
                    BlockSpool.Erase(wav);
                    wav.Refresh();
                    if (wav.Exists)
                    {
                        throw new AudioCaptureException(
                            $"'{source.Blocks.FullName}' could not be read through and "
                            + $"'{wav.FullName}' could not be taken back, so what stands under that "
                            + "name is not this source's audio. A file under the name of a source "
                            + "is worse than no file at all, so nothing of this export is left "
                            + "standing.",
                            unreadable);
                    }

                    wouldNotRead.Add(new SourceThatWouldNotRead(source.Channel, unreadable.Message));
                }
            }

            return new TakenOut(exported, notMade, wouldNotRead);
        }
        catch
        {
            foreach (var wav in claimed)
            {
                BlockSpool.Erase(wav);
            }

            throw;
        }
        finally
        {
            // Unconditional, and the emptiness of the folder is what decides rather than a second
            // opinion held here: a destination with a file in it is refused by the non-recursive
            // delete, whether that file is one that poured or one somebody else put there. Asking
            // `exported.Count` as well would be a second authority on a question the disk already
            // answers, and it would leave the every-source-refused case and the throwing case
            // spelled two different ways.
            // `finally` rather than the end of the `catch` for the ordering: it runs after the loop
            // above, and a folder is not empty until what was claimed in it has gone — `Claim`
            // leaves a zero-byte file standing under every name it took.
            UnfinishedRecordings.TakeBackWhatWasMade(made);
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
    /// <b>Do not call this on a thread somebody is looking at.</b> Removing a recording is removing
    /// its files, which for a two-hour meeting is a few hundred megabytes per source and takes as
    /// long as the disk takes — that was always true here and has not changed. What this change
    /// adds on top is bounded and lands only where a removal is being refused: each of the two
    /// halves waits out a holder for up to
    /// <see cref="UnfinishedRecordings.RemovalPatienceMilliseconds"/>, so a discard that fails
    /// costs at most two of those beyond the work it did. Nothing is waited out when nothing is
    /// holding the folder.
    /// </para>
    /// </remarks>
    public void Discard()
    {
        // Both, and in this order, and neither of them is what makes this safe. The move below is
        // the authority and these two are only the sentence — a save or a read that starts between
        // the check and the rename is refused by Windows on the rename, with the folder exactly as
        // it was.
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
    /// Both halves of a removal spend it, so a discard that is refused costs at most two of these
    /// on top of whatever work it did. Internal because nothing outside this file has a product
    /// reason to read it.
    /// </para>
    /// </remarks>
    internal const int RemovalPatienceMilliseconds = 250;

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
    /// Throws unless every one of this recording's files is a spool nobody is writing, and nothing
    /// is saving it and nothing is reading it.
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
    /// a rename — <see cref="Remove"/> — and a rename either happens or does not, so a save or a
    /// read that starts after this line is refused with the folder exactly as it was. What that refusal
    /// cannot say is <em>which</em> of the things it is; that is what this is for, said while it is
    /// still possible to say it. The one thing here that is not a sentence is the spool opens: a
    /// file named like a spool that is not one is something no rename can tell apart, so this is
    /// still what stands between <c>--discard</c> and the wrong directory.
    /// </para>
    /// <para>
    /// The spool opens are wrapped for the one refusal <see cref="SpoolReader.Open"/> cannot phrase
    /// on its own: the folder having gone between the listing and this line, which is a second
    /// window or a prompt having discarded the same recording while this one was being decided
    /// about. Windows answers that with <em>Could not find a part of the path
    /// '…\loopback.blocks'</em> — a sentence about a block file, which is the shape every refusal
    /// in this engine is held not to have, arriving on exactly the race a removal exists to
    /// survive.
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

        // Second, because a save is also a read of these blocks and has the more specific thing to
        // tell somebody about what they are waiting for.
        if (ReadingMark.IsHeldIn(recording.Folder))
        {
            throw new AudioCaptureException(
                $"Something on this machine is reading the recording in '{recording.Folder.FullName}' "
                + "right now — a list of what is waiting, a keep, or an export — and blocks somebody is "
                + "reading are not something to throw away underneath them. Once the read is over — or "
                + "once whatever is running it is gone — throwing it away is open again.");
        }

        foreach (var source in recording.Sources)
        {
            try
            {
                SpoolReader.Open(source.Blocks).Dispose();
            }
            catch (DirectoryNotFoundException gone)
            {
                throw new AudioCaptureException(
                    $"Nothing was removed: there is no longer a recording in "
                    + $"'{recording.Folder.FullName}'. Something else threw it away while this was "
                    + "being decided about — another window, or an answer typed at a prompt.",
                    gone);
            }
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
    /// <see cref="ReadingMark"/> now covers the first two of those three, which is what lets a
    /// refusal name a reader; the rename stays the authority, and it is the only one for the holders
    /// nothing here wrote — a scanner, a backup, a file manager.
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
    /// Internal until something outside this project has a product reason to remove a recording.
    /// A public one beside <see cref="UnfinishedRecording.Discard"/> would today be a second way to
    /// remove a recording with neither
    /// <see cref="UnfinishedRecording.EnsureThereIsSomethingToDecide"/> nor
    /// <see cref="EnsureRemovable"/> in front of it — and the sweep that holds this rule greps for
    /// spellings rather than for visibility, so the modifier is what holds it and nothing else
    /// would object. Widening it is one line, and whoever does has to say what stands in front of
    /// it instead.
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
                $"Nothing was removed: there is nothing above '{folder.FullName}', so there is "
                + "nowhere to move it aside to.");

        var aside = new DirectoryInfo(Path.Combine(above.FullName, RecordingFiles.BeingRemovedPrefix + folder.Name));

        MakeTheAsideReady(aside, folder);

        try
        {
            MoveWaitingOutWhoeverHasIt(
                folder, new DirectoryInfo(Path.Combine(aside.FullName, folder.Name)));
        }
        catch
        {
            // Empty, whether this call made it or found it that way, so it goes back rather than
            // being left as a folder every start after a refusal has to have an opinion about.
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
    /// filled holds only files this engine makes before there is anything to record — the three
    /// marks, a spool carrying its header and nothing else, the card, and the note of a channel
    /// somebody moved — and anything else in it is either a recording or somebody's.
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
    /// <b>The delete is the authority and the question above it is only the sentence.</b> A press
    /// that claimed this folder between the two holds <see cref="CaptureMark"/> in a share mode that
    /// forbids unlinking, so the first delete throws and the folder stands with its meeting — which
    /// is why nothing here asks whether a mark is held. What Windows answers cannot be out of date;
    /// what a listing answered a moment ago can.
    /// </para>
    /// <para>
    /// The claimant is the press and not the capture, which is what makes that hold. A meeting's
    /// folder is claimed where it is made, in <c>MeetingRecordings.Open</c>, and the claim is
    /// handed on to the session — so this runs into a mark from the moment the folder exists,
    /// rather than from the moment a device is opened.
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
            File.Delete(file.FullName);
        }

        // Both removals name the type they take, which is the convention `UnfinishedRecordingsTests`
        // rests on: it reads text, and a handle already in hand says nothing about whether what is
        // under it is a file or a folder. The folder still goes non-recursively, for the reason the
        // refusal above gives.
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
    /// Runs <paramref name="attempt"/> until Windows stops saying somebody else still has a file
    /// open, and hands back the refusal it gave up on, or nothing when it went through.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves of a removal use it, and that is the point: a rename and a delete meet the same
    /// holder in the same window, so a wait that only one of them got would be waiting where it was
    /// cheapest rather than where it was worth most. It hands the refusal back rather than throwing
    /// because what each half has to say about giving up is different, and that sentence belongs
    /// where the half is.
    /// </para>
    /// <para>
    /// The wait is not insurance against a rare machine. <see cref="EnsureRemovable"/> opens and
    /// closes every spool roughly a millisecond before this runs, and whatever reads a file the
    /// moment it is closed — a real-time scanner is the usual explanation — is the most likely
    /// holder of all. A build that drops the retry produces a discard that fails intermittently on
    /// real machines and never on a build agent.
    /// </para>
    /// <para>
    /// A refusal that is <em>not</em> somebody reading is not caught here at all: it is the answer,
    /// so it goes straight out to the caller's own wrap. A destination already there, a parent that
    /// is not, another volume, a denied ACL, and <see cref="PathTooLongException"/>, which the two
    /// extra levels of path can produce and which nothing was ever going to fix by waiting.
    /// </para>
    /// <para>
    /// <c>tests/MeetingTranscriber.Testing/Folders.cs</c> holds a second copy of this loop for the
    /// same Windows fact, and the duplication is deliberate. The two projects share
    /// <c>MeetingTranscriber.Domain</c>, which is plain <c>net10.0</c> and could hold it without a
    /// new project reference or a target framework change anywhere — so the boundary does not
    /// forbid sharing, and saying it did would be a false impossibility. What decides it is that
    /// <c>Domain</c> holds no filesystem code at all: it is the audio contract, time and jobs, and
    /// an NTFS rename-retry put there would make the one project with no I/O in it depend on how
    /// Windows behaves when a scanner reopens a file. DRY and that pull apart, and keeping
    /// <c>Domain</c> clean wins. Each copy names the other so neither is deleted as an oversight.
    /// </para>
    /// </remarks>
    private static IOException? WaitingOutWhoeverIsStillReading(Action attempt)
    {
        var clock = Stopwatch.StartNew();

        while (true)
        {
            try
            {
                attempt();
                return null;
            }
            catch (IOException refused) when (SomebodyIsStillReading(refused))
            {
                if (clock.ElapsedMilliseconds >= RemovalPatienceMilliseconds)
                {
                    return refused;
                }

                // A sleep rather than a spin, so waiting does not take the core off whoever is
                // reading. How long it actually is, is the platform's timer resolution.
                Thread.Sleep(1);
            }
        }
    }

    /// <summary>
    /// Renames <paramref name="from"/> to <paramref name="to"/>, waiting out whatever still has a
    /// file under it open. Nothing is removed when it refuses, and the recording is untouched.
    /// </summary>
    private static void MoveWaitingOutWhoeverHasIt(DirectoryInfo from, DirectoryInfo to)
    {
        try
        {
            if (WaitingOutWhoeverIsStillReading(() => Directory.Move(from.FullName, to.FullName))
                is { } refused)
            {
                throw new AudioCaptureException(
                    $"Nothing was removed from '{from.FullName}': something on this machine still "
                    + "has a file in it open, and it was still refusing "
                    + $"{RemovalPatienceMilliseconds} ms later. A recording is thrown away by "
                    + "moving its folder aside first, so a refusal leaves it exactly as it was — "
                    + "every block, the card and the changes beside them are still where they "
                    + "were. Once whatever is reading it lets go, throwing it away is open again.",
                    refused);
            }
        }
        catch (Exception refused) when (refused is IOException or UnauthorizedAccessException)
        {
            // What was attempted, and never a claim about the recording: this also catches the
            // folder having gone, which is another discard of the same recording having won, and
            // that recording is removed rather than as it was.
            throw new AudioCaptureException(
                $"'{from.FullName}' was not moved aside and nothing here removed anything: "
                + refused.Message,
                refused);
        }
    }

    /// <summary>
    /// Takes back an empty folder this class has no further use for. It does not throw: both of its
    /// callers run while something is already failing, and what the caller has to hear is that
    /// refusal.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Non-recursive, which is what makes the silence safe — the only thing this can ever take is a
    /// directory that is empty. <see cref="BlockSpool.Erase"/> is the same shape for the same
    /// reason. It is one half of a pair and neither half is redundant: this keeps the common
    /// refusal from leaving a folder behind, and <see cref="MakeTheAsideReady"/> waving an existing
    /// empty one through is what keeps a refusal <em>here</em> from leaving a recording nobody can
    /// ever discard. Deleting either half re-opens the case the other one covers.
    /// </para>
    /// <para>
    /// Its two callers hold <em>opposite</em> bounds and this method holds neither: <see cref="Remove"/>
    /// hands over an aside deliberately whether this call made it or found it that way, and
    /// <see cref="TakeBackWhatWasMade"/> hands over only levels its own call created, because an
    /// export must never take a destination the person already had. Which folders may be offered is
    /// the caller's question and the emptiness of one is this method's, so it stays
    /// <see langword="private"/>: <see cref="Remove"/>'s remarks say the sweep behind ISC-125 greps
    /// for spellings rather than for visibility, and an <see langword="internal"/> door into
    /// <see cref="Directory.Delete"/> is one nothing in that sweep would object to.
    /// </para>
    /// </remarks>
    private static void EraseIfItIsStillEmpty(DirectoryInfo folder)
    {
        try
        {
            Directory.Delete(folder.FullName);
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            // Swallowed on purpose: see the summary.
        }
    }

    /// <summary>
    /// Makes <paramref name="into"/> and every level above it that is missing, and hands back
    /// exactly the levels this call created, leaf first, for <see cref="TakeBackWhatWasMade"/> to
    /// take back when what they were made for does not happen.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The answer is taken before the call rather than derived after it, because
    /// <see cref="DirectoryInfo.Create"/> is <see cref="Directory.CreateDirectory"/> and does not
    /// tell creating from finding: afterwards every level is simply there, and nothing says which
    /// of them were not a moment ago. So the walk up from the leaf happens first and stops at the
    /// first level that is already there — a folder somebody already had is never in the list and
    /// is therefore never taken back. Deleting one somebody had would be worse than leaving an
    /// empty one behind, and that is the whole reason this remembers rather than asking afterwards.
    /// </para>
    /// <para>
    /// That remembering is a snapshot, not a lock, and it is not the last word. Nothing serialises
    /// the walk against the <see cref="DirectoryInfo.Create"/> that follows it, so a level somebody
    /// else made in between is one this call will believe it made. What holds when the snapshot is
    /// wrong is the other end: <see cref="EraseIfItIsStillEmpty"/> is non-recursive, so a level that
    /// turns out to be somebody's stays the moment anything is in it. The remembering is what keeps
    /// an <em>empty</em> folder somebody had, which nothing else would.
    /// </para>
    /// <para>
    /// It sits here rather than beside <see cref="UnfinishedRecording.Export"/>, its only caller,
    /// because <see cref="EraseIfItIsStillEmpty"/> is what the undo is made of and that is private
    /// to this class. Reaching it from the other type in this file would mean widening it, and the
    /// paragraph on it says what that costs.
    /// </para>
    /// <para>
    /// It is the chain and not the leaf because one call makes every missing level. A destination
    /// two levels below anything that existed would otherwise leave the level above it standing,
    /// which is the same empty folder under a shorter name.
    /// </para>
    /// <para>
    /// A create that is refused partway is undone here rather than left to the caller, and it is
    /// the failure this pair exists for: a denied path, a read-only volume, a disk that filled.
    /// <c>CreateDirectory</c> may have made two levels before the third is refused, and this is the
    /// one shape where the caller never receives a list to take back, because the throw comes out
    /// in place of the assignment. So the undo happens on the way out of this method, where the
    /// list is already in hand, and what the caller sees is the file system's own refusal with
    /// nothing of this attempt left standing under it.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<DirectoryInfo> MakeTheDestination(DirectoryInfo into)
    {
        var missing = new List<DirectoryInfo>();

        for (var level = into; level is not null; level = level.Parent)
        {
            // Asked of the disk rather than of the handle: a `DirectoryInfo` answers from whenever
            // it last looked, and `into` is one the caller built before any of this ran.
            level.Refresh();
            if (level.Exists)
            {
                break;
            }

            missing.Add(level);
        }

        try
        {
            into.Create();
        }
        catch
        {
            TakeBackWhatWasMade(missing);
            throw;
        }

        return missing;
    }

    /// <summary>
    /// Takes back whichever of the levels <see cref="MakeTheDestination"/> made are still empty. It
    /// is best effort and silent, exactly as <see cref="EraseIfItIsStillEmpty"/> is, so a level it
    /// could not take is one nobody hears about.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Leaf first, and that ordering is the whole of it: a parent is not empty until its child has
    /// gone. It does not stop when a level survives and does not need to — <paramref name="made"/>
    /// is a parent chain, so a level something else put a file into keeps every level above it
    /// non-empty and each delete after it is refused on its own. That containment is what holds the
    /// walk, not the loop; reversing the order would break it while leaving every line intact.
    /// A level that is not there at all — the leaf of a create that was refused on it — is the same
    /// silence.
    /// </para>
    /// <para>
    /// It inherits exactly <see cref="EraseIfItIsStillEmpty"/>'s swallow, <see cref="IOException"/>
    /// and <see cref="UnauthorizedAccessException"/> and nothing wider, and both of its callers are
    /// on their way to throwing something else: <see cref="MakeTheDestination"/>'s own <c>catch</c>,
    /// which is about to rethrow the file system's refusal, and the <c>finally</c> in
    /// <see cref="UnfinishedRecording.Export"/>, which on the failing path runs while an
    /// <see cref="AudioCaptureException"/> is in flight. Anything escaping here replaces the
    /// sentence the person was about to read with one about a folder. Whoever widens that clause is
    /// choosing which of two failures somebody hears about, and the answer is not obviously this one.
    /// </para>
    /// </remarks>
    internal static void TakeBackWhatWasMade(IReadOnlyList<DirectoryInfo> made)
    {
        foreach (var folder in made)
        {
            EraseIfItIsStillEmpty(folder);
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
    /// It waits out a holder exactly as the move does, and the reason is what a refusal costs
    /// rather than symmetry. A refused move is recoverable — the recording is still offered, and
    /// pressing Discard again is the whole of the remedy. A refusal here is not: the recording is
    /// already out from under every name the product looks in, so nothing offers it a second time
    /// and what is left waits for somebody to read a page and delete a folder by hand. The
    /// unrecoverable half is the last one to leave without a wait.
    /// </para>
    /// </remarks>
    private static void ThrowAwayTheCopy(DirectoryInfo aside, DirectoryInfo folder)
    {
        try
        {
            if (WaitingOutWhoeverIsStillReading(
                () => Directory.Delete(aside.FullName, recursive: true)) is { } refused)
            {
                throw SomeOfItIsStillThere(refused);
            }
        }
        catch (Exception left) when (left is IOException or UnauthorizedAccessException)
        {
            throw SomeOfItIsStillThere(left);
        }

        AudioCaptureException SomeOfItIsStillThere(Exception left) => new(
            $"The recording that was in '{folder.FullName}' is out of the way and nothing offers "
            + $"it any more, but some of its files are still on disk in '{aside.FullName}': "
            + $"{left.Message} Removing that folder by hand is safe.",
            left);
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
    /// about what an empty folder is allowed to hold. A fourth mark is no longer the one edit the
    /// third was: the type, a name in
    /// <see cref="MeetingTranscriber.Domain.Artifacts.RecordingFiles"/> — which is where the names
    /// these read come from — an arm in
    /// <see cref="MeetingTranscriber.Domain.Artifacts.RecordingFiles.WhatIsInASpoolFolder"/>, and a
    /// line here. Skipping the third is what put the second and third marks into <c>corpus check</c>
    /// as recordings to recover, so it is not left to a count in a comment:
    /// <c>RecordingFileNamesTests</c> fails on a name the engine declares and that method cannot
    /// place.
    /// </remarks>
    private static FileInfo[] NamesAPressLeaves(DirectoryInfo folder) =>
    [
        .. SpoolsIn(folder),
        SpoolManifest.In(folder),
        SpoolChanges.In(folder),
        new FileInfo(Path.Combine(folder.FullName, CaptureMark.FileName)),
        new FileInfo(Path.Combine(folder.FullName, ReadingMark.FileName)),
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
