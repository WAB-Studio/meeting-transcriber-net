using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Intake;
using MeetingTranscriber.Processing.Rendering;
using MeetingTranscriber.Recording;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The meetings in the corpus: bringing one in, producing what is derived from it again, and
/// asking the corpus a question.
/// </summary>
/// <remarks>
/// The whole flow with no microphone and no provider in it â€” a paid response goes in, the turns
/// and the files come out, and search finds what was said. That is what makes it worth having: it
/// is the application's own path, driven from a prompt, so it can be exercised without automating
/// a window.
/// </remarks>
public static class MeetingCommands
{
    /// <summary>
    /// What a meeting was spoken in when nobody says. The response does not carry it, and this is
    /// the language every meeting in this product's corpus has been in so far.
    /// </summary>
    /// <remarks>
    /// Reachable across this assembly because a third command reads it — <c>deepgram-live</c> asks
    /// the provider in it — and one place saying what this product's default language is beats a
    /// third spelling of the same two letters.
    /// </remarks>
    internal const string DefaultLanguage = "es";

    /// <summary>Files a paid response, as a meeting of its own or onto one already here.</summary>
    /// <remarks>
    /// <para>
    /// <b><c>--meeting</c> is what tells the two halves apart.</b> Without it this is a response
    /// nothing in the corpus knows about and the command has to be told when the meeting was and
    /// what it was recorded as. With it the response belongs to a meeting this machine recorded,
    /// every one of those facts is already on the row, and none of them may be given.
    /// </para>
    /// <para>
    /// <b><c>--as-next-version</c> is a flag on the first half, not a third half.</b> It is for a
    /// re-transcription whose filing the corpus refused: the paid response is kept beside the
    /// meeting rather than lost, and this is the door back in — <see cref="MeetingIntake.ReceiveWhatWasRefused"/>,
    /// with no charge and no call to the provider. It is refused without <c>--meeting</c>, by
    /// <see cref="Told"/>'s own <c>EnsureNothingLeftOver</c>, as a line typed wrong: a response
    /// nothing in the corpus knows about cannot be the one a run already paid for.
    /// </para>
    /// </remarks>
    public static int ImportResponse(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var response = new FileInfo(arguments.Only($"The {ResponseVersions.First} to import"));

        // Which door this is, and every flag that door takes, is settled before the corpus is
        // opened. A line that will be refused for what was typed on it refuses for that, rather
        // than for a corpus that happens not to be at the path beside it.
        Func<CorpusDbContext, int> filing;
        if (arguments.Optional("--meeting") is { } named)
        {
            // Read before OnlyTheMeeting's own EnsureNothingLeftOver, so this half's own flag is
            // never the one it goes on to refuse everything else by.
            var asNextVersion = arguments.Flag("--as-next-version");
            var meeting = OnlyTheMeeting(arguments, named);
            filing = asNextVersion
                ? opened => ImportedAsNextVersion(opened, meeting, response, output)
                : opened => Imported(output, MeetingIntake.ReceiveInto(opened, meeting, response, Clock.Now()));
        }
        else
        {
            var details = Told(arguments);
            filing = opened => Imported(output, MeetingIntake.Receive(opened, response, details, Clock.Now()));
        }

        using var context = corpus.Write();
        return filing(context);
    }

    /// <summary>What every filing reports, whichever door it came through.</summary>
    private static int Imported(TextWriter output, ReceivedMeeting received)
    {
        Report.Line(
            output,
            "meeting",
            received.WasAlreadyThere
                ? $"{received.MeetingId} (this response was already here)"
                : $"{received.MeetingId}");
        Report.Line(output, "response", received.Response.RelativePath);
        Report.Line(output, "manifest", received.Manifest.RelativePath);
        PutBack(output, received.PutBack);
        Rendered(output, received.Turns, received.Transcript.RelativePath, received.Utterances.RelativePath);
        return Cli.Ok;
    }

