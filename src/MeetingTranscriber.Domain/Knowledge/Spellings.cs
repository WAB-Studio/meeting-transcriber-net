using System.Globalization;
using System.Text;

namespace MeetingTranscriber.Domain.Knowledge;

/// <summary>
/// How alike two spellings are, and what a word is. The one place either is decided: finding the
/// words a transcript keeps writing wrongly and finding the person a typed name might already be
/// ask the same question of two different texts, and two measures would answer it two ways.
/// </summary>
/// <remarks>
/// <para>
/// The measure is an edit distance over the folded text, normalised by the longer of the two.
/// Folded means lower case, accents gone and spaces gone, so <c>Nube co</c>, <c>nubeco</c> and
/// <c>NUBECO</c> are one spelling and so are <c>sesión</c> and <c>sesion</c>. A swapped pair of
/// letters costs one edit rather than two, because it is the commonest typo there is.
/// </para>
/// <para>
/// Not a phonetic code: Soundex and Metaphone are built for English names and this corpus is
/// mostly Spanish. Not SQLite's <c>spellfix1</c>: the bundled SQLite does not load it.
/// </para>
/// </remarks>
public static class Spellings
{
    /// <summary>
    /// Lower-cased with the invariant culture, decomposed, with every mark that rides on a letter
    /// and every white-space character dropped.
    /// </summary>
    public static string Folded(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var built = new StringBuilder(text.Length);
        foreach (var character in text.ToLowerInvariant().Normalize(NormalizationForm.FormD))
        {
            if (char.IsWhiteSpace(character)
                || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            built.Append(character);
        }

        return built.ToString();
    }

    /// <summary>
    /// One minus the edit distance between the two folded spellings over the length of the longer:
    /// <c>1</c> is the same spelling and <c>0</c> shares nothing. Symmetric.
    /// </summary>
    public static double Resemblance(string a, string b)
    {
        var left = Folded(a);
        var right = Folded(b);
        var longest = Math.Max(left.Length, right.Length);

        return longest is 0 ? 1 : 1 - (double)Distance(left, right) / longest;
    }

    /// <summary>
    /// What counts as part of a word: letters, digits, underscore and the marks that ride on a
    /// letter, so "sesión" is one word in a corpus that is mostly Spanish.
    /// </summary>
    public static bool IsWordCharacter(char character) =>
        char.IsLetterOrDigit(character)
        || character is '_'
        || CharUnicodeInfo.GetUnicodeCategory(character) is UnicodeCategory.NonSpacingMark;

    /// <summary>Every maximal run of word characters, in order, as written.</summary>
    public static IReadOnlyList<string> WordsOf(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var words = new List<string>();
        var from = -1;
        for (var index = 0; index <= text.Length; index++)
        {
            var inside = index < text.Length && IsWordCharacter(text[index]);
            if (inside && from < 0)
            {
                from = index;
            }
            else if (!inside && from >= 0)
            {
                words.Add(text[from..index]);
                from = -1;
            }
        }

        return words;
    }

    /// <summary>
    /// Optimal string alignment: insert, delete, substitute and swap two adjacent letters each cost
    /// one.
    /// </summary>
    private static int Distance(string a, string b)
    {
        var rows = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++)
        {
            rows[i, 0] = i;
        }

        for (var j = 0; j <= b.Length; j++)
        {
            rows[0, j] = j;
        }

        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var substitution = a[i - 1] == b[j - 1] ? 0 : 1;
                var best = Math.Min(
                    Math.Min(rows[i - 1, j] + 1, rows[i, j - 1] + 1),
                    rows[i - 1, j - 1] + substitution);

                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    best = Math.Min(best, rows[i - 2, j - 2] + 1);
                }

                rows[i, j] = best;
            }
        }

        return rows[a.Length, b.Length];
    }
}
