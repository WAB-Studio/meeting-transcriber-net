using MeetingTranscriber.Presentation;

using Microsoft.UI.Xaml;

using Windows.System.UserProfile;

namespace MeetingTranscriber.App;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
/// <remarks>
/// It is also the one place that decides what language the application reads in, and the only one
/// that writes down what somebody chose. A screen is handed the answer and told when it changes;
/// none of them works it out for itself, or two screens would eventually disagree.
/// </remarks>
public partial class App : Application
{
    private readonly LanguageChoice _choice = LanguageChoice.OfThisUser();

    private MainWindow? _window;

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
        var window = new MainWindow(UiLanguages.Resolve(_choice.Read(), WindowsLanguages()));
        window.LanguageChosen += OnLanguageChosen;

        _window = window;
        _window.Activate();
    }

    /// <summary>
    /// What Windows is set to, most wanted first. <c>GlobalizationPreferences</c> rather than
    /// <c>ApplicationLanguages</c> on purpose: the second is already narrowed to what this
    /// application declares it speaks, so asking it would be asking ourselves.
    /// </summary>
    private static IReadOnlyList<string> WindowsLanguages() => GlobalizationPreferences.Languages;

    private void OnLanguageChosen(object? sender, UiLanguage language)
    {
        // The window reads in it first. What somebody just asked for is not held back by a
        // preference file, and a file that cannot be written is a language that does not survive
        // the session rather than a session that ends here.
        _window?.ReadIn(language);

        try
        {
            _choice.Write(language);
        }
        catch (Exception unwritable) when (unwritable is IOException or UnauthorizedAccessException)
        {
            // Said rather than swallowed: the application looks exactly as it would have if the
            // choice had stuck, so the only way anybody learns it did not is the next launch.
            _window?.Report(UiTexts.LanguageNotRemembered);
        }
    }
}
