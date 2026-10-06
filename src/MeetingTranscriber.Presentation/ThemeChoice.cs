namespace MeetingTranscriber.Presentation;

/// <summary>
/// Which of the application's two palettes it is drawn in. <see cref="System"/> is not a third
/// palette but the answer "whichever Windows is set to", and it is what nobody having chosen reads
/// as.
/// </summary>
/// <remarks>The numbers are given for the reason <c>AfterARecording</c> gives.</remarks>
public enum AppTheme
{
    /// <summary>Follow Windows, including a switch made while the application is open.</summary>
    System = 1,

    /// <summary>The light palette, whatever Windows is set to.</summary>
    Light = 2,

    /// <summary>The dark palette, whatever Windows is set to.</summary>
    Dark = 3,
}

/// <summary>
/// The theme somebody picked, kept so that picking it once is enough. It is shaped like
/// <see cref="LanguageChoice"/> and kept for the same reason in the same place: a file under
/// <c>%LOCALAPPDATA%</c>, read before the first window opens and before there is any corpus to ask.
/// </summary>
/// <remarks>
/// Nobody having picked one is <see cref="AppTheme.System"/>, which is a first-class answer rather
/// than a fallback: a file that is not there, cannot be read or holds something this application
/// does not know all read the same way, because refusing to open over a colour is not something a
/// preference gets to do.
/// </remarks>
public sealed class ThemeChoice
{
    public ThemeChoice(FileInfo location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Location = location;
    }

    /// <summary>The file the choice is kept in.</summary>
    public FileInfo Location { get; }

    /// <summary>Where this user's choice is kept, beside the language's.</summary>
    public static ThemeChoice OfThisUser() => new(new FileInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingTranscriber",
        "ui-theme")));

    /// <summary>What was picked, or <see cref="AppTheme.System"/> when nobody picked anything.</summary>
    public AppTheme Read()
    {
        try
        {
            return Location.Exists ? Parse(File.ReadAllText(Location.FullName)) : AppTheme.System;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return AppTheme.System;
        }
    }

    /// <summary>
    /// Records what somebody picked. Throws if it cannot, for the reason
    /// <see cref="LanguageChoice.Write"/> does: whether that is worth telling somebody about is the
    /// caller's.
    /// </summary>
    public void Write(AppTheme theme)
    {
        Location.Directory?.Create();
        File.WriteAllText(Location.FullName, Tag(theme));
    }

    /// <summary>The word a theme is stored as.</summary>
    public static string Tag(AppTheme theme) => theme switch
    {
        AppTheme.System => "system",
        AppTheme.Light => "light",
        AppTheme.Dark => "dark",
        _ => throw new ArgumentOutOfRangeException(nameof(theme)),
    };

    /// <summary>The theme a stored word names, and <see cref="AppTheme.System"/> for anything else.</summary>
    public static AppTheme Parse(string? stored) => stored?.Trim().ToLowerInvariant() switch
    {
        "light" => AppTheme.Light,
        "dark" => AppTheme.Dark,
        _ => AppTheme.System,
    };
}
