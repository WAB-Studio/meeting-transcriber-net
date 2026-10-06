using System.Globalization;

using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Processing.Corrections;
using MeetingTranscriber.Processing.Rendering;
using MeetingTranscriber.Recording;

using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// The screen the words a transcript keeps getting wrong are corrected from, on one meeting: the
/// word as it should be, how the corpus actually wrote it, and the ones that turned up by
/// themselves.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing about which spellings resemble what was typed, which start ticked or what can
/// be saved. That is <see cref="FindingCorrections"/> and <see cref="WordsScreen"/>, in projects a
/// build agent runs; this control turns their answers into rows and the presses back into calls —
/// the split <see cref="SayingWhoIsWho"/> keeps for the same reason.
/// </para>
/// <para>
/// Saving goes through <see cref="CorrectingWords.Correct"/> and nowhere else, because it is the
/// one act that renders the meetings a correction touches: a screen writing the correction itself
/// would promise a transcript it did not change. Every read and every write runs off the UI thread
/// on a context of its own, since finding the words a corpus keeps getting wrong reads every
/// meeting's paid response and takes seconds on a real one; what returns after the screen was
/// closed or moved on is dropped and never drawn.
/// </para>
/// <para>
/// Every brush on this screen comes from its own markup's styles, as <c>ThemeResource</c>s, and
/// nothing is resolved by name in code: which value a key holds is the theme's to say.
/// </para>
/// </remarks>
public sealed partial class WordsThatComeOutWrong : UserControl
{
    /// <summary>How long after the last key the corpus is asked what resembles what was typed.</summary>
    private static readonly TimeSpan AfterTyping = TimeSpan.FromMilliseconds(400);

    private readonly DispatcherQueueTimer _typing;
    private readonly ScreenStatus _status = new();

    /// <summary>The forms ticked, by their text. Kept across drawing, because several are ticked at once.</summary>
    private readonly HashSet<string> _ticked = new(StringComparer.Ordinal);

    private CorpusFolder? _corpus;
    private UiLanguage _language;

    /// <summary>The meeting on screen, or none when this control is not showing one.</summary>
    private Guid? _meeting;

    /// <summary>What was read about the meeting, or nothing while it is being read or the read refused.</summary>
    private Held? _held;

    /// <summary>The forms the corpus wrote something like <see cref="_searched"/> in.</summary>
    private IReadOnlyList<WrittenForm> _forms = [];

    /// <summary>The term <see cref="_forms"/> answers, or nothing before any search.</summary>
    private string _searched = string.Empty;

    /// <summary>The suspects not yet answered <em>no</em> to on this screen.</summary>
    private List<SuspectWord> _suspects = [];

    /// <summary>Counts every time the meeting on screen changes, so a late answer for another one is dropped.</summary>
    private int _generation;

    /// <summary>Counts every search started, so an answer for a term no longer in the field is dropped.</summary>
    private int _search;

    /// <summary>
    /// The words brought from a selection, as they were selected, until the meeting has been read and
    /// they are either pinned or said to be corrected already. Nothing when the screen was opened on
    /// its own.
    /// </summary>
    private string? _asked;

    /// <summary>
    /// The form brought from a selection, kept at the top of the list and ticked whatever a later
    /// search returns: the words somebody pointed at are the ones this screen was opened for, and a
    /// search that did not happen to find them must not lose them.
    /// </summary>
    private string? _pinned;

    /// <summary>
    /// True until the first search over the pinned words has ticked them. Ticked once and not on
    /// every search: a person who unticks the form they brought, and then refines the search, has
    /// said no, and a later search must not say yes for them.
    /// </summary>
    private bool _theyAreToBeTicked;

    private bool _saving;

    /// <summary>
    /// The one place <see cref="_saving"/> changes, so <see cref="MayGoBackChanged"/> is raised
    /// wherever it does and the app bar never draws its back press against a stale answer.
    /// </summary>
    private void Saving(bool value)
    {
        if (_saving == value)
        {
            return;
        }

        _saving = value;
        MayGoBackChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>True while this screen is setting its own controls, so that is not read as somebody typing.</summary>
    private bool _drawing;

    public WordsThatComeOutWrong()
    {
        InitializeComponent();

        _typing = DispatcherQueue.CreateTimer();
        _typing.Interval = AfterTyping;
        _typing.IsRepeating = false;
        _typing.Tick += OnTypingPaused;
    }

    /// <summary>Somebody asked to go back to the meeting.</summary>
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
            throw new InvalidOperationException("The screen that corrects words already has a corpus.");
        }

