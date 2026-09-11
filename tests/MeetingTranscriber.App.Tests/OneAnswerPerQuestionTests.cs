namespace MeetingTranscriber.App.Tests;

/// <summary>
/// A question the application puts to the audio stack is put in one place, so the rule about what
/// to do with the answer has one place to live.
/// </summary>
/// <remarks>
/// <para>
/// The failure this exists against is one that already happened and was fixed by moving the rule
/// rather than by guarding it. What the machine plays through is asked once, on the notification
/// that the default endpoint moved, and <c>WhatTheMachinePlaysThrough</c> keeps the last answer
/// through a refusal — so a machine that stops answering does not take a warning off the screen at
/// the moment it is least able to justify taking it away. All four of ISC-150.1's tests are about
/// that type. None of them can see a second caller: an edit that goes back to asking the machine on
/// every redraw leaves every one of them green and puts the claim back where it was.
/// </para>
/// <para>
/// So the guard is on the application's source, and it is the shape <c>DeepgramKeyTests</c>' two
/// sweeps already use — the whole set of files, not a contains, so a caller that stopped calling
/// goes missing from the list and a sweep that read nothing cannot be green.
/// </para>
/// <para>
/// What it measures is which <em>file</em> of the application names the question, and it is worth
/// being exact about what that is and is not. It holds that the application reaches the audio stack
/// for this answer in one place, so there is one place the rule about the answer can live — and it
/// is blind to how often that place is called: a third call to the wrapper from inside a redraw
/// leaves it green. The wider rule is in <c>MainWindow</c>'s own hands and this is the part a build
/// agent can run. It is also only the application: <c>AudioCommands</c> asks the same question from
/// the command line, deliberately and outside this sweep, because a command that reports devices is
/// not a window redrawing.
/// </para>
/// <para>
/// <c>SourceLines.Occurrences</c> and not a plain search, because the sentences arguing this rule
/// name the thing they are arguing against, and a guard that goes red over its own explanation is
/// one somebody edits around. A file name and not a path from the root: the application is one flat
/// folder, and four segments in front of the only word that matters is four segments to read past.
/// </para>
/// </remarks>
public class OneAnswerPerQuestionTests
{
    [Fact]
    public void The_machine_is_asked_what_it_plays_through_in_one_place() =>
        AppSources.With(".cs")
            .Where(file => SourceLines
                .Occurrences(File.ReadAllText(file.FullName), "AudioDevices.Playback")
                .Any())
            .Select(file => file.Name)
            .Order(StringComparer.Ordinal)
            .ToArray()
            .ShouldBe(
                ["MainWindow.xaml.cs"],
                "what this machine plays through is asked on the notification that it moved, and "
                + "the answer is kept by WhatTheMachinePlaysThrough through a refusal, which is "
                + "what ISC-150.1 closed on. A second file under the application asking the machine "
                + "the same question is a second place that rule would have to be written, and all "
                + "four of that claim's tests would stay green over it. Ask through _playback, or "
                + "say here what the second question is.");
}
