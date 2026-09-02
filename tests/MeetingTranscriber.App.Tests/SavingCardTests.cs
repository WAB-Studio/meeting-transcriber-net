using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// ISC-158.3 on the screen's own side: filing the meeting is not something this window does any
/// part of. Beside it, the state saving a meeting puts the recorder half into is held to naming
/// every step a save runs and no others.
/// </summary>
/// <remarks>
/// Source and not a running window, for the reason every check in this project is: there is no
/// <c>ProjectReference</c> to the application, because the Windows App SDK bootstraps a runtime the
/// moment a type of it is touched. What decides which steps a save has runs in
/// <c>MeetingTranscriber.Recording.Tests</c>; what is here is the two things a screen can get wrong
/// on its own — a step or a standing with no answer, and a window that files a meeting itself.
/// </remarks>
public partial class SavingCardTests
{
    private static readonly string Screen = Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs");

    private static readonly string StepsDeclaredIn =
        Path.Combine("MeetingTranscriber.Recording", "SavingTheMeeting.cs");

    /// <summary>
    /// The prompt's own recording command, which is the other half of what ISC-158.3 compares.
    /// </summary>
    private static readonly string AtThePrompt =
        Path.Combine("MeetingTranscriber.Cli", "RecordingCommands.cs");

    /// <summary>
    /// The screen has an answer for every step a save runs and for nothing else, and the steps a
    /// save runs are the whole of <c>SavingWork</c> — so a screen cannot name a step the save does
    /// not run without this going red.
    /// </summary>
    [Fact]
    public void The_screen_names_every_step_a_save_runs_and_no_others() =>
        EnumTable
            .Read(Screen, "step", "SavingWork", StepsDeclaredIn)
            .ShouldNameItsWholeEnum("the steps of a save");

    /// <summary>
    /// The other table on that card: what each step's mark looks like and what a narrator is told
    /// it means. A standing with no mark is a line that draws nothing where the save's own progress
    /// should be.
    /// </summary>
    [Fact]
    public void The_screen_marks_every_standing_a_step_can_be_in() =>
        EnumTable
            .Read(Screen, "standing", "StepStanding", StepsDeclaredIn)
            .ShouldNameItsWholeEnum("where a step of a save stands");

    /// <summary>
    /// ISC-158.3. Stopping on a screen and stopping at a prompt reach the same call, and the window
    /// does no part of the filing itself.
    /// </summary>
    /// <remarks>
    /// What the meeting comes out as, given that one call, is <c>SavingTheMeetingTests</c>'. What no
    /// probe of theirs can reach is a second path: a window that started to write a row of its own,
    /// or did half of what finishing does, would file something a prompt never files and every one
    /// of their assertions would stay green. So this reads the two entry points and holds them to
    /// being one — the call named on the recording it was started for, so that the metering timer's
    /// own <c>Stop</c> cannot stand in for it.
    /// </remarks>
    [Fact]
    public void The_application_and_the_prompt_stop_a_meeting_through_the_same_call()
    {
        foreach (var entry in new[] { Screen, AtThePrompt })
        {
            var source = File.ReadAllText(AppSources.At(entry).FullName);
            var name = Path.GetFileName(entry);

            source.ShouldContain(
                "MeetingRecording.Start(",
                customMessage: $"{name} no longer starts a recording through the one call both "
                + "entry points make.");

            source.ShouldContain(
                "recording.Stop(",
                customMessage: $"{name} no longer stops the recording it started through that "
                + "recording's own call.");
        }

        var filing = AppSources
            .With(".cs")
            .Select(file => (file.Name, Found: FilesAMeetingItself().Matches(File.ReadAllText(file.FullName))))
            .SelectMany(read => read.Found.Select(match => $"{read.Name}: {match.Value.Trim()}"))
            .ToArray();

        filing.ShouldBeEmpty(
            "the application files part of a meeting itself instead of leaving all of it to the "
            + "call the prompt makes, so what it puts in the corpus can come apart from what a "
            + "prompt puts there: " + string.Join("; ", filing));
    }

    /// <summary>
    /// A screen doing part of what finishing a recording does, or writing rows into a corpus
    /// directly.
    /// </summary>
    /// <remarks>
    /// A tripwire and not a proof, and worth being exact about which: what it names is
    /// <c>MeetingRecordings</c> — the composition's own steps, every one of which belongs to the
    /// call both entry points make — and the two calls that put a row or an artifact in a corpus
    /// without going through a service. It does not reach a window that reimplemented finishing out
    /// of <c>MeetingAudio</c> and <c>File.Copy</c>, and it deliberately does not name
    /// <c>MeetingWork</c>, which the meetings list writes through on purpose. What is left out is a
    /// commented call, which cannot put anything in a corpus.
    /// </remarks>
    [GeneratedRegex(@"^(?![ ]*(//|\*|/\*)).*(?<![\w.])(MeetingRecordings\.\w+\(|SaveChanges\w*\(|DurableArtifact\.)",
        RegexOptions.Multiline)]
    private static partial Regex FilesAMeetingItself();
}
