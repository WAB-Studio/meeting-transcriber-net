using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace MeetingTranscriber.App;

/// <summary>
/// The settings screen: what should happen when a recording ends, what runs it, who is using this
/// install and in which language, and where the corpus is.
/// </summary>
/// <remarks>
/// <para>
/// It decides nothing about a meeting. The one question on it that spends money is what happens
/// when a recording ends, and what that answer then does is <c>WhatStoppingStarts</c>'s, in a
/// project a build agent can run — this control's whole job is turning the answer into a row and
/// the row back into three options.
/// </para>
/// <para>
/// It opens no corpus it does not let go of, for the reason <see cref="MeetingsDrawer"/> gives: a
/// corpus that came into existence while this screen was up — the first recording makes one, and
/// so does keeping who is using the application — is exactly the case a remembered answer gets
/// wrong. So every read happens inside the method that needs it, and <see cref="Show"/> re-reads
/// the lot.
/// </para>
/// <para>
/// <b>The options go dead unless the answer on disk was really read.</b> Not merely when there is
/// no corpus: a corpus that would not open reads exactly like one nobody has answered in, and the
/// difference between those two is whether the next recording somebody stops costs them money. So
/// a failed read leaves no option ticked and nothing pressable, and the line under it says what the
/// machine said — which is the same three-state shape <see cref="WhoIsUsingThisRow"/> carries one
/// block down, applied to the question that spends.
/// </para>
/// <para>
/// A control and not a window, the way the meeting screen and the filing screen are. It is reached
/// from the gear on the front door and returns there, and a meeting under way keeps its strip above
/// it for the whole of that — which is why the window counts this as the room below having the
/// window rather than as a second opinion beside the recorder.
/// </para>
/// </remarks>
public sealed partial class Configuracion : UserControl
{
    /// <summary>
    /// The order the picker offers, and the order it reads a selection back in. One array, read
    /// twice, so the two cannot come apart.
    /// </summary>
    private static readonly UiLanguage[] Languages = Enum.GetValues<UiLanguage>();

    /// <summary>
    /// Where this application's corpus is, or what stopped it being found. Handed over once by the
    /// window that holds this, for the reason the sibling screens give: what XAML constructs takes
    /// no arguments, so <see cref="Open"/> is the seam instead.
    /// </summary>
    private CorpusFolder? _corpus;

    /// <summary>
    /// The window this screen is on, which the folder picker needs and a <c>UserControl</c> has
    /// none of its own. Handed over alongside the corpus rather than reached for through
    /// <c>XamlRoot</c>, which is one more thing to be null while the screen is being built.
    /// </summary>
    private WindowId _window;

    private UiLanguage _language;

    /// <summary>Whether this screen is up. There is no meeting under it, so nothing else says.</summary>
    private bool _open;

    /// <summary>
    /// Whether a folder picker is up, or the folder it answered with is being written. The shape
    /// <see cref="_writingTheAnswer"/> already has, so a second press cannot land while the first
    /// is still running.
    /// </summary>
    private bool _choosingAFolder;

    /// <summary>
    /// What the row about who is using the application knows that the field itself does not: what
    /// the last read of the corpus found, and whether a press is in flight. What is typed is read
    /// off the field at the moment it is asked, which is why that half is not kept here.
    /// </summary>
    private WhoIsUsingThisRow _whoIsUsingThis = WhoIsUsingThisRow.Unread;

    /// <summary>
    /// What is settled about a recording that ends, as of the last read. Kept so that a press
    /// choosing what is already chosen writes nothing.
    /// </summary>
    private AfterARecording _afterARecording = AfterARecording.DoNothing;

    /// <summary>
    /// Whether the field above is what the corpus says, rather than the answer this screen falls
    /// back on when it cannot ask. The two look identical on screen and mean opposite things, so
    /// the difference is kept rather than inferred.
    /// </summary>
    private bool _settledIsKnown = true;

