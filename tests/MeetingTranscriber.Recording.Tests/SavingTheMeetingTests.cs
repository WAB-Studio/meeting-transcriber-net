using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-158.7 on the half that decides it: what saving a meeting does, what it says while it is
/// doing it, and that what it files does not depend on anybody watching.
/// </summary>
/// <remarks>
/// A screen is what a person sees this on, and no probe here opens one — a WinUI tree needs a UI
/// thread and a packaged host. What runs here is everything that decides what such a screen has to
/// show: the steps, their order, where each stands, and the reports that move it along.
/// </remarks>
public sealed class SavingTheMeetingTests : IDisposable
{
    private readonly TemporaryCorpus corpus = new();
    private readonly UtcTimestamp now = UtcTimestamp.Parse("2026-09-01T10:00:00.000Z");

    /// <summary>
    /// The steps a save runs are the whole of what saving does and nothing else — letting the
    /// sources go, then writing the meeting down. There is no third one about transcribing,
    /// because stopping starts nothing.
    /// </summary>
    [Fact]
    public void A_save_runs_the_two_steps_there_are_in_the_order_it_runs_them() =>
        SavingTheMeeting.Steps.ShouldBe(
            [SavingWork.LettingTheSourcesGo, SavingWork.WritingTheMeetingDown]);

    /// <summary>
    /// ISC-158.7. What a save is doing is one of its steps, with everything behind it done and
    /// everything ahead of it still to come — which is what a screen marks each line with.
    /// </summary>
    [Fact]
    public void Every_step_stands_behind_the_one_under_way_or_ahead_of_it()
    {
        Standing(SavingWork.LettingTheSourcesGo, SavingWork.LettingTheSourcesGo)
            .ShouldBe(StepStanding.Underway);
        Standing(SavingWork.WritingTheMeetingDown, SavingWork.LettingTheSourcesGo)
            .ShouldBe(StepStanding.NotYet);

        Standing(SavingWork.LettingTheSourcesGo, SavingWork.WritingTheMeetingDown)
            .ShouldBe(StepStanding.Done);
        Standing(SavingWork.WritingTheMeetingDown, SavingWork.WritingTheMeetingDown)
            .ShouldBe(StepStanding.Underway);

        static StepStanding Standing(SavingWork step, SavingWork underway) =>
            SavingTheMeeting.StandingOf(step, underway);
    }

    /// <summary>
    /// A step no save runs is refused rather than placed. Unreachable while the steps are the whole
    /// enum, and the refusal is what says so out loud instead of answering with a standing worked
    /// out from a number that means nothing.
    /// </summary>
    [Fact]
    public void A_step_no_save_runs_is_refused()
    {
        var invented = (SavingWork)99;

        Should.Throw<RecordingException>(
            () => SavingTheMeeting.StandingOf(invented, SavingWork.LettingTheSourcesGo));
        Should.Throw<RecordingException>(
            () => SavingTheMeeting.StandingOf(SavingWork.LettingTheSourcesGo, invented));
    }

    /// <summary>
    /// ISC-158.7 over the work itself: writing the meeting down is announced before any of it has
    /// happened, so what somebody watches for the minutes it takes is what is being done rather
    /// than an account of what already was.
    /// </summary>
    [Fact]
    public void Writing_the_meeting_down_is_announced_before_it_is_done()
    {
        using var context = corpus.OpenMigrated();
        var prepared = MeetingRecordings.Open(context, "es", now);
        Fabricated.Spools(prepared.Spool, seconds: 2);

        var said = new List<SavingWork>();
        var filedWhenTold = new List<int>();

        MeetingRecordings.Finish(
            context,
            prepared.MeetingId,
            now + Duration.FromSeconds(2),
            new Watcher(step =>
            {
                said.Add(step);

                // What the corpus held at the moment it was said. The audio row is the whole of
                // what this step produces, so its absence is what makes "before" a measurement
                // rather than an ordering read off the source.
                filedWhenTold.Add(context.Artifacts.Count(row => row.Kind == ArtifactKind.Audio));
            }));

        said.ShouldBe([SavingWork.WritingTheMeetingDown]);
        filedWhenTold.ShouldBe([0]);

        context.Artifacts.Count(row => row.Kind == ArtifactKind.Audio).ShouldBe(1);
    }

    /// <summary>
    /// ISC-158.3. What the application files and what a prompt files are the same thing: the
    /// application is the one being told how the save is going, and being told changes nothing
    /// about the meeting that comes out of it.
    /// </summary>
    /// <remarks>
    /// Two meetings out of identical spools finished at the same instant, one save watched and one
    /// not, compared on every stored fact that is not the meeting's own identity — the bytes
    /// through their hash, the length, the row describing the audio and the run being closed off.
    /// What it cannot reach is a window really recording one, which needs two devices and a
    /// meeting somebody sat through.
    /// </remarks>
    [Fact]
    public void What_is_filed_is_the_same_whether_or_not_anybody_is_watching()
    {
        using var context = corpus.OpenMigrated();
        var stoppedAt = now + Duration.FromSeconds(2);

        var watched = Recorded();
        var alone = Recorded();

        var onScreen = MeetingRecordings.Finish(context, watched, stoppedAt, new Watcher(_ => { }));
        var atThePrompt = MeetingRecordings.Finish(context, alone, stoppedAt);

        onScreen.Length.ShouldBe(atThePrompt.Length);
        onScreen.Queued.ShouldBe(atThePrompt.Queued);
        onScreen.Audio.Sha256.ShouldBe(atThePrompt.Audio.Sha256);
        onScreen.Audio.ByteSize.ShouldBe(atThePrompt.Audio.ByteSize);
        onScreen.Audio.Kind.ShouldBe(atThePrompt.Audio.Kind);
        onScreen.Audio.Origin.ShouldBe(atThePrompt.Audio.Origin);

        using var reopened = corpus.Open();
        var mine = reopened.Meetings.Single(row => row.Id == watched);
        var yours = reopened.Meetings.Single(row => row.Id == alone);

        mine.Duration.ShouldBe(yours.Duration);
        mine.SourceProfile.ShouldBe(yours.SourceProfile);
        mine.LifecycleState.ShouldBe(yours.LifecycleState);
        mine.UpdatedAt.ShouldBe(yours.UpdatedAt);

        reopened.CaptureRuns.Single(row => row.MeetingId == watched).FinishedAt
            .ShouldBe(reopened.CaptureRuns.Single(row => row.MeetingId == alone).FinishedAt);

        Guid Recorded()
        {
            var prepared = MeetingRecordings.Open(context, "es", now);
            Fabricated.Spools(prepared.Spool, seconds: 2);

            var card = Fabricated.CardFor(prepared.MeetingId, now);
            SpoolManifest.Write(prepared.Spool, card);
            MeetingRecordings.Began(context, card);

            return prepared.MeetingId;
        }
    }

    public void Dispose() => corpus.Dispose();

    /// <summary>
    /// Somebody watching a save, which is what a window is. Written out rather than taken from
    /// <see cref="Progress{T}"/>, whose whole point is putting the report back on the thread that
    /// asked — a test wants it where it was raised, and at the moment it was raised, which is what
    /// the corpus is read at.
    /// </summary>
    private sealed class Watcher(Action<SavingWork> told) : IProgress<SavingWork>
    {
        public void Report(SavingWork value) => told(value);
    }
}
