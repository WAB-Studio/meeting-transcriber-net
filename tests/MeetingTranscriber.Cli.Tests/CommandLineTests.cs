using System.Reflection;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Cli.Tests;

/// <summary>
/// What the command line does with a corpus that is not there, not current or not sound, and with
/// a line that does not say what it looks like it says.
/// </summary>
/// <remarks>
/// This is the diagnostic half, and the reason it is worth its own suite: the commands here are
/// the ones somebody runs when the application will not open, so each of them has to answer rather
/// than fail, and each answer has to be one a script can act on — hence an exit code per kind of
/// refusal rather than one for everything that went wrong.
/// </remarks>
public class CommandLineTests
{
    private const string Fixture = DeepgramFixtures.TwoChannelShort;

    [Fact]
    public void Nothing_at_all_is_the_usage_and_a_misuse()
    {
        var run = CommandLine.Of();

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("usage:");
        run.Output.ShouldBeEmpty();
    }

    [Fact]
    public void Asking_for_help_names_every_command()
    {
        var run = CommandLine.Of("--help");

        run.Code.ShouldBe(Cli.Ok);
        foreach (var command in new[]
        {
            "migrate", "status", "check", "sweep", "restore", "compact", "import-response",
            "import-audio", "render", "rebuild", "devices", "capture", "recordings", "recover",
            "search",
        })
        {
            run.Output.ShouldContain(command);
        }
    }

    [Fact]
    public void A_command_this_program_does_not_have_says_so()
    {
        var run = CommandLine.Of("transcribe", "--corpus", ".");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("transcribe");
    }

    /// <summary>
    /// The failure this refusal exists for: a check that quietly ignored <c>--verify-content</c>
    /// would report a corpus as sound without ever having hashed anything, which is the wrong
    /// answer arriving as the right one.
    /// </summary>
    [Fact]
    public void An_option_no_command_reads_stops_the_command()
    {
        using var corpus = new TemporaryCorpus();
        CommandLine.Of("migrate", "--corpus", corpus.Root.FullName);

        var run = CommandLine.Of("check", "--corpus", corpus.Root.FullName, "--verify-content");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("--verify-content");
    }

    [Fact]
    public void The_corpus_to_work_on_is_never_guessed()
    {
        var run = CommandLine.Of("status");

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain(Corpus.Option);
    }

    /// <summary>
    /// A directory with no corpus in it is a path typed wrong far more often than it is a corpus
    /// somebody meant to start, and answering a typo with an empty corpus reads as success.
    /// </summary>
    [Fact]
    public void A_corpus_that_is_not_there_is_refused_rather_than_made()
    {
        using var corpus = new TemporaryCorpus();

        var run = CommandLine.Of("status", "--corpus", corpus.Root.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain(CorpusDatabase.DatabaseName);
        run.Error.ShouldContain("migrate");
        File.Exists(corpus.DatabasePath).ShouldBeFalse();
    }

    /// <summary>
    /// A corpus this build's schema has moved past: everything refuses and names what to do about
    /// it, and the one command whose whole job is saying what state the corpus is in still answers.
    /// </summary>
    [Fact]
    public void A_corpus_behind_this_build_is_refused_by_everything_but_status()
    {
        using var corpus = new TemporaryCorpus();
        using (var context = corpus.Open())
        {
            // A database with none of this build's migrations applied to it, which is what a
            // corpus written by an older build looks like from here.
            context.Database.ExecuteSqlRaw("CREATE TABLE meetings (id TEXT PRIMARY KEY);");
        }

        var refused = CommandLine.Of("rebuild", "--corpus", corpus.Root.FullName);
        refused.Code.ShouldBe(Cli.Refused);
        refused.Error.ShouldContain("migrate");

        var status = CommandLine.Of("status", "--corpus", corpus.Root.FullName);
        status.Code.ShouldBe(Cli.Ok, status.Error);
        status.Value("schema").ShouldContain("behind this build");
    }

    /// <summary>
    /// A create that was cut off leaves a <c>corpus.db</c> with nothing in it, and SQLite will not
    /// put an empty file into WAL — so before this it was a file that stopped a corpus from ever
    /// being made in that directory, answering every attempt with <c>attempt to write a readonly
    /// database</c>, which reads as a permissions problem and is not one.
    /// </summary>
    [Fact]
    public void A_corpus_db_with_nothing_in_it_is_not_a_corpus_and_migrate_makes_one()
    {
        using var corpus = new TemporaryCorpus();
        File.WriteAllBytes(corpus.DatabasePath, []);

        var refused = CommandLine.Of("status", "--corpus", corpus.Root.FullName);
        refused.Code.ShouldBe(Cli.Refused);
        refused.Error.ShouldContain("empty");

        var made = CommandLine.Of("migrate", "--corpus", corpus.Root.FullName);

        made.Code.ShouldBe(Cli.Ok, made.Error);
        CommandLine.Of("status", "--corpus", corpus.Root.FullName).Value("meetings").ShouldBe("none");
    }

    /// <summary>
    /// A file that is not a database at all, which is what half a copy of one looks like. SQLite's
    /// own words are kept — the same error covers a corpus this user may not write to — and what
    /// is added is which file, because that is the part a person cannot work out from the message.
    /// </summary>
    [Fact]
    public void A_corpus_db_that_is_not_a_database_is_refused_naming_the_file()
    {
        using var corpus = new TemporaryCorpus();
        File.WriteAllText(corpus.DatabasePath, "half of something that was being copied here");

        var run = CommandLine.Of("status", "--corpus", corpus.Root.FullName);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain(corpus.DatabasePath);
        run.Error.ShouldContain("did not open as a corpus");
    }

    /// <summary>
    /// The whole point of the check: a file the corpus claims and does not have is named, with the
    /// path a person can go and look at, and the exit code says so without anybody reading the text.
    /// </summary>
    [Fact]
    public void A_file_the_corpus_claims_and_does_not_have_is_named()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);
        var imported = Import(root);