    /// <summary>
    /// Whether an answer about a recording that ends is on its way to the corpus. It is the state
    /// the options would not otherwise have, and without it a second press lands while the first is
    /// still running — which on a machine that has never recorded is a second migration of the
    /// whole schema racing the first for the same write lock.
    /// </summary>
    private bool _writingTheAnswer;

    /// <summary>True while the controls are being filled, so filling them is not somebody choosing.</summary>
    private bool _filling;

    private readonly ScreenStatus _status = new();

    /// <summary>True once the window closed, so nothing draws into a control that is going.</summary>
    private bool _closed;

    public Configuracion() => InitializeComponent();

    /// <summary>Somebody asked to go back to where they came from.</summary>
    public event EventHandler? Left;

    /// <summary>
    /// Somebody picked the language the application is read in. What is done about it is not this
    /// screen's: it is raised on up through the window, which is what owns that answer.
    /// </summary>
    public event EventHandler<UiLanguage>? LanguageChosen;

    /// <summary>
    /// Somebody named a folder this application can open its corpus from and it was recorded as
    /// where the corpus is. What is done about it is not this screen's, the same way the language
    /// is not: it is raised on up through the window. No folder rides with it — the one answer that
    /// matters is the setting this just wrote, which the application re-reads rather than trusting
    /// a folder handed across a screen boundary, so a payload here would be a fact nothing reads.
    /// </summary>
    public event EventHandler? CorpusChosen;

    /// <summary>Whether this screen is on the window.</summary>
    public bool IsOpen => _open;

    /// <summary>
    /// Hands over the corpus this install keeps its meetings in, and the window it is on, which
    /// the folder picker needs. Reads nothing: nothing on this screen is answered until it is
    /// shown.
    /// </summary>
    /// <exception cref="InvalidOperationException">It was opened twice.</exception>
    public void Open(CorpusFolder corpus, WindowId window)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        if (_corpus is not null)
        {
            throw new InvalidOperationException("The settings screen already has a corpus.");
        }

