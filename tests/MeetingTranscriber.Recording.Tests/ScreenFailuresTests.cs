using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Rendering;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// The one list every screen and every reader of the corpus asks, and the two answers it has to
/// keep straight: a failure written to be read out, and a defect that has to stop the application
/// rather than be whispered in a status line.
/// </summary>
public class ScreenFailuresTests
{
    /// <summary>
    /// The classification tree's own refusal — a node something still points at, a person still
    /// named on a meeting, a name already taken beside it. Every one of those throws is a sentence
    /// saying which node or person and why, which is the definition of something to report.
    /// </summary>
    /// <remarks>
    /// It is the one <see cref="InvalidOperationException"/> the list names, and it is named rather
    /// than reached by its base type: the handlers that ask this are <c>async void</c>, so the day
    /// it is left out is the day the first screen offering removal takes the window down over a
    /// refusal that was written to be read.
    /// </remarks>
    [Fact]
    public void A_classification_that_cannot_be_taken_back_is_something_to_say()
    {
        ScreenFailures.Reportable(
            new ClassificationException("Two meetings are still filed under it.")).ShouldBeTrue();
    }

    /// <summary>
    /// And its base type is still a defect. Naming the derived type is what keeps the two apart —
    /// a list that named <see cref="InvalidOperationException"/> instead would swallow every defect
    /// in the application and leave it looking like a corpus somebody could not read.
    /// </summary>
    [Fact]
    public void The_invalid_operation_a_defect_throws_is_still_a_defect()
    {
        ScreenFailures.Reportable(new InvalidOperationException("Sequence contains no elements."))
            .ShouldBeFalse();
    }

    /// <summary>
    /// A meeting that cannot be rendered again — a response the corpus names and the disk does not
    /// have — is something a screen says, the moment saving the names on a meeting's voices or
    /// correcting a person's name renders files again.
    /// </summary>
    [Fact]
    public void A_meeting_that_cannot_be_rendered_is_something_to_say()
    {
        ScreenFailures.Reportable(new RenderException("Meeting has no response to render from."))
            .ShouldBeTrue();
    }

    /// <summary>
    /// A response this build cannot read is something to say, for the same reason: it arrives from
    /// the same render a screen now reaches.
    /// </summary>
    [Fact]
    public void A_response_this_build_cannot_read_is_something_to_say()
    {
        ScreenFailures.Reportable(new DeepgramResponseException("Missing something the parser needs."))
            .ShouldBeTrue();
    }
}
