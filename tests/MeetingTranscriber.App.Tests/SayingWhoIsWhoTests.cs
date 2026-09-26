using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The screen that names a meeting's voices, and the dialogue it shares with
/// <c>ClassifyingAMeeting</c>, held to what no rule of either's own can reach: that the screen has
/// a word for every style it names, that it is reachable from the meeting screen at all, that
/// saving goes through the one door that renders the meeting again, and that the label a voice is
/// stored under never reaches a person.
/// </summary>
/// <remarks>
/// What <c>SayingWhoIsWho</c> decides is <c>WhoIsWhoTests</c> and <c>MeetingVoicesTests</c>, which
/// run. What neither of them can see is whether the window has a way to reach it, or whether the
/// screen writes through the act that keeps a saved name and its transcript in step.
/// </remarks>
public class SayingWhoIsWhoTests
{
    private static readonly string Screen =
        Path.Combine("MeetingTranscriber.App", "SayingWhoIsWho.xaml.cs");

    private static readonly string Markup =
        Path.Combine("MeetingTranscriber.App", "SayingWhoIsWho.xaml");

    private static readonly string ClassifyingMarkup =
        Path.Combine("MeetingTranscriber.App", "ClassifyingAMeeting.xaml");

    private static readonly string ReadingScreen =
        Path.Combine("MeetingTranscriber.App", "ReadingAMeeting.xaml.cs");

    private static readonly string Dialogue =
        Path.Combine("MeetingTranscriber.App", "AddingSomebody.xaml.cs");

    /// <summary>
    /// Every style this screen looks up by name is one it declares.
    /// </summary>
    /// <remarks>
    /// <c>ClassifyingAMeetingTests</c>' own form: <c>Chrome</c> is a <c>ResourceDictionary</c>
    /// indexer over this screen's own resources and does not walk up to the application's, so
    /// naming an Olivo key straight from the code-behind is green in CI and throws on the UI
    /// thread the moment a card is drawn.
    /// </remarks>
    [Fact]
    public void Every_style_this_screen_names_is_one_it_declares()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        Regex.Matches(source, @"Chrome\((?<taking>[^)]*)\)")
            .Select(match => match.Groups["taking"].Value.Trim())
            .Where(taking => !Regex.IsMatch(taking, @"^""\w+""$") && taking != "string named")
            .ToArray()
            .ShouldBeEmpty("these lookups do not name their style as a literal, so nothing checks it.");

        var named = Regex.Matches(source, @"Chrome\(""(?<key>\w+)""\)")
            .Select(match => match.Groups["key"].Value)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var declared = Regex.Matches(
                File.ReadAllText(AppSources.At(Markup).FullName),
                @"x:Key=""(?<key>\w+)""")
            .Select(match => match.Groups["key"].Value)
            .ToArray();

        named.ShouldNotBeEmpty("SayingWhoIsWho.xaml.cs names no style, so this check reads nothing.");
        declared.ShouldNotBeEmpty("SayingWhoIsWho.xaml declares no style, so this check reads nothing.");

