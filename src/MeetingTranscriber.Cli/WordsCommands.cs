using System.Globalization;

using MeetingTranscriber.Processing.Corrections;

namespace MeetingTranscriber.Cli;

/// <summary>
/// The command that asks the corpus which words a transcript keeps getting wrong. Read only, and
/// no model reads anything.
/// </summary>
public static class WordsCommands
{
    /// <summary>
    /// With <c>--like</c>, every way the corpus wrote something like a term. Without it, the words
    /// it seems to keep getting wrong — in the whole corpus, or in the one meeting <c>--meeting</c>
    /// names. Finding nothing is an answer, so an empty result is a success.
    /// </summary>
    public static int Words(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var like = arguments.Optional("--like");
        var meeting = arguments.Optional("--meeting");
        arguments.EnsureNothingLeftOver();

        if (like is not null && meeting is not null)
        {
            throw new UsageException("--like and --meeting ask two different questions; give one.");
        }

        var meetingId = meeting is null ? (Guid?)null : Arguments.Meeting(meeting);

        using var context = corpus.Read();
        if (like is not null)
        {
            var forms = FindingCorrections.LikeTyped(context, like);
            foreach (var form in forms)
            {
                Report.Line(
                    output,
                    form.Text,
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{Times(form.Times)} in {form.Meetings} {Plural(form.Meetings, "meeting")}, {form.Resemblance:0.00} alike"));
            }

            output.WriteLine(string.Create(
                CultureInfo.InvariantCulture, $"{forms.Count} {Plural(forms.Count, "way")} it was written."));
            return Cli.Ok;
        }

        var found = meetingId is { } id
            ? FindingCorrections.UnpromptedIn(context, id)
            : FindingCorrections.Unprompted(context);

        foreach (var suspect in found.Suspects)
        {
            Report.Line(
                output,
                suspect.Word,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{Times(suspect.Times)}, heard at {suspect.MeanConfidence:0.00}, looks like {suspect.LooksLike} ({Times(suspect.LooksLikeTimes)})"));
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{found.Suspects.Count} {Plural(found.Suspects.Count, "word")} it may keep getting wrong."));

        foreach (var unread in found.Unread)
        {
            Report.Line(output, "not read", $"{unread}: its response could not be read");
        }

        return Cli.Ok;
    }

    private static string Times(int count) => $"{count} {Plural(count, "time")}";

    private static string Plural(int count, string noun) => count is 1 ? noun : noun + "s";
}
