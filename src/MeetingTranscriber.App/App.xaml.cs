using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Processing.Jobs;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;

using Windows.System.UserProfile;

namespace MeetingTranscriber.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
/// <remarks>
/// <para>
/// It is also the one place that decides what language the application reads in, and the only one
/// that writes down what somebody chose. A screen is handed the answer and told when it changes;
/// none of them works it out for itself, or two screens would eventually disagree.
/// </para>
/// <para>
/// And it is where the corpus is resolved: before a window opens, once, and never again from
/// inside one. Anything that opened a corpus for itself would be a second answer to the one
/// question the application cannot be wrong about, and the wrong answer to it puts a person's
/// meetings somewhere an uninstall takes them.
/// </para>
/// </remarks>
public partial class App : Application
{
    private readonly LanguageChoice _choice = LanguageChoice.OfThisUser();

    private MainWindow? _main;
    private PackagingChecksWindow? _checks;

    /// <summary>
    /// What the application is being read in now. Held here rather than read back off the
    /// preference file: a choice that could not be written is still the language this session is
    /// in, and a second window opening in the one before it would be the bug the whole language
    /// card is about wearing a different hat.
    /// </summary>
    private UiLanguage _language = UiLanguages.WhenWindowsSpeaksNeither;

    /// <summary>
    /// Where the corpus is, answered once at launch. Every window is handed this rather than
    /// asking for itself, for the reason the class says: two answers to that question is how a
    /// person's meetings end up in two places.
    /// </summary>
    private CorpusFolder? _corpus;

    /// <summary>
    /// What lets go of the runner's pump over whichever corpus <see cref="_corpus"/> named last.
    /// One per launch of <see cref="StartWhatThisLaunchOwesTheCorpus"/>, cancelled before the next
    /// is made and never disposed here: the pump this cancels may still be reading its token on
    /// another thread when the next corpus is chosen, and disposing out from under that read is
    /// the race, not the fix. Letting it go is the process ending or the pump itself finishing —
    /// either way, nobody but the pump reads it again.
    /// </summary>
    private CancellationTokenSource? _work;

    /// <summary>
    /// Initializes the singleton application object.  This is the first line of authored code
    /// executed, and as such is the logical equivalent of main() or WinMain().
    /// </summary>
    public App()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        _language = UiLanguages.Resolve(_choice.Read(), WindowsLanguages());
        _corpus = CorpusLocation.OfThisUser().Resolve();

        OpenMainWindow(_corpus);