    /// <summary>What <c>--as-next-version</c> reports on top of every other filing.</summary>
    private static int ImportedAsNextVersion(
        CorpusDbContext context, Guid meeting, FileInfo response, TextWriter output)
    {
        var filed = MeetingIntake.ReceiveWhatWasRefused(context, meeting, response, Clock.Now());
        Imported(output, filed.Received);

        Report.Line(output, "run", $"{filed.RunId} (finished)");
        Report.Line(
            output,
            "job",
            filed.JobSettled
                ? "settled: nothing is waiting on a person"
                : "left as it was: it was no longer waiting on a person, so it is not this "
                  + "command's to settle");

        Report.Line(
            output,
            "refused copy",
            $"{filed.KeptPath} — filed as {filed.Received.Response.RelativePath}; nothing reads it "
            + "now and it can be deleted");

        return Cli.Ok;
    }

    /// <summary>What the command line knows about a meeting nothing in the corpus does.</summary>
    private static MeetingDetails Told(Arguments arguments)
    {
        var details = new MeetingDetails(
            arguments.Instant("--started-at"),
            arguments.Profile("--profile"),
            arguments.Language("--language", DefaultLanguage),
            arguments.Optional("--title"),
            arguments.Optional("--context"));

        arguments.EnsureNothingLeftOver();
        return details;
    }

    /// <summary>
    /// The meeting a response is being filed onto, and nothing else off this command line.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Anything this command does not read is refused by name rather than ignored, and the sentence
    /// after it says why — which is a sentence about this command and not about the flag, because it
    /// is now appended to every unread flag and not only to the five the other half reads. The
    /// meeting already says when it was, what it was recorded as, what was expected to be spoken in
    /// it and what it is called, so a flag here would be a second answer to a question the corpus has
    /// already settled — and for <c>--profile</c> it would be one that decides whether two channels
    /// are the meeting's two sources. A flag silently doing nothing is worse than a refusal: somebody
    /// would go on typing it and go on believing it.
    /// </para>
    /// <para>
    /// It is a <see cref="UsageException"/> and so <c>Cli.Misused</c>, because that is what this is:
    /// the line was typed wrong, and it was decided from the tokens alone before the corpus was
    /// opened. <c>Cli.Refused</c> is for the corpus or the input saying no, and reporting a grammar
    /// error as one would make the same flag answer 1 with a value and 2 without it — so a script
    /// reading the code could no longer tell "you typed the line wrong" from "the corpus said no".
    /// It also prints the usage, which is where the alternation these flags belong to is written.
    /// <c>Arguments.Refusing</c> builds a <see cref="UsageException"/> too, so the exit code is the
    /// same one it always was.
    /// </para>
    /// </remarks>
    private static Guid OnlyTheMeeting(Arguments arguments, string named)
    {
        var meeting = Arguments.Meeting(named);

        arguments.EnsureNothingLeftOver(
            $"This command is given --meeting, and meeting {meeting} already says when it was, what it "
            + "was recorded as and what was spoken in it.");

        return meeting;
    }

    /// <summary>
    /// Brings an audio file in as a meeting of its own.
    /// </summary>
    /// <remarks>
    /// <b>There is no <c>--profile</c> on it, and there is not going to be one.</b> What the audio
    /// is gets decided from the file and the folder it came in — see <see cref="AudioIntake"/> —
    /// and a flag here would be the one way back to somebody declaring that two channels of a file
    /// off a phone are a meeting's loopback and microphone. That is also the difference from
    /// <see cref="ImportResponse"/> one line above, which takes a response somebody has already paid for
    /// and is told what it was recorded as, because by then it is a fact about a request that was
    /// made rather than a guess about a file.
    /// </remarks>
    public static int ImportAudio(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var audio = new FileInfo(arguments.Only("The audio file to bring in"));
        var details = new BroughtDetails(
            arguments.Instant("--started-at"),
            arguments.Language("--language", DefaultLanguage),
            arguments.Optional("--title"),
            arguments.Optional("--context"));
        arguments.EnsureNothingLeftOver();

        using var context = corpus.Write();
        var brought = AudioIntake.Bring(context, audio, details, Clock.Now());

        Report.Line(
            output,
            "meeting",
            brought.WasAlreadyThere
                ? $"{brought.MeetingId} (this audio was already here)"
                : $"{brought.MeetingId}");
        Report.Line(
            output,
            "profile",
            brought.MixedDown
                ? $"{brought.Profile.ToWireName()} (mixed down to one track)"
                : brought.Profile.ToWireName());
        Report.Line(output, "audio", brought.Audio.RelativePath);
        Report.Line(output, "length", Report.Offset(brought.Length));
        PutBack(output, brought.PutBack);
        return Cli.Ok;
    }

