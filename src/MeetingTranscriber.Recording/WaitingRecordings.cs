using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording;

/// <summary>
/// One recording a corpus is holding that nobody got to stop, and everything somebody needs in
/// order to say what happens to it.
/// </summary>
/// <remarks>
/// <para>
/// Two halves, because there are two accounts of the same recording and they are allowed to
/// disagree. <see cref="Spooled"/> is what is on disk — the card, the blocks, whether a capture is
/// still writing them, and the three things that may be done with them. <see cref="Meeting"/> is
/// what the corpus was told before the first sample. A recording whose corpus was deleted has the
/// first and not the second, and it is still a recording somebody may want taken out.
/// </para>
/// <para>
/// Making one of these reads no block, and <see cref="Read"/> is where every byte is read. That
/// split is the whole shape: two hours of meeting is a few hundred megabytes a source, so the
/// listing is what a start can run before anything is on screen, and what a recording turns out to
/// be worth is asked of the one recording somebody is looking at.
/// </para>
/// </remarks>
/// <param name="Spooled">The folder, as the audio engine sees it.</param>
/// <param name="MeetingId">
/// Which meeting this is, or nothing when neither the card nor the folder's own name says. The
/// card decides when it is there: a recording says which meeting it is, and the folder is only
/// where it happens to be sitting.
/// </param>
/// <param name="Meeting">The row the corpus holds for it, or nothing when this corpus has none.</param>
public sealed record WaitingRecording(UnfinishedRecording Spooled, Guid? MeetingId, Meeting? Meeting)
{
    /// <summary>Where the recording is.</summary>
    public DirectoryInfo Folder => Spooled.Folder;

    /// <summary>Whether a capture still holds these files, which on this machine means a meeting in progress.</summary>
    public bool Running => Spooled.Running;

    /// <summary>What its blocks occupy, which is what says a recording caught anything at all.</summary>
    public long Bytes => Spooled.Sources.Sum(source => source.Bytes);

    /// <summary>
    /// Why there is nothing to decide about this recording yet, or nothing when there is — the
    /// spool's own answer, which is where the rule and the three outcomes it shuts both live.
    /// </summary>
    /// <remarks>
    /// The two questions this type answers about a recording, and they are not the same one.
    /// <see cref="Unrecoverable"/> names the single choice a damaged recording is shut out of,
    /// with the other two still open; this one shuts all three, because the recording is
    /// unfinished rather than broken. Whoever shows a recording asks this first: a meeting that is
    /// still happening is nothing to decide about, and everything the other property has to say
    /// about it is about a recording that has not stopped yet.
    /// </remarks>
    public string? NothingToDecideYet => Spooled.NothingToDecideYet;

