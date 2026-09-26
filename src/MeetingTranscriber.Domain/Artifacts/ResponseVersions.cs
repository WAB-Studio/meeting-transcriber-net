using System.Globalization;

namespace MeetingTranscriber.Domain.Artifacts;

/// <summary>
/// The one rule that says which paid response of a meeting is which version, off nothing but the
/// name it is filed under.
/// </summary>
/// <remarks>
/// <para>
/// The version is in the name and nowhere else. It is not read off <see cref="Artifact.ConfirmedAt"/>,
/// because <c>StagedArtifact.Record</c>'s put-back path rewrites that field on every restore — a
/// response put back after the disk lost it would read as filed after one that came later and
/// never was. <c>deepgram.json</c> keeps the name every corpus already has, so nothing already
/// filed has to move for a second one to exist.
/// </para>
/// <para>It throws nothing. A row this cannot place is a corpus state each caller refuses in its
/// own words — the render's, the filing door's, or <c>MeetingWork</c>'s — because what to say about
/// it depends on what the caller was about to do with it.</para>
/// </remarks>
public static class ResponseVersions
{
    /// <summary>The first version's name, which is the one name every corpus already has.</summary>
    public const string First = "deepgram.json";

    private const string Prefix = "deepgram.v";
    private const string Suffix = ".json";

    /// <summary>The file name one version is stored under.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="version"/> is below 1.</exception>
    public static string Named(int version)
    {
        if (version < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(version), version, "There is no version below the first.");
        }

        return version == 1
            ? First
            : $"{Prefix}{version.ToString(CultureInfo.InvariantCulture)}{Suffix}";
    }

    /// <summary>
    /// Which version <paramref name="fileName"/> names, or nothing when it names no version at all.
    /// </summary>
    /// <remarks>
    /// <see cref="First"/> answers 1, and <c>deepgram.v&lt;n&gt;.json</c> answers <c>n</c> from 2 up,
    /// where the digits are nothing but digits and carry no leading zero. Everything else — a
    /// leading zero, <c>v1</c>, <c>v0</c>, a suffix too long to be an <see cref="int"/>, the refused
    /// name, a partial write, a file that is not a response at all — answers <c>null</c> rather than
    /// throwing, so a caller reading a row this corpus never wrote under this rule gets a fact to
    /// refuse on and not an exception to catch. The comparison is case-insensitive, to match
    /// <c>CorpusFiles.PathComparer</c>, which Domain does not reference and so cannot name by type.
    /// </remarks>
    public static int? VersionOf(string fileName)
    {
        ArgumentNullException.ThrowIfNull(fileName);

        if (string.Equals(fileName, First, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (fileName.Length <= Prefix.Length + Suffix.Length
            || !fileName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase)
            || !fileName.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var digits = fileName[Prefix.Length..^Suffix.Length];

        if (digits.Length == 0 || digits[0] == '0')
        {
            return null;
        }

        return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var version)
            && version >= 2
            ? version
            : null;
    }

    /// <summary>
    /// Which version <paramref name="response"/> names, off the last <c>/</c>-separated segment of
    /// its <see cref="Artifact.RelativePath"/> and nothing else — the file name, wherever the
    /// response's folder put it.
    /// </summary>
    public static int? VersionOf(Artifact response)
    {
        ArgumentNullException.ThrowIfNull(response);

        var path = response.RelativePath;
        var slash = path.LastIndexOf('/');
        return VersionOf(slash < 0 ? path : path[(slash + 1)..]);
    }
}
