namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The five things a probe can do to a running application.
/// </summary>
/// <remarks>
/// Closed on purpose, and small on purpose. Everything a screen is checked for is some
/// arrangement of these — get in, look, press, fill in, pick from a list — and a sixth verb should
/// have to argue that no arrangement of the five would have done.
/// </remarks>
internal enum Verb
{
    /// <summary>Write the tree and the picture of the screen, under a given name.</summary>
    See,

    /// <summary>Do to a control what pressing it does.</summary>
    Press,

    /// <summary>Put text in a field.</summary>
    Type,

    /// <summary>Pick a named thing out of a list.</summary>
    Choose,

    /// <summary>Stop until something is on a screen, and say which screen that is.</summary>
    Wait,
}

/// <summary>One instruction, as it was written on the command line.</summary>
internal sealed record Instruction(Verb Verb, string Subject, string Detail)
{
    private static readonly Dictionary<Verb, int> Takes = new()
    {
        [Verb.See] = 1,
        [Verb.Press] = 1,
        [Verb.Type] = 2,
        [Verb.Choose] = 2,
        [Verb.Wait] = 1,
    };

    /// <summary>
    /// Reads the whole script off a flat list of words. Each verb says how many words follow it,
    /// so a script needs no punctuation and no file — which is what makes a walk through two
    /// screens one line somebody can read in a transcript.
    /// </summary>
    internal static IReadOnlyList<Instruction> Read(IReadOnlyList<string> words)
    {
        var script = new List<Instruction>();

        var at = 0;
        while (at < words.Count)
        {
            var verb = VerbIn(words[at]);
            var takes = Takes[verb];
            if (words.Count - at - 1 < takes)
            {
                throw new ProbeFailed(
                    $"\"{words[at]}\" wants {takes} word(s) after it and the script ends there.");
            }

            script.Add(new Instruction(
                verb,
                words[at + 1],
                takes == 2 ? words[at + 2] : string.Empty));

            at += takes + 1;
        }

        return script;
    }

    private static Verb VerbIn(string word)
    {
        // Not TryParse alone: it also accepts the numbers behind the names, so a mis-counted
        // script whose element happens to be "2" would silently become a different verb.
        if (word.Length > 0
            && !char.IsAsciiDigit(word[0])
            && Enum.TryParse<Verb>(word, ignoreCase: true, out var verb)
            && Takes.ContainsKey(verb))
        {
            return verb;
        }

        throw new ProbeFailed(
            $"\"{word}\" is not something a probe does. It is one of: "
            + $"{string.Join(", ", Takes.Keys.Select(one => one.ToString().ToLowerInvariant()))}.");
    }

    public override string ToString() =>
        $"{Verb.ToString().ToLowerInvariant()} {Subject}"
        + (Detail.Length > 0 ? $" {Detail}" : string.Empty);
}
