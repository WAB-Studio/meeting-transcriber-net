using System.Collections;
using System.Diagnostics;

using MeetingTranscriber.Presentation;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

using Windows.ApplicationModel;
using Windows.Storage.Pickers;

namespace MeetingTranscriber.App;

/// <summary>
/// Temporary scaffold for the two packaging checks of step 4. It goes when the two stop being
/// needed — the CLI's diagnostics are about the corpus and answer neither of them.
/// </summary>
/// <remarks>
/// It is also, until there is a real screen, where the language is switched from. Nothing about
/// how that works belongs to this window: it says which language was picked and is told to read
/// in it, and <c>App</c> is what decides and remembers.
/// </remarks>
public sealed partial class PackagingChecksWindow : Window
{
    /// <summary>
    /// The order the picker offers, and the order it reads a selection back in. One array, read
    /// twice, so the two cannot come apart.
    /// </summary>
    private static readonly UiLanguage[] Languages = Enum.GetValues<UiLanguage>();

    private const string ProbeFileName = "meeting-transcriber-probe.txt";
    private const string ReportFileName = "packaging-checks.txt";

    /// <summary>
    /// The report as the lines it is made of rather than as the string it currently reads as.
    /// A report already on screen re-reads itself when the language changes; one kept as text
    /// would be exactly the stretch of the window left behind in the previous one.
    /// </summary>
    private readonly List<TextLine> _report = [];

    private UiLanguage _language;
    private TextLine? _status;

    public PackagingChecksWindow(UiLanguage language)
    {
        // Before InitializeComponent: the bindings in the XAML are read while it runs.
        _language = language;

        InitializeComponent();

        Say(UiTexts.Package, Package.Current.Id.FamilyName);
        Say(UiTexts.InstalledAt, Package.Current.InstalledLocation.Path);
        Say(UiTexts.Process, Environment.ProcessPath ?? string.Empty);

        ReadIn(language);
    }

    /// <summary>
    /// Somebody picked a language on this screen. What is done about it is not this window's.
    /// </summary>
    public event EventHandler<UiLanguage>? LanguageChosen;

    /// <summary>
    /// Reads the whole window in this language: what the XAML bound, the title, the report already
    /// produced and the status line. Nothing on screen is left in the one before.
    /// </summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();
        Title = UiTexts.PackagingChecks.In(language);
        Render();

        // The picker is filled here rather than once at the start so that it re-reads with
        // everything else. The two names come back the same either way — that is what the
        // catalogue says about them — but a control that kept a rendering from before is the
        // shape of the bug this whole card is about, and it does not get one by exception.
        LanguagePicker.ItemsSource = Languages.Select(offered => In(UiLanguages.Endonym(offered))).ToArray();
        LanguagePicker.SelectedIndex = Array.IndexOf(Languages, language);
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

    /// <summary>
    /// Check 1: what environment a child process launched from the package inherits. Phase 5 has
    /// to launch Claude Code with a sane environment, so what reaches it and what does not is
    /// something that has to be known.
    /// </summary>
    private async void OnRunEnvironmentProbe(object sender, RoutedEventArgs e)
    {
        EnvironmentButton.IsEnabled = false;
        try
        {
            Blank();
            Say(UiTexts.ChildEnvironmentHeading);

            var child = await CaptureChildEnvironmentAsync();
            var parent = Environment.GetEnvironmentVariables()
                .Cast<DictionaryEntry>()
                .ToDictionary(entry => (string)entry.Key, entry => (string?)entry.Value ?? string.Empty,
                              StringComparer.OrdinalIgnoreCase);

            Say(UiTexts.VariablesInTheApp, parent.Count);
            Say(UiTexts.VariablesInTheChild, child.Count);

            var onlyInChild = child.Keys.Except(parent.Keys, StringComparer.OrdinalIgnoreCase).Order().ToArray();
            var onlyInParent = parent.Keys.Except(child.Keys, StringComparer.OrdinalIgnoreCase).Order().ToArray();

            Blank();
            Say(UiTexts.OnlyInTheChild, onlyInChild.Length);
            foreach (var key in onlyInChild)
            {
                Dump($"  {key}={child[key]}");
            }

            Blank();
            Say(UiTexts.OnlyInTheApp, onlyInParent.Length);
            foreach (var key in onlyInParent)
            {
                Dump($"  {key}={parent[key]}");
            }

            Blank();
            Say(UiTexts.TheChildEnvironmentInFull);
            foreach (var (key, value) in child.OrderBy(pair => pair.Key))
            {
                Dump($"  {key}={value}");
            }

            await ReportChildRedirectionAsync();

            Blank();
            Say(UiTexts.CompareTheEnvironmentByHand);

            Status(UiTexts.ChildEnvironmentCheckDone);
        }
        catch (Exception ex)
        {
            Say(UiTexts.Failed);
            Dump(ex.ToString());
            Status(UiTexts.ChildEnvironmentCheckFailed);
        }
        finally
        {
            EnvironmentButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// Check 2: whether a write path the user chose is redirected by the container's filesystem
    /// virtualisation.
    /// </summary>
    private async void OnRunFileSystemProbe(object sender, RoutedEventArgs e)
    {
        FileSystemButton.IsEnabled = false;
        try
        {
            Blank();
            Say(UiTexts.WritePathHeading);

            var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
            picker.FileTypeFilter.Add("*");
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder is null)
            {
                Say(UiTexts.NoFolderWasChosen);
                Status(UiTexts.WritePathCheckCancelled);
                return;
            }

            var chosen = Path.Combine(folder.Path, ProbeFileName);
            var stamp = Guid.NewGuid().ToString();
            await File.WriteAllTextAsync(chosen, stamp);

            var readBack = File.Exists(chosen) ? await File.ReadAllTextAsync(chosen) : null;

            Say(UiTexts.ChosenPath, chosen);
            Say(UiTexts.ExistsThere, Word(File.Exists(chosen)));
            Say(UiTexts.ContentsReadBack, readBack ?? "-");
            Say(UiTexts.ContentsMatch, Word(readBack == stamp));

            // The classic redirection case: a write straight to LOCALAPPDATA.
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appDataProbe = Path.Combine(localAppData, ProbeFileName);
            await File.WriteAllTextAsync(appDataProbe, stamp);

            Blank();
            Say(UiTexts.LocalAppDataResolvedTo, localAppData);
            Say(UiTexts.WrittenAt, appDataProbe);

            var packageRoot = Path.Combine(
                Path.GetDirectoryName(localAppData.TrimEnd(Path.DirectorySeparatorChar))!,
                "Local", "Packages", Package.Current.Id.FamilyName);
            Say(UiTexts.PackageRoot, packageRoot);
            Say(UiTexts.PackageRootExists, Word(Directory.Exists(packageRoot)));

            foreach (var shadow in FindShadows(packageRoot))
            {
                Say(UiTexts.RedirectedCopy, shadow);
            }

            Blank();
            Say(UiTexts.CheckTheWritePathByHand, folder.Path, ProbeFileName);

            Status(UiTexts.WritePathCheckDone);
        }
        catch (Exception ex)
        {
            Say(UiTexts.Failed);
            Dump(ex.ToString());
            Status(UiTexts.WritePathCheckFailed);
        }
        finally
        {
            FileSystemButton.IsEnabled = true;
        }
    }

    private async void OnSaveReport(object sender, RoutedEventArgs e)
    {
        try
        {
            var target = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                ReportFileName);
            await File.WriteAllTextAsync(target, ReportText());
            Status(UiTexts.ReportSavedAt, target);
        }
        catch (Exception ex)
        {
            Status(UiTexts.ReportNotSaved, ex.Message);
        }
    }

