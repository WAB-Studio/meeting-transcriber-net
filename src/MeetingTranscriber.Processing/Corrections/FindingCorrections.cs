using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Processing.Corrections;

/// <summary>Every word the corpus's responses hold, tallied, and the meetings that could not be read.</summary>
public sealed record HeardInTheCorpus(IReadOnlyList<HeardWord> Words, IReadOnlyList<Guid> Unread);

/// <summary>The words the corpus seems to keep getting wrong, and the meetings that could not be read.</summary>
public sealed record UnpromptedWords(IReadOnlyList<SuspectWord> Suspects, IReadOnlyList<Guid> Unread);

/// <summary>
/// Finding what a person may want to correct, with no model reading anything.
/// </summary>
/// <remarks>
/// <para>
/// How the corpus wrote a word is counted from the stored turns, because they are what a
/// correction is applied to: a spelling counted anywhere else could be offered and then never
/// reached. How sure the provider was is only in the paid response, so it is read when asked for,
/// from the response each meeting's turns came from — <c>turn_sources</c>, and not the newest
/// response filed, for the reason <see cref="MeetingReading.TranscribedFrom"/> gives.
/// </para>
/// <para>
/// Nothing here writes, and nothing here may reach the summaries: a finding that needed Claude
/// Code would be a correction nobody could ask for without it.
/// </para>
/// </remarks>
public static class FindingCorrections
{
    /// <summary>Every way the turns of an active meeting wrote something like <paramref name="typed"/>.</summary>
    public static IReadOnlyList<WrittenForm> LikeTyped(CorpusDbContext context, string typed)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(typed);

        var turns = context.Utterances
            .AsNoTracking()
            .Where(turn => context.Meetings.Any(meeting =>
                meeting.Id == turn.MeetingId && meeting.LifecycleState == LifecycleState.Active))
            .Select(turn => new { turn.MeetingId, turn.Text })
            .ToList()
            .Select(turn => new TurnText(turn.MeetingId, turn.Text));

        return MisspelledWords.LikeTyped(typed, turns);
    }

    /// <summary>Every word of every active meeting's response, tallied once.</summary>
    public static HeardInTheCorpus Heard(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var heard = new List<WordAsHeard>();
        var unread = new List<Guid>();
        var sources = context.TurnSources
            .AsNoTracking()
            .Where(source => context.Meetings.Any(meeting =>
                meeting.Id == source.MeetingId && meeting.LifecycleState == LifecycleState.Active))
            .OrderBy(source => source.MeetingId)
            .ToList();
        var paths = context.Artifacts
            .AsNoTracking()
            .Where(artifact => artifact.Kind == ArtifactKind.DeepgramResponse)
            .ToDictionary(artifact => artifact.Id, artifact => artifact.RelativePath);

        // A turn_sources row naming an artifact that is not there is a response that cannot be
        // read like any other, and is named rather than dropped.
        var responses = sources
            .Select(source => (source.MeetingId, Path: paths.GetValueOrDefault(source.ResponseArtifactId)))
            .ToList();

        foreach (var response in responses)
        {
            if (response.Path is not null && Read(context, response.Path) is { } words)
            {
                heard.AddRange(words);
            }
            else
            {
                unread.Add(response.MeetingId);
            }
        }

        return new HeardInTheCorpus(MisspelledWords.Tally(heard), unread);
    }

    /// <summary>The words the corpus seems to keep getting wrong, with nothing typed.</summary>
    public static UnpromptedWords Unprompted(CorpusDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var heard = Heard(context);
        var answered = context.TerminologyCorrections
            .AsNoTracking()
            .Select(correction => correction.WrongText)
            .ToList()
            .Concat(new CorpusSettings(context).WordsSaidRight());

        return new UnpromptedWords(MisspelledWords.Unprompted(heard.Words, answered), heard.Unread);
    }

    /// <summary>The words one meeting's response holds; empty when it cannot be read.</summary>
    public static IReadOnlyList<HeardWord> HeardIn(CorpusDbContext context, Guid meetingId)
    {
        ArgumentNullException.ThrowIfNull(context);

        var path = context.TurnSources
            .AsNoTracking()
            .Where(source => source.MeetingId == meetingId)
            .Join(
                context.Artifacts.AsNoTracking(),
                source => source.ResponseArtifactId,
                artifact => artifact.Id,
                (source, artifact) => artifact.RelativePath)
            .FirstOrDefault();

        return path is not null && Read(context, path) is { } words
            ? MisspelledWords.Tally(words)
            : [];
    }

    /// <summary>
    /// What one meeting heard that the whole corpus seems to keep getting wrong, judged exactly as
    /// <see cref="Unprompted"/> judges it.
    /// </summary>
    /// <exception cref="MeetingStageException">This corpus holds no such meeting.</exception>
    public static UnpromptedWords UnpromptedIn(CorpusDbContext context, Guid meetingId)
    {
        ArgumentNullException.ThrowIfNull(context);

        _ = new MeetingReading(context, TimeProvider.System).Row(meetingId);

        var corpus = Unprompted(context);

        // A meeting whose own response could not be read has nothing to be judged by, and says so
        // by being named rather than by an empty list that looks like a clean one.
        return corpus.Unread.Contains(meetingId)
            ? corpus with { Suspects = [] }
            : corpus with { Suspects = MisspelledWords.HeardIn(corpus.Suspects, HeardIn(context, meetingId)) };
    }

    /// <summary>
    /// The spelling the stored turns of active meetings most often wrote <paramref name="word"/> in,
    /// counted case-blind, ties broken by ordinal order. <paramref name="word"/> as given when no
    /// turn holds it.
    /// </summary>
    /// <remarks>
    /// A lookalike is counted lower-cased, so on its own it would store <c>deepgram</c> where the
    /// person means <c>Deepgram</c>; this gives back what the corpus mostly says.
    /// </remarks>
    public static string AsMostOftenWritten(CorpusDbContext context, string word)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(word);

        var wanted = word.ToLowerInvariant();
        var texts = context.Utterances
            .AsNoTracking()
            .Where(turn => context.Meetings.Any(meeting =>
                meeting.Id == turn.MeetingId && meeting.LifecycleState == LifecycleState.Active))
            .Select(turn => turn.Text)
            .ToList();

        return texts
            .SelectMany(Spellings.WordsOf)
            .Where(written => string.Equals(written.ToLowerInvariant(), wanted, StringComparison.Ordinal))
            .GroupBy(written => written, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => group.Key)
            .FirstOrDefault() ?? word;
    }

    private static IReadOnlyList<WordAsHeard>? Read(CorpusDbContext context, string relativePath)
    {
        var file = CorpusFiles.Locate(context.Root, relativePath);
        if (!file.Exists)
        {
            return null;
        }

        try
        {
            return DeepgramTranscriptParser.WordsAsHeardInFile(file.FullName);
        }
        // The parser checks every JsonElement's kind before it reads one as a string, an array or a
        // number, so nothing on this path throws InvalidOperationException; a defect that did would
        // be a bug to see and not a response to skip.
        catch (Exception unreadable) when (unreadable is DeepgramResponseException
            or IOException
            or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
