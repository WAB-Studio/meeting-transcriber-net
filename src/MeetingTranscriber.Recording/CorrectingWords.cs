using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording;

/// <summary>What <see cref="CorrectingWords.Correct"/> did: the corrections it wrote and the meetings it rendered.</summary>
public sealed record CorrectedWords(IReadOnlyList<TerminologyCorrection> Saved, IReadOnlyList<Guid> Rendered);

/// <summary>
/// Saves the forms a word came out wrong in, commits that alone, and then renders again every
/// meeting those corrections touch, one meeting and one transaction at a time, so the transcripts
/// say what the screen just promised.
/// </summary>
/// <remarks>
/// <para>
/// A correction is applied when a transcript is rendered and never to the stored turns or the paid
/// response, so saving one changes nothing anybody can read until the meetings it touches are
/// rendered again. <see cref="OwedRenders.TouchedBy"/> decides which, by the render's own rules.
/// </para>
/// <para>
/// The corrections are not held open across the renders, for <see cref="RenamingSomebody"/>'s
/// reason: a word that appears in dozens of meetings would otherwise keep the corpus's write lock
/// for as long as all of those renders take, past <see cref="CorpusDatabase.BusyTimeoutMilliseconds"/>
/// for the job runner and every other save waiting behind it, for corrections that have by then
/// already landed. Each render opens a connection of its own, and a render that throws leaves its
/// rows in its own context and nowhere else.
/// </para>
/// <para>
/// A form already corrected in the same place is replaced and not added twice, through
/// <see cref="HumanLayer.Drop"/> and <see cref="HumanLayer.Correct"/>, so that the layer stays the
/// one writer of <c>terminology_corrections</c> and the row's <c>CreatedAt</c> moves, which is what
/// <see cref="OwedRenders"/> reads to find the meetings a failed render left behind. Corrections
/// are stored ignoring case: <c>FindingCorrections.LikeTyped</c> hands forms back lower-cased, so
/// an exact one would never match the capitalised word that opens a turn.
/// </para>
/// </remarks>
public static class CorrectingWords
{
    /// <summary>
    /// Saves <paramref name="wrong"/> as forms of <paramref name="right"/> inside <paramref name="under"/>
    /// (or everywhere) and renders every meeting that touches, at <paramref name="clock"/>'s instant.
    /// </summary>
    /// <param name="root">The corpus folder.</param>
    /// <param name="right">The word as it should be.</param>
    /// <param name="wrong">
    /// The forms it came out as. One identical to <paramref name="right"/> is skipped; one that differs
    /// by case alone is not, because fixing <c>deepgram</c> to <c>Deepgram</c> is a correction.
    /// </param>
    /// <param name="under">The node the correction holds in and below, or nothing for everywhere.</param>
    /// <param name="clock">The instant of the corrections and of the renders.</param>
    /// <exception cref="ArgumentException">
    /// A blank word, no forms, a blank form, or a node this corpus does not hold. These are the
    /// screen's defects and nothing is written.
    /// </exception>
    /// <exception cref="RenderException">
    /// The corrections always land. One or more meetings could not be rendered again; the exception
    /// names every one of them and the next launch renders each again, through <c>OwedRenders</c>.
    /// </exception>
    public static CorrectedWords Correct(
        DirectoryInfo root, string right, IReadOnlyCollection<string> wrong, Guid? under, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(wrong);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(right);

        if (wrong.Count is 0)
        {
            throw new ArgumentException("A correction needs at least one form to replace.", nameof(wrong));
        }

        if (wrong.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A form to replace cannot be blank.", nameof(wrong));
        }

        var saved = new List<TerminologyCorrection>();
        IReadOnlyList<Guid> touched;

        using (var context = CorpusDatabase.Open(root))
        using (var writing = context.Database.BeginTransaction())
        {
            Node? node = null;
            if (under is { } id)
            {
                node = context.Nodes.Find(id)
                    ?? throw new ArgumentException($"This corpus holds no node {id}.", nameof(under));
            }

            var layer = new HumanLayer(context, clock);
            var inThatPlace = context.TerminologyCorrections
                .Where(correction => correction.NodeId == under && correction.MeetingId == null)
                .ToArray();

            foreach (var form in wrong
                .Select(form => form.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(form => !string.Equals(form, right.Trim(), StringComparison.Ordinal)))
            {
                foreach (var replaced in inThatPlace.Where(correction =>
                    string.Equals(correction.WrongText, form, StringComparison.OrdinalIgnoreCase)))
                {
                    layer.Drop(replaced);
                }

                saved.Add(layer.Correct(form, right.Trim(), TerminologyMatchMode.IgnoreCase, node));
            }

            writing.Commit();

            // After the commit and not inside it: the scan reads every turn of every meeting with a
            // transcript, and the write lock is not held for that. A crash between the commit and
            // the renders leaves nothing lost, because OwedRenders finds the same meetings again.
            touched = OwedRenders.TouchedBy(context, saved);
        }

        var now = UtcTimestamp.From(clock.GetUtcNow());
        var rendered = new List<Guid>();
        var notRendered = new List<Guid>();

        foreach (var meeting in touched)
        {
            try
            {
                using var context = CorpusDatabase.Open(root);
                RenderingAgain.OneMeeting(context, meeting, now);
                rendered.Add(meeting);
            }
            catch (Exception unrendered) when (Absorbable(unrendered))
            {
                notRendered.Add(meeting);
            }
        }

        if (notRendered.Count > 0)
        {
            throw new RenderException(
                $"{saved.Count} correction(s) of \"{right.Trim()}\" were saved, but could not be rendered for "
                + $"{notRendered.Count} meeting(s): {string.Join(", ", notRendered)}. "
                + "The next launch renders each one again.");
        }

        return new CorrectedWords(saved, rendered);
    }

    /// <summary>
    /// What one meeting's failed render turns into an entry on the list instead of aborting every
    /// meeting behind it: everything except out of memory, <see cref="RenamingSomebody"/>'s rule and
    /// <c>OwedRenders.Absorbable</c>'s, for the same reason.
    /// </summary>
    private static bool Absorbable(Exception thrown) => thrown is not OutOfMemoryException;
}
