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
    private const string DefaultLanguage = "es";

    public static int ImportResponse(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var response = new FileInfo(arguments.Only($"The {MeetingIntake.ResponseFileName} to import"));
        var details = new MeetingDetails(
            arguments.Instant("--started-at"),
            arguments.Profile("--profile"),
            arguments.Language("--language", DefaultLanguage),
            arguments.Optional("--title"),
            arguments.Optional("--context"));
        arguments.EnsureNothingLeftOver();

        using var context = corpus.Write();
        var received = MeetingIntake.Receive(context, response, details, Clock.Now());

        Report.Line(
            output,
            "meeting",
            received.WasAlreadyThere
                ? $"{received.MeetingId} (this response was already here)"
                : $"{received.MeetingId}");
        Report.Line(output, "response", received.Response.RelativePath);
        Report.Line(output, "manifest", received.Manifest.RelativePath);
        Rendered(output, received.Turns, received.Transcript.RelativePath, received.Utterances.RelativePath);
        return Cli.Ok;
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
        return Cli.Ok;
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
    /// Where the hit is, which is the pair a citation anchors on for a turn and nothing at all for
    /// a summary, because a summary is about the whole meeting.
    /// </summary>
    private static string Anchor(SearchHit hit) => hit.Ordinal is { } ordinal
        ? $"turn #{ordinal} at {Report.Offset(hit.Start ?? Duration.Zero)}"
        : WireNames<SearchSource>.Of(hit.Source);

    private static void Rendered(TextWriter output, int turns, string transcript, string utterances)
    {
        Report.Line(output, "turns", $"{turns}");
        Report.Line(output, "transcript", transcript);
        Report.Line(output, "utterances", utterances);
    }
}
