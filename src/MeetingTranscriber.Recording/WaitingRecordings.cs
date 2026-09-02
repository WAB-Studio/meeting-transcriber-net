using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording;

/// <summary>
/// Why a recording nobody got to stop cannot be made into the meeting it was of.
/// </summary>
/// <remarks>
/// Closed, and one member per way the corpus and the folder can disagree about a recording. It is
/// an enum and not a sentence because the answer reaches a person: what a screen says about each of
/// these is in `UiTexts` in both languages, and a member added here with no words for it is caught
/// by `MeetingCardTextTests` before it can reach anybody as another one.
/// </remarks>
public enum WhyNotAMeeting
{
    /// <summary>Neither the folder's card nor its own name says which meeting this is.</summary>
    NothingSaysWhichMeetingItIs,

    /// <summary>Its card is torn, so what the recording says about itself cannot be read.</summary>
    WhatItSaysAboutItselfCannotBeRead,

    /// <summary>It is sitting in a folder that is not the one its meeting's blocks belong in.</summary>
    ItIsInAnotherMeetingsFolder,

    /// <summary>The recording names a meeting this corpus does not hold.</summary>
    ThisCorpusHasNoSuchMeeting,

    /// <summary>Fewer sources are on disk than a meeting is made of.</summary>
    NotAllOfItsSourcesAreHere,
}

/// <summary>
/// Why a recording cannot become the meeting it was of, and the facts the reason turns on.
/// </summary>
/// <remarks>
/// <para>
/// The two travel together and that is the whole point of the type. The reason alone would leave
/// whoever says it in words to go back to the recording for the folder name and the meeting id, and
/// then nothing anywhere would hold the values it fetched to the reason it fetched them for: two
/// values in the wrong order is a row telling somebody their recording is in a folder named after a
/// meeting, and no test on either side of that seam can see it. Read once, here, beside the branch
/// that chose the reason.
/// </para>
/// <para>
/// <see cref="Says"/> is data and never words — a folder name, a meeting id, a count. What the
/// reason reads as is `UiTexts`', in both languages, and nothing in this project has an opinion
/// about it. That is also the rule for what may go in here: an English sentence this repository
/// wrote would be a translated frame with an untranslated clause inside it, which is the defect
/// this type was made to end.
/// </para>
/// </remarks>
/// <param name="Why">Which of the five it is.</param>
/// <param name="Says">The values that reason's sentence leaves room for, in the order it takes them.</param>
public sealed record NotAMeeting(WhyNotAMeeting Why, IReadOnlyList<object?> Says)
{
    /// <summary>The values that reason's sentence leaves room for, in the order it takes them.</summary>
    public IReadOnlyList<object?> Says { get; } = AsMany(Why, Says);

    /// <summary>
    /// How many values a reason's sentence leaves room for.
    /// </summary>
    /// <remarks>
    /// Declared here and asked everywhere, because otherwise the number is written down three times
    /// and agreed on nowhere: once by the branch that builds the reason, once by the Spanish text
    /// and once by the English one. A screen reads the words with as many values as it was handed,
    /// so an entry wanting one more than its reason carries throws inside a draw, on a list nothing
    /// but a running window builds — the last place to find out. This is what the two ends are held
    /// to: the branch by the constructor below, the words by <c>MeetingCardTextTests</c>, which is
    /// the only project that can see both.
    /// <para>
    /// It says nothing about which value is which. Both of the reasons carrying two carry values of
    /// one type — a folder name beside a meeting id reads as a string either way round, and two
    /// counts are both <c>int</c> — so a count is the one thing about them a compiler could never
    /// have caught, and the order is pinned in <c>WaitingRecordingsTests</c> instead.
    /// </para>
    /// </remarks>
    public static int Values(WhyNotAMeeting why) => why switch
    {
        WhyNotAMeeting.NothingSaysWhichMeetingItIs => 0,
        WhyNotAMeeting.WhatItSaysAboutItselfCannotBeRead => 0,
        WhyNotAMeeting.ItIsInAnotherMeetingsFolder => 2,
        WhyNotAMeeting.ThisCorpusHasNoSuchMeeting => 1,
        WhyNotAMeeting.NotAllOfItsSourcesAreHere => 2,
        _ => throw new ArgumentOutOfRangeException(
            nameof(why), why, "There is no count of values for this reason."),
    };

    /// <summary>
    /// The reason named, with the facts behind it — machine words, for an exception message and for
    /// the developer listing the CLI prints. Never a sentence somebody reads on a screen.
    /// </summary>
    public override string ToString() =>
        Says.Count == 0 ? Why.ToString() : $"{Why}: {string.Join(", ", Says)}";

