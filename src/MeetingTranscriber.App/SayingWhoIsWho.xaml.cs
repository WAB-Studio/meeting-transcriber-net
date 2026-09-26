using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// The screen one meeting's voices are named from: who spoke, what each voice is called until
/// somebody says otherwise, and the one press that saves what was answered.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing about who spoke. Which voices there are, what each is called and who is
/// settled already is <see cref="WhoIsWho"/>'s, read through <see cref="MeetingVoices"/> in a
/// project a build agent can run; this control turns that answer into cards and the presses back
/// into calls — the same split <see cref="ClassifyingAMeeting"/> and <see cref="ReadingAMeeting"/>
/// keep for the same reason.
/// </para>
/// <para>
/// It opens no corpus it does not let go of, for the reason <see cref="MeetingsDrawer"/> gives.
/// </para>
/// </remarks>
public sealed partial class SayingWhoIsWho : UserControl
{
    private CorpusFolder? _corpus;
    private UiLanguage _language;

    /// <summary>The meeting on screen, or none when this control is not showing one.</summary>
    private Guid? _meeting;

    /// <summary>What was read the last time this screen drew, or nothing when the read refused.</summary>
    private VoicesAsHeard? _read;

    /// <summary>
    /// The answer as it stands on screen for every voice not already settled by the recording,
    /// keyed on the label — the corpus's own answer until somebody changes it, and nothing writes
    /// it back until <em>Guardar</em>.
    /// </summary>
    private readonly Dictionary<string, Guid?> _draft = new(StringComparer.Ordinal);

    private readonly ScreenStatus _status = new();

    /// <summary>
    /// True while this screen is building its own controls, so a picker being set to what it
    /// already says is not read as somebody having chosen something.
    /// </summary>
    private bool _drawing;

    public SayingWhoIsWho() => InitializeComponent();

    /// <summary>The names were saved, and this screen is done with the meeting.</summary>
    public event EventHandler<Guid>? Named;

    /// <summary>Somebody asked to go back without naming anybody.</summary>
    public event EventHandler? Left;

    /// <summary>Whether this screen is showing a meeting.</summary>
    public bool IsOpen => _meeting is not null;

    /// <summary>
    /// Hands over the corpus the meetings are in. Reads nothing: nothing is shown until a meeting
    /// is chosen.
    /// </summary>
    /// <exception cref="InvalidOperationException">It was opened twice.</exception>
    public void Open(CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        if (_corpus is not null)
        {
            throw new InvalidOperationException("The screen that names voices already has a corpus.");
        }

        _corpus = corpus;
    }

    /// <summary>Which language this screen is being read in. It draws again and it keeps the draft.</summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();