    /// <summary>
    /// Why this cannot become a meeting somebody plays, or nothing when it can.
    /// </summary>
    /// <remarks>
    /// Said rather than discovered by pressing the button. Every one of them is a recording
    /// somebody still has to decide about — taking one out to a folder and throwing one away both
    /// stay open — so answering here is the difference between one choice being unavailable and a
    /// recording looking like it is not there. That is what keeps the recording still being
    /// written out of this list: all three are shut on that one, so a reason here that offered the
    /// other two would be a sentence about a meeting still happening that is not true of it.
    /// <see cref="NothingToDecideYet"/> is that case, and the engine refuses all three outcomes
    /// itself, so nothing rests on which of the two a caller thought to ask.
    /// </remarks>
    public string? Unrecoverable
    {
        get
        {
            if (MeetingId is not Guid meeting)
            {
                return "nothing here says which meeting it is";
            }

            // The card is read again by the finish, so a folder whose card is torn cannot become a
            // meeting however much audio is in it. Offering it and then throwing would be this
            // list saying a choice was open and the choice failing on the sentence it already had.
            if (Spooled.Unreadable is { } torn)
            {
                return $"what it says about itself cannot be read: {torn}";
            }

            // A recording is finished into the folder its meeting's blocks belong in, which is the
            // folder named after the meeting. One sitting under another name would have this
            // report naming one folder and the finish reading another.
            if (!string.Equals(Folder.Name, meeting.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return $"it is in '{Folder.Name}', and meeting {meeting}'s recording belongs in a "
                    + "folder of that meeting's own name";
            }

            if (Meeting is null)
            {
                return $"this corpus has no meeting {meeting}";
            }

            return Spooled.Sources.Count == CapturedAudio.ChannelCount
                ? null
                : $"only {Spooled.Sources.Count} of its {CapturedAudio.ChannelCount} sources is "
                    + "here, and a meeting is both";
        }
    }

    /// <summary>
    /// How long this recording is and what survived in it, read through block by block.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The two numbers somebody decides on, and the reason they are a method rather than a
    /// property: answering costs a pass over every byte of every source, which for two hours of
    /// meeting is a few hundred megabytes each. So it is asked per recording, by whoever is
    /// showing one — never inside the listing, which is what a start runs before anything is on
    /// screen.
    /// </para>
    /// <para>
    /// The length is the longest stretch any one source covers rather than a sum or an average.
    /// Two devices recording one meeting cover the same stretch; when one of them was cut off
    /// earlier than the other, the meeting is as long as the one that lasted.
    /// </para>
    /// </remarks>
    public WhatSurvived Read()
    {
        var sources = Spooled.Keep();

        return new WhatSurvived(
            sources.Count == 0
                ? Duration.Zero
                : sources.Max(source => source.Covers),
            sources);
    }
}

/// <summary>What a recording turned out to be worth once its blocks were read through.</summary>
/// <param name="Length">
/// How long it is — the stretch the source that lasted longest covers, which is the meeting the
/// blocks would become.
/// </param>
/// <param name="Sources">What each source held, in channel order.</param>
public sealed record WhatSurvived(Duration Length, IReadOnlyList<SurvivingSource> Sources);

/// <summary>
/// What a start finds waiting in a corpus after the application was killed in the middle of a
/// meeting, and what recovering one of them means.
/// </summary>
/// <remarks>
/// <para>
/// The audio engine already lists the folders and already holds the three choices —
/// <see cref="UnfinishedRecordings"/> — and it is not wrapped here. What this adds is the only
/// thing it cannot know: the corpus. That buys two things, and they are the whole of this type.
/// </para>
/// <para>
/// The first is that recovering means what the product means by it. The engine's <c>Keep</c>
/// reads the blocks through and says what survived; that is a reading and not a meeting, and a
/// person who has just lost a meeting is not owed a report about it. So recovering here is
/// <see cref="MeetingRecordings.Finish"/> — the same call stopping makes — and what comes out is
/// the meeting filed, hashed, as long as it turned out to be, and playable.
/// </para>
/// <para>
/// The second is knowing which folders are actually waiting. Stopping a meeting leaves its blocks
/// where they are, so every meeting ever recorded on this machine has a spool folder, and a list
/// built from the folders alone would offer somebody their entire history as wreckage. The corpus
/// is what tells the two apart: a meeting the corpus knows the length of was finished, and is not
/// waiting for anybody. What becomes of those blocks afterwards is a separate decision and a
/// separate card; nothing here removes them, and <c>recordings --spool</c> still shows every
/// folder on disk to whoever wants one.
/// </para>
/// <para>
/// Nothing here removes anything, and nothing here is done by time. A recording waits until
/// somebody keeps it, takes it out or throws it away, however many starts go past it, and a start
/// that tidied one away would be the crash winning the second time.
/// </para>
/// </remarks>
public static class WaitingRecordings
{
    /// <summary>
    /// Every recording in <paramref name="corpus"/> still waiting for somebody to decide about it,
    /// in the order their folders are named.
    /// </summary>
    /// <remarks>
    /// A meeting still being recorded is in the list and says so, rather than being left out: the
    /// meeting somebody is in the middle of is the last thing to hide, and what it is not is
    /// something to decide about — <see cref="WaitingRecording.NothingToDecideYet"/> says so, and
    /// it is the first thing anything showing one of these asks.
    /// </remarks>
    public static IReadOnlyList<WaitingRecording> In(CorpusDbContext corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        var spooled = UnfinishedRecordings.In(CorpusFiles.SpoolRootIn(corpus.Root));
        if (spooled.Count == 0)
        {
            return [];
        }

        var named = spooled.Select(recording => (Recording: recording, Id: Identified(recording))).ToArray();
        var wanted = named.Select(pair => pair.Id).OfType<Guid>().Distinct().ToArray();

        var meetings = corpus.Meetings
            .Where(meeting => wanted.Contains(meeting.Id))
            .ToDictionary(meeting => meeting.Id);

        return
        [
            .. named
                .Select(pair => new WaitingRecording(
                    pair.Recording,
                    pair.Id,
                    pair.Id is Guid meeting && meetings.TryGetValue(meeting, out var row) ? row : null))
                .Where(StillWaiting),
        ];
    }

