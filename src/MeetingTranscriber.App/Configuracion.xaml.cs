using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Processing.Export;
using MeetingTranscriber.Processing.Summaries;
using MeetingTranscriber.Recording;

using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;

namespace MeetingTranscriber.App;

/// <summary>
/// The settings screen: what should happen when a recording ends, what runs it, the Deepgram key it
/// transcribes with, who is using this install, the language and theme the application is read and
/// drawn in, where the corpus is, what an export takes out of it, and where Claude Code is.
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
/// <b>The fifth block asks a process, not the corpus.</b> Whether Claude Code answers is asked off
/// the UI thread on every <see cref="Show"/>, the same way starting one is kept off it everywhere
/// else in this application — <see cref="ShowWhereClaudeCodeIs"/> draws what was chosen at once, and
/// <see cref="CheckClaudeCodeAsync"/> fills in whether it answered once that call returns. A screen
/// left showing neither state while the ask is still in flight would be wrong about nothing; it
/// would just be quiet about the one thing this block is for a moment longer than the four beside
/// it.
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
/// from the word at the foot of the front door and is left through the window's app bar, and a meeting under way keeps its strip above
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

    /// <summary>The order the model picker offers and reads a selection back in, for the same reason.</summary>
    private static readonly SummaryModel[] Models = Enum.GetValues<SummaryModel>();

    /// <summary>The effort picker's order, for the same reason.</summary>
    private static readonly SummaryEffort[] Efforts = Enum.GetValues<SummaryEffort>();

    /// <summary>The theme picker's order, for the same reason.</summary>
    private static readonly AppTheme[] Themes = Enum.GetValues<AppTheme>();

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

    /// <summary>
    /// The theme the application is drawn in, as of the last time this screen read the file it is
    /// kept in. Read there and not asked of the window: a pick is what writes it.
    /// </summary>
    private AppTheme _theme = AppTheme.System;

    /// <summary>Whether this screen is up. There is no meeting under it, so nothing else says.</summary>
    private bool _open;

    /// <summary>
    /// Whether a folder picker is up, or the folder it answered with is being written, so a second
    /// press cannot land while the first is still running.
    /// </summary>
    private bool _choosingAFolder;

    /// <summary>The empty folder the meetings were offered a move to, until it is pressed or cancelled.</summary>
    private DirectoryInfo? _offeredMoveTo;

    /// <summary>True while the meetings are being copied; the one place it changes is <see cref="Moving"/>.</summary>
    private bool _moving;

    /// <summary>Cancelled when the window goes, so a copy in flight stops and takes away what it wrote.</summary>
    private readonly CancellationTokenSource _movingStops = new();

    /// <summary>The same shape as <see cref="_choosingAFolder"/>, for the file picker beside it.</summary>
    private bool _choosingClaudeCode;

    /// <summary>
    /// Whether an export is running, which is the state the press and the four ticks would not
    /// otherwise have: the press is dead while it runs, and so are the ticks, so what was chosen is
    /// what was exported.
    /// </summary>
    private bool _exporting;

    /// <summary>
    /// The last export this corpus made, as of the last read or the last export this screen made,
    /// or nothing. Nothing when the corpus would not say, and nothing is also what draws no line.
    /// </summary>
    private LastExport? _lastExport;

    /// <summary>
    /// Whether Claude Code answered the last time this screen asked, and what it said about
    /// itself. <c>null</c> until the first ask on this <see cref="Show"/> returns, which is what
    /// keeps <see cref="ShowWhereClaudeCodeIs"/> from drawing a sentence about a question nobody
    /// has answered yet.
    /// </summary>
    private SummaryAvailability? _claudeCode;

    /// <summary>
    /// Which ask of <see cref="CheckClaudeCodeAsync"/> is the current one. <see cref="Show"/> and
    /// <see cref="OnChangeWhereClaudeCodeIs"/> can each start one, a check takes as long as
    /// starting a process takes, and nothing stops a person from closing and reopening this screen,
    /// or pressing <em>Cambiar</em> again, before an earlier ask has come back. Bumped by whichever
    /// starts a check and carried into it, so a check that returns after a later one has already
    /// started sees its own number no longer matches and writes nothing over the fresher answer.
    /// </summary>
    private int _claudeCodeAsk;

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
    /// What was last asked about a recording that ends, which is what is on disk or on its way
    /// there. A press choosing what is already asked writes nothing; one choosing the old answer
    /// again while a write is in flight is a choice and is written after it.
    /// </summary>
    private AfterARecording _wantedAfterARecording = AfterARecording.DoNothing;

    /// <summary>Which model summaries are asked of, as of the last read or write.</summary>
    private SummaryModel _summaryModel = SummaryModel.Sonnet;

    /// <summary>What was last asked of the model picker, for the reason the field above gives.</summary>
    private SummaryModel _wantedSummaryModel = SummaryModel.Sonnet;

    /// <summary>How much reasoning summaries are asked for, as of the last read or write.</summary>
    private SummaryEffort _summaryEffort = SummaryEffort.High;

    /// <summary>What was last asked of the effort picker, for the reason the field above gives.</summary>
    private SummaryEffort _wantedSummaryEffort = SummaryEffort.High;

    /// <summary>
    /// Which choice of what happens after a recording is the newest. A write that comes up in turn
    /// and finds a newer one waiting writes nothing: the last choice wins, and the one before it
    /// was never going to be seen.
    /// </summary>
    private readonly LastChoice _afterARecordingChoice = new();

    /// <summary>The same for the model picker.</summary>
    private readonly LastChoice _summaryModelChoice = new();

    /// <summary>The same for the effort picker.</summary>
    private readonly LastChoice _summaryEffortChoice = new();

    /// <summary>
    /// The line of writes. Everything this screen writes to the corpus waits for the one before it:
    /// the first step puts a name and an after-recording choice on one screen, and on a fresh
    /// install two writes at once are two migrations of a folder with no schema racing for one
    /// write lock — which <see cref="WhoIsUsingThisRow.BeingKept"/> alone could not see across two
    /// different writes. What it does is <see cref="WritesInTurn"/>'s, which runs.
    /// </summary>
    private readonly WritesInTurn _writes = new();

    /// <summary>
    /// Whether this screen is open as the first step: while nobody has said who is using the
    /// application, with only what is needed to begin on it.
    /// </summary>
    private bool _firstStep;

    /// <summary>
    /// Whether somebody pressed <em>Probar</em> since the screen was shown, which is the only time
    /// Claude Code answering is something this screen says.
    /// </summary>
    private bool _claudeCodeWasTested;

    /// <summary>True while the controls are being filled, so filling them is not somebody choosing.</summary>
    private bool _filling;

    private readonly ScreenStatus _status = new();

    /// <summary>
    /// Whether this machine held a Deepgram key the last time this screen asked, or
    /// <see langword="null"/> when Windows would not say. A yes or a no and never the key: the
    /// screen asks <see cref="KeepingThisMachinesKey"/>, which cannot read one.
    /// </summary>
    private bool? _aKeyIsKept;

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

    /// <summary>
    /// The meetings were copied to the empty folder somebody named, found whole there, and the
    /// folder was recorded as where the corpus is. What is done about it is not this screen's: the
    /// application opens a window over the new folder and, when the old copy was to go, removes it
    /// once nothing is using it. Unlike <see cref="CorpusChosen"/> it carries both folders, because
    /// the second half of a move needs the one the setting no longer names.
    /// </summary>
    public event EventHandler<MeetingsMoved>? MeetingsMoved;

    /// <summary>Raised where <see cref="MayGoBack"/> changes value.</summary>
    public event EventHandler? MayGoBackChanged;

    /// <summary>
    /// Whether a meeting is being recorded or saved, set by the window each time it settles what is
    /// on screen. The meetings cannot be moved under one: the spool is being written, and the
    /// recording is in no row yet for the move to find.
    /// </summary>
    public bool ARecordingIsUnderWay { get; set; }

    /// <summary>
    /// Stops the runner's pump over this corpus and answers what starts it again, set by the
    /// application, which owns the pump. A move asks for it before it copies, so nothing writes the
    /// folder it is leaving, and carries on with what it returns when the move did not happen.
    /// </summary>
    public Func<Task<Action>>? StopTheRunner { get; set; }

    /// <summary>
    /// Whether the way back is open: not while the meetings are being copied, because leaving would
    /// put the window back over a corpus that is about to be replaced. The window's app bar draws
    /// its back press dead on it.
    /// </summary>
    public bool MayGoBack => !_moving;

    /// <summary>Whether this screen is on the window.</summary>
    public bool IsOpen => _open;

    /// <summary>
    /// Hands over the corpus this install keeps its meetings in, and the window it is on, which
    /// the folder picker needs — and opens the screen as the first step when nobody has said who is
    /// using the application.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The rule is <see cref="WhoIsUsingThisRow.IsAsking"/>, which the row already carries and
    /// tests: the corpus is reachable and nobody has said. A corpus that was refused does not open
    /// it, since every block on it would be dead there and the folder card that fixes it is on the
    /// full screen. One that will not be read answers nobody-has-said and opens it with the
    /// sentence saying so, because <see cref="ReadWhoIsUsingThis"/> speaks for it.
    /// </para>
    /// <para>
    /// Nothing is asked of Claude Code here, and no process started: this runs inside the window's
    /// constructor. The window already re-reads <see cref="IsOpen"/> when it arranges itself.
    /// </para>
    /// </remarks>
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

        ReadWhoIsUsingThis();

        if (!WhoIsUsingThis().IsAsking)
        {
            _status.Nothing();
            return;
        }

        _open = true;
        _firstStep = true;

        ReadWhatHappensWhenARecordingEnds();
        _aKeyIsKept = WhetherAKeyIsKept();
        _theme = ThemeChoice.OfThisUser().Read();

        FillTheLanguagePicker();
        FillTheThemePicker();
        FillTheModelPicker();
        FillTheEffortPicker();
        ShowWhatHappensWhenARecordingEnds();
        ShowWhoIsUsingThis();
        ShowTheKey();
        SayWhereTheCorpusIs();
        Arrange();
        Render();
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
        FillTheThemePicker();
        FillTheModelPicker();
        FillTheEffortPicker();
        ShowWhatHappensWhenARecordingEnds();
        ShowWhoIsUsingThis();
        ShowTheKey();
        SayWhereTheCorpusIs();
        ShowTheExport();
        Arrange();
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
    public async void Show()
    {
        _open = true;
        _firstStep = false;
        _claudeCodeWasTested = false;
        _status.Nothing();
        _claudeCode = null;

        ReadWhatHappensWhenARecordingEnds();
        ReadWhoIsUsingThis();
        ReadTheLastExport();
        _aKeyIsKept = WhetherAKeyIsKept();
        _theme = ThemeChoice.OfThisUser().Read();

        FillTheLanguagePicker();
        FillTheThemePicker();
        FillTheModelPicker();
        FillTheEffortPicker();
        ShowWhatHappensWhenARecordingEnds();
        ShowWhoIsUsingThis();
        ShowTheKey();
        SayWhereTheCorpusIs();
        ShowTheExport();
        ShowWhereClaudeCodeIs();
        Arrange();
        Render();

        await CheckClaudeCodeAsync(++_claudeCodeAsk);
    }

    /// <summary>Takes this screen off the window.</summary>
    public void Close()
    {
        _open = false;
        _firstStep = false;
        _status.Nothing();
        StopOfferingTheMove();

        // A key pasted and never saved does not wait in a screen nobody is looking at.
        DeepgramKeyBox.Password = string.Empty;
        Render();
    }

    /// <summary>The window is going. Nothing here draws after this.</summary>
    public void Closing()
    {
        _closed = true;
        _movingStops.Cancel();
    }

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
    /// Reads what was settled about a recording that ends, and the model and the effort summaries
    /// are asked with — the preferences the corpus keeps for the whole install.
    /// </summary>
    /// <remarks>
    /// A corpus nobody has answered in really does answer <see cref="AfterARecording.DoNothing"/>,
    /// and a corpus that would not open answers nothing at all — which is the whole of why
    /// <see cref="_settledIsKnown"/> exists. Shown as the first, the second would leave somebody
    /// looking at <em>Nada</em> over a corpus holding <c>transcribe</c>, and the next
    /// recording they stopped would be paid for.
    /// </remarks>
    private void ReadWhatHappensWhenARecordingEnds()
    {
        _afterARecording = AfterARecording.DoNothing;
        _summaryModel = SummaryModel.Sonnet;
        _summaryEffort = SummaryEffort.High;
        _settledIsKnown = true;

        try
        {
            if (Corpus().Folder is { } folder && CorpusDatabase.HoldsACorpus(folder))
            {
                using var context = CorpusDatabase.Open(folder);
                var settings = new CorpusSettings(context);
                _afterARecording = settings.WhenARecordingEnds();
                _summaryModel = settings.SummaryModel();
                _summaryEffort = settings.SummaryEffort();
            }
        }
        catch (Exception wouldNotRead) when (ScreenFailures.Reportable(wouldNotRead))
        {
            // The machine's own words, which is what `ThatDidNotGoThrough` carries everywhere else
            // in this application. Not the sentence about a list that would not open: that one
            // sends somebody to this screen, and this screen saying it would be sending them here.
            _settledIsKnown = false;
            Say(UiTexts.ThatDidNotGoThrough, wouldNotRead.Message);
        }

        _wantedAfterARecording = _afterARecording;
        _wantedSummaryModel = _summaryModel;
        _wantedSummaryEffort = _summaryEffort;
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
    /// Reads when the corpus was last exported and what went.
    /// </summary>
    /// <remarks>
    /// A corpus that will not open says nothing here. The two reads before this one already speak
    /// for it, and <see cref="Show"/>'s remark about which read speaks last stays true; a line
    /// about an export nobody can describe is the same as no line.
    /// </remarks>
    private void ReadTheLastExport()
    {
        _lastExport = null;

        if (Corpus().Folder is not { } folder || !ThereIsACorpus())
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            _lastExport = new CorpusSettings(context).LastExportMade();
        }
        catch (Exception wouldNotRead) when (ScreenFailures.Reportable(wouldNotRead))
        {
            _lastExport = null;
        }
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
    /// Sets the row's two controls from the one answer. Nothing here decides anything.
    /// </summary>
    private void ShowWhoIsUsingThis()
    {
        var row = WhoIsUsingThis();
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
                Offers(answer).IsChecked = _settledIsKnown && answer == _wantedAfterARecording;
            }
        }
        finally
        {
            _filling = false;
        }

        // Two reasons to be dead and they are one rule: there is nowhere this press could write
        // to, or there is nothing it could be correcting because the answer was never read. A
        // write on its way is not one: a choice made meanwhile waits its turn, so the group stays
        // live however long a fresh install takes to lay the schema out.
        var live = Corpus().Folder is not null && _settledIsKnown;
        AfterARecordingOptions.IsEnabled = live;

        _filling = true;
        try
        {
            SummaryModelPicker.SelectedIndex = _settledIsKnown ? Array.IndexOf(Models, _wantedSummaryModel) : -1;
        }
        finally
        {
            _filling = false;
        }

        SummaryModelPicker.IsEnabled = live;

        _filling = true;
        try
        {
            EffortPicker.SelectedIndex = _settledIsKnown ? Array.IndexOf(Efforts, _wantedSummaryEffort) : -1;
        }
        finally
        {
            _filling = false;
        }

        EffortPicker.IsEnabled = live;
    }

    /// <summary>
    /// Arranges the screen as the first step or as the whole of it: the title, which cards are on
    /// it (the application's own card, the folder, the export and Claude Code come off in the first step;
    /// the rest stay), and whether <em>Empezar</em> is. Nothing here decides which — <see cref="_firstStep"/>
    /// is set by <see cref="Open"/> and <see cref="Show"/>.
    /// </summary>
    private void Arrange()
    {
        TitleText.Text = In(_firstStep ? UiTexts.GetStarted : UiTexts.Settings);

        // The two engine cards stay in the first step: the answer about what happens after a
        // recording is not given without the engines it starts in view, and the second says how it
        // is paid (`OnYourClaudePlan`) before anything is chosen from it.
        var whole = _firstStep ? Visibility.Collapsed : Visibility.Visible;
        AppCard.Visibility = whole;
        FolderCard.Visibility = whole;
        ExportCard.Visibility = whole;
        ClaudeCodeCard.Visibility = whole;
        StartButton.Visibility = _firstStep ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Puts the models in the picker without any of it reading as somebody choosing.</summary>
    private void FillTheModelPicker()
    {
        _filling = true;
        try
        {
            SummaryModelPicker.ItemsSource = Models.Select(offered => In(Named(offered))).ToArray();
            SummaryModelPicker.SelectedIndex = _settledIsKnown ? Array.IndexOf(Models, _wantedSummaryModel) : -1;
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>What a model is called in the picker.</summary>
    /// <remarks>
    /// One table, held to <see cref="SummaryModel"/> by <c>ConfiguracionTests</c> and ending in a
    /// throw, like every table on this screen: a model added with no name here would otherwise be
    /// offered as one of the others, and the one it would be shown as is the one that gets asked.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This screen has no name for that model.</exception>
    private static UiText Named(SummaryModel model) => model switch
    {
        SummaryModel.Sonnet => UiTexts.SummaryModelSonnet,
        SummaryModel.Opus => UiTexts.SummaryModelOpus,
        SummaryModel.Haiku => UiTexts.SummaryModelHaiku,
        SummaryModel.Fable => UiTexts.SummaryModelFable,
        _ => throw new InvalidOperationException(
            $"This screen has no name for the model summaries are asked of: '{model}'."),
    };

    /// <summary>Puts the efforts in the picker without any of it reading as somebody choosing.</summary>
    private void FillTheEffortPicker()
    {
        _filling = true;
        try
        {
            EffortPicker.ItemsSource = Efforts.Select(offered => In(Named(offered))).ToArray();
            EffortPicker.SelectedIndex = _settledIsKnown ? Array.IndexOf(Efforts, _wantedSummaryEffort) : -1;
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>What an effort is called in the picker.</summary>
    /// <remarks>
    /// One table, held to <see cref="SummaryEffort"/> by <c>ConfiguracionTests</c> and ending in a
    /// throw, for the reason <see cref="Named(SummaryModel)"/> does.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This screen has no name for that effort.</exception>
    private static UiText Named(SummaryEffort effort) => effort switch
    {
        SummaryEffort.High => UiTexts.EffortHigh,
        SummaryEffort.Medium => UiTexts.EffortMedium,
        SummaryEffort.Low => UiTexts.EffortLow,
        _ => throw new InvalidOperationException(
            $"This screen has no name for the effort summaries are asked with: '{effort}'."),
    };

    /// <summary>Puts the themes in the picker without any of it reading as somebody choosing.</summary>
    private void FillTheThemePicker()
    {
        _filling = true;
        try
        {
            ThemePicker.ItemsSource = Themes.Select(offered => In(Named(offered))).ToArray();
            ThemePicker.SelectedIndex = Array.IndexOf(Themes, _theme);
        }
        finally
        {
            _filling = false;
        }
    }

    /// <summary>What a theme is called in the picker.</summary>
    /// <remarks>
    /// One table, held to <see cref="AppTheme"/> by <c>ConfiguracionTests</c> and ending in a throw,
    /// for the reason <see cref="Named(SummaryModel)"/> does.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This screen has no name for that theme.</exception>
    private static UiText Named(AppTheme theme) => theme switch
    {
        AppTheme.System => UiTexts.ThemeSystem,
        AppTheme.Light => UiTexts.ThemeLight,
        AppTheme.Dark => UiTexts.ThemeDark,
        _ => throw new InvalidOperationException(
            $"This screen has no name for the theme the application is drawn in: '{theme}'."),
    };

    /// <summary>
    /// Says when the last export was and what it took, and whether the press may be used. Nothing
    /// here decides anything.
    /// </summary>
    /// <remarks>
    /// The press is live only with a corpus that opened and holds something, at least one tick, and
    /// no export already running. The press's name and tooltip are set here and not left to the
    /// binding, because <c>Bindings.Update</c> runs on a change of language and would otherwise put
    /// <em>Exportar</em> back over an export that is still going.
    /// </remarks>
    private void ShowTheExport()
    {
        if (_lastExport is { } last)
        {
            var meetings = last.Meetings == 1
                ? UiTexts.OneMeeting.In(_language)
                : UiTexts.MeetingsCounted.In(_language, last.Meetings);

            LastExportText.Text = UiTexts.LastExport.In(
                _language,
                ScreenNumbers.Beside([ScreenNumbers.At(last.At), meetings, .. last.Kinds.Select(kind => Called(kind).In(_language))]),
                last.Folder);
            LastExportText.Visibility = Visibility.Visible;
        }
        else
        {
            LastExportText.Text = string.Empty;
            LastExportText.Visibility = Visibility.Collapsed;
        }

        foreach (var kind in Enum.GetValues<ExportKind>())
        {
            Ticks(kind).IsEnabled = !_exporting;
        }

        // A glyph carries no word, so what it is called and what it does are set here: the name
        // while it runs says so, and the tooltip says what an export is for.
        AutomationProperties.SetName(
            ExportButton, In(_exporting ? UiTexts.Exporting : UiTexts.ExportTheCorpusToAFolder));
        ToolTipService.SetToolTip(ExportButton, In(UiTexts.WhatAnExportIsFor));
        ExportButton.IsEnabled =
            Corpus().Refusal is null && ThereIsACorpus() && Ticked().Count > 0 && !_exporting;
    }

    /// <summary>Which kinds are ticked right now.</summary>
    private HashSet<ExportKind> Ticked() =>
        [.. Enum.GetValues<ExportKind>().Where(kind => Ticks(kind).IsChecked == true)];

    /// <summary>Which tick offers <paramref name="kind"/>.</summary>
    /// <remarks>
    /// One table, read both ways like <see cref="Offers"/>: what is drawn and what is exported come
    /// through here, so the tick a kind means and the kind a tick means cannot come apart. The last
    /// arm stops rather than substituting, so a kind added with no tick is a build that fails a
    /// test and never a package quietly missing something somebody chose to take.
    /// </remarks>
    /// <exception cref="InvalidOperationException">This screen has no tick for that kind.</exception>
    private CheckBox Ticks(ExportKind kind) => kind switch
    {
        ExportKind.Audio => ExportAudio,
        ExportKind.Transcripts => ExportTranscripts,
        ExportKind.Summaries => ExportSummaries,
        ExportKind.HandCorrections => ExportHandCorrections,
        _ => throw new InvalidOperationException(
            $"This screen has no tick for what an export takes: '{kind}'."),
    };

    /// <summary>What a kind is called in the line about the last export.</summary>
    /// <exception cref="InvalidOperationException">This screen has no words for that kind.</exception>
    private static UiText Called(ExportKind exported) => exported switch
    {
        ExportKind.Audio => UiTexts.ExportsAudio,
        ExportKind.Transcripts => UiTexts.ExportsTranscripts,
        ExportKind.Summaries => UiTexts.ExportsSummaries,
        ExportKind.HandCorrections => UiTexts.ExportsHandCorrections,
        _ => throw new InvalidOperationException(
            $"This screen has no words for what an export takes: '{exported}'."),
    };

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
        UiText? text = Corpus().Refusal switch
        {
            null => null,
            CorpusRefusal.SettingSaysNothingUsable => UiTexts.TheSettingSaysNothingUsable,
            CorpusRefusal.FolderDoesNotAnswer => UiTexts.TheCorpusFolderDidNotAnswer,
            CorpusRefusal.NoCorpusInTheFolder => UiTexts.ThereIsNoCorpusInThatFolder,
            CorpusRefusal.GoesWhenThePackageDoes => UiTexts.TheCorpusFolderGoesWhenThePackageDoes,
            _ => throw new InvalidOperationException(
                $"This screen has no text for corpus refusal '{Corpus().Refusal}'."),
        };

        // The path is data, and so is all there is to say about a folder that opened. Every arm
        // above takes the path and nothing else, so there is no punctuation for this screen to
        // choose between two of them.
        CorpusText.Text = text is null ? Corpus().Path : text.In(_language, Corpus().Path);
    }

    /// <summary>
    /// Puts what was chosen, and what it answered when this screen last asked, on screen. Nothing
    /// here decides anything — it is <see cref="ShowWhatHappensWhenARecordingEnds"/>'s own rule
    /// applied to a process instead of a row.
    /// </summary>
    /// <remarks>
    /// <see cref="_claudeCode"/> being <c>null</c> — nobody has asked yet on this <see cref="Show"/>,
    /// or the answer has not come back — draws the path alone and no sentence under it: a screen
    /// that guessed at an answer it does not have yet would be wrong more often than the second it
    /// takes to actually ask. Answering <see cref="Availability.Answers"/> draws no sentence either,
    /// the same way an opened corpus draws none of the four refusal ones above it — silence is what
    /// "it is there" gets on this screen.
    /// </remarks>
    private void ShowWhereClaudeCodeIs()
    {
        ClaudeCodeText.Text = SummarisingOnThisMachine.WhereClaudeCodeIs()?.FullName ?? string.Empty;

        var sentence = _claudeCode switch
        {
            null => null,
            { Is: Availability.Answers } => _claudeCodeWasTested ? UiTexts.ClaudeCodeAnswers : null,
            { Is: Availability.NotOnThisMachine } => UiTexts.ClaudeCodeIsNotOnThisMachine,
            { Is: Availability.DoesNotAnswer } => UiTexts.ClaudeCodeDidNotAnswer,
            _ => throw new InvalidOperationException(
                $"This screen has no text for Claude Code availability '{_claudeCode.Is}'."),
        };

        ClaudeCodeStatusText.Text = sentence is null ? string.Empty : sentence.In(_language, _claudeCode?.Said);
        ClaudeCodeStatusText.Visibility = sentence is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Asks whether Claude Code answers, off the UI thread — starting a process, even one asked only
    /// for its version, has no business running on the thread the window draws on. Fills
    /// <see cref="_claudeCode"/> in and draws it once the answer is back, unless the screen has since
    /// closed, or a later ask — another <see cref="Show"/>, or another press of
    /// <em>Cambiar</em> — has started before this one returned.
    /// </summary>
    /// <param name="ask">
    /// This call's own number, from <see cref="_claudeCodeAsk"/> at the moment it started. Compared
    /// against the field again once the answer is back, so an ask that takes longer than a later one
    /// never overwrites what that later one already wrote — <see cref="_claudeCodeAsk"/>'s own
    /// remark says why one is not enough on its own.
    /// </param>
    private async Task CheckClaudeCodeAsync(int ask)
    {
        var availability = await Task.Run(
                () => SummarisingOnThisMachine.Provider().IsAvailableAsync(CancellationToken.None))
            .ConfigureAwait(true);

        if (_closed || !_open || ask != _claudeCodeAsk)
        {
            return;
        }

        _claudeCode = availability;
        ShowWhereClaudeCodeIs();
        Render();
    }

    /// <summary>
    /// Asks whether this machine holds a Deepgram key, and answers nothing rather than ending the
    /// application when Windows would not say — opening the settings screen is not something a
    /// credential store gets to crash.
    /// </summary>
    private static bool? WhetherAKeyIsKept()
    {
        try
        {
            return KeepingThisMachinesKey.IsThere;
        }
        catch (DeepgramKeyException)
        {
            return null;
        }
    }

    /// <summary>
    /// Draws whether a Deepgram key is kept, and the press that takes it away while one may be.
    /// </summary>
    private void ShowTheKey()
    {
        var said = _aKeyIsKept switch
        {
            true => UiTexts.ADeepgramKeyIsKept,
            false => UiTexts.NoDeepgramKeyIsKept,
            null => UiTexts.WhetherADeepgramKeyIsKeptIsUnknown,
        };

        DeepgramKeyText.Text = said.In(_language);
        RemoveTheKeyButton.Visibility = _aKeyIsKept is false ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// The press is dead while the field holds nothing at all, which the empty field already says.
    /// A paste of nothing but spaces is not dead: it shows as dots, so it reaches <c>Keep</c> and
    /// comes back as the sentence saying it was empty, rather than as a press that will not go
    /// with nothing to say why.
    /// </summary>
    private void OnDeepgramKeyTyped(object sender, RoutedEventArgs e) =>
        KeepTheKeyButton.IsEnabled = DeepgramKeyBox.Password.Length > 0;

    /// <summary>
    /// Keeps what was pasted as this machine's Deepgram key, in place of whatever was there. Says
    /// nothing when it worked: the line under the field is what says so.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every refusal is <c>Keep</c>'s and arrives as <see cref="DeepgramKeyException"/>, which is
    /// caught by its kind and said in this screen's own words: its message is English written for a
    /// prompt, and this screen speaks the language somebody chose.
    /// </para>
    /// <para>
    /// The field is emptied whatever happened, and the store is asked again afterwards rather than
    /// the line being set from the outcome: a write refused part-way may or may not have left the
    /// key that was there before, and only asking says which.
    /// </para>
    /// </remarks>
    private void OnKeepTheKey(object sender, RoutedEventArgs e)
    {
        UiText? said = null;
        try
        {
            KeepingThisMachinesKey.Keep(DeepgramKeyBox.Password);
        }
        catch (DeepgramKeyException refused)
        {
            said = Refused(refused.Refusal);
        }
        finally
        {
            DeepgramKeyBox.Password = string.Empty;
        }

        _aKeyIsKept = WhetherAKeyIsKept();
        ShowTheKey();

        // The line under the field changing is what says a key was kept; only a refusal is said.
        if (said is null)
        {
            _status.Nothing();
            Render();
            return;
        }

        Say(said);
    }

    /// <summary>
    /// Takes the Deepgram key off this machine, and says it is gone only once the store, asked
    /// again, says there is none.
    /// </summary>
    private void OnRemoveTheKey(object sender, RoutedEventArgs e)
    {
        try
        {
            KeepingThisMachinesKey.Forget();
        }
        catch (DeepgramKeyException)
        {
            // Said below: the store is asked again either way, and whatever it answers is the line.
        }

        _aKeyIsKept = WhetherAKeyIsKept();
        ShowTheKey();

        if (_aKeyIsKept is false)
        {
            _status.Nothing();
            Render();
            return;
        }

        Say(UiTexts.TheDeepgramKeyWasNotRemoved);
    }

    /// <summary>What this screen says for each way a paste can be refused.</summary>
    /// <remarks>
    /// Keeping a key refuses in these two ways and no other — there being no key, or a store that
    /// would not answer a read, are what asking says — so any other arm is a defect and not a
    /// sentence.
    /// </remarks>
    private static UiText Refused(DeepgramKeyRefusal refusal) => refusal switch
    {
        DeepgramKeyRefusal.NothingInIt => UiTexts.ThatIsNotADeepgramKey,
        DeepgramKeyRefusal.NotKept => UiTexts.ThisMachineWouldNotKeepTheKey,
        _ => throw new InvalidOperationException(
            $"Keeping a Deepgram key refused as '{refusal}', which only asking for one can say."),
    };

    /// <summary>Leaves the screen, which is the window's app bar's to ask.</summary>
    public void GoBack()
    {
        // Refused here and not only by the bar drawing its press dead: Alt+Left and the Start press
        // reach the same door.
        if (_moving)
        {
            return;
        }

        Left?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// <em>Empezar</em>: the first step is done, and leaving it is leaving the screen. Nothing is
    /// required of it — the app bar's way back leaves it too, and it is offered again at the next
    /// launch until a name is saved.
    /// </summary>
    private void OnStart(object sender, RoutedEventArgs e) => GoBack();

    /// <summary>
    /// <em>Probar</em>: asks Claude Code the question <see cref="Show"/> asks, and says the answer
    /// either way — including that it works, which opening the screen never says.
    /// </summary>
    private async void OnTestClaudeCode(object sender, RoutedEventArgs e)
    {
        _claudeCodeWasTested = true;
        await CheckClaudeCodeAsync(++_claudeCodeAsk);
    }

    /// <summary>
    /// Somebody asked to change where the corpus is kept. A folder that holds meetings is switched
    /// to; an empty one, over a corpus that opened, is offered a move.
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
        if (_choosingAFolder || _moving)
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
                    if (refusal == CorpusRefusal.NoCorpusInTheFolder && ThereAreMeetingsToMoveInto(folder))
                    {
                        OfferTheMove(folder);
                    }
                    else
                    {
                        Say(RefusalOfAPickedFolder(refusal), inspected.Path);
                    }
                }

                return;
            }

            try
            {
                await Task.Run(() => App.Home.Corpus.Choose(folder));
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
    /// Whether a folder with no corpus in it is one the meetings can be offered a move to: this
    /// install has meetings to move, and the folder holds nothing at all. A folder that holds
    /// something else is not offered, because the move writes into a folder it finds empty and
    /// would otherwise be a way of mixing a corpus into somebody's files.
    /// </summary>
    private bool ThereAreMeetingsToMoveInto(DirectoryInfo folder)
    {
        if (Corpus().Folder is not { } current || !CorpusDatabase.HoldsACorpus(current))
        {
            return false;
        }

        try
        {
            return !Directory.EnumerateFileSystemEntries(folder.FullName).Any();
        }
        catch (Exception unanswered) when (unanswered is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private void OfferTheMove(DirectoryInfo folder)
    {
        _offeredMoveTo = folder;
        _status.Nothing();
        Render();

        MoveText.Text = UiTexts.TheMeetingsMoveTo.In(_language, folder.FullName);
        RemoveTheOldCopyTick.IsChecked = false;
        MovePanel.Visibility = Visibility.Visible;
    }

    private void StopOfferingTheMove()
    {
        _offeredMoveTo = null;
        MovePanel.Visibility = Visibility.Collapsed;
    }

    private void OnCancelTheMove(object sender, RoutedEventArgs e) => StopOfferingTheMove();

    /// <summary>
    /// <em>Mover</em>: copies every meeting to the folder that was offered, finds every file the
    /// corpus records there whole, and only then records the folder and tells the window.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The copy is <see cref="CorpusMove.Copy"/> and the write of the setting is
    /// <see cref="CorpusLocation.Choose"/>, in that order and on one thread of work: the setting
    /// names a folder only after the copy was proved, so a move that failed anywhere leaves the
    /// application pointed at the folder it had. What is said for each way out is one sentence —
    /// a refusal says which, and anything the disk or SQLite did says what they said.
    /// </para>
    /// <para>
    /// Refused while a meeting is being recorded or saved, here and not only by the front door
    /// being behind this screen: a recording in progress is the one thing a copy cannot see.
    /// </para>
    /// </remarks>
    private async void OnMoveTheMeetings(object sender, RoutedEventArgs e)
    {
        if (_moving || _offeredMoveTo is not { } to || Corpus().Folder is not { } from)
        {
            return;
        }

        if (ARecordingIsUnderWay)
        {
            Say(UiTexts.AMeetingIsBeingRecorded);
            return;
        }

        var removeTheOldCopy = RemoveTheOldCopyTick.IsChecked == true;
        var stopping = _movingStops.Token;
        Action? carryOn = null;
        var moved = false;

        Moving(true);

        try
        {
            // The runner first: its pump holds the old corpus's lease and a job that finished after
            // the backup would be written to the folder being left.
            if (StopTheRunner is { } stop)
            {
                carryOn = await stop();
            }

            await Task.Run(
                () => CorpusMove.Copy(from, to, stopping, whenWhole: () => App.Home.Corpus.Choose(to)),
                stopping);

            moved = true;
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // The window went while the copy was running. Nothing is left in the folder and
            // nothing is drawn.
            return;
        }
        catch (CorpusMoveRefused refused)
        {
            Moving(false);
            Say(RefusalOfAMove(refused.Refusal));
        }
        catch (Exception failed) when (ScreenFailures.Reportable(failed))
        {
            Moving(false);
            Say(UiTexts.TheMeetingsCouldNotBeMoved, failed.Message);
        }

        if (!moved)
        {
            // Still pointed at the folder it had, so the runner goes on over that one.
            carryOn?.Invoke();
            return;
        }

        Moving(false);

        if (_closed)
        {
            return;
        }

        StopOfferingTheMove();
        MeetingsMoved?.Invoke(this, new MeetingsMoved(from, to, removeTheOldCopy));
    }

    /// <summary>
    /// Says the screen is copying, or no longer is: the one place <see cref="_moving"/> changes, so
    /// <see cref="MayGoBackChanged"/> is raised wherever it does. The whole screen goes dead while
    /// it does, because nothing on it means anything over a corpus that is about to be replaced.
    /// </summary>
    private void Moving(bool value)
    {
        if (_moving == value)
        {
            return;
        }

        _moving = value;

        if (_closed)
        {
            return;
        }

        IsEnabled = !value;

        if (value)
        {
            Say(UiTexts.Moving);
        }
        else
        {
            _status.Nothing();
            Render();
        }

        MayGoBackChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// What each refusal of a move says. The last arm stops rather than substituting, for the reason
    /// <see cref="RefusalOfAPickedFolder"/>'s does: a refusal added without a sentence would be said
    /// as one of the others.
    /// </summary>
    private static UiText RefusalOfAMove(CorpusMoveRefusal refusal) => refusal switch
    {
        CorpusMoveRefusal.NotEmpty => UiTexts.TheFolderIsNotEmpty,
        CorpusMoveRefusal.GoesWhenThePackageDoes => UiTexts.ThatFolderGoesOnUninstall,
        CorpusMoveRefusal.InsideTheOther => UiTexts.OneFolderIsInsideTheOther,
        CorpusMoveRefusal.WorkPending => UiTexts.WorkMustFinishFirst,
        CorpusMoveRefusal.ARecordingWaits => UiTexts.ARecordingIsWaitingToBeDecided,
        _ => throw new InvalidOperationException(
            $"This screen has no text for corpus move refusal '{refusal}'."),
    };

    /// <summary>
    /// Somebody asked to change which executable Claude Code runs from.
    /// </summary>
    /// <remarks>
    /// The same two guards <see cref="OnChangeWhereItIsKept"/> carries, for the same two reasons:
    /// the picker itself is a call into Windows, caught bare because there is nothing narrower to
    /// name it by and because this is an <c>async void</c> handler nothing may escape; writing the
    /// chosen file into the setting is this application's own write and takes
    /// <see cref="ScreenFailures.Reportable"/> unwidened, the same as every other write on this
    /// screen. Unlike the corpus folder, nothing here is refused for what it names — any file the
    /// picker hands back is recorded, and whether it is really Claude Code is what the next ask of
    /// <see cref="CheckClaudeCodeAsync"/> finds out, the same way a chosen corpus folder is trusted
    /// to <see cref="CorpusLocation.Choose"/>'s own re-inspection rather than a second check here.
    /// </remarks>
    private async void OnChangeWhereClaudeCodeIs(object sender, RoutedEventArgs e)
    {
        if (_choosingClaudeCode)
        {
            return;
        }

        _choosingClaudeCode = true;
        ChangeWhereClaudeCodeIs.IsEnabled = false;

        try
        {
            FileInfo executable;

            try
            {
                var picker = new FileOpenPicker(_window);
                picker.FileTypeFilter.Add(".exe");
                picker.FileTypeFilter.Add(".cmd");

                var picked = await picker.PickSingleFileAsync();

                if (picked is null)
                {
                    // Nothing chosen is nothing said and nothing done.
                    return;
                }

                executable = new FileInfo(picked.Path);
            }
            catch (Exception failedToOpen) when (failedToOpen is not OutOfMemoryException)
            {
                if (!_closed)
                {
                    Say(UiTexts.TheFilePickerDidNotOpen);
                }

                return;
            }

            try
            {
                await Task.Run(() => App.Home.ClaudeCode.Choose(executable));
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

            _status.Nothing();
            _claudeCode = null;
            _claudeCodeWasTested = false;
            ShowWhereClaudeCodeIs();
            Render();

            await CheckClaudeCodeAsync(++_claudeCodeAsk);
        }
        finally
        {
            _choosingClaudeCode = false;

            if (!_closed)
            {
                ChangeWhereClaudeCodeIs.IsEnabled = true;
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

    /// <summary>A tick changed, which is only ever a reason to redraw the block it is in.</summary>
    /// <remarks>
    /// Before the corpus is handed over there is nothing to draw against, and the ticks are set
    /// ticked while the markup is still being built.
    /// </remarks>
    private void OnExportTicked(object sender, RoutedEventArgs e)
    {
        if (_corpus is null)
        {
            return;
        }

        ShowTheExport();
    }

    /// <summary>
    /// Somebody asked to take the corpus out, into a folder they choose.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The same two guards <see cref="OnChangeWhereItIsKept"/> carries and for the same two reasons:
    /// the picker is a call into Windows and is caught bare, because nothing narrower names it and
    /// an <c>async void</c> handler lets nothing escape; the export is this application's own work
    /// and takes <see cref="ScreenFailures.Reportable"/> unwidened, because a full disk is not the
    /// picker failing to open.
    /// </para>
    /// <para>
    /// Off this thread: it reads every meeting and copies every file, which for hours of audio is
    /// the longest thing this screen does. It opens its own context, migrated, because the export
    /// writes the row that says it was made.
    /// </para>
    /// </remarks>
    private async void OnExport(object sender, RoutedEventArgs e)
    {
        // Asked again inside the handler, because a press already in flight arrives after the
        // press was drawn dead.
        var kinds = Ticked();
        if (_exporting
            || _choosingAFolder
            || kinds.Count == 0
            || Corpus().Refusal is not null
            || Corpus().Folder is not { } folder
            || !ThereIsACorpus())
        {
            return;
        }

        DirectoryInfo chosen;
        _choosingAFolder = true;

        try
        {
            var picker = new FolderPicker(_window);
            var picked = await picker.PickSingleFolderAsync();

            if (picked is null)
            {
                // Nothing chosen is nothing said and nothing done.
                return;
            }

            chosen = new DirectoryInfo(picked.Path);
        }
        catch (Exception failedToOpen) when (failedToOpen is not OutOfMemoryException)
        {
            if (!_closed)
            {
                Say(UiTexts.TheFolderPickerDidNotOpen);
            }

            return;
        }
        finally
        {
            _choosingAFolder = false;
        }

        if (_closed)
        {
            return;
        }

        var at = Now();
        _exporting = true;
        ShowTheExport();

        CorpusExported exported;

        try
        {
            CorpusExported? made = null;
            await _writes.Run(async () => made = await Task.Run(() =>
            {
                using var context = CorpusDatabase.OpenMigrated(folder);
                return CorpusExport.Into(context, chosen.FullName, kinds, TimeZoneInfo.Local, at);
            }));
            exported = made!;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (!_closed)
            {
                Say(UiTexts.ThatDidNotGoThrough, refused.Message);
            }

            return;
        }
        finally
        {
            _exporting = false;

            if (!_closed)
            {
                ShowTheExport();
            }
        }

        if (_closed)
        {
            return;
        }

        // What the export said, rather than the corpus asked again: it either threw or wrote the
        // row this describes.
        _lastExport = new LastExport(at, [.. kinds.Order()], exported.Meetings, exported.Folder.FullName);
        _status.Nothing();
        ShowTheExport();

        if (exported.LeftOut.Count == 0)
        {
            Render();
            return;
        }

        var index = Path.Combine(exported.Folder.FullName, CorpusExport.IndexName);
        if (exported.LeftOut.Count == 1)
        {
            Say(UiTexts.OneFileWasNotThere, index);
        }
        else
        {
            Say(UiTexts.SomeFilesWereNotThere, exported.LeftOut.Count, index);
        }
    }

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
    /// Off this thread, and one write at a time: this is one of the presses that can make the
    /// corpus, so on a machine that has never recorded it runs every migration there is before the
    /// first row — and the first step has the name beside it, so a second write beside this one is
    /// two migrations of one schema racing for one write lock. The group is not dead for it. A
    /// choice made while a write runs waits for it, and a newer choice replaces one still waiting:
    /// the last one wins, and a write that finds itself replaced writes nothing.
    /// </para>
    /// </remarks>
    private async void OnAfterARecordingChosen(object sender, RoutedEventArgs e)
    {
        // Asked again inside the handler, because a press already in flight arrives after the
        // group was drawn dead.
        if (_filling
            || !_settledIsKnown
            || Chose(sender) is not { } chosen
            || chosen == _wantedAfterARecording
            || Corpus().Folder is not { } folder)
        {
            return;
        }

        var at = Now();
        var ask = _afterARecordingChoice.Ask();
        _wantedAfterARecording = chosen;
        var wrote = false;

        try
        {
            await _writes.Run(async () =>
            {
                if (!_afterARecordingChoice.IsStill(ask))
                {
                    return;
                }

                await Task.Run(() =>
                {
                    folder.Create();
                    using var context = CorpusDatabase.OpenMigrated(folder);
                    new CorpusSettings(context).WhenARecordingEnds(chosen, at);
                });

                wrote = true;
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (!_closed)
            {
                // The options go back to what is really on disk, unless a newer choice is waiting
                // and will say its own. A screen left showing what was pressed would have somebody
                // expecting a transcription that was never asked for.
                if (_afterARecordingChoice.IsStill(ask))
                {
                    _wantedAfterARecording = _afterARecording;
                    ShowWhatHappensWhenARecordingEnds();
                }

                Say(UiTexts.ThatDidNotGoThrough, refused.Message);
            }

            return;
        }

        if (wrote)
        {
            // What the write said, rather than the corpus asked again — the same rule the name
            // keeps: the write either threw or put this answer on the one row that carries it.
            _afterARecording = chosen;

            // Also what is wanted when nothing newer is, so a screen shown again mid-write (which
            // reads the old value) cannot leave a later press of that old value looking like no change.
            if (_afterARecordingChoice.IsStill(ask))
            {
                _wantedAfterARecording = chosen;
            }
        }

        if (_closed || !_afterARecordingChoice.IsStill(ask))
        {
            return;
        }

        _status.Nothing();
        ShowWhatHappensWhenARecordingEnds();

        // The corpus may not have existed a moment ago, and the line about where it is would still
        // be saying so.
        SayWhereTheCorpusIs();
        Render();
    }

    /// <summary>
    /// Somebody chose the model summaries are asked of. Written the way
    /// <see cref="OnAfterARecordingChosen"/> is written, for the same reasons, and through the same
    /// line of writes.
    /// </summary>
    private async void OnSummaryModelChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling
            || !_settledIsKnown
            || SummaryModelPicker.SelectedIndex < 0
            || Corpus().Folder is not { } folder)
        {
            return;
        }

        var chosen = Models[SummaryModelPicker.SelectedIndex];
        if (chosen == _wantedSummaryModel)
        {
            return;
        }

        var at = Now();
        var ask = _summaryModelChoice.Ask();
        _wantedSummaryModel = chosen;
        var wrote = false;

        try
        {
            await _writes.Run(async () =>
            {
                if (!_summaryModelChoice.IsStill(ask))
                {
                    return;
                }

                await Task.Run(() =>
                {
                    folder.Create();
                    using var context = CorpusDatabase.OpenMigrated(folder);
                    new CorpusSettings(context).SummaryModel(chosen, at);
                });

                wrote = true;
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (!_closed)
            {
                if (_summaryModelChoice.IsStill(ask))
                {
                    _wantedSummaryModel = _summaryModel;
                    ShowWhatHappensWhenARecordingEnds();
                }

                Say(UiTexts.ThatDidNotGoThrough, refused.Message);
            }

            return;
        }

        if (wrote)
        {
            _summaryModel = chosen;

            if (_summaryModelChoice.IsStill(ask))
            {
                _wantedSummaryModel = chosen;
            }
        }

        if (_closed || !_summaryModelChoice.IsStill(ask))
        {
            return;
        }

        _status.Nothing();
        ShowWhatHappensWhenARecordingEnds();
        SayWhereTheCorpusIs();
        Render();
    }

    /// <summary>
    /// Somebody chose how much reasoning summaries are asked for. Written the way
    /// <see cref="OnSummaryModelChosen"/> is written, for the same reasons, and through the same
    /// line of writes.
    /// </summary>
    private async void OnSummaryEffortChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling
            || !_settledIsKnown
            || EffortPicker.SelectedIndex < 0
            || Corpus().Folder is not { } folder)
        {
            return;
        }

        var chosen = Efforts[EffortPicker.SelectedIndex];
        if (chosen == _wantedSummaryEffort)
        {
            return;
        }

        var at = Now();
        var ask = _summaryEffortChoice.Ask();
        _wantedSummaryEffort = chosen;
        var wrote = false;

        try
        {
            await _writes.Run(async () =>
            {
                if (!_summaryEffortChoice.IsStill(ask))
                {
                    return;
                }

                await Task.Run(() =>
                {
                    folder.Create();
                    using var context = CorpusDatabase.OpenMigrated(folder);
                    new CorpusSettings(context).SummaryEffort(chosen, at);
                });

                wrote = true;
            });
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            if (!_closed)
            {
                if (_summaryEffortChoice.IsStill(ask))
                {
                    _wantedSummaryEffort = _summaryEffort;
                    ShowWhatHappensWhenARecordingEnds();
                }

                Say(UiTexts.ThatDidNotGoThrough, refused.Message);
            }

            return;
        }

        if (wrote)
        {
            _summaryEffort = chosen;

            if (_summaryEffortChoice.IsStill(ask))
            {
                _wantedSummaryEffort = chosen;
            }
        }

        if (_closed || !_summaryEffortChoice.IsStill(ask))
        {
            return;
        }

        _status.Nothing();
        ShowWhatHappensWhenARecordingEnds();
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
            await _writes.Run(() => Task.Run(() =>
            {
                folder.Create();
                using var context = CorpusDatabase.OpenMigrated(folder);
                new HumanLayer(context, TimeProvider.System).ThisIsMe(name);
            }));
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
    /// Somebody chose the theme the application is drawn in: put on this window at once, and kept
    /// so that the next window the application makes — and the next launch — is drawn in it too.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied to this window before it is written, as the language is: what somebody just asked
    /// for is not held back by a preference file, and a file that cannot be written is a theme that
    /// does not survive the session and is said, rather than a pick that does nothing. The window
    /// is reached from what this screen is drawn on and from the id it was handed, so the pick
    /// does not travel up through the window for something it can do itself.
    /// </para>
    /// <para>
    /// Selecting what is already selected is not somebody choosing, for the reason the language's
    /// is not: the picker is filled from what is kept, and taking that for a pick would write a
    /// choice every time this screen opened.
    /// </para>
    /// </remarks>
    private void OnThemeChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || ThemePicker.SelectedIndex < 0)
        {
            return;
        }

        var chosen = Themes[ThemePicker.SelectedIndex];
        if (chosen == _theme)
        {
            return;
        }

        _theme = chosen;
        ShownInTheme.Apply(XamlRoot?.Content, AppWindow.GetFromWindowId(_window), chosen);

        try
        {
            ThemeChoice.OfThisUser().Write(chosen);
            _status.Nothing();
            Render();
        }
        catch (Exception unwritable) when (ScreenFailures.Reportable(unwritable))
        {
            Say(UiTexts.TheThemeWasNotRemembered);
        }
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

/// <summary>
/// The meetings were moved: where they were, where they are, and whether the old copy was to go
/// once the application stopped using it.
/// </summary>
public sealed record MeetingsMoved(DirectoryInfo From, DirectoryInfo To, bool RemoveTheOld);
