namespace MeetingTranscriber.Presentation;

/// <summary>
/// The language somebody picked, kept so that picking it once is enough. Nobody having picked one
/// is a first-class answer: it is what sends <see cref="UiLanguages.Resolve"/> to ask Windows.
/// </summary>
/// <remarks>
/// A file rather than the corpus, because the application has to know what language to say
/// "choose a corpus" in before there is a corpus to read it from. Under `%LOCALAPPDATA%`, which
/// a packaged build redirects into the container and uninstalling wipes: a preference somebody
/// re-picks in one click is not a source, and `docs/corpus.md` is about what cannot be obtained
/// again.
/// </remarks>
public sealed class LanguageChoice
{
    public LanguageChoice(FileInfo location)
    {
        ArgumentNullException.ThrowIfNull(location);
        Location = location;
    }

    /// <summary>The file the choice is kept in.</summary>
    public FileInfo Location { get; }

    /// <summary>Where this user's choice is kept.</summary>
    public static LanguageChoice OfThisUser() => new(new FileInfo(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "MeetingTranscriber",
        "ui-language")));

    /// <summary>
    /// What was picked, or <c>null</c> if nobody picked anything.
    /// </summary>
    /// <remarks>
    /// A file that is not there, cannot be read, or holds something that names no language this
    /// application is written in all read the same way: nobody picked. The only thing that writes
    /// it is <see cref="Write"/>, so anything else in it was put there by hand — and this is read
    /// before the first window opens, where refusing to carry on means refusing to open at all.
    /// Windows' own language is a perfectly good answer and is right there, so the one outcome
    /// worth ruling out is the application not starting over a preference.
    /// </remarks>
    public UiLanguage? Read()
    {
        try
        {
            return Location.Exists ? UiLanguages.Parse(File.ReadAllText(Location.FullName)) : null;
        }
        catch (Exception unreadable) when (unreadable is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Records what somebody picked. Throws if it cannot: a choice that was not written down is
    /// one that will not be there next time, and the caller is what decides whether that is worth
    /// telling somebody about.
    /// </summary>
    public void Write(UiLanguage language)
    {
        Location.Directory?.Create();
        File.WriteAllText(Location.FullName, UiLanguages.Tag(language));
    }
}