    /// <summary>
    /// Names every file a filing put back, and writes nothing when it put none back.
    /// </summary>
    /// <remarks>
    /// Both doors, and one line of code so that they cannot drift. Handing a file the corpus
    /// already has is read as a command that did nothing, so a source this put back on the way past
    /// is exactly what a report saying nothing would hide — and what it means is that the corpus
    /// was missing a file it cannot produce again, which is worth going to look at. Named rather
    /// than counted for the reason the restore command names them: a path is something to act on
    /// and a number is not.
    /// </remarks>
    private static void PutBack(TextWriter output, IReadOnlyList<string> paths)
    {
        foreach (var path in paths)
        {
            Report.Line(output, "put back", path);
        }
    }

    public static int Render(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var meeting = Arguments.Meeting(arguments.Only("The meeting to render"));
        arguments.EnsureNothingLeftOver();

        using var context = corpus.Write();
        var rendered = MeetingRenderer.Render(context, meeting, Clock.Now());

        Report.Line(output, "meeting", $"{meeting}");
        Rendered(output, rendered.Turns, rendered.Transcript.RelativePath, rendered.Utterances.RelativePath);
        return Cli.Ok;
    }

    /// <summary>
    /// Every meeting, from its sources. A meeting whose response is gone is named rather than
    /// counted out silently, and the run does not come back as success with it in the list.
    /// </summary>
    public static int Rebuild(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        arguments.EnsureNothingLeftOver();

        using var context = corpus.Write();
        var report = CorpusRebuild.Run(context, Clock.Now());

        output.WriteLine(report.ToString());
        return report.CouldNotRebuild.Count == 0 ? Cli.Ok : Cli.Refused;
    }

    /// <summary>
    /// Finding nothing is an answer, so an empty result is a success. What is not is a query the
    /// index cannot parse, which comes back naming the query.
    /// </summary>
    public static int Search(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var query = arguments.Only("A query");
        var limit = arguments.Number("--limit", CorpusSearch.DefaultLimit);
        arguments.EnsureNothingLeftOver();

        // Read only, and the connection is what enforces it: a question about the corpus has no
        // business being able to change it.
        using var context = corpus.Read();
        var hits = CorpusSearch.Find(context, query, limit);

        foreach (var hit in hits)
        {
            output.WriteLine($"{hit.MeetingId}  {hit.StartedAt}  {hit.Title ?? "untitled"}");
            output.WriteLine($"  {Anchor(hit)}  {hit.Snippet}");
        }

        output.WriteLine($"{hits.Count} hit(s).");
        return Cli.Ok;
    }

    /// <summary>
    /// What kind of hit this is and where it is. Everything that cites a turn carries that turn's
    /// position; what is about the whole meeting carries none.
    /// </summary>
    /// <remarks>
    /// A turn says its position and does not say its kind, because <em>turn</em> is what a position
    /// already means. Everything else that carries one says its kind first: a decision, an action
    /// and an open question all anchor on the turn they were said in, so branching on the ordinal
    /// printed all four the same way and left somebody unable to tell a settled decision from
    /// somebody merely saying the words — which is the distinction the extraction exists to draw.
    /// </remarks>
    private static string Anchor(SearchHit hit)
    {
        if (hit.Ordinal is not { } ordinal)
        {
            return WireNames<SearchSource>.Of(hit.Source);
        }

        var where = $"turn #{ordinal}, {Report.Offset(hit.Start ?? Duration.Zero)}";
        return hit.Source is SearchSource.Turn
            ? where
            : $"{WireNames<SearchSource>.Of(hit.Source)} at {where}";
    }

    private static void Rendered(TextWriter output, int turns, string transcript, string utterances)
    {
        Report.Line(output, "turns", $"{turns}");
        Report.Line(output, "transcript", transcript);
        Report.Line(output, "utterances", utterances);
    }
}
