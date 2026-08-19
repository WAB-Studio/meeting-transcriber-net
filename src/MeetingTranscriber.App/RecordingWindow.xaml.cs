using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

// WinUI has a Duration of its own — an animation's, in ticks — and this file is the one place in
// the application where the domain's meets it. Aliased rather than qualified at the use, so that
// the day a second use appears it cannot quietly be the other one.
using Duration = MeetingTranscriber.Domain.Time.Duration;

namespace MeetingTranscriber.App;

/// <summary>
/// Where a meeting is recorded: what it will record, and record, pause, carry on and stop. The
/// first screen of this application that is the application rather than a way of testing it.
/// </summary>
/// <remarks>
/// <para>
/// It holds no rule of its own about what can be pressed. <see cref="RecorderScreen"/> answers
/// that, in a project a build agent can run, and every handler here asks it before doing anything
/// — including the handlers of controls it has just disabled, because a click already in flight
/// arrives after that.
/// </para>
/// <para>
/// Two calls do not happen on this thread and it is not a preference in either case. Starting
/// opens two devices, each with a deadline for one that never answers; stopping pours the spools
/// onto a timeline, reads the file back and hashes it, which is minutes of work for a long
/// meeting. Both say so where they are defined.
/// </para>
/// <para>
/// The corpus is opened once, when the first meeting starts, and stays open until it is stopped.
/// That is <see cref="MeetingRecording"/>'s constraint rather than a choice made here: it holds
/// the context it was handed for as long as the meeting lasts, so one opened inside this handler
/// and let go of would be disposed by the time somebody pressed stop an hour later.
/// </para>
/// </remarks>
public sealed partial class RecordingWindow : Window
{
    /// <summary>
    /// The order the picker offers, and the order it reads a selection back in. One array, read
    /// twice, so the two cannot come apart.
    /// </summary>
    private static readonly UiLanguage[] Languages = Enum.GetValues<UiLanguage>();

    /// <summary>
    /// What a meeting can be said to be spoken in: the name a person picks, and the tag the corpus
    /// stores and the provider is asked for.
    /// </summary>
    /// <remarks>
    /// Two, because the application is written in two and nothing downstream could be asked for a
    /// third yet. The tags are spelled here rather than taken from what the application is being
    /// read in, which is the whole point of this picker existing beside the other one — the two
    /// answer different questions and a meeting spoken in English is not filed as Spanish for
    /// having been recorded from a Spanish menu.
    /// </remarks>
    private static readonly (UiText Name, string Tag)[] Spoken =
    [
        (UiTexts.SpanishName, "es"),
        (UiTexts.EnglishName, "en"),
    ];

    /// <summary>Where this application's corpus is, or what stopped it being found.</summary>
    private readonly CorpusFolder _corpus;

    /// <summary>
    /// What has happened, as the lines it is made of rather than as the string it currently reads
    /// as, so that what is already on screen re-reads itself when the language changes.
    /// </summary>
    private readonly List<TextLine> _report = [];

    /// <summary>
    /// What asks the recording whether the program it is following has gone silent. Once a second,
    /// which is what the metering loop at a prompt does, and it shows the offer and nothing else:
    /// the levels are their own card.
    /// </summary>
    private readonly DispatcherTimer _watch = new() { Interval = TimeSpan.FromSeconds(1) };

    private UiLanguage _language;
    private TextLine? _status;

    /// <summary>True while the pickers are being filled, so refilling them is not a choice.</summary>
    private bool _filling;

    /// <summary>
    /// What this machine has, read once when the window opens. A microphone appearing mid-session
    /// is a device change, which is its own claim and its own card; a program appearing is not,
    /// which is why only the list below is re-read on a press.
    /// </summary>
    private readonly AudioDevice[] _microphones;

    private RecorderSource[] _sources;

    private CorpusDbContext? _context;
    private MeetingRecording? _recording;

    /// <summary>Whichever press is still running, which is the only thing that owns it.</summary>
    private RecorderStep _step = RecorderStep.Nothing;

    /// <summary>
    /// Whether the window has been closed. Read by whichever press was in flight when it was, so
    /// that a recording that finished opening its devices into a window nobody is looking at is
    /// let go of by the handler that made it rather than published to a screen that is gone.
    /// </summary>
    private bool _closed;

