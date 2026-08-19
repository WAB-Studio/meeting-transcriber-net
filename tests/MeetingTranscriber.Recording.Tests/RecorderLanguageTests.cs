using System.Reflection;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-158.4: what a meeting is expected to be spoken in is said for that meeting, and is never
/// taken from the language the application is being read in.
/// </summary>
/// <remarks>
/// Two halves, and neither is enough on its own. The first is that the recording screen will not
/// start without having been told, which is what makes it a question somebody answers; the second
/// is that nothing on this side of the application can reach the answer to the other language
/// question, so the default that would file an English meeting as Spanish for having a Spanish
/// menu is not something this code could take even by accident.
/// <para>
/// What neither reaches is the window, which is the one place both languages are on screen at once
/// and the one place a build agent cannot go. What stands there instead is that the two are
/// different types with no conversion between them, so wiring one into the other does not compile
/// into a mistake that reads like a shortcut.
/// </para>
/// </remarks>
public class RecorderLanguageTests
{
    /// <summary>The catalogue of what a person reads, and the choice of which language to read in.</summary>
    private const string WhereTheApplicationsOwnLanguageLives = "MeetingTranscriber.Presentation";

    [Fact]
    public void A_meeting_is_not_recorded_until_what_will_be_spoken_in_it_has_been_said()
    {
        var chosen = RecorderChoices.Nothing with
        {
            Microphone = new Audio.AudioDevice("{a-microphone}", "A microphone", IsDefault: false),
            Source = RecorderSource.TheWholeMachine,
        };

        chosen.Settled.ShouldBeFalse();
        chosen.Spoken.ShouldBeNull();

        (chosen with { Spoken = "en" }).Settled.ShouldBeTrue();
    }

    /// <summary>
    /// Everything this suite can reach and not only what it names, for the reason the domain suite
    /// walks the same graph: a helper that wrapped the catalogue and handed back a plain string
    /// would be invisible to a check of direct references, and a plain string is exactly the shape
    /// a language tag has.
    /// </summary>
    [Fact]
    public void Nothing_a_recording_reaches_knows_what_language_the_application_is_read_in()
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<Assembly>([typeof(RecorderScreen).Assembly, typeof(RecorderLanguageTests).Assembly]);

        while (pending.Count > 0)
        {
            var ours = pending.Dequeue().GetReferencedAssemblies()
                .Where(assembly => assembly.Name!.StartsWith("MeetingTranscriber.", StringComparison.Ordinal));

            foreach (var assembly in ours.Where(assembly => reached.Add(assembly.Name!)))
            {
                pending.Enqueue(Assembly.Load(assembly));
            }
        }

        reached.ShouldNotContain(WhereTheApplicationsOwnLanguageLives);
    }
}
