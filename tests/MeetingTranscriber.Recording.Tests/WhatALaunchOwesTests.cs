using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// What a launch owes the corpus, done one piece at a time in a stated order — and what adding a
/// third piece costs somebody, which is the whole reason the list exists.
/// </summary>
/// <remarks>
/// <para>
/// The first four run fabricated chores through the two-argument <c>RunIn</c>, because what they
/// hold is the list's own rules — the order, the one-at-a-time, the boundary a chore that throws
/// runs into — and a real chore cannot be made to throw on demand. The fifth runs the real
/// <see cref="WhatALaunchOwes.InOrder"/> end to end over one corpus, and it is what says the two
/// entries in it are still the sweep and the renders.
/// </para>
/// <para>
/// <see cref="Filed"/> and <see cref="Transcribed"/> are copied from
/// <c>OwedRendersTests</c> rather than shared. <c>tests/MeetingTranscriber.Testing</c> stops at
/// <c>Infrastructure</c> on purpose, so a helper that files a <c>deepgram.json</c> against a
/// meeting cannot live there — moving it is not an improvement waiting to be made.
/// </para>
/// </remarks>
public sealed class WhatALaunchOwesTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.Parse("2026-09-02T09:30:00.000Z");

    /// <summary>
    /// The point of the card: one piece at a time, on one thread, in the order the list states.
    /// Two writers over one SQLite corpus is what this replaced.
    /// </summary>
    /// <remarks>
    /// The thread id is the assertion that does the work, and it is deterministic: any dispatch
    /// that hands a chore to the pool fails it, including the ones that would happen not to overlap
    /// on a quiet machine. The bracketed log catches the other shape, an interleave on this thread.
    /// An in-flight counter was tried on top of both and removed — it is probabilistic where these
    /// two are not, and a third guard over a proved fact is one somebody later has to understand.
    /// </remarks>
    [Fact]
    public void The_work_a_launch_owes_runs_one_piece_at_a_time_in_the_order_it_is_stated()
    {
        using var corpus = new TemporaryCorpus();

        var log = new List<string>();
        var threads = new List<int>();

        LaunchChore Watched(string name) => new(name, _ =>
        {
            threads.Add(Environment.CurrentManagedThreadId);
            log.Add($"{name} in");
            log.Add($"{name} out");
            return [];
        });

        var done = WhatALaunchOwes.RunIn(corpus.Root, [Watched("a"), Watched("b")]);

        log.ShouldBe(["a in", "a out", "b in", "b out"]);
        threads.ShouldAllBe(id => id == Environment.CurrentManagedThreadId);
        done.Ran.ShouldBe(["a", "b"]);
        done.Left.ShouldBeEmpty();
    }

    /// <summary>
    /// What a chore did not manage arrives in one list with the chore's name on it, without the
    /// chore arranging anything — which is what a third one inherits.
    /// </summary>
    /// <remarks>
    /// Declining is what a correct chore does — a folder somebody is recording into is one the
    /// sweep is right to leave — so a chore that declined is one that ran. This is also the fact
    /// that makes every <c>Left.ShouldBeEmpty()</c> elsewhere in this file a real guard: without a
    /// path that fills the list, an empty one proves nothing.
    /// </remarks>
    [Fact]
    public void What_a_piece_of_launch_work_declined_is_said_with_the_chore_that_declined_it()
    {
        using var corpus = new TemporaryCorpus();

        var declined = new LaunchChore("a", _ => ["a folder somebody was recording into"]);
        var quiet = new LaunchChore("b", _ => []);

        var done = WhatALaunchOwes.RunIn(corpus.Root, [declined, quiet]);

        done.Ran.ShouldBe(["a", "b"]);
        done.Left.ShouldBe(["a: a folder somebody was recording into"]);
    }

    /// <summary>
    /// The boundary a third chore inherits instead of arguing: whatever it turns out to raise is
    /// written down, and the rest of what the launch owes still happens.
    /// </summary>
    /// <remarks>
    /// The exception is deliberately of a type nothing on either real chore's path throws. What is
    /// held here is "whatever a third one raises", not a list — a list is what goes wrong the first
    /// time a path is a junction.
    /// </remarks>
    [Fact]
    public void A_piece_of_launch_work_that_throws_does_not_stop_the_ones_behind_it()
    {
        using var corpus = new TemporaryCorpus();

        var broken = new LaunchChore(
            "a", _ => throw new NotSupportedException("there is no clock here"));
        var behind = new LaunchChore("b", _ => []);

        var done = WhatALaunchOwes.RunIn(corpus.Root, [broken, behind]);

        done.Ran.ShouldBe(["b"]);
        done.Left.ShouldBe(["a: there is no clock here"]);
    }

    /// <summary>
    /// The one exception that leaves, which is the same closed exclusion both real chores state for
    /// themselves.
    /// </summary>
    [Fact]
    public void Running_out_of_memory_stops_the_launch_rather_than_being_written_down()
    {
        using var corpus = new TemporaryCorpus();

        var exhausted = new LaunchChore("a", _ => throw new OutOfMemoryException());
        var behind = 0;
        var next = new LaunchChore("b", _ =>
        {
            behind++;
            return [];
        });

        Should.Throw<OutOfMemoryException>(
            () => WhatALaunchOwes.RunIn(corpus.Root, [exhausted, next]));

        behind.ShouldBe(0);
    }

    /// <summary>
    /// The real list, end to end: a launch takes the meeting nobody recorded off the corpus first,
    /// and only then produces the files the meeting that was transcribed is owed.
    /// </summary>
    /// <remarks>
    /// Both effects are read off the disk afterwards rather than out of a live context, and the
    /// press is let go of before the sweep runs — a <see cref="CaptureMark"/> still held reads as a
    /// sweep that correctly declined, which is a line in <c>Left</c> here rather than a silence.
    /// </remarks>
    [Fact]
    public void A_launch_sweeps_the_meetings_nobody_recorded_before_it_renders_what_is_owed()
    {
        using var corpus = new TemporaryCorpus();

        Guid phantom;
        DirectoryInfo spool;

        using (var recording = corpus.OpenMigrated())
        {
            using var prepared = MeetingRecordings.Open(recording, "es", When);
            phantom = prepared.MeetingId;
            spool = prepared.Spool;
        }

        var transcribed = Transcribed(corpus, When + Duration.FromMilliseconds(3_600_000));

        var done = WhatALaunchOwes.RunIn(corpus.Root);

        done.Ran.ShouldBe(["the meetings nobody recorded", "the renders nobody asked for"]);
        done.Left.ShouldBeEmpty();

        using var context = corpus.Open();
        context.Meetings.Any(meeting => meeting.Id == phantom).ShouldBeFalse();
        Directory.Exists(spool.FullName).ShouldBeFalse();

        var rendered = context.Artifacts
            .Where(artifact => artifact.MeetingId == transcribed
                && artifact.Kind != ArtifactKind.DeepgramResponse)
            .ToArray();

        rendered.Select(artifact => artifact.Kind)
            .ShouldBe([ArtifactKind.Transcript, ArtifactKind.Utterances], ignoreOrder: true);

        foreach (var artifact in rendered)
        {
            CorpusFiles.Locate(corpus.Root, artifact.RelativePath).Exists.ShouldBeTrue();
        }
    }

    /// <summary>
    /// A meeting whose transcription has arrived and which has nothing rendered from it — the state
    /// the application finds at launch. The context is let go of before the launch's work runs.
    /// </summary>
    private static Guid Transcribed(TemporaryCorpus corpus, UtcTimestamp startedAt) =>
        Filed(corpus, startedAt, stream =>
        {
            using var response = File.OpenRead(
                DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelOneVoiceMe));
            response.CopyTo(stream);
        });

    /// <summary>A meeting with a response filed against it, under the profile it was recorded on.</summary>
    private static Guid Filed(
        TemporaryCorpus corpus, UtcTimestamp startedAt, Action<Stream> response)
    {
        using var context = corpus.OpenMigrated();
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            StartedAt = startedAt,
            SourceProfile = DeepgramFixtures.ProfileOf(DeepgramFixtures.TwoChannelOneVoiceMe),
            Language = "es",
            CreatedAt = When,
            UpdatedAt = When,
        };

        context.Meetings.Add(meeting);
        context.SaveChanges();

        DurableArtifact.Write(
            context,
            meeting.Id,
            ArtifactKind.DeepgramResponse,
            CorpusFiles.PathFor(meeting.Id, "deepgram.json"),
            When,
            response);

        return meeting.Id;
    }
}
