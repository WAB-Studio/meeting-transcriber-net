using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;

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

    private RecordingWindow? _recorder;
    private MainWindow? _checks;
    private MeetingsWindow? _meetings;

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

        var window = new RecordingWindow(_language, _corpus);
        window.LanguageChosen += OnLanguageChosen;
        window.PackagingChecksAsked += OnPackagingChecksAsked;
        window.MeetingsAsked += OnMeetingsAsked;
        window.Closed += (_, _) => _recorder = null;

        _recorder = window;
        _recorder.Activate();
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

        var window = new MainWindow(_language);
        window.LanguageChosen += OnLanguageChosen;
        window.Closed += (_, _) => _checks = null;

        _checks = window;
        _checks.Activate();
    }

    /// <summary>
    /// The meetings already recorded and what the application still owes each one. A window of
    /// its own rather than a panel on the recorder: what is on it is about meetings that are over,
    /// and one of them can be pressed while another is being recorded.
    /// </summary>
    private void OnMeetingsAsked(object? sender, EventArgs e)
    {
        if (_meetings is not null)
        {
            _meetings.Activate();
            return;
        }

        if (_corpus is not { } corpus)
        {
            return;
        }

        var window = new MeetingsWindow(_language, corpus);
        window.Closed += (_, _) => _meetings = null;

        _meetings = window;
        _meetings.Activate();
    }

    private void OnLanguageChosen(object? sender, UiLanguage language)
    {
        _language = language;

        // Every window open reads in it first. What somebody just asked for is not held back by a
        // preference file, and a file that cannot be written is a language that does not survive
        // the session rather than a session that ends here.
        _recorder?.ReadIn(language);
        _checks?.ReadIn(language);
        _meetings?.ReadIn(language);

        try
        {
            _choice.Write(language);
        }
        catch (Exception unwritable) when (unwritable is IOException or UnauthorizedAccessException)
        {
            // Said rather than swallowed: the application looks exactly as it would have if the
            // choice had stuck, so the only way anybody learns it did not is the next launch.
            _recorder?.Report(UiTexts.LanguageNotRemembered);
            _checks?.Report(UiTexts.LanguageNotRemembered);
            _meetings?.Report(UiTexts.LanguageNotRemembered);
        }
    }
}