    /// <summary>Says what the report cannot say for itself — what happened around it.</summary>
    public void Report(UiText text) => Say(text);

    private void OnLanguageChosen(object sender, SelectionChangedEventArgs e)
    {
        if (LanguagePicker.SelectedIndex < 0)
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

    /// <summary>
    /// Inheriting the environment is not the same as inheriting the container. The child sees
    /// LOCALAPPDATA pointing at the real path, but if its writes are redirected the same way the
    /// app's are, anything Claude Code leaves there dies on uninstall.
    /// </summary>
    private async Task ReportChildRedirectionAsync()
    {
        const string ChildProbeFileName = "meeting-transcriber-child-probe.txt";

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var stamp = Guid.NewGuid().ToString();

        var startInfo = new ProcessStartInfo("cmd.exe", $"/c echo {stamp}> \"%LOCALAPPDATA%\\{ChildProbeFileName}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using (var child = Process.Start(startInfo) ?? throw new InvalidOperationException("cmd.exe did not start."))
        {
            await child.WaitForExitAsync();
        }

        var containerPath = Path.Combine(
            Path.GetDirectoryName(localAppData.TrimEnd(Path.DirectorySeparatorChar))!,
            "Local", "Packages", Package.Current.Id.FamilyName, "LocalCache", "Local", ChildProbeFileName);

        Blank();
        Say(UiTexts.ChildWriteToLocalAppData);
        Say(UiTexts.StampWritten, stamp);
        Say(UiTexts.InTheContainer, Word(File.Exists(containerPath)), containerPath);

        if (File.Exists(containerPath))
        {
            Say(UiTexts.ChildInheritsTheRedirection);
        }
        else
        {
            Say(UiTexts.ChildWritesOutsideTheContainer, Path.Combine(localAppData, ChildProbeFileName));
        }
    }

    private static async Task<Dictionary<string, string>> CaptureChildEnvironmentAsync()
    {
        var startInfo = new ProcessStartInfo("cmd.exe", "/c set")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var child = Process.Start(startInfo)
            ?? throw new InvalidOperationException("cmd.exe did not start.");

        var output = await child.StandardOutput.ReadToEndAsync();
        await child.WaitForExitAsync();

        return output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r'))
            .Select(line => line.Split('=', 2))
            .Where(parts => parts.Length == 2)
            .ToDictionary(parts => parts[0], parts => parts[1], StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> FindShadows(string packageRoot)
    {
        if (!Directory.Exists(packageRoot))
        {
            yield break;
        }

        IEnumerable<string> matches;
        try
        {
            matches = Directory.EnumerateFiles(packageRoot, ProbeFileName, SearchOption.AllDirectories);
        }
        catch (UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var match in matches)
        {
            yield return match;
        }
    }

    private static UiText Word(bool value) => value ? UiTexts.Yes : UiTexts.No;

    private void Say(UiText text, params object?[] values)
    {
        _report.Add(TextLine.Says(text, values));
        Render();
    }

    /// <summary>
    /// A line that is data and not a sentence — a variable, a path — and so has no language.
    /// </summary>
    private void Dump(string line)
    {
        _report.Add(TextLine.Data(line));
        Render();
    }

    private void Blank() => Dump(string.Empty);

    private void Status(UiText text, params object?[] values)
    {
        _status = TextLine.Says(text, values);
        Render();
    }

    private string ReportText() =>
        string.Join(Environment.NewLine, _report.Select(line => line.In(_language)));

    private void Render()
    {
        OutputText.Text = ReportText();
        StatusText.Text = _status?.In(_language) ?? string.Empty;
    }
}