    private bool _offered;
    private bool _taken;

    private RecorderChoices _chosen = RecorderChoices.Nothing;

    public RecordingWindow(UiLanguage language, CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        // Before InitializeComponent: the bindings in the XAML are read while it runs.
        _language = language;
        _corpus = corpus;

        InitializeComponent();

        _watch.Tick += OnWatch;
        Closed += OnClosed;

        _microphones = [.. Ask(AudioDevices.Microphones)];
        _sources = SourcesNow();

        // A report line and not the status one. The status says what the screen is doing and is
        // rewritten every time anything changes, so a machine with no microphone would announce it
        // once and lose it to the next refresh — and it is the one thing on this screen that
        // cannot be got round by pressing something else.
        if (_microphones.Length == 0)
        {
            Say(UiTexts.NoMicrophoneOnThisMachine);
        }

        ReadIn(language);
    }

    /// <summary>
    /// Somebody picked a language on this screen. What is done about it is not this window's.
    /// </summary>
    public event EventHandler<UiLanguage>? LanguageChosen;

    /// <summary>Somebody asked for the packaging checks, which are a window of their own.</summary>
    public event EventHandler? PackagingChecksAsked;

    /// <summary>
    /// Reads the whole window in this language: what the XAML bound, the title, the pickers, what
    /// has happened so far and the status line. Nothing on screen is left in the one before.
    /// </summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();
        Title = UiTexts.RecordAMeeting.In(language);