        File.Delete(Path.Combine(root, imported.Value("transcript")));

        var run = CommandLine.Of("check", "--corpus", root);

        run.Code.ShouldBe(Cli.Refused);
        run.Output.ShouldContain(imported.Value("transcript"));
        run.Output.ShouldContain($"{ArtifactState.Missing}");
    }

    /// <summary>
    /// A write that never finished was never an artifact, so removing one is the only repair that
    /// needs nobody's decision.
    /// </summary>
    [Fact]
    public void A_write_that_never_finished_is_swept_and_nothing_else_is()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);
        var imported = Import(root);

        var unfinished = Path.Combine(root, imported.Value("transcript") + CorpusFiles.UnfinishedSuffix);
        File.WriteAllText(unfinished, "half a file");

        var run = CommandLine.Of("sweep", "--corpus", root);

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Output.ShouldContain("1 unfinished write(s) removed.");
        File.Exists(unfinished).ShouldBeFalse();
        File.Exists(Path.Combine(root, imported.Value("transcript"))).ShouldBeTrue();
    }

    /// <summary>
    /// A write somebody is still making is left where it is, and the command says so rather than
    /// reporting the nothing it removed.
    /// </summary>
    /// <remarks>
    /// The ordinary way this command is run: at a prompt, beside an application that is rendering
    /// meetings. An unfinished write is held open for as long as it is being made, so the sweep is
    /// refused it — and told only how many it took, somebody who has just read a report of
    /// unfinished writes cannot tell that from a sweep that is not working.
    /// </remarks>
    [Fact]
    public void A_write_still_being_made_is_left_where_it_is_and_named()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);
        var imported = Import(root);

        var unfinished = Path.Combine(root, imported.Value("transcript") + CorpusFiles.UnfinishedSuffix);
        using var held = new FileStream(
            unfinished, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        held.Write("half a file"u8);
        held.Flush();

        var run = CommandLine.Of("sweep", "--corpus", root);

        run.Code.ShouldBe(Cli.Ok, run.Error);
        run.Output.ShouldContain("0 unfinished write(s) removed.");
        run.Output.ShouldContain("1 left alone");
        run.Output.ShouldContain(imported.Value("transcript") + CorpusFiles.UnfinishedSuffix);
        File.Exists(unfinished).ShouldBeTrue();
    }

    /// <summary>
    /// The repair the check reports and cannot make, run end to end: the paid file the corpus
    /// claims is gone, somebody still has the original, and one command puts it back where the row
    /// says it is. Nothing tells the command which meeting or which path — the bytes do, because
    /// the corpus already recorded what they hash to.
    /// </summary>
    [Fact]
    public void A_paid_file_the_corpus_lost_is_put_back_from_the_bytes_it_already_describes()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);
        var imported = Import(root);
        var response = Path.Combine(root, imported.Value("response"));

        File.Delete(response);
        CommandLine.Of("check", "--corpus", root).Code.ShouldBe(Cli.Refused);

        var restored = CommandLine.Of("restore", DeepgramFixtures.PathOf(Fixture), "--corpus", root);

        restored.Code.ShouldBe(Cli.Ok, restored.Error);
        restored.Value("put back").ShouldBe(imported.Value("response"));
        File.Exists(response).ShouldBeTrue();

        var sound = CommandLine.Of("check", "--corpus", root, "--verify-contents");
        sound.Code.ShouldBe(Cli.Ok, sound.Error);
        sound.Output.ShouldContain("Sound");

        // Twice is how somebody actually runs this, and the second one is not an error.
        var again = CommandLine.Of("restore", DeepgramFixtures.PathOf(Fixture), "--corpus", root);
        again.Code.ShouldBe(Cli.Ok, again.Error);
        again.Value("in place").ShouldBe(imported.Value("response"));
    }

    /// <summary>
    /// A file of somebody else's corpus, or a version of this one that was never confirmed here.
    /// Either way no row is asking for it, and the refusal says so rather than putting it somewhere.
    /// </summary>
    [Fact]
    public void A_file_no_row_of_the_corpus_describes_is_refused_rather_than_filed_somewhere()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);
        Import(root);

        var stranger = Path.Combine(root, "from-another-machine.json");
        File.WriteAllText(stranger, "{\"results\":\"a meeting this corpus never had\"}");

        var run = CommandLine.Of("restore", stranger, "--corpus", root);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain(stranger);
        CommandLine.Of("check", "--corpus", root, "--verify-contents").Code.ShouldBe(Cli.Ok);
    }

    /// <summary>
    /// Compacting is the one operation that can leave search answering with the wrong rows and say
    /// nothing about it, so what is asserted is that the same question still comes back the same.
    /// </summary>
    [Fact]
    public void Compacting_leaves_search_answering_what_it_answered()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);
        var spoken = Words.SaidIn(new FileInfo(Path.Combine(root, Import(root).Value("utterances"))));
        var before = CommandLine.Of("search", spoken, "--corpus", root);
        before.Output.ShouldNotContain("0 hit(s)");

        var compacted = CommandLine.Of("compact", "--corpus", root);

        compacted.Code.ShouldBe(Cli.Ok, compacted.Error);
        CommandLine.Of("search", spoken, "--corpus", root).Output.ShouldBe(before.Output);
    }

    /// <summary>
    /// Search takes the index's own query syntax, so a query it cannot parse is an answer this
    /// program owes the person who typed it — naming the query rather than a column nobody wrote.
    /// </summary>
    [Fact]
    public void A_query_the_index_cannot_parse_is_refused_naming_it()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);
        Import(root);

        var run = CommandLine.Of("search", "presupuesto AND", "--corpus", root);

        run.Code.ShouldBe(Cli.Refused);
        run.Error.ShouldContain("presupuesto AND");
    }

    [Fact]
    public void A_meeting_that_is_not_there_is_not_something_to_render()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);

        var run = CommandLine.Of("render", $"{Guid.NewGuid()}", "--corpus", root);

        run.Code.ShouldBe(Cli.Refused);
    }

    [Fact]
    public void Something_that_is_not_a_meeting_id_is_a_misuse_and_not_a_refusal()
    {
        using var corpus = new TemporaryCorpus();
        var root = corpus.Root.FullName;
        CommandLine.Of("migrate", "--corpus", root);

        var run = CommandLine.Of("render", "the-one-about-the-orchard", "--corpus", root);

        run.Code.ShouldBe(Cli.Misused);
        run.Error.ShouldContain("the-one-about-the-orchard");
    }

    /// <summary>
    /// One corpus, and more than one thing writing to it: the application in front of somebody,
    /// and this prompt. A write the corpus turns down is the corpus working as designed — nothing
    /// is broken and the answer is usually to run it again — so it is an answer this program owes
    /// whoever typed the command, under the exit code every other corpus refusal already uses.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It asks the predicate rather than typing a command because no command can reach the wrapped
    /// shape today, and that is the point rather than a gap in the test. Every write path reachable
    /// from here opens a transaction first and Microsoft.Data.Sqlite starts one with
    /// <c>BEGIN IMMEDIATE</c>, so a corpus another writer is holding is met one statement before
    /// <c>SaveChanges</c> and arrives unwrapped, as <see cref="SqliteException"/>, which was already
    /// a refusal. Nobody chose that ordering and nothing holds it still.
    /// </para>
    /// <para>
    /// That <c>SaveChanges</c> against this corpus really does throw this was measured rather than
    /// assumed: with the write lock held by a second connection it came back as
    /// <see cref="DbUpdateException"/> wrapping <c>SQLite Error 5: 'database is locked'</c>. The
    /// untransacted saves that produce it live in <c>HumanLayer</c>, which is why all three of the
    /// window's predicates have named this type since they were written.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_corpus_write_that_comes_back_wrapped_is_still_a_refusal()
    {
        var wrapped = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new SqliteException("SQLite Error 5: 'database is locked'.", 5));

        Inside("IsRefusal").Invoke(null, [wrapped]).ShouldBe(true);

        // And says which corpus failure it was, rather than sending somebody to look at an inner
        // exception they have no way of seeing. It is the sentence the unwrapped path prints.
        Inside("Said").Invoke(null, [wrapped]).ShouldBe("SQLite Error 5: 'database is locked'.");
    }

    /// <summary>
    /// One of the two the refusal contract is made of. They are private because nothing outside
    /// this class decides a refusal, and reached this way because no command can hand them a
    /// wrapped corpus failure — a rename lands here as a failure that says which one went.
    /// </summary>
    private static MethodInfo Inside(string name) =>
        typeof(Cli).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException($"Cli.{name} decides what a refusal is, and it is gone or renamed.");

    private static Run Import(string root) => CommandLine.Of(
        "import-response",
        DeepgramFixtures.PathOf(Fixture),
        "--corpus",
        root,
        "--started-at",
        "2026-03-04T14:00:00Z",
        "--profile",
        DeepgramFixtures.ProfileOf(Fixture).ToWireName());
}