        _corpus = corpus;
        _window = window;
    }

    /// <summary>Which language this screen is being read in.</summary>
    /// <remarks>
    /// It reads nothing out of the corpus, and that is the same rule the front door applied while
    /// it carried the row about who is using the application: what is in that field may be half
    /// typed, and re-reading it here would take a name out from under somebody mid-answer because
    /// they switched language to read the question.
    /// </remarks>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();

        FillTheLanguagePicker();
        ShowWhatHappensWhenARecordingEnds();
        ShowWhoIsUsingThis();
        SayWhereTheCorpusIs();
        Render();
    }

    /// <summary>Puts this screen on the window, reading everything on it afresh.</summary>
    /// <remarks>
    /// Read here and not when the corpus was handed over, because the corpus comes into existence
    /// under this screen: the first recording makes one and so does keeping who is using the
    /// application, so an answer read once at start-up would go on saying there is none.
    /// <para>
    /// The two reads are in this order because both fail together when the corpus will not open and
    /// there is one line to say it in. The second one to speak is the one left standing, and of the
    /// two the sentence about somebody's own name is the one they cannot work out for themselves —
    /// the money question says what is wrong by going dead with nothing ticked.
    /// </para>
    /// </remarks>
    public void Show()
    {
        _open = true;
        _status.Nothing();

        ReadWhatHappensWhenARecordingEnds();
        ReadWhoIsUsingThis();

        FillTheLanguagePicker();
        ShowWhatHappensWhenARecordingEnds();
        ShowWhoIsUsingThis();
        SayWhereTheCorpusIs();
        Render();
    }

    /// <summary>Takes this screen off the window.</summary>
    public void Close()
    {
        _open = false;
        _status.Nothing();
        Render();
    }

    /// <summary>The window is going. Nothing here draws after this.</summary>
    public void Closing() => _closed = true;

    /// <summary>
    /// What a text says in the language this screen is being read in. The XAML binds to it, which
    /// is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>The corpus this screen was given, which every read goes through.</summary>
    private CorpusFolder Corpus() => _corpus
        ?? throw new InvalidOperationException(
            "The settings screen was never given a corpus, so it has nothing to answer about.");

    /// <summary>
    /// Reads what was settled about a recording that ends.
    /// </summary>
    /// <remarks>
    /// A corpus nobody has answered in really does answer <see cref="AfterARecording.DoNothing"/>,
    /// and a corpus that would not open answers nothing at all — which is the whole of why
    /// <see cref="_settledIsKnown"/> exists. Shown as the first, the second would leave somebody
    /// looking at <em>No hacer nada</em> over a corpus holding <c>transcribe</c>, and the next
    /// recording they stopped would be paid for.
    /// </remarks>
    private void ReadWhatHappensWhenARecordingEnds()
    {
        _afterARecording = AfterARecording.DoNothing;
        _settledIsKnown = true;

        if (Corpus().Folder is not { } folder || !CorpusDatabase.HoldsACorpus(folder))
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            _afterARecording = new CorpusSettings(context).WhenARecordingEnds();
        }
        catch (Exception wouldNotRead) when (ScreenFailures.Reportable(wouldNotRead))
        {
            // The machine's own words, which is what `ThatDidNotGoThrough` carries everywhere else
            // in this application. Not the sentence about a list that would not open: that one
            // sends somebody to this screen, and this screen saying it would be sending them here.
            _settledIsKnown = false;
            Say(UiTexts.ThatDidNotGoThrough, wouldNotRead.Message);
        }
    }

    /// <summary>
    /// Reads who is using this application out of the corpus and puts it in the field.
    /// </summary>
    private void ReadWhoIsUsingThis()
    {
        var name = string.Empty;

        // No corpus yet is not a failure and is not read as one: nobody has answered, which is
        // what an empty field with the question under it already says. Keeping an answer is what
        // makes the corpus, the same way the first recording does.
        if (Corpus().Folder is { } folder && ThereIsACorpus())
        {
            try
            {
                using var context = CorpusDatabase.Open(folder);
                name = new HumanLayer(context, TimeProvider.System).Me()?.DisplayName ?? string.Empty;
            }
            catch (Exception wouldNotRead) when (ScreenFailures.Reportable(wouldNotRead))
            {
                // Said rather than left blank. A corpus that will not open reads exactly like one
                // nobody has answered in, and the difference is somebody's own name: shown the
                // empty field alone, they would answer again believing nobody had.
                //
                // The row stays live through it, which is the other half. The press opens the
                // corpus the way this read did not — bringing the schema up — so a corpus one
                // migration behind is repaired by being answered; and answering cannot make a
                // second person however this read failed, because the write renames whoever
                // carries the flag rather than adding to them.
                Say(UiTexts.WhoIsUsingThisCouldNotBeRead);
            }
        }

        // The facts before the field, because setting the field is a change and the change is
        // handled: OnWhoIsUsingThisTyped draws the row before the next line would have run.
        _whoIsUsingThis = _whoIsUsingThis with
        {
            CorpusIsReachable = Corpus().Folder is not null,
            SomebodyHasSaid = name.Length > 0,
        };

        WhoIsUsingThisBox.Text = name;
    }

    /// <summary>
    /// Whether there is a corpus in that folder as of now, rather than as of when this screen was
    /// given one.
    /// </summary>
    /// <remarks>
    /// Asked and not kept, which is what <see cref="MeetingsDrawer"/> already does on every read
    /// and for the same reason: the corpus comes into existence under this screen — keeping who is
    /// using the application makes one, and so does the first recording — so an answer read once
    /// would go on saying there is none under the press that just made one. What is kept is the
    /// refusal beside it, because nothing on this screen can lift one.
    /// </remarks>
    private bool ThereIsACorpus() =>
        Corpus().Folder is { } folder && CorpusDatabase.HoldsACorpus(folder);

    /// <summary>
    /// The row as the facts that decide what it does, built fresh rather than kept, so what is on
    /// screen cannot come to disagree with what is in the field.
    /// </summary>
    private WhoIsUsingThisRow WhoIsUsingThis() =>
        _whoIsUsingThis with { Typed = WhoIsUsingThisBox.Text };

    /// <summary>
    /// Sets the row's three controls from the one answer, the way the front door sets the
    /// recorder's. Nothing here decides anything.
    /// </summary>
    private void ShowWhoIsUsingThis()
    {
        var row = WhoIsUsingThis();

        // Visibility and not merely a greyer line: an explanation that stayed would keep asking a
        // question this install has an answer to.
        NobodyHasSaidYet.Visibility = row.IsAsking ? Visibility.Visible : Visibility.Collapsed;
        WhoIsUsingThisBox.IsEnabled = row.FieldIsLive;
        WhoIsUsingThisButton.IsEnabled = row.MayBeKept;
    }

    /// <summary>
    /// Puts the three options where the answer is, and says whether any of them may be pressed.
    /// Nothing here decides anything.
    /// </summary>
    /// <remarks>
    /// The whole group every time, so the ones that are not chosen are cleared as well. A radio
    /// that was ticked and is not any more stays ticked unless something unticks it, and two ticked
    /// options is a screen saying the application will do two things.
    /// </remarks>
    private void ShowWhatHappensWhenARecordingEnds()
    {
        _filling = true;
        try
        {
            foreach (var answer in Enum.GetValues<AfterARecording>())
            {
                Offers(answer).IsChecked = _settledIsKnown && answer == _afterARecording;
            }
        }
        finally
        {
            _filling = false;
        }

        // Three reasons to be dead and they are one rule: there is nowhere this press could write
        // to, there is nothing it could be correcting because the answer was never read, or a
        // write is already on its way. The first is what the row about who is using the
        // application applies one block down.
        AfterARecordingOptions.IsEnabled =
            Corpus().Folder is not null && _settledIsKnown && !_writingTheAnswer;
    }

    /// <summary>Puts the languages in the picker without any of it reading as somebody choosing.</summary>
    private void FillTheLanguagePicker()
    {
        _filling = true;
        try
        {
            LanguagePicker.ItemsSource = Languages.Select(offered => In(UiLanguages.Endonym(offered))).ToArray();
            LanguagePicker.SelectedIndex = Array.IndexOf(Languages, _language);
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>
    /// Where the meetings go, said before the first one rather than found out afterwards — and
    /// when there is nowhere, which folder and what was wrong with it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The corpus refused for a reason this screen has no words for.
    /// </exception>
    /// <remarks>
    /// The last arm stops rather than substituting, which is <c>RecorderStates.Reaches</c> on a
    /// state it does not have and is the same rule for the same reason: a refusal added to
    /// <see cref="CorpusRefusal"/> and not given a text here would otherwise be shown to somebody
    /// as one of the others, sending them to check a folder that is fine or a setting they never
    /// wrote. A screen that says the wrong reason confidently is worse than one that stops, and
    /// this is a table falling behind its enum — a fault of the code, which nothing a person does
    /// can reach. <c>CorpusTextTests</c> is what catches it before it can be thrown.
    /// <para>
    /// This is the only line in the application that names the folder and says which refusal it
    /// was, and the meetings list's own sentence sends somebody here for it — which is what moved
    /// with it off the recording card.
    /// </para>
    /// </remarks>
    private void SayWhereTheCorpusIs()
    {
        var text = Corpus().Refusal switch
        {
            null => ThereIsACorpus()
                ? UiTexts.MeetingsAreKeptAt
                : UiTexts.TheFirstThingKeptMakesTheCorpusAt,
            CorpusRefusal.SettingSaysNothingUsable => UiTexts.TheSettingSaysNothingUsable,
            CorpusRefusal.FolderDoesNotAnswer => UiTexts.TheCorpusFolderDidNotAnswer,
            CorpusRefusal.NoCorpusInTheFolder => UiTexts.ThereIsNoCorpusInThatFolder,
            CorpusRefusal.GoesWhenThePackageDoes => UiTexts.TheCorpusFolderGoesWhenThePackageDoes,
            _ => throw new InvalidOperationException(
                $"This screen has no text for corpus refusal '{Corpus().Refusal}'."),
        };

        // One entry, read once. Every arm above takes the path and nothing else, so there is no
        // second case here and no punctuation for this screen to choose between two of them.
        CorpusText.Text = text.In(_language, Corpus().Path);

        // Drawn only over a refused corpus: a corpus that opened is moved by moving the files and
        // then saying so, and no screen offers the first half.
        ChangeWhereItIsKept.Visibility = Corpus().Refusal is null
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private void OnBack(object sender, RoutedEventArgs e) => Left?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Somebody asked to change where the corpus is kept, which is only offered over a refused
    /// one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Windows App SDK picker and not <c>Windows.Storage.Pickers.FolderPicker</c>: the older
    /// one throws <c>COMException</c> unless <c>WinRT.Interop.InitializeWithWindow.Initialize</c>
    /// has run first and throws from <c>PickSingleFolderAsync</c> unless <c>FileTypeFilter</c> has
    /// an entry. This one takes the window id in its constructor and needs neither.
    /// </para>
    /// <para>
    /// A folder somebody picked is held to the same rules a folder the setting names already is —
    /// <see cref="CorpusLocation.Inspect"/>, through the same table <see cref="SayWhereTheCorpusIs"/>
    /// reads, <see cref="RefusalOfAPickedFolder"/>.
    /// </para>
    /// <para>
    /// <b>Which line says which folder.</b> Two of them are on screen at once and they name
    /// different things: after a picked folder is refused, <see cref="CorpusText"/> goes on naming
    /// the folder the setting points at — it is the line that says where the corpus is, and it is
    /// still where the corpus is — while the status line names the folder that was just picked and
    /// what was wrong with it. <see cref="SayWhereTheCorpusIs"/> is therefore not called after a
    /// refused pick, and is called after a successful one — where the window is being replaced
    /// anyway.
    /// </para>
    /// <para>
    /// <b>Two guards and not one, because they are two different failures.</b> Only the picker
    /// itself is wrapped in a bare <c>catch</c>: it is a call into Windows, not this application's
    /// own code, so there is nothing narrower to name it by, and an <c>async void</c> handler
    /// cannot let anything escape it. Writing the chosen folder into the setting is this
    /// application's own file write and takes the same guard every other write on this screen
    /// does — <see cref="ScreenFailures.Reportable"/>, unwidened — because a disk-full or a locked
    /// settings file is not the picker failing to open, and saying so would send somebody looking
    /// at the wrong thing. <see cref="CorpusLocation.Choose"/> re-runs <see cref="CorpusLocation.Inspect"/>
    /// itself before it writes, so the folder going between this handler's own inspection and that
    /// one — unplugged, an ACL revoked — is a real, narrow window and not a closed one; accepted
    /// rather than defended against, the way every window this narrow is elsewhere in this corpus.
    /// </para>
    /// </remarks>
    private async void OnChangeWhereItIsKept(object sender, RoutedEventArgs e)
    {
        if (_choosingAFolder || Corpus().Refusal is null)
        {
            return;
        }

        _choosingAFolder = true;
        ChangeWhereItIsKept.IsEnabled = false;

        try
        {
            DirectoryInfo folder;

            try
            {
                var picker = new FolderPicker(_window);
                var picked = await picker.PickSingleFolderAsync();

                if (picked is null)
                {
                    // Nothing chosen is nothing said and nothing done.
                    return;
                }

                folder = new DirectoryInfo(picked.Path);
            }
            catch (Exception failedToOpen) when (failedToOpen is not OutOfMemoryException)
            {
                if (!_closed)
                {
                    Say(UiTexts.TheFolderPickerDidNotOpen);
                }

                return;
            }

            var inspected = CorpusLocation.Inspect(folder);

            if (inspected.Refusal is { } refusal)
            {
                if (!_closed)
                {
                    Say(RefusalOfAPickedFolder(refusal), inspected.Path);
                }

                return;
            }

            try
            {
                await Task.Run(() => CorpusLocation.OfThisUser().Choose(folder));
            }
            catch (Exception refused) when (ScreenFailures.Reportable(refused))
            {
                if (!_closed)
                {
                    Say(UiTexts.ThatDidNotGoThrough, refused.Message);
                }

                return;
            }

            if (_closed)
            {
                return;
            }

            CorpusChosen?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            _choosingAFolder = false;

            if (!_closed)
            {
                ChangeWhereItIsKept.IsEnabled = true;
            }
        }
    }

    /// <summary>
    /// The same table <see cref="SayWhereTheCorpusIs"/> reads, for a refusal <see cref="CorpusLocation.Inspect"/>
    /// answered rather than one read off <see cref="Corpus"/>. Kept exhaustive over the whole of
    /// <see cref="CorpusRefusal"/> and not narrowed to the three a picker can actually produce —
    /// <see cref="CorpusRefusal.SettingSaysNothingUsable"/> is about the setting file and a picker
    /// never touches it — because an exhaustive table is what <c>CorpusTextTests</c> can hold to
    /// <see cref="CorpusRefusal"/> the same way it already holds the one above; a table narrowed to
    /// what is reachable today has no such test and drifts silently the day a fifth refusal is
    /// added.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The corpus refused for a reason this screen has no words for.
    /// </exception>
    private static UiText RefusalOfAPickedFolder(CorpusRefusal refusal) => refusal switch
    {
        CorpusRefusal.SettingSaysNothingUsable => UiTexts.TheSettingSaysNothingUsable,
        CorpusRefusal.FolderDoesNotAnswer => UiTexts.TheCorpusFolderDidNotAnswer,
        CorpusRefusal.NoCorpusInTheFolder => UiTexts.ThereIsNoCorpusInThatFolder,
        CorpusRefusal.GoesWhenThePackageDoes => UiTexts.TheCorpusFolderGoesWhenThePackageDoes,
        _ => throw new InvalidOperationException(
            $"This screen has no text for corpus refusal '{refusal}' from a picked folder."),
    };

    /// <summary>
    /// Somebody chose what should happen when a recording ends.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written the moment it is chosen and not behind a press of its own: there is nothing to
    /// confirm — the answer costs nothing until a recording ends, and the row it replaces is the
    /// same row. The instant is this press's, taken before the write leaves this thread, which is
    /// why <see cref="CorpusSettings"/> takes one rather than a clock.
    /// </para>
    /// <para>
    /// Off this thread, and for the reason keeping who is using the application is: this is one of
    /// the two presses that can make the corpus, so on a machine that has never recorded it runs
    /// every migration there is before the first row. Answering this before recording anything is
    /// the likeliest first act on a fresh install, so it is exactly the press that pays that cost.
    /// </para>
    /// <para>
    /// The group is dead for as long as the write is in flight, which is the other half and is the
    /// lesson the row below already learnt: a second press landing beside the first is two
    /// migrations of one schema racing each other for one write lock.
    /// </para>
    /// </remarks>
    private async void OnAfterARecordingChosen(object sender, RoutedEventArgs e)
    {
        // Asked again inside the handler, because a press already in flight arrives after the group
        // was drawn dead.
        if (_filling
            || _writingTheAnswer
            || !_settledIsKnown
            || Chose(sender) is not { } chosen
            || chosen == _afterARecording
            || Corpus().Folder is not { } folder)
        {
            return;
        }

        var at = Now();
        _writingTheAnswer = true;
        ShowWhatHappensWhenARecordingEnds();

        try
        {
            await Task.Run(() =>
            {
                folder.Create();
                using var context = CorpusDatabase.OpenMigrated(folder);
                new CorpusSettings(context).WhenARecordingEnds(chosen, at);
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (!_closed)
            {
                // The finally below puts the options back to what is really on disk. A screen left
                // showing what was pressed would have somebody expecting a transcription that was
                // never asked for.
                Say(UiTexts.ThatDidNotGoThrough, refused.Message);
            }

            return;
        }
        finally
        {
            _writingTheAnswer = false;

            if (!_closed)
            {
                ShowWhatHappensWhenARecordingEnds();
            }
        }

        if (_closed)
        {
            return;
        }

        // What the write said, rather than the corpus asked again — the same rule the row below
        // keeps: the write either threw or put this answer on the one row that carries it.
        _afterARecording = chosen;
        _status.Nothing();
        ShowWhatHappensWhenARecordingEnds();

        // The corpus may not have existed a moment ago, and the line about where it is would still
        // be saying so.
        SayWhereTheCorpusIs();
        Render();
    }

    /// <summary>Which option on this screen offers <paramref name="answer"/>.</summary>
    /// <remarks>
    /// One table and read both ways: what is drawn comes through here and so does what was pressed,
    /// so the option a press means and the option a read ticks cannot come apart. The last arm stops
    /// rather than substituting, which is the rule every table in this application's screens keeps —
    /// an answer added to <see cref="AfterARecording"/> with no option here would otherwise be shown
    /// to somebody as one of the others, and the one it would be shown as is the one that spends
    /// money. <c>ConfiguracionTests</c> is what catches it before it can be thrown.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// This screen has no option for that answer.
    /// </exception>
    private RadioButton Offers(AfterARecording answer) => answer switch
    {
        AfterARecording.TranscribeAndSummarise => AfterTranscribeAndSummarise,
        AfterARecording.Transcribe => AfterTranscribe,
        AfterARecording.DoNothing => AfterNothing,
        _ => throw new InvalidOperationException(
            $"This screen has no option for what happens after a recording: '{answer}'."),
    };

    /// <summary>
    /// Which of the three was pressed, or nothing when it was something else.
    /// </summary>
    /// <remarks>
    /// The table above read backwards, by the control and not by an index, so the group can be
    /// reordered on screen without the meaning of a row moving with it.
    /// </remarks>
    private AfterARecording? Chose(object sender) => Enum.GetValues<AfterARecording>()
        .Cast<AfterARecording?>()
        .FirstOrDefault(answer => ReferenceEquals(Offers(answer!.Value), sender));

    private void OnWhoIsUsingThisTyped(object sender, TextChangedEventArgs e) => ShowWhoIsUsingThis();

    /// <summary>
    /// Keeps who is using this application, which is the same press whether it is the first answer
    /// or a correction of it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Off this thread, and for the reason starting a recording is: the corpus may have every
    /// migration to run before the first row, which on a machine that has never recorded is the
    /// whole schema. It is the same two lines the recorder uses — make the folder, open migrated —
    /// so an install where this is answered before anything is recorded lays the corpus out the
    /// same way the first recording would have.
    /// </para>
    /// <para>
    /// A blank answer is not one, and it is refused by the press being dead rather than by a
    /// sentence: there is nothing to say about it that the empty field does not already say.
    /// </para>
    /// <para>
    /// The row is dead for as long as the press is in flight, and that is not tidiness. Disabling
    /// the button alone left the field live, and a keystroke into it drew the row again and armed
    /// the button back — so a second press ran beside the first, both found that nobody had
    /// answered, and both wrote a person who had. The recorder's own presses are held the same
    /// way and by the same kind of state, which is why this one is in
    /// <see cref="WhoIsUsingThisRow"/> rather than beside the handler.
    /// </para>
    /// </remarks>
    private async void OnKeepWhoIsUsingThis(object sender, RoutedEventArgs e)
    {
        // Asked again inside the handler, because a click already in flight arrives after the row
        // was drawn dead.
        if (!WhoIsUsingThis().MayBeKept || Corpus().Folder is not { } folder)
        {
            return;
        }

        var name = WhoIsUsingThis().Name;
        _whoIsUsingThis = _whoIsUsingThis with { BeingKept = true };
        ShowWhoIsUsingThis();

        try
        {
            await Task.Run(() =>
            {
                folder.Create();
                using var context = CorpusDatabase.OpenMigrated(folder);
                new HumanLayer(context, TimeProvider.System).ThisIsMe(name);
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (!_closed)
            {
                Say(UiTexts.WhoIsUsingThisWasNotKept);
            }

            return;
        }
        finally
        {
            _whoIsUsingThis = _whoIsUsingThis with { BeingKept = false };

            if (!_closed)
            {
                ShowWhoIsUsingThis();
            }
        }

        if (_closed)
        {
            return;
        }

        // What the write said, rather than the corpus asked again. The write either threw or put
        // this name on the one row that carries the flag, so a read back could only disagree by
        // failing — and a "could not be read" printed under a "done" is a screen contradicting
        // itself about an act that worked.
        _whoIsUsingThis = _whoIsUsingThis with { SomebodyHasSaid = true };
        WhoIsUsingThisBox.Text = name;

        // The corpus may not have existed a moment ago, and the line about where it is kept would
        // still be saying so.
        SayWhereTheCorpusIs();
        Say(UiTexts.WhoIsUsingThisIsKept, name);
    }

    private void OnLanguageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || LanguagePicker.SelectedIndex < 0)
        {
            return;
        }

        var chosen = Languages[LanguagePicker.SelectedIndex];

        // Selecting what is already selected is not somebody choosing. The picker is set from the
        // language the screen is read in, and taking that for a choice would record one every time
        // this screen was opened — after which the application would never follow Windows again.
        if (chosen == _language)
        {
            return;
        }

        LanguageChosen?.Invoke(this, chosen);
    }

    /// <summary>
    /// Now, off the machine's own clock. The second one of these in the application and, until this
    /// screen, the only place outside the window that needed one.
    /// </summary>
    /// <remarks>
    /// A method here and not a <see cref="TimeProvider"/> handed to <see cref="CorpusSettings"/>,
    /// which is what the four corpus-side types this screen's siblings build are given. Those read
    /// the clock on more than one path, so a field is what keeps one answer across them; this
    /// screen has exactly one write, and the instant it carries is the press that made it.
    /// </remarks>
    private static UtcTimestamp Now() => UtcTimestamp.From(TimeProvider.System.GetUtcNow());

    /// <summary>
    /// Says what happened, which is one line and the last thing said.
    /// </summary>
    /// <remarks>
    /// One line and not a report, which is what the three sibling sub-screens carry and what the
    /// design gives a screen that has something to say about itself. What the machine said goes
    /// inside the sentence through <c>ThatDidNotGoThrough</c> rather than on a line of its own: it
    /// is a <c>COMException</c>'s English or SQLite's, printed because it is the evidence somebody
    /// quotes, and a line of it standing alone would read as this application talking in a language
    /// nobody chose.
    /// </remarks>
    private void Say(UiText text, params object?[] values)
    {
        _status.Says(text, values);
        Render();
    }

    private void Render()
    {
        StatusText.Text = _status.In(_language);
        StatusText.Visibility = _status.IsSaying ? Visibility.Visible : Visibility.Collapsed;
    }
}