    /// <summary>Throws unless the reason was handed the values its sentence leaves room for.</summary>
    private static IReadOnlyList<object?> AsMany(WhyNotAMeeting why, IReadOnlyList<object?> says)
    {
        ArgumentNullException.ThrowIfNull(says);

        var wanted = Values(why);

        return says.Count == wanted
            ? says
            : throw new ArgumentException(
                $"'{why}' leaves room for {wanted} values and was given {says.Count}.",
                nameof(says));
    }
}

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

    /// <summary>
    /// Whether a save of this recording is running right now, wherever it is running from.
    /// </summary>
    /// <remarks>
    /// The spool's own answer, which is a mark held in the folder and readable by anything looking
    /// at it — another window, a prompt, the next start. It is the half of "being saved" that
    /// survives the process doing the saving: a screen still has to say so for the stretch between
    /// somebody pressing stop and the finish claiming the folder, and that stretch is its own to
    /// know.
    /// </remarks>
    public bool BeingSaved => Spooled.BeingSaved;

    /// <summary>What its blocks occupy, which is what says a recording caught anything at all.</summary>
    public long Bytes => Spooled.Sources.Sum(source => source.Bytes);

    /// <summary>
    /// Why there is nothing to decide about this recording yet, or nothing when there is — the
    /// spool's own answer, which is where the rule and the three outcomes it shuts both live.
    /// </summary>
    /// <remarks>
    /// Asked before <see cref="Unrecoverable"/>, which answers about a recording that has stopped.
    /// </remarks>
    public string? NothingToDecideYet => Spooled.NothingToDecideYet;

    /// <summary>
    /// Why a recording somebody may decide about cannot become a meeting they play, or nothing
    /// when it can. Asked after <see cref="NothingToDecideYet"/>, never instead of it.
    /// </summary>
    /// <remarks>
    /// Said rather than discovered by pressing the button. Every one of them is a recording
    /// somebody still has to decide about — taking one out to a folder and throwing one away both
    /// stay open — so answering here is the difference between one choice being unavailable and a
    /// recording looking like it is not there. That is what keeps the recording still being
    /// written out of this list: all three are shut on that one, so a reason here that offered the
    /// other two would be a sentence about a meeting still happening that is not true of it.
    /// <see cref="NothingToDecideYet"/> is that case, and what keeps a caller who asked only this
    /// one from acting on the answer is that the engine refuses all three outcomes itself.
    /// <para>
    /// The reason and not the sentence. This used to hand back English prose, which a screen then
    /// printed inside a catalogued frame — so somebody reading in Spanish got "No puede volverse
    /// una reunión: this corpus has no meeting …", half of it in a language they did not choose.
    /// What comes back now is a closed reason and the data it turns on, and the words for it are in
    /// `UiTexts` beside every other sentence a person reads.
    /// </para>
    /// <para>
    /// The torn card is the one that carries nothing. What a spool throws when its card will not
    /// read is a sentence this repository wrote, so putting it on the row would be the same defect
    /// one level down — and the answer the row offers is the same whatever the card says. It is on
    /// the diagnosis surfaces instead, which is where `TheBlocksOfThisOneWouldNotRead` already puts
    /// the same call.
    /// </para>
    /// </remarks>
    public NotAMeeting? Unrecoverable
    {
        get
        {
            if (MeetingId is not Guid meeting)
            {
                return new NotAMeeting(WhyNotAMeeting.NothingSaysWhichMeetingItIs, []);
            }

            // The card is read again by the finish, so a folder whose card is torn cannot become a
            // meeting however much audio is in it. Offering it and then throwing would be this
            // list saying a choice was open and the choice failing on the sentence it already had.
            if (Spooled.Unreadable is not null)
            {
                return new NotAMeeting(WhyNotAMeeting.WhatItSaysAboutItselfCannotBeRead, []);
            }

            // A recording is finished into the folder its meeting's blocks belong in, which is the
            // folder named after the meeting. One sitting under another name would have this
            // report naming one folder and the finish reading another.
            if (!string.Equals(Folder.Name, meeting.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                return new NotAMeeting(
                    WhyNotAMeeting.ItIsInAnotherMeetingsFolder, [Folder.Name, meeting]);
            }

            if (Meeting is null)
            {
                return new NotAMeeting(WhyNotAMeeting.ThisCorpusHasNoSuchMeeting, [meeting]);
            }

            return Spooled.Sources.Count == CapturedAudio.ChannelCount
                ? null
                : new NotAMeeting(
                    WhyNotAMeeting.NotAllOfItsSourcesAreHere,
                    [Spooled.Sources.Count, CapturedAudio.ChannelCount]);
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
        // meeting that has not stopped is not a recording this can find anything wrong with. This
        // is the only one of the three ways in that does not go through `Keep`, `Export` or
        // `Discard` — it reaches `Finish`, and a finish over blocks a capture still holds would
        // read half a meeting.
        recording.Spooled.EnsureThereIsSomethingToDecide();

        // The reason named, with its facts after it — `NotAMeeting.ToString`'s, which is machine
        // words for whoever is debugging. What a person reads is `UiTexts`' and is not built here.
        if (recording.Unrecoverable is { } reason)
        {
            throw new RecordingException(
                $"'{recording.Folder.FullName}' cannot be made into a meeting: {reason}. Its "
                + "blocks are untouched, and taking them out to a folder or throwing them away "
                + "are still open.");
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
