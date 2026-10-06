using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Processing.Rendering;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// The dialogue that corrects one word where it is read: over a line of a meeting's transcript, or
/// over a voice's quotation.
/// </summary>
/// <remarks>
/// <para>
/// The third dialogue <c>docs/design.md</c> §Notices allows, and the one place that list was opened
/// after being closed: the owner asked for a dialogue in as many words. It is one component for the
/// two screens that open it, for the reason <see cref="AddingSomebody"/> is.
/// </para>
/// <para>
/// Saving is <see cref="CorrectingWords.Correct"/> and nothing else, off the UI thread. That is the
/// one door corrections go through: it writes them, commits, and renders again every meeting they
/// touch, so what a screen says was corrected is what the rendered files say. A
/// <see cref="RenderException"/> out of it is not a failure to save — the corrections always land
/// first — so it closes the dialogue as saved and leaves <see cref="Afterwards"/> for the screen to
/// say, read off the exception's type and its list of meetings and never its message.
/// </para>
/// <para>
/// What reaches it is one word: <see cref="TheWordIn"/> is what both screens trim a selection with,
/// so <em>Deepgram,</em> or a stretch across a space never becomes a form the transcript has no
/// match for. The transcript shows corrected text, so a word may be the very word a correction
/// already reaching the meeting wrote — and a correction keyed on that would leave the transcript
/// as it is and close as though it had saved, so the dialogue says so and offers no save.
/// </para>
/// </remarks>
public sealed partial class CorrectingAWord : ContentDialog
{
    private UiLanguage _language;
    private CorpusFolder? _corpus;
    private string _written = string.Empty;
    private IReadOnlyList<(Guid Node, IReadOnlyList<string> Path)> _places = [];
    private bool _saved;

    public CorrectingAWord() => InitializeComponent();

    /// <summary>
    /// What the screen that opened this still has to say after a save, or nothing: the corrections
    /// landed and some meetings are not yet showing them.
    /// </summary>
    public TextLine? Afterwards { get; private set; }

    /// <summary>
    /// What this dialogue says, in the language it is being asked in. Every word on it comes
    /// through here, which is how it names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>
    /// The one word a selection stands for, or nothing when it holds none: the first run of word
    /// characters in it, as <see cref="Spellings.WordsOf"/> reads them — the rule corrections are
    /// applied with.
    /// </summary>
    public static string? TheWordIn(string? selection) =>
        string.IsNullOrEmpty(selection) ? null : Spellings.WordsOf(selection).FirstOrDefault();

    /// <summary>
    /// What a right-click on selectable words offers: <paramref name="label"/>, which hands what is
    /// selected to <paramref name="chose"/>. The one menu both screens that open this dialogue put
    /// on their words, so the two cannot offer the act under different conditions.
    /// </summary>
    /// <remarks>
    /// Alive only while the selection holds a word, which is read when the menu opens and again when
    /// it is pressed and never remembered from before: the words are the line's own at that moment.
    /// </remarks>
    public static MenuFlyout OfferedOver(TextBlock words, string label, Action<string> chose)
    {
        ArgumentNullException.ThrowIfNull(words);
        ArgumentNullException.ThrowIfNull(chose);

        var correct = new MenuFlyoutItem { Text = label };
        correct.Click += (_, _) => chose(words.SelectedText);

        var menu = new MenuFlyout();
        menu.Items.Add(correct);
        menu.Opening += (_, _) => correct.IsEnabled = TheWordIn(words.SelectedText) is not null;
        return menu;
    }

    /// <summary>
    /// Opens the dialogue over one word of one meeting.
    /// </summary>
    /// <param name="corpus">The corpus the meeting is in.</param>
    /// <param name="meeting">The meeting the word was read in, which is where its places come from.</param>
    /// <param name="asWritten">The word as the transcript shows it, already <see cref="TheWordIn"/>'s.</param>
    /// <param name="language">The language the screen is read in.</param>
    /// <param name="over">The window's root, for a dialogue not already in its tree.</param>
    /// <returns>Whether a correction was saved. The caller reads the transcript again when it was.</returns>
    public async Task<bool> AskAsync(
        CorpusFolder corpus, Guid meeting, string asWritten, UiLanguage language, XamlRoot over)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentException.ThrowIfNullOrWhiteSpace(asWritten);
        ArgumentNullException.ThrowIfNull(over);

        _corpus = corpus;
        _language = language;
        _written = asWritten;
        _saved = false;
        Afterwards = null;
        _places = [];

        // The markup's one-time labels were evaluated when the page was built, before any language
        // was set, so they read Spanish under an English title. They are evaluated again now.
        Bindings.Update();

        TheWordAsWritten.Text = asWritten;
        TheWordAsItShouldBeBox.Text = string.Empty;
        DialogueStatusText.Text = string.Empty;
        DialogueStatusText.Visibility = Visibility.Collapsed;
        IsPrimaryButtonEnabled = false;

        var already = ReadWhatIsAround(meeting);

        TheWordAsItShouldBeBox.Visibility = already ? Visibility.Collapsed : Visibility.Visible;
        ScopeBox.Visibility = already ? Visibility.Collapsed : Visibility.Visible;
        AlreadyCorrectedText.Visibility = already ? Visibility.Visible : Visibility.Collapsed;