        if (_meeting is not null)
        {
            Render();
        }
    }

    /// <summary>Opens one meeting: reads its voices, and shows who is on each.</summary>
    public void Show(Guid meetingId)
    {
        _meeting = meetingId;
        Draw();
    }

    /// <summary>Lets go of the meeting and of everything drawn about it.</summary>
    public void Close()
    {
        _meeting = null;
        _read = null;
        _draft.Clear();
        _status.Nothing();

        TheVoices.Children.Clear();
        WhichMeetingText.Text = string.Empty;
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// What this screen says, in the language it is being read in. Every word on it comes through
    /// here, which is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    private CorpusFolder Corpus() => _corpus
        ?? throw new InvalidOperationException(
            "The screen that names voices was never given a corpus, so it has no meeting to show.");

    private Style Chrome(string named) => (Style)Root.Resources[named];

    /// <summary>Reads the meeting's voices again and puts them back on the controls.</summary>
    private void Draw()
    {
        _read = null;
        _draft.Clear();
        _status.Nothing();

        if (_meeting is not { } meetingId)
        {
            Render();
            return;
        }

        if (Corpus().Folder is not { } folder)
        {
            _status.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
        }
        else
        {
            try
            {
                using var context = CorpusDatabase.Open(folder);
                _read = new MeetingVoices(context, TimeProvider.System).Of(meetingId);
            }
            catch (MeetingStageException gone)
            {
                _status.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            }
            catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
            {
                _status.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
            }
        }

        if (_read is { } read)
        {
            foreach (var voice in read.Voices.Voices.Where(voice => !voice.SettledByTheRecording))
            {
                _draft[voice.Label] = voice.PersonId;
            }
        }

        Render();
    }

    /// <summary>The draft moved: whatever went wrong last is no longer what is on screen.</summary>
    private void Changed()
    {
        _status.Nothing();
        Render();
    }

    /// <summary>Puts the voices onto the controls, one card each.</summary>
    private void Render()
    {
        _drawing = true;

        try
        {
            TheVoices.Children.Clear();
            ShowTheStatus();

            if (_read is not { } read)
            {
                WhichMeetingText.Text = string.Empty;
                SaveVoicesButton.IsEnabled = false;
                return;
            }

            WhichMeetingText.Text = ScreenNumbers.Which(read.Meeting);

            var voices = read.Voices.Voices;
            SaveVoicesButton.IsEnabled = voices.Count > 0;

            for (var position = 0; position < voices.Count; position++)
            {
                TheVoices.Children.Add(ACard(read, voices[position], position));
            }
        }
        finally
        {
            _drawing = false;
        }
    }

    /// <summary>One voice's card: what it is called, how much it said, its quotation, and who is
    /// on it.</summary>
    private UIElement ACard(VoicesAsHeard read, Voice voice, int position)
    {
        var settled = voice.SettledByTheRecording;
        var standing = settled ? voice.PersonId : _draft[voice.Label];
        var unnamed = !settled && standing is null;

        var card = new Border { Style = unnamed ? Chrome("VoiceCardUnnamed") : Chrome("VoiceCard") };

        var layout = new Grid { ColumnSpacing = 16 };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel { Spacing = 6 };
        var heading = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        heading.Children.Add(new TextBlock
        {
            Text = VoiceWords.Handle(voice, _language),
            Style = Chrome("VoiceHeading"),
        });

        // A voice the recording settled is shown and not offered: `docs/design.md` §QuienEsQuien
        // says it is settled already and says so, and it is the one voice this loop never puts a
        // picker on.
        if (voice.IsTheMicrophonesOwn)
        {
            heading.Children.Add(new Border
            {
                Style = Chrome("OnlyOneVoiceTag"),
                VerticalAlignment = VerticalAlignment.Center,
                Child = new TextBlock { Text = In(UiTexts.OnlyOneVoice), Style = Chrome("OnlyOneVoiceTagSays") },
            });
        }

        left.Children.Add(heading);
        left.Children.Add(new TextBlock
        {
            Text = VoiceWords.TurnsSaid(voice.TurnsSaid, _language),
            Style = Chrome("VoiceData"),
        });
        left.Children.Add(new TextBlock { Text = voice.Quoted.Text, Style = Chrome("VoiceQuoted") });

        Grid.SetColumn(left, 0);
        layout.Children.Add(left);

        var right = settled ? SettledName(voice) : APicker(read, voice, standing, position);
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);

        card.Child = layout;
        return card;
    }

    /// <summary>The name shown and not chosen, on a voice the recording already settled.</summary>
    private FrameworkElement SettledName(Voice voice) => new Border
    {
        Style = Chrome("TheirName"),
        VerticalAlignment = VerticalAlignment.Top,
        Child = new TextBlock { Text = voice.PersonName, Style = Chrome("TheirNameSays") },
    };

    /// <summary>
    /// The picker over everybody the corpus holds, for a voice nothing has settled — naming
    /// somebody new, and correcting whoever the draft already has standing there.
    /// </summary>
    private FrameworkElement APicker(VoicesAsHeard read, Voice voice, Guid? standing, int position)
    {
        var extras = new List<(UiText Words, Action Chose)>
        {
            (UiTexts.NameANewOne, () => _ = AskWhoTheyAre(read, voice, correcting: null)),
        };

        if (standing is { } standingId
            && read.Everybody.FirstOrDefault(person => person.Id == standingId) is { } them)
        {
            extras.Add((UiTexts.CorrectThisName, () => _ = AskWhoTheyAre(read, voice, them)));
        }

        var picker = OneOfThese.Build(
            In,
            Chrome("Picker"),
            [.. read.Everybody.Select(person => (person.Id, person.DisplayName))],
            standing,
            chosen => ChoseSomebody(voice.Label, chosen),
            extras,
            UiTexts.ChooseSomebody,
            VoiceWords.Handle(voice, _language),
            $"voice-{position}",
            () => _drawing);

        picker.VerticalAlignment = VerticalAlignment.Top;
        return picker;
    }

    private void ChoseSomebody(string label, Guid? person)
    {
        _draft[label] = person;
        Changed();
    }

    /// <summary>
    /// Opens the one dialogue this screen shares with <see cref="ClassifyingAMeeting"/>, over one
    /// voice: adding a person when <paramref name="correcting"/> is nothing, or correcting theirs
    /// when it names somebody standing on the voice already.
    /// </summary>
    private async Task AskWhoTheyAre(VoicesAsHeard read, Voice voice, Person? correcting)
    {
        var made = await AskingWhoTheyAre.AskAsync(
            Corpus(), _language, Root.XamlRoot, read.Organizations, correcting);

        if (made is { } id)
        {
            _draft[voice.Label] = id;
        }

        Render();
    }

    /// <summary>Puts the line saying what went wrong on the screen, or takes it off.</summary>
    private void ShowTheStatus()
    {
        StatusText.Text = _status.In(_language);
        StatusText.Visibility = _status.IsSaying ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Saves what was answered and renders the meeting again in the same transaction, so a name
    /// saved is a name the transcript already shows.
    /// </summary>
    /// <remarks>
    /// <see cref="ArgumentException"/> is caught beside <see cref="MeetingStageException"/> and not
    /// left to <see cref="ScreenFailures.Reportable"/>, which does not name it: it is what
    /// <c>MeetingVoices.Save</c>'s own remark calls the corpus having moved underneath this screen's
    /// stale draft — a label no turn of the meeting carries any more, or a person this corpus no
    /// longer holds — read between the draw and this press. It is not a defect to stop the window
    /// over any more than a node renamed away is on the screen that files a meeting.
    /// </remarks>
    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return;
        }

        var answers = _draft.Select(entry => new VoiceAnswer(entry.Key, entry.Value)).ToArray();

        try
        {
            NamingTheVoices.Save(folder, meeting, answers, TimeProvider.System);
        }
        catch (MeetingStageException gone)
        {
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            Render();
            return;
        }
        catch (ArgumentException stale)
        {
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message);
            Render();
            return;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
            Render();
            return;
        }

        Close();
        Named?.Invoke(this, meeting);
    }

    /// <summary>Back and Cancelar both leave without writing anything.</summary>
    private void OnLeave(object sender, RoutedEventArgs e)
    {
        Close();
        Left?.Invoke(this, EventArgs.Empty);
    }
}
