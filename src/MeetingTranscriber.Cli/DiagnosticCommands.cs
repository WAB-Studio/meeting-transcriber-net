using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Cli;

/// <summary>
/// What is true of the corpus itself: what it holds, whether it is sound, the two repairs that are
/// safe to run without a person deciding anything, and the one that starts with a file a person
/// went and found.
/// </summary>
/// <remarks>
/// None of this opens the application. That is the reason the commands exist: a corpus that will
/// not open in the UI is exactly the corpus somebody needs to ask questions about, and every
/// answer here comes from the same services the UI would have used.
/// </remarks>
public static class DiagnosticCommands
{
    public static int Migrate(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        arguments.EnsureNothingLeftOver();

        var made = !File.Exists(corpus.DatabasePath);
        using var context = corpus.Migrate();

        Report.Line(output, "corpus", made ? $"{corpus.Root.FullName} (made)" : corpus.Root.FullName);
        Report.Line(output, "schema", context.Database.GetAppliedMigrations().LastOrDefault() ?? "none");
        return Cli.Ok;
    }

    public static int Status(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        arguments.EnsureNothingLeftOver();

        // The one command that opens a corpus this build has moved past, because "why does
        // everything else refuse" is a question about the corpus's state and this is what answers
        // questions about the corpus's state.
        using var context = corpus.Inspect();
        var pending = corpus.Pending(context);

        Report.Line(output, "corpus", corpus.Root.FullName);

        if (pending.Count > 0)
        {
            // Counted no further, and this is the whole reason the command opens a corpus the
            // others refuse: reading a row here means reading the columns this build's model has,
            // which are not the columns this corpus holds. Saying how far behind it is is the
            // answer; a count that threw would be the same failure the other commands already give.
            Report.Line(
                output,
                "schema",
                $"{pending.Count} migration(s) behind this build, the first being '{pending[0]}'. "
                + "'migrate' brings it up, and what it holds is counted then.");
            return Cli.Ok;
        }

        Report.Line(output, "schema", context.Database.GetAppliedMigrations().LastOrDefault() ?? "none");

        Report.Line(output, "meetings", Tally(context.Meetings.GroupBy(meeting => meeting.LifecycleState)
            .Select(group => new { State = group.Key, Count = group.Count() })
            .ToArray()
            .Select(row => (WireNames<LifecycleState>.Of(row.State), row.Count))));

        Report.Line(output, "turns", $"{context.Utterances.Count()}");

        Report.Line(output, "artifacts", Tally(context.Artifacts.GroupBy(artifact => artifact.Origin)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToArray()
            .Select(row => (WireNames<ArtifactOrigin>.Of(row.Key), row.Count))));

        Report.Line(output, "jobs", Tally(context.ProcessingJobs.GroupBy(job => job.State)
            .Select(group => new { group.Key, Count = group.Count() })
            .ToArray()
            .Select(row => (WireNames<JobState>.Of(row.Key), row.Count))));

        return Cli.Ok;
    }

    public static int Check(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);

        // Off by default for the same reason the reconciler has it off: hashing every artifact of
        // a corpus holding hours of audio is a check whose cost grows with the corpus, and one
        // nobody runs is worth less than one that runs at every start.
        var verifyContents = arguments.Flag("--verify-contents");
        arguments.EnsureNothingLeftOver();

        // Not read only: comparing an FTS5 index against the table it indexes is written as an
        // INSERT, and a read only connection would fail the check rather than run it.
        using var context = corpus.Write();

        var problems = CorpusIntegrity.Check(context);
        var findings = ArtifactReconciler.Check(context, verifyContents);

        foreach (var problem in problems)
        {
            output.WriteLine(problem.ToString());
        }

        foreach (var finding in findings)
        {
            output.WriteLine(finding.ToString());
        }

        if (problems.Count + findings.Count == 0)
        {
            output.WriteLine(verifyContents
                ? "Sound, and every artifact is the one its row describes."
                : "Sound, and every artifact is where its row says and the size it says.");
            return Cli.Ok;
        }

        output.WriteLine(
            $"{problems.Count} problem(s) with the corpus, {findings.Count} thing(s) the disk and "
            + "the database disagree about.");
        return Cli.Refused;
    }

    public static int Sweep(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        arguments.EnsureNothingLeftOver();

        // Opened read only, and a schema this build has moved past is no reason to leave a dead
        // write behind — but this command deletes, so what it deletes from has to be a corpus and
        // not a directory that happens to have a file of that name in it. The sweep is entirely on
        // the filesystem and the corpus is what says which folder that is.
        using var context = corpus.Inspect();
        _ = corpus.Pending(context);

        var swept = ArtifactReconciler.Sweep(context);
        foreach (var write in swept)
        {
            output.WriteLine(write);
        }

        output.WriteLine($"{swept.Count} unfinished write(s) removed.");
        return Cli.Ok;
    }

    /// <summary>
    /// The repair the check reports and cannot make: a row whose file is gone, put back from a copy
    /// somebody still has. What identifies the file is what is in it, so the command asks for
    /// nothing but the corpus and the bytes — a backup folder does not say which meeting a loose
    /// <c>deepgram.json</c> belongs to, and the corpus does.
    /// </summary>
    public static int Restore(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var offered = new FileInfo(arguments.Only("The file to put back"));
        arguments.EnsureNothingLeftOver();

        using var context = corpus.Write();
        var restored = ArtifactRestore.Restore(context, offered, Clock.Now());

        Report.Line(output, "sha256", restored.Sha256);
        foreach (var path in restored.PutBack)
        {
            Report.Line(output, "put back", path);
        }

        // Named rather than counted, because a path already holding a file is the answer to running
        // this twice and it is also the answer when what is at that path is not what the row
        // describes. Which of the two it is belongs to 'check', and somebody who cannot see the
        // path has nothing to ask it about.
        foreach (var path in restored.AlreadyThere)
        {
            Report.Line(output, "in place", path);
        }

        output.WriteLine(
            $"{restored.PutBack.Count} file(s) put back, {restored.AlreadyThere.Count} already in place.");
        return Cli.Ok;
    }

    public static int Compact(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        arguments.EnsureNothingLeftOver();

        using var context = corpus.Write();
        var before = new FileInfo(corpus.DatabasePath).Length;
        CorpusIntegrity.Compact(context);
        var after = new FileInfo(corpus.DatabasePath).Length;

        output.WriteLine(
            $"{before} bytes before, {after} after, and both search indexes were put back.");
        return Cli.Ok;
    }

    /// <summary>A count per name, or the word for having none of anything.</summary>
    private static string Tally(IEnumerable<(string Name, int Count)> counted)
    {
        var rows = counted.OrderBy(row => row.Name, StringComparer.Ordinal).ToArray();
        return rows.Length == 0
            ? "none"
            : string.Join(", ", rows.Select(row => $"{row.Count} {row.Name}"));
    }
}
