namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Something the probe asked for was not there: an element nothing on the screen answers to, a
/// control that cannot be pressed, an application that never opened a window.
/// </summary>
/// <remarks>
/// <para>
/// Thrown rather than reported, and the message is written for whoever reads the transcript: it
/// says what was looked for and what was found instead. A probe that carried on after missing its
/// target would write a tree and a picture of the wrong screen, which is worse than no artifact —
/// the artifact would be believed.
/// </para>
/// <para>
/// Not sealed, for one subclass and one reason. What tells this apart from every other exception
/// the tool can throw is an exit code: this is a finding about a screen and exits 1, anything else
/// is the probe itself broken and exits 3. A failure that is a finding and does not derive from
/// here is one forgotten <c>catch</c> away from being reported as a bug in the tool, so the
/// taxonomy is held by the hierarchy rather than by every host remembering to re-raise it.
/// </para>
/// </remarks>
internal class ProbeFailed(string message) : Exception(message);

/// <summary>
/// A <c>see</c> whose window would not be photographed, carrying the tree it did read.
/// </summary>
/// <remarks>
/// <para>
/// Still a failure and still ends the run: a <c>see</c> promises both halves and one of them is
/// missing. What it stops being is a total loss. Measured on 2026-09-02, on a packaged build whose
/// window printed its frame and nothing else for the whole ten-second budget — foreground or not,
/// three runs in a row — while the automation tree read whole through the same window, every other
/// application on the machine printed normally, and the desktop composited normally. Under the old
/// shape the picture was taken first, so that run answered with a sentence about a photograph and
/// nothing at all about the screen, which is the half the run was for.
/// </para>
/// <para>
/// So both hosts hand the tree back beside the reason there is no picture. Neither is allowed to
/// let that read as success — the command line writes the tree, says why the picture is missing and
/// exits on it, and the server answers with both and marks the answer an error.
/// </para>
/// </remarks>
internal sealed class ScreenWouldNotBePhotographed(string tree, string why)
    : ProbeFailed(why)
{
    /// <summary>What the screen was, which reading it never depended on the picture.</summary>
    internal string Tree { get; } = tree;
}
