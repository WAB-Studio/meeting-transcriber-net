using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
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
/// It opens no corpus it does not let go of, for the reason <see cref="MeetingsDrawer"/> gives. The
/// one exception is a clip: the file plays from is opened once, alongside the voices, and let go of
/// on <see cref="Close"/>, on <em>Guardar</em> and on leaving — the same three moments
/// <see cref="ReadingAMeeting"/> lets go of its own player.
/// </para>
/// </remarks>
public sealed partial class SayingWhoIsWho : UserControl
{
    /// <summary>How often a clip playing is checked against where it has to stop.</summary>
    private static readonly TimeSpan HowOftenAClipIsChecked = TimeSpan.FromMilliseconds(100);

    private readonly DispatcherTimer _clipWatch = new() { Interval = HowOftenAClipIsChecked };

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
    /// What this screen says in place of every voice's clip, or nothing while there is one to
    /// offer: a meeting whose recording will not play, said once for the whole screen rather than
    /// on every card.
    /// </summary>
    private readonly ScreenStatus _noAudio = new();

    /// <summary>
    /// The one recording every clip on this screen plays from, opened once for as long as this
    /// screen is showing the meeting it belongs to. Null while there is nothing to play, or while
    /// the machine would not open it.
    /// </summary>
    private Playback? _playing;

    /// <summary>Which voice's clip <see cref="_playing"/> is seeked to, or nothing.</summary>
    private string? _playingLabel;

    /// <summary>Where <see cref="_playing"/> has to stop, for the voice it is playing.</summary>
    private HeardAlone? _playingClip;

    /// <summary>
    /// True while this screen is building its own controls, so a picker being set to what it
    /// already says is not read as somebody having chosen something.
    /// </summary>
    private bool _drawing;

    public SayingWhoIsWho()
    {
        InitializeComponent();
        _clipWatch.Tick += OnClipWatch;
    }

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
        _noAudio.Nothing();
        StopClip();

        TheVoices.Children.Clear();
        WhichMeetingText.Text = string.Empty;
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
        NoAudioText.Text = string.Empty;
        NoAudioText.Visibility = Visibility.Collapsed;
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
        _noAudio.Nothing();
        StopClip();

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