        named.Except(declared, StringComparer.Ordinal).ShouldBeEmpty(
            "SayingWhoIsWho.xaml declares no style by these names, so the screen throws where it "
            + "is drawn.");
    }

    /// <summary>
    /// The meeting screen asks to name the voices, and the window opens the screen that names them
    /// — the one thing that makes this screen reachable at all.
    /// </summary>
    /// <remarks>
    /// Read out of source because opening a window is what it would otherwise take, and no build
    /// agent has one — <c>ClassifyingAMeetingTests</c>' own reason for its sibling check.
    /// </remarks>
    [Fact]
    public void The_meeting_screen_asks_for_the_voices_and_the_window_opens_the_screen_that_names_them()
    {
        var meeting = File.ReadAllText(AppSources.At(ReadingScreen).FullName);
        var window = File.ReadAllText(
            AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml.cs")).FullName);

        meeting.ShouldContain("NameTheVoices?.Invoke");
        window.ShouldContain("Reading.NameTheVoices += OnNameTheVoices");
        window.ShouldContain("Voices.Show(meeting)");

        // Both ways back, and they are not the same answer: the names were saved and the meeting
        // screen has to read them again, or they were not and it has exactly what it had.
        window.ShouldContain("Voices.Named += OnVoicesNamed");
        window.ShouldContain("Voices.Left += OnLeftTheVoices");
        window.ShouldContain("Reading.ReadAgain()");

        // And the window lets go of it. A window that shut over a screen still holding a meeting
        // leaves it holding a recording with it.
        window.ShouldContain("Voices.Close()");
    }

    /// <summary>
    /// The recording is stopped before the screen that names voices takes the window.
    /// </summary>
    /// <remarks>
    /// A player left running behind a collapsed screen is sound coming out of an application that
    /// appears to be doing nothing else — <c>ReadingAMeeting.Close</c>'s own remark, and
    /// <c>ClassifyingAMeetingTests</c>' sibling check for the screen that files a meeting.
    /// </remarks>
    [Fact]
    public void The_recording_is_stopped_before_the_screen_that_names_voices_takes_the_window()
    {
        var meeting = File.ReadAllText(AppSources.At(ReadingScreen).FullName);

        var raising = Regex.Match(
            meeting,
            @"private void OnNameTheVoices\(object sender, RoutedEventArgs e\)\r?\n[ ]{4}\{.*?\r?\n[ ]{4}\}",
            RegexOptions.Singleline);

        raising.Success.ShouldBeTrue(
            "ReadingAMeeting.xaml.cs no longer has an OnNameTheVoices, so this check reads nothing.");

        var stopped = raising.Value.IndexOf("Pause()", StringComparison.Ordinal);
        var raised = raising.Value.IndexOf("NameTheVoices?.Invoke", StringComparison.Ordinal);

        stopped.ShouldBeGreaterThan(-1, "OnNameTheVoices does not stop the recording at all.");
        raised.ShouldBeGreaterThan(-1, "OnNameTheVoices raises nothing, so no screen is opened.");

        stopped.ShouldBeLessThan(
            raised,
            "OnNameTheVoices raises NameTheVoices before it pauses, so the recording keeps playing "
            + "behind a screen nobody can see.");
    }

    /// <summary>
    /// Saving goes through the one act that renders the meeting again in the same transaction, and
    /// not through a write of this screen's own.
    /// </summary>
    /// <remarks>
    /// <c>MeetingVoices.Save</c> on its own writes the names and nothing else; a screen that
    /// called it directly would leave the transcript stale until something else rendered the
    /// meeting again, which is exactly the state <c>NamingTheVoices</c> exists to make unreachable.
    /// </remarks>
    [Fact]
    public void Saving_goes_through_the_act_that_renders_the_meeting_again()
    {
        var source = File.ReadAllText(AppSources.At(Screen).FullName);

        source.ShouldNotContain(
            "new HumanLayer(",
            customMessage: "this screen writes through HumanLayer directly, which is not the door "
            + "that renders the meeting again in the same transaction as the names.");

        Regex.IsMatch(source, @"MeetingVoices\([^;]*\.Save\(").ShouldBeFalse(
            "this screen calls MeetingVoices.Save directly, which writes the names without "
            + "rendering the meeting again. Draw's `new MeetingVoices(context, "
            + "TimeProvider.System).Of(id)` is the only construction this screen is allowed.");

        SourceLines.Occurrences(source, "NamingTheVoices.Save(").Count().ShouldBe(
            1,
            "this screen no longer saves through NamingTheVoices.Save exactly once, which is the "
            + "one door that renders the meeting again in the same transaction as the names.");
    }

    /// <summary>
    /// No screen shows the label a voice is stored under — only who it is once somebody has said,
    /// or the handle it wears until then.
    /// </summary>
    /// <remarks>
    /// A label reaching a person is <c>docs/design.md</c> §The rules the design imposes: the label
    /// <c>speaker_assignments</c> is keyed on is not itself a word a person reads.
    /// </remarks>
    [Fact]
    public void No_screen_shows_the_label_a_voice_is_stored_under()
    {
        var meeting = File.ReadAllText(AppSources.At(ReadingScreen).FullName);

        Body(meeting, "private IReadOnlyList<UIElement> TheTranscriptAround(").ShouldContain(
            "VoiceWords.ReadsAs(",
            customMessage: "TheTranscriptAround no longer reads a voice's name, so the unfolded "
            + "transcript is back to showing whatever it shows instead.");

        meeting.ShouldNotContain(
            "Spoken(turn.SpeakerLabel",
            customMessage: "a turn's own label reaches Spoken again, which is exactly the label "
            + "speaker_assignments is the only thing allowed to answer.");
    }

    /// <summary>
    /// The dialogue that adds somebody, and corrects somebody's name, is one component both
    /// screens share rather than two copies of a form.
    /// </summary>
    [Fact]
    public void The_dialogue_that_adds_somebody_is_one_component_on_both_screens()
    {
        var screens = new[]
        {
            ("ClassifyingAMeeting.xaml", File.ReadAllText(AppSources.At(ClassifyingMarkup).FullName)),
            ("SayingWhoIsWho.xaml", File.ReadAllText(AppSources.At(Markup).FullName)),
        };

        foreach (var (name, markup) in screens)
        {
            markup.ShouldContain(
                "local:AddingSomebody",
                customMessage: $"{name} no longer declares AddingSomebody, so this screen has no "
                + "way to add or correct a person.");

            markup.ShouldNotContain(
                "<ContentDialog",
                customMessage: $"{name} declares its own ContentDialog again, which is the second "
                + "copy AddingSomebody was built to remove.");
        }
    }

    /// <summary>
    /// A correction goes through the act that renders again every meeting the person's voices are
    /// on, and not through a rename of the dialogue's own.
    /// </summary>
    [Fact]
    public void A_correction_goes_through_the_act_that_renders_every_meeting_that_names_them()
    {
        var dialogue = File.ReadAllText(AppSources.At(Dialogue).FullName);

        SourceLines.Occurrences(dialogue, "RenamingSomebody.Rename(").Count().ShouldBe(
            1,
            "this dialogue no longer corrects a name through RenamingSomebody.Rename exactly "
            + "once, so a correction no longer renders again every meeting the person's voices "
            + "are on.");

        dialogue.ShouldNotContain(
            "human.Rename(",
            customMessage: "this dialogue renames through HumanLayer directly, which moves no "
            + "meeting's transcript and leaves every one of them naming the old one.");
    }

    /// <summary>
    /// The dialogue opens the corpus once to add somebody — the #296 divergence's own shape, held
    /// to the file it now lives in.
    /// </summary>
    [Fact]
    public void The_dialogue_opens_the_corpus_once_to_add_somebody()
    {
        var dialogue = File.ReadAllText(AppSources.At(Dialogue).FullName);

        SourceLines.Occurrences(dialogue, "new HumanLayer(").Count().ShouldBe(
            1,
            "adding a person is written more than once, which is the shape the divergence #296 "
            + "fixed grew in.");

        SourceLines.Occurrences(dialogue, "CorpusDatabase.Open(").Count().ShouldBe(
            1,
            "this dialogue opens the corpus more than once for the one write it owns.");
    }

    /// <summary>
    /// One method's body, anchored on the closing brace at its own indentation.
    /// <c>ClassifyingAMeetingTests</c>' own helper, for the reason given there.
    /// </summary>
    private static string Body(string source, string signature)
    {
        var found = Regex.Match(
            source,
            Regex.Escape(signature) + @".*?\r?\n[ ]{4}\}",
            RegexOptions.Singleline);

        found.Success.ShouldBeTrue($"the file no longer has a `{signature}`.");
        return found.Value;
    }
}