        FillThePickers();
        Render();
        Refresh();
    }

    /// <summary>
    /// What a text says in the language this window is being read in. The XAML binds to it, which
    /// is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>Says what the screen cannot say for itself — what happened around it.</summary>
    public void Report(UiText text) => Say(text);

    /// <summary>
    /// The screen as the facts that decide what can be pressed, built fresh every time rather than
    /// kept, so that it cannot come to disagree with the recording it describes.
    /// </summary>
    private RecorderScreen Screen() => new()
    {
        State = RecorderStates.Of(
            corpus: _corpus.Refusal is null,
            started: _recording is not null,
            paused: _recording?.IsPaused ?? false,
            step: _step),
        Chosen = _chosen,
        WholeMachineOffered = _offered,
        WholeMachineTaken = _taken,
    };

    /// <summary>
    /// Sets every control from the one answer. Nothing here decides anything: it is the reading of
    /// <see cref="RecorderScreen"/> onto a window, which is why it is one method and not a
    /// condition inside each of nine handlers.
    /// </summary>
    private void Refresh()
    {
        var screen = Screen();

        RecordButton.IsEnabled = screen.Allows(RecorderPress.Start);
        PauseButton.IsEnabled = screen.Allows(RecorderPress.Pause);
        ResumeButton.IsEnabled = screen.Allows(RecorderPress.Resume);
        StopButton.IsEnabled = screen.Allows(RecorderPress.Stop);

        // Visibility and not merely disabled: an offer that has not been made is not a button
        // waiting to become live, it is a thing there is no reason to have heard of yet.
        WholeMachineOffer.Visibility = screen.Allows(RecorderPress.RecordTheWholeMachine)
            ? Visibility.Visible
            : Visibility.Collapsed;

        // What the next meeting records cannot be changed once one is running: the devices are
        // open, and the engine has no way to swap one out under a recording.
        var choosing = screen.State == RecorderState.Choosing;
        MicrophonePicker.IsEnabled = choosing;
        SourcePicker.IsEnabled = choosing;
        SpokenPicker.IsEnabled = choosing;
        RefreshProgramsButton.IsEnabled = choosing;

        Announce(screen.State);
    }

    /// <summary>What the screen says about itself, which is one line and always the same one.</summary>
    private void Announce(RecorderState state)
    {
        switch (state)
        {
            case RecorderState.WithoutACorpus:
                Status(UiTexts.ChoosingAnotherFolderIsNotHereYet);
                break;
            case RecorderState.Choosing:
                Status(UiTexts.ReadyToRecord);
                break;
            case RecorderState.Recording when _recording is not null:
                Status(UiTexts.RecordingMeeting, _recording.MeetingId);
                break;
            case RecorderState.Paused:
                Status(UiTexts.PausedAndTheClockKeepsRunning);
                break;
            case RecorderState.Starting:
                Status(UiTexts.OpeningTheDevices);
                break;
            case RecorderState.Finishing:
                Status(UiTexts.MakingTheMeeting);
                break;
            default:
                break;
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
    /// The last arm stops rather than substituting, which is <see cref="RecorderStates.Reaches"/>
    /// on a state it does not have and is the same rule for the same reason: a refusal added to
    /// <see cref="CorpusRefusal"/> and not given a text here would otherwise be shown to somebody
    /// as one of the others, sending them to check a folder that is fine or a setting they never
    /// wrote. A screen that says the wrong reason confidently is worse than one that stops, and
    /// this is a table falling behind its enum — a fault of the code, which nothing a person does
    /// can reach. <c>CorpusTextTests</c> is what catches it before it can be thrown.
    /// </remarks>
    private void SayWhereTheCorpusIs()
    {
        var text = _corpus.Refusal switch
        {
            null => _corpus.HoldsACorpus ? UiTexts.MeetingsAreKeptAt : UiTexts.TheFirstRecordingMakesTheCorpus,
            CorpusRefusal.SettingSaysNothingUsable => UiTexts.TheSettingSaysNothingUsable,
            CorpusRefusal.FolderDoesNotAnswer => UiTexts.TheCorpusFolderDidNotAnswer,
            CorpusRefusal.NoCorpusInTheFolder => UiTexts.ThereIsNoCorpusInThatFolder,
            CorpusRefusal.GoesWhenThePackageDoes => UiTexts.TheCorpusFolderGoesWhenThePackageDoes,
            _ => throw new InvalidOperationException(
                $"This screen has no text for corpus refusal '{_corpus.Refusal}'."),
        };

        CorpusText.Text = _corpus.Refusal is null && !_corpus.HoldsACorpus
            ? $"{UiTexts.MeetingsAreKeptAt.In(_language, _corpus.Path)} — {text.In(_language)}"
            : text.In(_language, _corpus.Path);
    }

    private void FillThePickers()
    {
        _filling = true;
        try
        {
            // A device's name is what its maker called it, so it is data and has no language.
            MicrophonePicker.ItemsSource = _microphones.Select(device => device.ToString()).ToArray();
            MicrophonePicker.SelectedIndex = _chosen.Microphone is null
                ? -1
                : Array.FindIndex(_microphones, device => device.Id == _chosen.Microphone.Id);

            SourcePicker.ItemsSource = _sources.Select(NameOf).ToArray();
            SourcePicker.SelectedIndex = _chosen.Source is null ? -1 : Array.IndexOf(_sources, _chosen.Source);

            SpokenPicker.ItemsSource = Spoken.Select(offered => In(offered.Name)).ToArray();
            SpokenPicker.SelectedIndex = _chosen.Spoken is null
                ? -1
                : Array.FindIndex(Spoken, offered => offered.Tag == _chosen.Spoken);

            LanguagePicker.ItemsSource = Languages.Select(offered => In(UiLanguages.Endonym(offered))).ToArray();
            LanguagePicker.SelectedIndex = Array.IndexOf(Languages, _language);
        }
        finally
        {
            _filling = false;
        }

        SayWhereTheCorpusIs();
    }

    /// <summary>
    /// What is running now, with the whole machine first. First because it is the answer that is
    /// always available and always right about what it records, where following a program is the
    /// one that can turn out to have been the wrong process.
    /// </summary>
    private RecorderSource[] SourcesNow() =>
    [
        RecorderSource.TheWholeMachine,
        .. Ask(AudioProcesses.Running)
            .OrderBy(program => program.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(program => program.Id)
            .Select(RecorderSource.Following),
    ];

    private string NameOf(RecorderSource source) =>
        source.IsTheWholeMachine ? In(UiTexts.EverythingThisMachinePlays) : source.Follow!.ToString();

    /// <summary>
    /// Asks the machine what it has, and answers nothing rather than failing to open when it will
    /// not say. A window that would not start because the audio stack was busy is worse than one
    /// that opens with an empty picker and a line saying so.
    /// </summary>
    private IReadOnlyList<T> Ask<T>(Func<IReadOnlyList<T>> machine)
    {
        try
        {
            return machine();
        }
        catch (AudioCaptureException unanswered)
        {
            Dump(unanswered.Message);
            return [];
        }
    }

    private void OnMicrophoneChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || MicrophonePicker.SelectedIndex < 0)
        {
            return;
        }

        _chosen = _chosen with { Microphone = _microphones[MicrophonePicker.SelectedIndex] };
        Refresh();
    }

    private void OnSourceChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || SourcePicker.SelectedIndex < 0)
        {
            return;
        }

        _chosen = _chosen with { Source = _sources[SourcePicker.SelectedIndex] };
        Refresh();
    }

    private void OnSpokenChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || SpokenPicker.SelectedIndex < 0)
        {
            return;
        }

        _chosen = _chosen with { Spoken = Spoken[SpokenPicker.SelectedIndex].Tag };
        Refresh();
    }

    /// <summary>
    /// Programs come and go while somebody is deciding, and the meeting they are about to record
    /// is usually one they started after opening this. A choice that is no longer running is
    /// dropped rather than carried into a recording that would follow a process id nothing owns.
    /// </summary>
    private void OnRefreshPrograms(object sender, RoutedEventArgs e)
    {
        _sources = SourcesNow();

        if (_chosen.Source is { } chosen && !_sources.Contains(chosen))
        {
            _chosen = _chosen with { Source = null };
        }

        FillThePickers();
        Refresh();
    }

    private void OnLanguageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (_filling || LanguagePicker.SelectedIndex < 0)
        {
            return;
        }

        var chosen = Languages[LanguagePicker.SelectedIndex];

        // Selecting what is already selected is not somebody choosing. The picker is set from the
        // language the window opened in, and taking that for a choice would record one on every
        // launch — after which the application would never follow Windows again.
        if (chosen == _language)
        {
            return;
        }

        LanguageChosen?.Invoke(this, chosen);
    }

    private void OnOpenPackagingChecks(object sender, RoutedEventArgs e) =>
        PackagingChecksAsked?.Invoke(this, EventArgs.Empty);

    private async void OnRecord(object sender, RoutedEventArgs e)
    {
        if (!Screen().Allows(RecorderPress.Start) || _corpus.Folder is not { } folder)
        {
            return;
        }

        // A program is a process id, and a process id outlives the program that had it. Between
        // the list being read and record being pressed the chosen program can have exited and
        // Windows can have handed its number to something else, which would put another
        // application's audio on channel 0 with nothing on screen looking wrong. So the answer is
        // taken again here rather than trusted, and a program that has gone is said and unchosen.
        if (_chosen.Source is { IsTheWholeMachine: false } following && !StillRunning(following))
        {
            _chosen = _chosen with { Source = null };
            _sources = SourcesNow();
            FillThePickers();
            Say(UiTexts.ThatProgramIsNoLongerRunning);
            Refresh();
            return;
        }

        var chosen = _chosen;

        _report.Clear();
        _offered = false;
        _taken = false;
        _step = RecorderStep.Starting;
        Refresh();

        try
        {
            // Off this thread: two devices are opened, each with a deadline for one that never
            // answers, and the corpus may have a migration to run before the first row.
            var started = await Task.Run(() =>
            {
                folder.Create();
                var context = CorpusDatabase.OpenMigrated(folder);

                try
                {
                    return (Context: context, Recording: MeetingRecording.Start(
                        context,
                        chosen.Spoken!,
                        AudioDevices.Playback(),
                        chosen.Microphone!,
                        chosen.Source!.Follow,
                        Now()));
                }
                catch
                {
                    context.Dispose();
                    throw;
                }
            });

            // The window can have been closed while the devices were opening. Publishing into it
            // would leave two devices recording with nothing left that could stop them, so what
            // this handler made this handler lets go of.
            if (_closed)
            {
                started.Recording.Dispose();
                started.Context.Dispose();
                return;
            }

            _context = started.Context;
            _recording = started.Recording;
            _watch.Start();
        }
        catch (Exception refused) when (Reportable(refused))
        {
            Say(UiTexts.TheRecordingCouldNotStart);
            Dump(refused.Message);
        }
        finally
        {
            _step = RecorderStep.Nothing;

            if (!_closed)
            {
                Refresh();
            }
        }
    }

    /// <summary>Whether the program somebody chose is one of the ones running now.</summary>
    /// <remarks>
    /// By name as well as by number. A process id on its own says nothing — Windows reuses them —
    /// and the pair is what says this is still the program that was picked rather than whatever
    /// inherited its number.
    /// </remarks>
    private bool StillRunning(RecorderSource following) =>
        Ask(AudioProcesses.Running).Any(program =>
            program.Id == following.Follow!.Id
            && string.Equals(program.Name, following.Follow.Name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Whether this is something to say on screen rather than something to end the application
    /// over. Every one of them is a fault in what the machine handed back: a device, a folder, or
    /// the corpus file itself.
    /// </summary>
    /// <remarks>
    /// The list is closed on purpose, and these handlers are <c>async void</c>, which is what makes
    /// it worth being exact about: anything not named here reaches the dispatcher and takes the
    /// application down in the middle of a meeting. What is named is what the layers underneath
    /// actually throw — the audio engine's refusal, the recording's own, the filesystem's two, and
    /// SQLite's, which arrives from a corpus that is locked, unwritable or not a database and which
    /// the command line already answers with a refusal rather than with a stack trace.
    /// </remarks>
    private static bool Reportable(Exception thrown) => thrown
        is AudioCaptureException
        or RecordingException
        or IOException
        or UnauthorizedAccessException
        or SqliteException
        or DbUpdateException;

    private void OnPause(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || !Screen().Allows(RecorderPress.Pause))
        {
            return;
        }

        recording.Pause();
        Refresh();
    }

    private void OnResume(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || !Screen().Allows(RecorderPress.Resume))
        {
            return;
        }

        recording.Resume();
        Refresh();
    }

    private async void OnStop(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || _context is not { } context || !Screen().Allows(RecorderPress.Stop))
        {
            return;
        }

        _watch.Stop();
        _step = RecorderStep.Finishing;
        Refresh();

        try
        {
            // Off this thread, and this is the one that matters: the spools are poured onto a
            // timeline, the file is read back and hashed, and for a long meeting that is minutes.
            // The devices are already let go of before any of it, so nothing is being recorded
            // while it runs.
            var finished = await Task.Run(() =>
            {
                try
                {
                    return recording.Stop(Now());
                }
                finally
                {
                    context.Dispose();
                }
            });

            Say(
                UiTexts.TheMeetingIsRecorded,
                finished.MeetingId,
                Length(finished.Length),
                finished.Audio.RelativePath);

            // Said out loud, every time, because it is the promise and not an omission.
            Say(UiTexts.NothingWasQueued);
        }
        catch (Exception broke) when (Reportable(broke))
        {
            // The recording is over either way — what is on disk is a spool recovery already knows
            // how to offer — so this says so and does not throw a meeting away over it.
            //
            // Let go of here and not only inside stopping: stopping releases the devices itself,
            // but it can throw before it gets that far, and then the meeting has failed with two
            // devices still open. Letting go of one that did stop does nothing, so this is the
            // same guarantee the command line gets from holding the recording in a `using`.
            recording.Dispose();
            Say(UiTexts.TheMeetingCouldNotBeMade);
            Dump(broke.Message);
        }
        finally
        {
            _context = null;
            _recording = null;
            _step = RecorderStep.Nothing;

            // Every meeting is asked its own questions again. What channel 0 followed is a process
            // id that has just outlived its meeting, and what was spoken in the last one is not an
            // answer about the next one — `MeetingRecordings.Open` asks rather than guessing for
            // exactly that reason. The microphone is a device and stays chosen.
            _chosen = _chosen with { Source = null, Spoken = null };
            _sources = SourcesNow();

            if (!_closed)
            {
                FillThePickers();
                Refresh();
            }
        }
    }

    /// <summary>
    /// Takes the whole machine's audio. Nothing gets here without the offer having been on screen,
    /// because until then there is no button.
    /// </summary>
    /// <remarks>
    /// It is marked taken before the move rather than after, which is the opposite of what the
    /// prompt does and right for the same reason: at a prompt the question is whether a key was
    /// pressed after the words appeared, and here it is whether a second press can arrive while
    /// the first is still opening a device. A move that comes back refused puts it back.
    /// </remarks>
    private async void OnRecordTheWholeMachine(object sender, RoutedEventArgs e)
    {
        if (_recording is not { } recording || !Screen().Allows(RecorderPress.RecordTheWholeMachine))
        {
            return;
        }

        _taken = true;
        Refresh();

        try
        {
            // Off this thread like the other two: it opens one device, stops another and lets a
            // third go. Unlike the other two, the meeting is being recorded the whole time.
            await Task.Run(recording.RecordTheWholeMachine);
            Say(UiTexts.NowRecordingTheWholeMachine);
        }
        catch (Exception refused) when (Reportable(refused))
        {
            // Reported and not thrown: the meeting is still being recorded either way, and the
            // channel is where it was, so the offer is there to take again. Wider than the audio
            // engine's own refusal because the move writes what it did into the recording's own
            // folder, and a folder refuses a write for reasons that have nothing to do with a
            // device.
            _taken = false;
            Dump(refused.Message);
        }
        finally
        {
            Refresh();
        }
    }

    /// <summary>
    /// Asks the recording whether the program it is following has brought back nothing at all.
    /// </summary>
    /// <remarks>
    /// Never while the meeting is paused. A paused recording hears nothing from anything, so the
    /// rule the offer rests on would be true of a program that is playing perfectly well — and an
    /// offer, once made, stays made.
    /// </remarks>
    private void OnWatch(object? sender, object e)
    {
        if (_recording is not { } recording || _offered || _step != RecorderStep.Nothing || recording.IsPaused)
        {
            return;
        }

        if (recording.HeardNothingFromTheProgram())
        {
            _offered = true;
            Refresh();
        }
    }

    /// <summary>
    /// Closing the window with a meeting running lets the devices go and finishes nothing. What
    /// stays on disk is a recording somebody decides about later, which is exactly what closing
    /// the application in the middle of a meeting has always left.
    /// </summary>
    /// <remarks>
    /// It lets go of nothing while a press is still running, and that is the whole of it: the
    /// handler that started one owns what it made until it comes back, and disposing a corpus
    /// context underneath a meeting being written into it, or a capture session underneath the
    /// code stopping it, is how a meeting somebody recorded becomes a half-written file. Closing
    /// during either leaves what that handler is holding to that handler — and if the process goes
    /// before it comes back, what is on disk is the spool recovery already offers, which is the
    /// same answer as the power being pulled out.
    /// </remarks>
    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _watch.Stop();

        if (_step != RecorderStep.Nothing)
        {
            return;
        }

        _recording?.Dispose();
        _context?.Dispose();
        _recording = null;
        _context = null;
    }

    /// <summary>
    /// Now, off the machine's own clock. The command line reads it through a clock of its own for
    /// the same reason this does not take one: an instant somebody could hand in would let a paid
    /// artifact be dated to whenever they liked.
    /// </summary>
    private static UtcTimestamp Now() => UtcTimestamp.From(TimeProvider.System.GetUtcNow());

    /// <summary>How long the meeting was, as somebody reads a length off a screen.</summary>
    private static string Length(Duration length) =>
        length.ToTimeSpan().ToString(@"h\:mm\:ss", CultureInfo.InvariantCulture);

    private void Say(UiText text, params object?[] values)
    {
        _report.Add(TextLine.Says(text, values));
        Render();
    }

    /// <summary>
    /// A line that is data and not a sentence — a path, a device, what a driver said — and so has
    /// no language.
    /// </summary>
    private void Dump(string line)
    {
        _report.Add(TextLine.Data(line));
        Render();
    }

    private void Status(UiText text, params object?[] values)
    {
        _status = TextLine.Says(text, values);
        Render();
    }

    private void Render()
    {
        OutputText.Text = string.Join(Environment.NewLine, _report.Select(line => line.In(_language)));
        StatusText.Text = _status?.In(_language) ?? string.Empty;
    }
}
