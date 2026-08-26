namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Something the probe asked for was not there: an element nothing on the screen answers to, a
/// control that cannot be pressed, an application that never opened a window.
/// </summary>
/// <remarks>
/// Thrown rather than reported, and the message is written for whoever reads the transcript: it
/// says what was looked for and what was found instead. A probe that carried on after missing its
/// target would write a tree and a picture of the wrong screen, which is worse than no artifact —
/// the artifact would be believed.
/// </remarks>
internal sealed class ProbeFailed(string message) : Exception(message);