        if (already)
        {
            // No save to press: the press is taken off rather than left dead beside a sentence
            // saying why. Set after the bindings were read again, which would put the words back.
            PrimaryButtonText = string.Empty;
        }

        ShowTheScopes();

        // A dialogue declared in a screen's own markup is already in the window's tree and has its
        // root; one that is not would throw where it is shown, off a build with nothing wrong in it.
        if (XamlRoot is null)
        {
            XamlRoot = over;
        }

        await ShowAsync();
        return _saved;
    }

    /// <summary>
    /// Reads the places a correction can hold in and whether the word is one a correction already
    /// wrote, in one read of the corpus, and lets go of it at once.
    /// </summary>
    /// <returns>True when a correction already reaching the meeting wrote this word.</returns>
    private bool ReadWhatIsAround(Guid meeting)
    {
        if (_corpus?.Folder is not { } folder)
        {
            Say(TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, _corpus?.Path ?? string.Empty));
            return false;
        }

        try
        {
            using var context = CorpusDatabase.OpenReadOnly(folder);

            _places = new MeetingClassifying(context, TimeProvider.System).Places(meeting);

            return MeetingRenderer.CorrectionsReaching(context, meeting)
                .Any(correction => string.Equals(correction.CorrectText, _written, StringComparison.Ordinal));
        }
        catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
        {
            // The dialogue goes on with nothing to hold the correction under but everywhere: the
            // places are a choice, and reading them is not what saving needs.
            Say(TextLine.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message));
            return false;
        }
    }

    /// <summary>
    /// Everywhere first, then each place the meeting is filed under or above, root to deepest —
    /// the choice the screen of every word that comes out wrong carries, read from the same place.
    /// </summary>
    private void ShowTheScopes()
    {
        ScopeBox.Items.Clear();
        ScopeBox.Items.Add(In(UiTexts.InEveryMeeting));

        foreach (var place in _places)
        {
            ScopeBox.Items.Add(UiTexts.OnlyIn.In(_language, ScreenNumbers.Inside([.. place.Path])));
        }

        ScopeBox.SelectedIndex = 0;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(ScopeBox, In(UiTexts.InEveryMeeting));
    }

    /// <summary>The node the correction would hold under, or nothing for everywhere.</summary>
    private Guid? ChosenScope() =>
        ScopeBox.SelectedIndex > 0 && ScopeBox.SelectedIndex - 1 < _places.Count
            ? _places[ScopeBox.SelectedIndex - 1].Node
            : null;

    private void OnOpened(ContentDialog sender, ContentDialogOpenedEventArgs args) =>
        TheWordAsItShouldBeBox.Focus(FocusState.Programmatic);

    /// <summary>
    /// The act is dead until there is a word that differs from the one that stands: a form
    /// identical to what it becomes is skipped by the write, which would close this as saved with
    /// nothing saved.
    /// </summary>
    private void OnTyped(object sender, TextChangedEventArgs e)
    {
        var typed = (TheWordAsItShouldBeBox.Text ?? string.Empty).Trim();
        IsPrimaryButtonEnabled = typed.Length > 0 && !string.Equals(typed, _written, StringComparison.Ordinal);
    }

    /// <summary>The act: saves, off the UI thread, and stays open having said why when it cannot.</summary>
    private async void OnCorrect(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // The dialogue holds still until the write is over, so a second press cannot start another
        // and the words typed are not lost to a corpus that was busy for a second.
        var deferral = args.GetDeferral();

        try
        {
            args.Cancel = !await CommitAsync();
        }
        finally
        {
            deferral.Complete();
        }
    }

    private async Task<bool> CommitAsync()
    {
        var right = (TheWordAsItShouldBeBox.Text ?? string.Empty).Trim();

        if (right.Length == 0 || _corpus?.Folder is not { } folder)
        {
            return false;
        }

        var under = ChosenScope();
        var written = _written;
        IsPrimaryButtonEnabled = false;

        try
        {
            await Task.Run(() => CorrectingWords.Correct(folder, right, [written], under, TimeProvider.System));
            _saved = true;
            return true;
        }
        catch (RenderException late)
        {
            // The corrections always land before this is thrown, and the next launch renders each
            // meeting it names again. That is not this dialogue's failure to report: it closes as
            // saved and the screen says how many meetings are not yet showing it.
            _saved = true;

            var unshown = late.Meetings.Count;
            Afterwards = TextLine.Says(
                UiTexts.SavedButNotYetShownIn,
                unshown == 1 ? In(UiTexts.OneMeeting) : UiTexts.MeetingsCounted.In(_language, unshown));

            return true;
        }
        catch (ArgumentException stale)
        {
            // A place removed since the dialogue opened: the corpus moved underneath it, and
            // nothing was written.
            Say(TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, stale.Message));
            IsPrimaryButtonEnabled = true;
            return false;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            Say(TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message));
            IsPrimaryButtonEnabled = true;
            return false;
        }
    }

    private void Say(TextLine line)
    {
        DialogueStatusText.Text = line.In(_language);
        DialogueStatusText.Visibility = Visibility.Visible;
    }
}
