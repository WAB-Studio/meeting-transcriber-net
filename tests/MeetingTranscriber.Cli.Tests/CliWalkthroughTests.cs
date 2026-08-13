using System.Globalization;

using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// The whole path from a paid response to an answer, driven by nothing but command lines: make a
/// corpus, file the response, render it again, rebuild everything, and find what was said.
/// </summary>
/// <remarks>
/// <para>
/// Nothing here opens the corpus itself. Every fact the test acts on is read back out of what a
/// command printed, because a walkthrough that reached into the database to check its own work
/// would stop proving the thing it exists to prove — that somebody with this program and a corpus
/// can get from one to the other without the application.
/// </para>
/// <para>
/// The one file it opens is the <c>utterances.jsonl</c> the import produced, and only to pick a
/// word to search for. A query nobody said would prove nothing about search, and asking the corpus
/// for a word to ask it about would be the reach into the database this test does not make.
/// </para>
/// </remarks>
public class CliWalkthroughTests
{
    /// <summary>
    /// Both channels and 34 minutes: long enough that the turns, the transcript and the index are
    /// doing real work, short enough not to be the reason the suite is slow.
    /// </summary>
    private const string Fixture = DeepgramFixtures.TwoChannelShort;

    private const string StartedAt = "2026-03-04T14:00:00Z";

    [Fact]
    public void A_response_becomes_a_meeting_that_renders_rebuilds_and_is_found_again()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;

        var made = CommandLine.Of("migrate", "--corpus", root);
        made.Code.ShouldBe(Cli.Ok, made.Error);
        made.Output.ShouldContain("(made)");
        File.Exists(corpus.DatabasePath).ShouldBeTrue();

        var imported = Import(root);
        imported.Code.ShouldBe(Cli.Ok, imported.Error);
        var meeting = imported.Value("meeting");
        Guid.TryParse(meeting, out _).ShouldBeTrue(meeting);
        var turns = int.Parse(imported.Value("turns"), CultureInfo.InvariantCulture);
        turns.ShouldBeGreaterThan(0);

        var utterances = new FileInfo(Path.Combine(root, imported.Value("utterances")));
        utterances.Exists.ShouldBeTrue(utterances.FullName);
        File.Exists(Path.Combine(root, imported.Value("transcript"))).ShouldBeTrue();

        // The card that names the meeting, opened as the flat file it is. Reading it back through
        // the corpus would be reading the thing it exists to survive the loss of.
        var card = new FileInfo(Path.Combine(root, imported.Value("manifest")));
        card.Exists.ShouldBeTrue(card.FullName);
        File.ReadAllText(card.FullName).ShouldContain(meeting);

        var status = CommandLine.Of("status", "--corpus", root);
        status.Code.ShouldBe(Cli.Ok, status.Error);
        status.Value("meetings").ShouldBe("1 active");
        status.Value("turns").ShouldBe($"{turns}");

        // Said in the meeting, so a hit is search answering rather than a query matching something
        // the index happened to keep.
        var spoken = Words.SaidIn(utterances);
        var found = CommandLine.Of("search", spoken, "--corpus", root);
        found.Code.ShouldBe(Cli.Ok, found.Error);
        found.Output.ShouldContain(meeting);
        found.Output.ShouldContain("turn #");

        var rendered = CommandLine.Of("render", meeting, "--corpus", root);
        rendered.Code.ShouldBe(Cli.Ok, rendered.Error);
        rendered.Value("turns").ShouldBe($"{turns}");

        var rebuilt = CommandLine.Of("rebuild", "--corpus", root);
        rebuilt.Code.ShouldBe(Cli.Ok, rebuilt.Error);
        rebuilt.Output.ShouldContain("1 meetings");

        // The same question after everything was produced again, which is the property a rebuild
        // has to have and the one nothing else would notice losing.
        CommandLine.Of("search", spoken, "--corpus", root).Output.ShouldBe(found.Output);

        var sound = CommandLine.Of("check", "--corpus", root, "--verify-contents");
        sound.Code.ShouldBe(Cli.Ok, sound.Error);
        sound.Output.ShouldContain("Sound");
    }

    /// <summary>
    /// The same response is the same meeting. A person retrying a command that half worked is the
    /// ordinary way this happens, and a second copy of something paid for once is the wrong answer
    /// to it.
    /// </summary>
    [Fact]
    public void The_same_response_imported_twice_is_one_meeting()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);

        var first = Import(root);
        var second = Import(root);

        second.Code.ShouldBe(Cli.Ok, second.Error);
        second.Value("meeting").ShouldStartWith(first.Value("meeting"));
        second.Output.ShouldContain("already here");
        second.Value("turns").ShouldBe(first.Value("turns"));
        CommandLine.Of("status", "--corpus", root).Value("meetings").ShouldBe("1 active");
    }

    /// <summary>
    /// A response filed under a profile that is not the shape of the audio it came from is refused
    /// with nothing written. It is the contract's own refusal arriving at the command line, and
    /// what it protects is the case where a meeting was billed as something other than what was
    /// sent — which a corpus that quietly accepted it would never be able to say again.
    /// </summary>
    [Fact]
    public void A_response_that_is_not_the_shape_its_profile_promises_is_refused_and_nothing_is_filed()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);

        var refused = CommandLine.Of(
            "import",
            DeepgramFixtures.PathOf(Fixture),
            "--corpus",
            root,
            "--started-at",
            StartedAt,
            "--profile",
            SourceProfile.Diarize.ToWireName());

        refused.Code.ShouldBe(Cli.Refused);
        refused.Error.ShouldContain(SourceProfile.Diarize.ToWireName());
        CommandLine.Of("status", "--corpus", root).Value("meetings").ShouldBe("none");
        Directory.Exists(Path.Combine(root, "meetings")).ShouldBeFalse();
    }

    private static Run Import(string root) => CommandLine.Of(
        "import",
        DeepgramFixtures.PathOf(Fixture),
        "--corpus",
        root,
        "--started-at",
        StartedAt,
        "--profile",
        DeepgramFixtures.ProfileOf(Fixture).ToWireName(),
        "--title",
        "la del presupuesto");
}
