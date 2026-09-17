using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;

using Microsoft.EntityFrameworkCore;

namespace MeetingTranscriber.Infrastructure.Storage;

/// <summary>
/// The preferences a corpus carries: what the person using it settled once, read back the same way
/// after the application was closed and reopened.
/// </summary>
/// <remarks>
/// <para>
/// A row in <c>settings</c> and not a file beside the corpus. <c>LanguageChoice</c> is a file
/// because the application has to know what language to say <em>choose a corpus</em> in before
/// there is a corpus to read; every answer here is only ever needed with a corpus already open,
/// and a preference about what to do with a meeting belongs with the meetings. A corpus that was
/// refused leaves these options dead on the screen that offers them, which is the rule
/// <see cref="WhoIsUsingThisRow.CorpusIsReachable"/> already applies one row up.
/// </para>
/// <para>
/// <b>Nothing here throws over what it read.</b> The stored value is matched through
/// <c>WireNames&lt;AfterARecording&gt;.FromWire</c> rather than parsed, so a corpus with no row and
/// a corpus whose row this build cannot read both answer <see cref="AfterARecording.DoNothing"/>.
/// That is the direction to fail in and it is chosen rather than defaulted into: the other two
/// answers spend the user's own Deepgram credit, and a preference nobody can read is not consent
/// to spend it. The screen then shows <em>No hacer nada</em> selected, so what the application is
/// going to do is on screen rather than inferred.
/// </para>
/// <para>
/// <b>It takes its instant on the write and not a clock on the constructor, which is a departure
/// from every sibling here.</b> <c>HumanLayer</c>, <c>MeetingReading</c>, <c>MeetingWork</c> and
/// <c>MeetingClassifying</c> are each handed a <see cref="TimeProvider"/> when they are built, and
/// fourteen call sites across the application hand them <c>TimeProvider.System</c>. Every one of
/// those types reads the clock on more than one path, so a field is what keeps one answer across
/// them. This type reads it on exactly one — there is one write, and the caller is the press that
/// made the choice, which already holds the instant it happened at. A constructor clock here would
/// be a field the reader never touches and a second instant available to the read, which needs
/// none.
/// </para>
/// </remarks>
public sealed class CorpusSettings(CorpusDbContext context)
{
    /// <summary>
    /// The key the answer about a finished recording is stored under. A constant rather than a
    /// literal at both ends, because a read and a write that disagree about it is a preference
    /// that silently stops being remembered.
    /// </summary>
    public const string AfterARecordingKey = "after-a-recording";

    /// <summary>
    /// What was settled about a recording that ends, or <see cref="AfterARecording.DoNothing"/>
    /// when nobody has settled anything and when what is stored is not something this build knows.
    /// </summary>
    /// <remarks>
    /// It reads no clock, which is why this type is not handed one: when the answer was written is
    /// not part of the answer.
    /// </remarks>
    public AfterARecording WhenARecordingEnds()
    {
        var stored = context.Settings
            .AsNoTracking()
            .FirstOrDefault(setting => setting.Key == AfterARecordingKey)?
            .Value;

        return stored is not null
            && WireNames<AfterARecording>.FromWire.TryGetValue(stored, out var settled)
            ? settled
            : AfterARecording.DoNothing;
    }

    /// <summary>
    /// Settles what happens when a recording ends, as of <paramref name="at"/>.
    /// </summary>
    /// <remarks>
    /// One row, rewritten rather than added to: this is a preference and not a history, and a
    /// second row under one key is not something the table can hold — <c>Key</c> is its primary
    /// key.
    /// </remarks>
    /// <param name="chosen">What was chosen.</param>
    /// <param name="at">When it was chosen, which is the press that chose it.</param>
    public void WhenARecordingEnds(AfterARecording chosen, UtcTimestamp at)
    {
        var wire = WireNames<AfterARecording>.Of(chosen);
        var stored = context.Settings.FirstOrDefault(setting => setting.Key == AfterARecordingKey);

        if (stored is null)
        {
            context.Settings.Add(new Setting
            {
                Key = AfterARecordingKey,
                Value = wire,
                UpdatedAt = at,
            });
        }
        else
        {
            stored.Value = wire;
            stored.UpdatedAt = at;
        }

        context.SaveChanges();
    }
}