    /// <summary>
    /// Makes the meeting this recording is: the blocks become its audio, the corpus is told how
    /// long it was, and the run it was made by is marked as one that came back from a spool.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It is the same finish stopping performs, and deliberately so. A meeting recovered after a
    /// crash is not a lesser meeting — it is the same blocks read the same way — and a second path
    /// that produced a nearly-identical file would be the one nobody exercises until the day
    /// somebody needs it.
    /// </para>
    /// <para>
    /// The run is written again from the card first when the corpus never got it, which is what
    /// the card carries a run id for: a process killed between the devices opening and the row
    /// being committed leaves a recording whose devices only the card remembers.
    /// </para>
    /// <para>
    /// It reads every block, pours two sources onto one timeline and hashes the result on the way
    /// in. For a long meeting that is minutes. <b>Do not call it on a thread somebody is looking
    /// at</b> — the same warning <see cref="MeetingRecording.Stop"/> carries, and for the same
    /// reason.
    /// </para>
    /// </remarks>
    public static FinishedRecording Recover(
        CorpusDbContext corpus, WaitingRecording recording, UtcTimestamp now)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(recording);

        // The spool's own refusal, asked rather than restated, and before the one below it: a
        // meeting that has not stopped is not a recording this can find anything wrong with. It is
        // what stands between a long finish and the blocks a capture is still writing, which is
        // the same thing `Keep`, `Export` and `Discard` each ask before they touch a file.
        recording.Spooled.EnsureThereIsSomethingToDecide();

        if (recording.Unrecoverable is not null)
        {
            throw new RecordingException(
                $"'{recording.Folder.FullName}' cannot be made into a meeting: "
                + $"{recording.Unrecoverable}. Its blocks are untouched, and taking them out to a "
                + "folder or throwing them away are still open.");
        }

        var card = recording.Spooled.Card;
        var meetingId = recording.MeetingId!.Value;

        if (card is not null && !corpus.CaptureRuns.Any(row => row.Id == card.CaptureRunId))
        {
            MeetingRecordings.Began(corpus, card);
        }

        var finished = MeetingRecordings.Finish(corpus, meetingId, now);

        // After the finish and not before. The row says the run came back from a spool, and an
        // attempt that threw on a corrupt block never did: marking it first would leave a run
        // claiming a recovery that did not happen, on a recording still sitting there waiting.
        var run = MeetingRecordings.Ran(corpus, meetingId, card);
        if (run is not null)
        {
            run.Recovered = true;
            corpus.SaveChanges();
        }

        return finished;
    }

    /// <summary>
    /// Whether this recording is still somebody's to decide about, which it is until the meeting
    /// it is of has been finished.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The meeting's length is what says finished, and it is the last thing a finish writes — after
    /// the audio is filed, in the same commit that closes the run off. A finish is three durable
    /// steps and a machine can die between any two of them, so the question has to be asked of the
    /// step that comes last: a recording judged done on the audio row alone would disappear off
    /// this list the moment that row landed, leaving a meeting with no length and an open run that
    /// nothing would ever come back to.
    /// </para>
    /// <para>
    /// Finishing again from the same blocks is what puts that right, and it is always allowed —
    /// the blocks are the recording and the file is read out of them, so it produces the same
    /// bytes every time.
    /// </para>
    /// </remarks>
    private static bool StillWaiting(WaitingRecording recording) =>
        recording.Meeting?.Duration is null;

    /// <summary>
    /// Which meeting a folder holds: what its card says, or failing that the name the folder was
    /// given, which is the meeting id every spool folder is created under.
    /// </summary>
    /// <remarks>
    /// The card first and the folder second. A folder can be renamed, copied or restored somewhere
    /// else by somebody with a file manager; the card is what the recording wrote about itself
    /// while it was being made, and <see cref="MeetingRecordings.Finish"/> refuses the pair when
    /// they disagree rather than picking one.
    /// </remarks>
    private static Guid? Identified(UnfinishedRecording recording) =>
        recording.Card?.MeetingId
        ?? (Guid.TryParse(recording.Folder.Name, out var named) ? named : null);
}