        StartWhatThisLaunchOwesTheCorpus(_corpus);
    }

    /// <summary>
    /// Builds the main window over <paramref name="corpus"/>, subscribes what it raises, and puts
    /// it on screen. A method of its own and not inlined into <see cref="OnLaunched"/>, because
    /// <see cref="OnCorpusChosen"/> does this a second time over a second corpus, once somebody has
    /// picked a folder from the refusal a first corpus was opened with.
    /// </summary>
    private void OpenMainWindow(CorpusFolder corpus)
    {
        var window = new MainWindow(_language, corpus);
        window.LanguageChosen += OnLanguageChosen;
        window.PackagingChecksAsked += OnPackagingChecksAsked;
        window.CorpusChosen += OnCorpusChosen;

        // The sender is compared before clearing anything, and not merely for the closing window's
        // own sake: while there is only one window this always agrees with `_main`, but
        // `OnCorpusChosen` replaces it while the old one is still on screen, and closing that old
        // window still fires this handler. Without the comparison it would clear `_main` out from
        // under the window that just replaced it.
        window.Closed += (sender, _) =>
        {
            if (ReferenceEquals(sender, _main))
            {
                _main = null;
            }
        };

        _main = window;
        _main.Activate();
    }

    /// <summary>
    /// Somebody named a folder this application can open its corpus from, on the settings screen of
    /// a window whose corpus was refused.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The setting is re-resolved rather than carried on the event, which is why
    /// <see cref="MainWindow.CorpusChosen"/> takes no folder to begin with: the setting is what the
    /// next launch will read, and a screen that opened a corpus the setting does not name would be
    /// a second answer to the one question this application cannot be wrong about. A folder that
    /// went between the picker and here comes back as a refusal, and the new window draws it.
    /// </para>
    /// <para>
    /// A window replaced and not five controls re-opened. Each of <c>MeetingsDrawer</c>,
    /// <c>ReadingAMeeting</c>, <c>ReadingANode</c>, <c>ClassifyingAMeeting</c> and
    /// <c>Configuracion</c> takes its corpus through an <c>Open</c> that refuses being called
    /// twice, and relaxing all five is a change to every screen in the application for a press that
    /// can only happen in one state — a refused corpus, where nothing is recording, no meeting is
    /// open and no list is drawn, so there is nothing on screen to lose.
    /// </para>
    /// <para>
    /// The old window is closed after the new one is up and activated, and its own <c>Closed</c>
    /// handler still runs — that is unavoidable, it is how Windows ends a window — but does nothing,
    /// because <see cref="OpenMainWindow"/> keys it to its own <c>sender</c> and <c>_main</c> is
    /// already the new window by the time it fires.
    /// </para>
    /// <para>
    /// <see cref="StartWhatThisLaunchOwesTheCorpus"/> runs again last, over the new corpus, the same
    /// method <see cref="OnLaunched"/> calls and never a second start of its own —
    /// <c>LaunchWorkTests</c> counts how many places in this file start background work, and it
    /// counts occurrences of the text rather than calls, so calling the one method twice costs
    /// nothing where a second <c>Task.Run</c> would go red.
    /// </para>
    /// </remarks>
    private void OnCorpusChosen(object? sender, EventArgs e)
    {
        _corpus = CorpusLocation.OfThisUser().Resolve();

        var closing = _main;

        OpenMainWindow(_corpus);

        closing?.Close();

        StartWhatThisLaunchOwesTheCorpus(_corpus);
    }

    /// <summary>
    /// Starts everything this launch owes the corpus it just opened, and then the runner's pump
    /// over that same corpus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What the launch's own work is, and what order it runs in, is
    /// <see cref="WhatALaunchOwes.InOrder"/>'s and is not restated here. What this holds is the
    /// application's half: that it happens at all, on a thread that is not the one the window draws
    /// on, after the window is up, and that the runner starts only once it has. A launch used to
    /// start two of these side by side, and the list is what replaced them — anything a launch
    /// comes to owe belongs in it rather than beside this call.
    /// </para>
    /// <para>
    /// Off the thread the window draws on because the work opens the corpus and holds a write
    /// transaction while it runs, which is not something a window should be inside. Nothing on
    /// screen waits for it either.
    /// </para>
    /// <para>
    /// <b>One <see cref="_work"/> at a time.</b> A corpus chosen a second time — from the settings
    /// screen of a window whose first corpus was refused — cancels whatever pump is running over
    /// the corpus this is replacing before starting the next. <c>OnCorpusChosen</c> reaches this
    /// only after a refused corpus, where the first call here found no folder to start a pump over
    /// at all, so the cancel is usually a no-op; it is still what keeps this correct on the day that
    /// stops being true.
    /// </para>
    /// <para>
    /// Discarding the task is an accepted silence and not a second one. <c>RunIn</c> answers with
    /// what happened instead of throwing about it, for everything a disk or a corpus can refuse, so
    /// what is dropped here is the launch's own report, and never the work. The pump's own report —
    /// <c>JobsRun.Left</c> and <c>RestartSettled.Left</c> — is read by nobody yet either, for the
    /// same reason: nothing on this side could act on it. The task itself ends when its token is
    /// cancelled or the process ends; a call in flight at either moment is stopped on a person by
    /// whoever next takes the corpus's lease, never by this method.
    /// </para>
    /// <para>
    /// The one thing neither answers with is running out of memory. <c>RunIn</c> leaves it so the
    /// chores behind the one that met it are not attempted, and <c>JobRunner.PumpAsync</c> leaves it
    /// so the pump does not carry on building its next look out of the same exhaustion. Both are
    /// dropped here too, and have to be: a heap that is gone is not something a window can be asked
    /// about, and this application does not get to end itself over work a launch owed a corpus.
    /// </para>
    /// </remarks>
    private void StartWhatThisLaunchOwesTheCorpus(CorpusFolder corpus)
    {
        if (corpus.Folder is { } folder)
        {
            _work?.Cancel();
            var work = new CancellationTokenSource();
            _work = work;

            _ = Task.Run(async () =>
            {
                WhatALaunchOwes.RunIn(folder);

                await JobRunner.PumpAsync(
                    folder,
                    TimeProvider.System,
                    TranscribingOnThisMachinesKey.Sending(),
                    JobRunner.HowOftenTheQueueIsLookedAt,
                    work.Token).ConfigureAwait(false);
            });
        }
    }

    /// <summary>
    /// What Windows is set to, most wanted first. <c>GlobalizationPreferences</c> rather than
    /// <c>ApplicationLanguages</c> on purpose: the second is already narrowed to what this
    /// application declares it speaks, so asking it would be asking ourselves.
    /// </summary>
    private static IReadOnlyList<string> WindowsLanguages() => GlobalizationPreferences.Languages;

    /// <summary>
    /// The temporary packaging-checks scaffold, which is not part of the product and is reached
    /// from a corner of the recording screen. It stays until ISC-110 closes: what it answers has
    /// to be answered from inside the package, so the command line cannot answer it.
    /// </summary>
    private void OnPackagingChecksAsked(object? sender, EventArgs e)
    {
        if (_checks is not null)
        {
            _checks.Activate();
            return;
        }

        var window = new PackagingChecksWindow(_language);
        window.LanguageChosen += OnLanguageChosen;
        window.Closed += (_, _) => _checks = null;

        _checks = window;
        _checks.Activate();
    }

    private void OnLanguageChosen(object? sender, UiLanguage language)
    {
        _language = language;

        // Every window open reads in it first. What somebody just asked for is not held back by a
        // preference file, and a file that cannot be written is a language that does not survive
        // the session rather than a session that ends here.
        _main?.ReadIn(language);
        _checks?.ReadIn(language);

        try
        {
            _choice.Write(language);
        }
        catch (Exception unwritable) when (unwritable is IOException or UnauthorizedAccessException)
        {
            // Said rather than swallowed: the application looks exactly as it would have if the
            // choice had stuck, so the only way anybody learns it did not is the next launch.
            _main?.Report(UiTexts.LanguageNotRemembered);
            _checks?.Report(UiTexts.LanguageNotRemembered);
        }
    }
}
