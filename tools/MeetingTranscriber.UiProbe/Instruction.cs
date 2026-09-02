using System.IO;

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
/// arrangement of these — get in, look, press, fill in, pick from a list — and a verb beyond the
/// five has to argue that no arrangement of them would have done. Two do, and neither is about a
/// screen at all: <see cref="Sleep"/>, because a meeting's screen is a function of elapsed real
/// time and nothing that reads a screen makes ninety seconds pass — <see cref="Wait"/> is bounded
/// at fifteen seconds and returns on the first frame that matches, which is the opposite of
/// holding one; and <see cref="Kill"/>, because what a crash leaves behind cannot be reached by
/// asking an application to shut down.
/// </para>
/// <para>
/// <see cref="Sleep"/> is not a way to wait for something to happen and <see cref="Wait"/> is:
/// a screen that will change is waited for, and only a screen that changes by the second is held.
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
/// <remarks>
/// This is the one part of the probe a build agent could run — reading words into steps opens no
/// window and needs no desktop — and it is nonetheless held by the reasoning written on
/// <see cref="Read"/> and <see cref="Seconds"/> and by nothing that goes red. That is not an
/// oversight to be fixed by adding a test project beside it: `docs/layout.md` says nothing under
/// `tests/` may come to depend on `tools/`, and the bright line is what stops a test in such a
/// project from reaching <see cref="Session.Open"/>, which no build agent can run. Narrowing that
/// rule to the parts that need a desktop is a decision about how this repository is laid out, and
/// it is worth taking — the walks this tool produces are the most expensive evidence in the repo —
/// but it is that decision and not a test.
/// </remarks>
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

            // Every check is here rather than where the step runs, and for one reason: a script
            // with a mistake in it is refused before an application is started, rather than after
            // minutes of real recording that then have to happen again. The see name was the one
            // left behind — it was held to being a file name inside the walk, so `see a/b` at the
            // end of a six-minute script cost the six minutes to say so.
            if (verb is Verb.Sleep)
            {
                _ = Seconds(words[at + 1]);
            }

            if (verb is Verb.See)
            {
                _ = Named(words[at + 1]);
            }

            if (verb is Verb.Kill && at + 1 < words.Count)
            {
                throw new ProbeFailed(
                    "\"kill\" ends the application, so nothing after it can be done to a screen: "
                    + $"\"{words[at + 1]}\" follows it. Put kill last, and read what it left "
                    + "behind with a second run.");
            }

            script.Add(new Instruction(
                verb,
                takes >= 1 ? words[at + 1] : string.Empty,
                takes == 2 ? words[at + 2] : string.Empty));

            at += takes + 1;
        }

        return script;
    }

    /// <summary>How long a <c>sleep</c> is, off the word written after it.</summary>
    /// <remarks>
    /// Static, and not a property of an instruction: five of the seven verbs take no number, so a
    /// member every instruction carries and six of them throw from is a member that lies about
    /// what an instruction is.
    /// </remarks>
    internal static TimeSpan Seconds(string word) =>
        double.TryParse(word, System.Globalization.CultureInfo.InvariantCulture, out var seconds)
        // The bounds are read off the number and not off a TimeSpan made from it: `TryParse` takes
        // "Infinity", and `TimeSpan.FromSeconds` of that throws out of here as the probe breaking
        // rather than as the script being wrong, which is what it is.
        && seconds >= 0
        && seconds <= LongestSleep.TotalSeconds
            ? TimeSpan.FromSeconds(seconds)
            : throw new ProbeFailed(
                $"\"{word}\" is not a number of seconds between 0 and "
                + $"{LongestSleep.TotalSeconds:0} to hold a screen for.");

    /// <summary>
    /// What a <c>see</c> is called, off the word written after it — the same word twice over, since
    /// a name becomes a <c>.tree.txt</c> and a <c>.png</c>. So it is held to being a file name and
    /// nothing else: a name with a path in it would write outside the folder it was given.
    /// </summary>
    internal static string Named(string name) =>
        name.Length > 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0
        && name is not ("." or "..")
            ? name
            : throw new ProbeFailed($"\"{name}\" is not a name a pair of files can be called.");

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