        _corpus = corpus;
    }

    /// <summary>Which language this screen is being read in. It draws again and it keeps what was typed.</summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();

        if (_meeting is not null)
        {
            Render();
        }
    }

    /// <summary>Opens one meeting: reads where it is filed, what it seems to get wrong and what is already fixed.</summary>
    /// <param name="meetingId">The meeting the words were read in.</param>
    /// <param name="asWritten">
    /// Words selected on the meeting's transcript or on a voice's quotation, already trimmed, which
    /// open in the field selected and pinned as a ticked form whatever the search finds — or nothing
    /// when the screen is opened on its own.
    /// </param>
    public void Show(Guid meetingId, string? asWritten = null)
    {
        Reset();
        _meeting = meetingId;
        _asked = string.IsNullOrWhiteSpace(asWritten) ? null : asWritten.Trim();

        if (_asked is not null)
        {
            _drawing = true;
            TypedField.Text = _asked;
            _drawing = false;
        }

        _ = ReadAsync(meetingId, ++_generation);
        Render();
    }

    /// <summary>Lets go of the meeting and of everything drawn about it.</summary>
    public void Close()
    {
        Reset();
        _meeting = null;
        WhichMeetingText.Text = string.Empty;
        Render();
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
            "The screen that corrects words was never given a corpus, so it has no meeting to show.");

    private Style Chrome(string named) => (Style)Root.Resources[named];

    /// <summary>Forgets everything about the meeting that was on screen, and drops what is still coming.</summary>
    private void Reset()
    {
        _generation++;
        _search++;
        _typing.Stop();
        _held = null;
        _forms = [];
        _searched = string.Empty;
        _suspects = [];
        _ticked.Clear();
        _asked = null;
        _pinned = null;
        _theyAreToBeTicked = false;
        _status.Nothing();
        Saving(false);

        ClearTheField();
    }

    /// <summary>Empties the field without that being read as somebody typing.</summary>
    private void ClearTheField()
    {
        _drawing = true;
        TypedField.Text = string.Empty;
        _drawing = false;
    }

    // ── Reading ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Everything the corpus says about this meeting that the screen draws, read once.</summary>
    private sealed record Held(
        Meeting Meeting,
        IReadOnlyList<(Guid Node, IReadOnlyList<string> Path)> Places,
        IReadOnlyDictionary<Guid, string[]> Paths,
        UnpromptedWords Unprompted,
        IReadOnlyList<WordCorrected> Corrected,
        IReadOnlySet<string> AlreadyWrittenByACorrection);

    /// <summary>Reads the meeting's places, its suspects and the corrections made, off the UI thread.</summary>
    private async Task ReadAsync(Guid meetingId, int generation)
    {
        if (Corpus().Folder is not { } folder)
        {
            _status.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
            Render();
            return;
        }

        try
        {
            var held = await Task.Run(() => Read(folder, meetingId));

            if (generation != _generation)
            {
                return;
            }

            _held = held;
            _suspects = [.. held.Unprompted.Suspects];
            WhichMeetingText.Text = ScreenNumbers.Which(held.Meeting);

            if (_asked is { } asked)
            {
                _asked = null;

                if (held.AlreadyWrittenByACorrection.Contains(asked))
                {
                    // A correction keyed on words a correction wrote would leave the transcript as it
                    // is and read as though it had saved: it is said here and nothing is pinned.
                    _status.Says(UiTexts.AlreadyCorrected);
                }
                else
                {
                    _pinned = asked;
                    _theyAreToBeTicked = true;
                    Render();
                    await SearchAsync();
                    _ = TypedField.Focus(FocusState.Programmatic);
                    TypedField.SelectAll();
                    return;
                }
            }
        }
        catch (MeetingStageException gone)
        {
            if (generation != _generation)
            {
                return;
            }

            _status.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
        }
        catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
        {
            if (generation != _generation)
            {
                return;
            }

            _status.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
        }

        Render();
    }

    private static Held Read(DirectoryInfo folder, Guid meetingId)
    {
        using var context = CorpusDatabase.Open(folder);

        var meeting = new MeetingReading(context, TimeProvider.System).Row(meetingId);

        var classifying = new MeetingClassifying(context, TimeProvider.System);

        // The places a correction can hold in, from the one read the dialogue on the meeting's own
        // screen offers too, so the two cannot offer different scopes for one meeting.
        var places = classifying.Places(meetingId);
        var paths = places.ToDictionary(place => place.Node, place => place.Path.ToArray());

        // A correction for one meeting is an edit to one transcript, which this screen does not
        // write and so does not list as a fix of words that keep coming out wrong.
        var made = context.TerminologyCorrections
            .AsNoTracking()
            .Where(correction => correction.MeetingId == null)
            .ToList();

        // Only for nodes the corpus still holds: PathTo refuses an absent one, and a correction
        // outliving its node is listed without a place.
        var existing = context.Nodes.AsNoTracking().Select(node => node.Id).ToHashSet();
        foreach (var node in made.Select(correction => correction.NodeId).OfType<Guid>().Distinct())
        {
            if (!paths.ContainsKey(node) && existing.Contains(node))
            {
                paths[node] = [.. classifying.PathTo(node).Nodes.Select(step => step.Name)];
            }
        }

        // What the corrections already reaching this meeting wrote: the transcript shows corrected
        // text, so words selected on it may be the very words a correction put there.
        var written = MeetingRenderer.CorrectionsReaching(context, meetingId)
            .Select(correction => correction.CorrectText)
            .ToHashSet(StringComparer.Ordinal);

        return new Held(
            meeting, places, paths, FindingCorrections.UnpromptedIn(context, meetingId), WordsScreen.Corrected(made), written);
    }

    // ── Searching ────────────────────────────────────────────────────────────────────────────

    private void OnTyped(object sender, TextChangedEventArgs e)
    {
        if (_drawing)
        {
            return;
        }

        _typing.Stop();
        _typing.Start();
        RefreshSave();
    }

    private void OnTypingPaused(DispatcherQueueTimer sender, object args)
    {
        _typing.Stop();

        // A form put in the field by an answer is already searched for.
        if (TypedField.Text.Trim() == _searched)
        {
            return;
        }

        _ = SearchAsync();
    }

    /// <summary>Asks the corpus how it wrote something like what is in the field, and ticks the close ones.</summary>
    private async Task SearchAsync()
    {
        var typed = TypedField.Text.Trim();
        var mine = ++_search;
        var generation = _generation;

        if (typed.Length == 0 || Corpus().Folder is not { } folder)
        {
            _forms = [];
            _searched = string.Empty;
            _ticked.Clear();
            Render();
            return;
        }

        IReadOnlyList<WrittenForm> found;
        try
        {
            found = await Task.Run(() =>
            {
                using var context = CorpusDatabase.Open(folder);
                return FindingCorrections.LikeTyped(context, typed);
            });
        }
        catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
        {
            if (mine == _search && generation == _generation)
            {
                _status.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
                Render();
            }

            return;
        }

        // The field moved on, or the screen did, while the corpus was being read: the answer is
        // for a term nobody is looking at.
        if (mine != _search || generation != _generation || TypedField.Text.Trim() != typed)
        {
            return;
        }

        _status.Nothing();
        _searched = typed;
        _forms = WithThePinned(found);
        _ticked.Clear();
        foreach (var form in _forms.Where(form => (_theyAreToBeTicked && form.Text == _pinned) || WordsScreen.StartsTicked(form)))
        {
            _ticked.Add(form.Text);
        }

        _theyAreToBeTicked = false;

        Render();
    }

    /// <summary>
    /// What the search found with the pinned form first: the one the corpus wrote with its own
    /// counts when it found it, and one made of the selection itself when it did not.
    /// </summary>
    /// <remarks>
    /// Made rather than left out: the words were read on this meeting a moment ago, so at least one
    /// meeting wrote them once, and a search for something typed over them that did not return them
    /// is not a reason to lose what this screen was opened for.
    /// </remarks>
    private IReadOnlyList<WrittenForm> WithThePinned(IReadOnlyList<WrittenForm> found)
    {
        if (_pinned is not { } pinned)
        {
            return found;
        }

        var theOne = found.FirstOrDefault(form => form.Text == pinned)
            ?? new WrittenForm(pinned, Times: 1, Meetings: 1, Resemblance: 1);

        return [theOne, .. found.Where(form => form.Text != pinned)];
    }

    // ── Drawing ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Puts everything the screen knows onto the controls.</summary>
    private void Render()
    {
        _drawing = true;

        try
        {
            ShowTheScopes();
            ShowTheForms();
            ShowTheSuspects();
            ShowWhatIsFixed();
            ShowTheStatus();
            RefreshSave();

            SomeUnreadText.Text = In(UiTexts.SomeMeetingsCouldNotBeRead);
            SomeUnreadText.Visibility = _held?.Unprompted.Unread.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally
        {
            _drawing = false;
        }
    }

    /// <summary>
    /// Everywhere first, then each place the meeting is filed under or above, root to deepest. The
    /// choice stays where it was when the language moves, and falls back to everywhere when the
    /// place it was on is not there any more.
    /// </summary>
    private void ShowTheScopes()
    {
        var chosen = ScopeBox.SelectedIndex;
        var places = _held?.Places ?? [];

        string[] offered =
        [
            In(UiTexts.InEveryMeeting),
            .. places.Select(place => UiTexts.OnlyIn.In(_language, ScreenNumbers.Inside([.. place.Path]))),
        ];

        ScopeBox.ItemsSource = offered;
        ScopeBox.SelectedIndex = chosen >= 0 && chosen < offered.Length ? chosen : 0;
        AutomationProperties.SetName(ScopeBox, In(UiTexts.InEveryMeeting));
    }

    /// <summary>The node the correction would hold under, or nothing for everywhere.</summary>
    private Guid? ChosenScope() =>
        ScopeBox.SelectedIndex > 0 && _held is { } held && ScopeBox.SelectedIndex - 1 < held.Places.Count
            ? held.Places[ScopeBox.SelectedIndex - 1].Node
            : null;

    private void ShowTheForms()
    {
        TheForms.Children.Clear();

        for (var position = 0; position < _forms.Count; position++)
        {
            TheForms.Children.Add(AForm(_forms[position], position));
        }

        var nothing = _searched.Length > 0 && _forms.Count == 0;
        NothingLikeItText.Text = nothing ? In(UiTexts.NothingWrittenLikeIt) : string.Empty;
        NothingLikeItText.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One way the corpus wrote the word: a tick, the form and how often.</summary>
    private CheckBox AForm(WrittenForm form, int position)
    {
        var meetings = form.Meetings == 1
            ? In(UiTexts.OneMeeting)
            : UiTexts.MeetingsCounted.In(_language, form.Meetings);
        var count = form.Times == 1
            ? UiTexts.OnceIn.In(_language, meetings)
            : UiTexts.TimesIn.In(_language, form.Times, meetings);

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        row.Children.Add(new TextBlock
        {
            Text = form.Text,
            Style = Chrome("FormText"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        row.Children.Add(new TextBlock
        {
            Text = count,
            Style = Chrome("FormCount"),
            VerticalAlignment = VerticalAlignment.Center,
        });

        var tick = new CheckBox
        {
            Style = Chrome("FormTick"),
            Content = row,
            IsChecked = _ticked.Contains(form.Text),
        };

        AutomationProperties.SetAutomationId(tick, $"form-{position}");
        AutomationProperties.SetName(tick, ScreenNumbers.Beside(form.Text, count));
        tick.Checked += (_, _) => Ticked(form.Text, on: true);
        tick.Unchecked += (_, _) => Ticked(form.Text, on: false);

        return tick;
    }

    private void Ticked(string form, bool on)
    {
        if (_drawing)
        {
            return;
        }

        _ = on ? _ticked.Add(form) : _ticked.Remove(form);
        RefreshSave();
    }

    /// <summary>The words that turned up by themselves, the first few, and how many more there are.</summary>
    private void ShowTheSuspects()
    {
        TheSuspects.Children.Clear();

        var shown = _suspects.Take(WordsScreen.SuspectsShown).ToArray();
        for (var position = 0; position < shown.Length; position++)
        {
            TheSuspects.Children.Add(ASuspect(shown[position], position));
        }

        var more = _suspects.Count - shown.Length;
        MoreSuspectsText.Text = more > 0 ? UiTexts.MoreLeft.In(_language, more) : string.Empty;
        MoreSuspectsText.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Still being read is not the same as nothing wrong, so nothing is claimed until it is read.
        SuspectsCard.Visibility = shown.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        var nothing = _held is not null && _suspects.Count == 0;
        NothingSeemsWrongText.Text = nothing ? In(UiTexts.NothingHereSeemsWrong) : string.Empty;
        NothingSeemsWrongText.Visibility = nothing ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>One word that may have come out wrong: it, how often, the commoner one it resembles, and the two answers.</summary>
    private Grid ASuspect(SuspectWord suspect, int position)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var words = new StackPanel { Spacing = 2 };
        words.Children.Add(new TextBlock { Text = suspect.Word, Style = Chrome("SuspectText") });
        words.Children.Add(new TextBlock
        {
            Text = ScreenNumbers.Beside(
                string.Create(CultureInfo.InvariantCulture, $"{suspect.Times}×"),
                string.Create(CultureInfo.InvariantCulture, $"{suspect.LooksLike} {suspect.LooksLikeTimes}×")),
            Style = Chrome("SuspectData"),
        });
        row.Children.Add(words);

        var no = new Button { Content = In(UiTexts.ItIsNot), Style = Chrome("SuspectAnswer") };
        AutomationProperties.SetAutomationId(no, $"suspect-no-{position}");
        no.Click += (_, _) => _ = SaidItIsRightAsync(suspect);
        Grid.SetColumn(no, 1);
        row.Children.Add(no);

        var yes = new Button { Content = In(UiTexts.ItIsThatOne), Style = Chrome("SuspectAnswer") };
        AutomationProperties.SetAutomationId(yes, $"suspect-yes-{position}");
        yes.Click += (_, _) => _ = ThatOneAsync(suspect);
        Grid.SetColumn(yes, 2);
        row.Children.Add(yes);

        return row;
    }

    /// <summary>What has already been fixed, the first few, and how many more there are.</summary>
    private void ShowWhatIsFixed()
    {
        TheFixed.Children.Clear();

        var made = _held?.Corrected ?? [];
        foreach (var fix in made.Take(WordsScreen.CorrectedShown))
        {
            var scope = fix.Under is { } node && _held is { } held && held.Paths.TryGetValue(node, out var path)
                ? UiTexts.OnlyIn.In(_language, ScreenNumbers.Inside(path))
                : In(UiTexts.InEveryMeeting);

            var one = new StackPanel { Spacing = 2 };
            one.Children.Add(new TextBlock { Text = fix.Right, Style = Chrome("FixedWord") });
            one.Children.Add(new TextBlock { Text = scope, Style = Chrome("FixedScope") });
            one.Children.Add(new TextBlock { Text = string.Join(", ", fix.Wrong), Style = Chrome("FixedForms") });
            TheFixed.Children.Add(one);
        }

        var more = made.Count - WordsScreen.CorrectedShown;
        MoreFixedText.Text = more > 0 ? UiTexts.AndMore.In(_language, more) : string.Empty;
        MoreFixedText.Visibility = more > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowTheStatus()
    {
        StatusText.Text = _status.In(_language);
        StatusText.Visibility = _status.IsSaying ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>The save press carries how many are ticked and is live exactly when there is something to save.</summary>
    private void RefreshSave()
    {
        var saving = FormsToSave(TypedField.Text.Trim()).Length;

        SaveTheTickedButton.Content = UiTexts.SaveTheTicked.In(_language, saving);

        // Only while the forms on screen answer the word in the field: a form list for a word that
        // was edited since would be saved as forms of the new one.
        SaveTheTickedButton.IsEnabled = !_saving
            && TypedField.Text.Trim() == _searched
            && WordsScreen.MayBeSaved(TypedField.Text, saving);
        TypedField.IsEnabled = !_saving;
        ScopeBox.IsEnabled = !_saving;
    }

    /// <summary>
    /// The ticked forms that are not the word itself: <c>CorrectingWords.Correct</c> skips that one,
    /// and a save of nothing but it would read as done.
    /// </summary>
    private string[] FormsToSave(string right) => [.. _forms
        .Select(form => form.Text)
        .Where(form => _ticked.Contains(form) && !string.Equals(form, right, StringComparison.Ordinal))];

    // ── Answering ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Saves the ticked forms as the word typed, in the place chosen, and renders every meeting
    /// they touch. The screen stays open, because several words get fixed in one sitting.
    /// </summary>
    private async void OnSave(object sender, RoutedEventArgs e)
    {
        var right = TypedField.Text.Trim();
        var forms = FormsToSave(right);

        if (_saving
            || _meeting is not { } meeting
            || Corpus().Folder is not { } folder
            || !WordsScreen.MayBeSaved(right, forms.Length))
        {
            return;
        }

        var generation = _generation;
        var scope = ChosenScope();

        Saving(true);
        _status.Nothing();
        RefreshSave();

        var saved = true;
        try
        {
            await Task.Run(() => CorrectingWords.Correct(folder, right, forms, scope, TimeProvider.System));
        }
        catch (RenderException late)
        {
            // The corrections landed; only the meetings named are not yet showing them, and the
            // next launch writes each of them again.
            var unshown = late.Meetings.Count;

            _status.Says(
                UiTexts.SavedButNotYetShownIn,
                unshown == 1 ? In(UiTexts.OneMeeting) : UiTexts.MeetingsCounted.In(_language, unshown));
        }
        catch (ArgumentException stale)
        {
            // A place removed since the screen drew: the corpus moved underneath it, and nothing was
            // written.
            saved = false;
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            saved = false;
            _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
        }

        if (generation != _generation)
        {
            return;
        }

        Saving(false);

        if (!saved)
        {
            Render();
            return;
        }

        _search++;
        _typing.Stop();
        _forms = [];
        _searched = string.Empty;
        _ticked.Clear();
        _pinned = null;
        ClearTheField();
        Render();

        await ReadAgainAsync(meeting, generation);
    }

    /// <summary>Reads the suspects and the corrections again after a save, keeping any line the save left.</summary>
    private async Task ReadAgainAsync(Guid meetingId, int generation)
    {
        if (Corpus().Folder is not { } folder)
        {
            return;
        }

        try
        {
            var held = await Task.Run(() => Read(folder, meetingId));

            if (generation == _generation)
            {
                _held = held;
                _suspects = [.. held.Unprompted.Suspects];
            }
        }
        catch (Exception unreadable) when (unreadable is MeetingStageException || ScreenFailures.Reportable(unreadable))
        {
            if (generation == _generation)
            {
                _status.KeepsWhatWasSaid(TextLine.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message));
            }
        }

        if (generation == _generation)
        {
            Render();
        }
    }

    /// <summary><em>No</em>: the word is right as written, and is not offered again.</summary>
    private async Task SaidItIsRightAsync(SuspectWord suspect)
    {
        if (_saving || Corpus().Folder is not { } folder)
        {
            return;
        }

        var generation = _generation;

        try
        {
            var now = UtcTimestamp.From(TimeProvider.System.GetUtcNow());
            await Task.Run(() =>
            {
                using var context = CorpusDatabase.Open(folder);
                new CorpusSettings(context).SayItIsRight(suspect.Word, now);
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (generation == _generation)
            {
                _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
                Render();
            }

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _suspects.Remove(suspect);
        Render();
    }

    /// <summary>
    /// <em>Sí, es esa</em>: the lookalike goes into the field, as the stored turns most often wrote
    /// it, and the search runs. It saves nothing: the lookalike is counted case-blind, and saving
    /// from here could store <c>deepgram</c> where the person means <c>Deepgram</c>.
    /// </summary>
    private async Task ThatOneAsync(SuspectWord suspect)
    {
        if (_saving || Corpus().Folder is not { } folder)
        {
            return;
        }

        var generation = _generation;
        string written;

        try
        {
            written = await Task.Run(() =>
            {
                using var context = CorpusDatabase.Open(folder);
                return FindingCorrections.AsMostOftenWritten(context, suspect.LooksLike);
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (generation == _generation)
            {
                _status.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
                Render();
            }

            return;
        }

        if (generation != _generation)
        {
            return;
        }

        _drawing = true;
        TypedField.Text = written;
        _drawing = false;

        await SearchAsync();
    }

    /// <summary>
    /// Whether the way back is open: not while a save renders, because leaving would read the
    /// meeting again before the transcripts the save is still rendering have changed. The
    /// window's app bar draws its back press dead on it.
    /// </summary>
    public bool MayGoBack => !_saving;

    /// <summary>Raised where <see cref="MayGoBack"/> changes value.</summary>
    public event EventHandler? MayGoBackChanged;

    /// <summary>
    /// Leaves without writing anything; whatever was saved is already in the corpus. Refused
    /// while a save renders, here rather than on whichever press asked: the app bar's button and
    /// Alt+Left reach the same door.
    /// </summary>
    public void GoBack()
    {
        if (_saving)
        {
            return;
        }

        Close();
        Left?.Invoke(this, EventArgs.Empty);
    }
}
