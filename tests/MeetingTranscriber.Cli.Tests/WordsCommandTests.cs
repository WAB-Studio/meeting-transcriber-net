using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>The <c>words</c> command, driven as somebody at a prompt drives it.</summary>
public class WordsCommandTests
{
    private const string Fixture = DeepgramFixtures.TwoChannelShort;

    [Fact]
    public void A_misspelt_word_comes_back_as_the_meeting_wrote_it()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        var imported = Filed(root);
        var real = Words.SaidIn(new FileInfo(Path.Combine(root, imported.Value("utterances")))).ToLowerInvariant();
        var typed = real[..^1] + (real[^1] is 'z' ? 'q' : 'z');

        var found = CommandLine.Of("words", "--corpus", root, "--like", typed);

        found.Code.ShouldBe(Cli.Ok, found.Error);
        found.Value(real).ShouldContain("time");
        found.Output.ShouldContain("it was written.");
    }

    [Fact]
    public void Nothing_typed_answers_with_a_count_of_suspects()
    {
        using var corpus = new TemporaryCorpus();
        Filed(corpus.Root.FullName);

        var found = CommandLine.Of("words", "--corpus", corpus.Root.FullName);

        found.Code.ShouldBe(Cli.Ok, found.Error);
        found.Output.ShouldContain("it may keep getting wrong.");
    }

    [Fact]
    public void One_meeting_answers_with_a_count_of_its_suspects()
    {
        using var corpus = new TemporaryCorpus();
        var imported = Filed(corpus.Root.FullName);

        var found = CommandLine.Of(
            "words", "--corpus", corpus.Root.FullName, "--meeting", imported.Value("meeting"));

        found.Code.ShouldBe(Cli.Ok, found.Error);
        found.Output.ShouldContain("it may keep getting wrong.");
    }

    [Fact]
    public void Asking_two_questions_at_once_is_a_line_typed_wrong()
    {
        using var corpus = new TemporaryCorpus();
        var imported = Filed(corpus.Root.FullName);

        var refused = CommandLine.Of(
            "words", "--corpus", corpus.Root.FullName, "--like", "nubeko", "--meeting", imported.Value("meeting"));

        refused.Code.ShouldBe(Cli.Misused);
        refused.Error.ShouldContain("--like and --meeting ask two different questions");
    }

    private static Run Filed(string root)
    {
        var made = CommandLine.Of("migrate", "--corpus", root);
        made.Code.ShouldBe(Cli.Ok, made.Error);

        var imported = CommandLine.Of(
            "import-response",
            DeepgramFixtures.PathOf(Fixture),
            "--corpus",
            root,
            "--started-at",
            "2026-03-04T14:00:00Z",
            "--profile",
            DeepgramFixtures.ProfileOf(Fixture).ToWireName());
        imported.Code.ShouldBe(Cli.Ok, imported.Error);
        return imported;
    }
}
