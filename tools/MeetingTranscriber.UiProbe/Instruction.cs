namespace MeetingTranscriber.UiProbe;

/// <summary>
/// How a command line spells what a probe can do to a running application, plus the two things it
/// does around one.
/// </summary>
/// <remarks>
/// Most of the things themselves are <see cref="Session"/>'s; this is one host's vocabulary for
/// them, and the server has its own because the two do not line up — a <c>see</c> here names the
/// pair of files it writes, and a <c>see</c> there names nothing. Generating one from the other
/// would have meant one special case for every verb, which is not an abstraction removing a
/// decision. <see cref="CommandLine.Do"/> throws on a verb it does not handle, so the drift that
/// costs is loud rather than a script that runs and does nothing.
/// <para>
/// Closed on purpose, and small on purpose. Everything a screen is checked for is some
/// arrangement of look, press, fill in and pick from a list, and a verb beyond those has to argue
/// that no arrangement of them would have done. Two do, and both are about a recorder rather than
/// about a screen: <see cref="Sleep"/>, because a meeting's screen is a function of elapsed real
/// time and nothing that reads a screen makes ninety seconds pass — <see cref="Wait"/> is bounded
/// at fifteen seconds and returns on the first frame that matches, which is the opposite of
/// holding one; and <see cref="Kill"/>, because what a crash leaves behind cannot be reached by
/// asking an application to shut down.
/// </para>
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

    /// <summary>Let a given number of seconds pass, touching nothing.</summary>
    Sleep,

    /// <summary>End the application the way a crash does, rather than asking it to close.</summary>
    Kill,
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
        [Verb.Sleep] = 1,
        [Verb.Kill] = 0,
    };

    /// <summary>
    /// The longest one <c>sleep</c> may be. A probe that holds a screen is watching a meeting run,
    /// and every meeting anybody records to look at a recorder is minutes rather than hours — so a
    /// number past this is a typo, and a typo here costs a run nobody is watching.
    /// </summary>
    private static readonly TimeSpan LongestSleep = TimeSpan.FromMinutes(20);

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

            var step = new Instruction(
                verb,
                takes >= 1 ? words[at + 1] : string.Empty,
                takes == 2 ? words[at + 2] : string.Empty);

            if (verb is Verb.Sleep)
            {
                // Read here and not where it is slept, so a script with a typo in it is refused
                // before an application is started rather than halfway through a meeting.
                _ = step.Held;
            }

            script.Add(step);

            at += takes + 1;
        }

        return script;
    }

    /// <summary>How long a <c>sleep</c> is, off the word after it.</summary>
    internal TimeSpan Held =>
        double.TryParse(Subject, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
        // The bounds are read off the number and not off a TimeSpan made from it: `TryParse` takes
        // "Infinity", and `TimeSpan.FromSeconds` of that throws out of here as the probe breaking
        // rather than as the script being wrong, which is what it is.
        && seconds >= 0
        && seconds <= LongestSleep.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : throw new ProbeFailed(
                $"\"{Subject}\" is not a number of seconds between 0 and "
                + $"{LongestSleep.TotalSeconds:0} to hold a screen for.");

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
        Verb.ToString().ToLowerInvariant()
        + (Subject.Length > 0 ? $" {Subject}" : string.Empty)
        + (Detail.Length > 0 ? $" {Detail}" : string.Empty);
}