            OpenTheClips(read);
        }

        Render();
    }

    /// <summary>
    /// Opens the recording every clip on this screen plays from, or says why there is none to
    /// offer.
    /// </summary>
    /// <remarks>
    /// A clip plays only off a recording that plays: the other two states of
    /// <see cref="RecordedAudio"/> already carry a sentence of their own, said once for the whole
    /// screen rather than repeated on every card that would otherwise offer nothing.
    /// </remarks>
    private void OpenTheClips(VoicesAsHeard read)
    {
        if (read.TheRecording is not RecordedAudio.Playable || read.Audio is not { } file)
        {
            _noAudio.Says(UiTexts.ThisMeetingHasNoAudioToListenTo);
            return;
        }

        try
        {
            _playing = Playback.Of(file);
        }
        catch (Exception wont) when (ScreenFailures.Reportable(wont))
        {
            // The file is opened here rather than lazily on the first press, so a recording a
            // backup has locked or that is not really a WAV says so the moment this screen draws
            // rather than behind a button that looks like it would work.
            _noAudio.Says(UiTexts.ThisMeetingWillNotPlay, wont.Message);
        }
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
            ShowNoAudio();

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

        // The microphone's own card carries this tag either way: `docs/design.md` §QuienEsQuien's
        // own words. It goes without a picker only once a Channel row has settled it
        // (HumanLayer.SettleTheMicrophone), which needs the install to know its user — `settled`
        // above is what actually gates the picker (the ternary at the end of this method), and not
        // this tag. Until the install has a user, the microphone's own voice is offered a picker
        // like any other one.
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

        if (ClipRow(read, voice, position) is { } clip)
        {
            left.Children.Add(clip);
        }

        Grid.SetColumn(left, 0);
        layout.Children.Add(left);

        var right = settled ? SettledName(voice) : APicker(read, voice, standing, position);
        Grid.SetColumn(right, 1);
        layout.Children.Add(right);

        card.Child = layout;
        return card;
    }

    /// <summary>
    /// The rows that offer to hear a voice alone, or nothing when there is nothing to offer: the
    /// main clip, and under it — only for a voice that spoke little — the further stretches it
    /// carries, each a clip of its own.
    /// </summary>
    /// <remarks>
    /// A clip is drawn only on a voice that carries a picker — the recording already settled a
    /// microphone that caught exactly one voice, and there is nothing to recognise somebody by
    /// there — only when <paramref name="read"/>'s own recording is
    /// <see cref="RecordedAudio.Playable"/>, the domain's own answer to whether there is anything to
    /// play, and only once <see cref="_playing"/> is really open on it. A voice with no stretch it
    /// spoke alone in is left with its longest turn to read and no clip to offer either. The main
    /// clip is <c>clip-{position}</c> and the further ones <c>clip-{position}-{n}</c>, n from 1,
    /// under <see cref="UiTexts.OtherStretches"/>.
    /// </remarks>
    private UIElement? ClipRow(VoicesAsHeard read, Voice voice, int position)
    {
        if (voice.SettledByTheRecording
            || voice.Alone is not { } stretch
            || read.TheRecording is not RecordedAudio.Playable
            || _playing is not { } playing)
        {
            return null;
        }

        var main = ClipLine(voice, stretch, $"clip-{position}", playing);

        if (voice.OtherStretches.Count == 0)
        {
            return main;
        }

        var rows = new StackPanel { Spacing = 4 };
        rows.Children.Add(main);
        rows.Children.Add(new TextBlock { Text = In(UiTexts.OtherStretches), Style = Chrome("OtherStretchesSay") });

        for (var n = 0; n < voice.OtherStretches.Count; n++)
        {
            rows.Children.Add(ClipLine(voice, voice.OtherStretches[n], $"clip-{position}-{n + 1}", playing));
        }

        return rows;
    }

    /// <summary>One clip: its Play/Pause press and where its stretch falls in the meeting.</summary>
    private StackPanel ClipLine(Voice voice, HeardAlone clip, string automationId, Playback playing)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var isPlayingThis = _playingLabel == voice.Label && _playingClip == clip && playing.IsPlaying;

        var button = new Button
        {
            Content = In(isPlayingThis ? UiTexts.Pause : UiTexts.Play),
            Style = Chrome("ClipButton"),
        };

        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += (_, _) => OnClipToggle(voice, clip);

        row.Children.Add(button);
        row.Children.Add(new TextBlock
        {
            Text = ScreenNumbers.Between(clip.From, clip.To),
            Style = Chrome("ClipRange"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        return row;
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
        // This screen's picker offers everybody, so the dialogue may offer everybody too.
        var openedOver = new OpenedOver(
            read.Meeting.Id,
            [.. _draft.Values.Where(person => person is not null).Select(person => person!.Value)],
            [],
            new HashSet<Guid>());

        var made = await AskingWhoTheyAre.AskAsync(
            Corpus(), openedOver, _language, Root.XamlRoot, read.Organizations, correcting);

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

    // ── The clips ─────────────────────────────────────────────────────────────────────────────

    /// <summary>Puts the sentence standing in for every voice's clip on the screen, or takes it off.</summary>
    private void ShowNoAudio()
    {
        NoAudioText.Text = _noAudio.In(_language);
        NoAudioText.Visibility = _noAudio.IsSaying ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// One card's press: starts its clip from the beginning of its stretch, or pauses it where it
    /// is when it is already the one playing.
    /// </summary>
    /// <remarks>
    /// Always the beginning, and never wherever a previous pause left it: this is a fifteen-second
    /// clip played to recognise a voice by, not a recording somebody is partway through listening
    /// to, and starting over is what makes a second press mean the same thing the first one did —
    /// hear the whole of what there is. Redraws the whole screen rather than touching one button's
    /// own <c>Content</c>, the same way every other change on this screen does
    /// (<see cref="Changed"/>): a card's Play/Pause label is <see cref="_playingLabel"/> and
    /// <see cref="Playback.IsPlaying"/> read back, not a second copy of the same fact kept in sync
    /// by hand.
    /// </remarks>
    private void OnClipToggle(Voice voice, HeardAlone clip)
    {
        if (_playing is not { } playing)
        {
            return;
        }

        if (_playingLabel == voice.Label && _playingClip == clip && playing.IsPlaying)
        {
            playing.Pause();
            _clipWatch.Stop();
            Render();
            return;
        }

        _playingLabel = voice.Label;
        _playingClip = clip;
        playing.Seek(clip.From);
        playing.Play();
        _clipWatch.Start();
        Render();
    }

    /// <summary>
    /// Stops a clip where its stretch ends, and says so when the endpoint stopped it instead.
    /// </summary>
    private void OnClipWatch(object? sender, object e)
    {
        if (_playing is not { } playing || _playingClip is not { } clip)
        {
            _clipWatch.Stop();
            return;
        }

        if (playing.WhatStoppedIt is { } broke)
        {
            // The endpoint pushes the audio on a thread of its own, for the same reason
            // ReadingAMeeting's own watch catches it there: a device pulled out mid clip fails over
            // there and nowhere this screen is standing.
            StopClip();
            _noAudio.Says(UiTexts.ThisMeetingWillNotPlay, broke.Message);
            Render();
            return;
        }

        if (!playing.IsPlaying || playing.At >= clip.To)
        {
            playing.Pause();
            _clipWatch.Stop();
            Render();
        }
    }

    /// <summary>Lets go of the recording every clip plays from, without touching anything else.</summary>
    private void StopClip()
    {
        _clipWatch.Stop();
        _playing?.Dispose();
        _playing = null;
        _playingLabel = null;
        _playingClip = null;
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
        // Before anything else, and whether or not the save goes through: the file and the
        // endpoint a clip holds are not this press's to leak, and a save that refuses still leaves
        // the screen without a running clip behind it.
        StopClip();

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
