using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Processing.Rendering;

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

        var window = new MainWindow(_language, _corpus);
        window.LanguageChosen += OnLanguageChosen;
        window.PackagingChecksAsked += OnPackagingChecksAsked;
        window.Closed += (_, _) => _main = null;

        _main = window;
        _main.Activate();

        CatchUpOnTheRenders(_corpus);
    }

    /// <summary>
    /// Produces the files of every meeting whose transcription has arrived and whose transcript
    /// and jsonl have not, in the corpus this session opened.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Here, and not on the meetings screen opening. The response arriving is what puts a meeting
    /// in this state, and launch is where the application learns of one that arrived while it was
    /// closed — today it is the only place it can learn of one at all, because nothing in this
    /// application runs a transcription: taking a stage queues a job and starts nothing, so there
    /// is no completion to hang this off yet. Hanging it on the screen instead would tie the work
    /// to how often somebody looks at a list, which is not what decides the files.
    /// </para>
    /// <para>
    /// Off the thread the window draws on, after the window is up, and nothing waits for it: it
    /// writes to the corpus and holds a write transaction while it does, which is not something a
    /// window should be inside. Nothing on screen is waiting on it either — a rendered file says
    /// nothing about the stage a meeting is at, so no card changes when one lands.
    /// </para>
    /// <para>
    /// Nobody is told how it went, and that is the decision: the files cost nothing and can be
    /// produced again, so a render that failed is one the next launch tries again and there is
    /// nothing in it for a person to answer. Saying it out loud instead would need a line on the
    /// window and words in the catalogue, which is a screen decision and not this one's — and it
    /// is owed for the failure that is not transient, a response the parser can never read, which
    /// this retries and drops again on every launch.
    /// </para>
    /// <para>
    /// Discarding the task is an accepted silence and not a second one. <c>CatchUpOn</c> answers
    /// with what happened instead of throwing about it, so what is dropped here is the report and
    /// never the sweep: no meeting is lost by nobody reading it, and the only thing that can leave
    /// it is what nothing on this side could have done anything with anyway. What is accepted is
    /// the paragraph above — the line naming the meeting goes with the report.
    /// </para>
    /// </remarks>
    private static void CatchUpOnTheRenders(CorpusFolder corpus)
    {
        if (corpus.Folder is { } folder)
        {
            _ = Task.Run(() => OwedRenders.CatchUpOn(folder, TimeProvider.System));
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
