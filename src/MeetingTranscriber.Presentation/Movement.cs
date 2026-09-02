namespace MeetingTranscriber.Presentation;

/// <summary>
/// The things that move for a length of time, as <c>docs/design.md</c> §What moves names them.
/// Closed, because that document's whole point about motion is that it answers two questions and a
/// screen moving for any other reason has not decided what it means: a fourth kind of movement is a
/// decision somebody takes and writes on that page, not something a screen invents.
/// <para>
/// Three where that table has four rows. The fourth is a meter's level falling back, which the page
/// writes as a rate — 20 dB in a second and a half — rather than as a duration, so it is not one of
/// the durations that go to zero and it is not this table's to hand out.
/// </para>
/// </summary>
public enum Move
{
    /// <summary>A control answering the press — fill, ring, tick.</summary>
    AnsweringAPress,

    /// <summary>Something entering or leaving — a row, a notice, a clip.</summary>
    EnteringOrLeaving,

    /// <summary>The meetings drawer, and a dialogue arriving.</summary>
    Travelling,
}

/// <summary>
/// How long each of the four takes, on a machine that has been asked for animation and on one that
/// has been asked for none.
/// </summary>
/// <remarks>
/// Here rather than beside a window, and rather than as durations in the resource dictionary, for
/// two reasons that point the same way. A <c>Duration</c> in the dictionary is fixed when the
/// application starts and there is nothing in it that can be zero on one machine and 300 ms on the
/// next, so obeying the platform would have to happen somewhere else anyway — and then the
/// dictionary would be a second place saying how long a move takes. And this is the half of the
/// rule a build agent can run: <c>MeetingTranscriber.App</c> fires the Windows App SDK's module
/// initializer the moment a type from it is touched, so a rule living there would be a rule proved
/// on one machine by hand. What Windows was asked for arrives as a constructor argument, which is
/// what lets a test drive both answers rather than the one the machine it happens to run on gives.
/// <para>
/// <c>docs/layout.md</c> puts a screen's rules in the project its subject lives in, and this one
/// has no subject in the corpus: it is not about a recording or a meeting, it is one rule every
/// screen obeys. That is the same thing that makes this project right for it — it is the one that
/// exists so what a screen is made of can be proved without a window, references nothing, and
/// targets plain <c>net10.0</c>.
/// </para>
/// <para>
/// Whole milliseconds as an <c>int</c>, not a <c>TimeSpan</c>: this project references nothing, so
/// <c>Domain/Time</c>'s <c>Duration</c> — which is itself whole milliseconds — cannot reach it, and
/// a bare <c>TimeSpan</c> is what that type exists to keep out of the codebase. The unit is in the
/// name instead.
/// </para>
/// </remarks>
/// <param name="ThePlatformAllowsAnimation">
/// What Windows was asked for. <c>false</c> is the setting for people who need the screen to stay
/// still, and it is obeyed rather than weighed against anything.
/// </param>
public sealed record Movement(bool ThePlatformAllowsAnimation)
{
    /// <summary>
    /// How long <paramref name="move"/> takes, in whole milliseconds. Zero on a machine asked for
    /// no animation — which is the whole of obeying it, because a move of no length still ends
    /// where it was going and so loses nothing by not being watchable.
    /// </summary>
    public int Milliseconds(Move move) => ThePlatformAllowsAnimation ? AtFullLength(move) : 0;

    /// <summary>
    /// The lengths on <c>docs/design.md</c> §What moves. The meter's fall is not among them on
    /// purpose: that page writes it as a rate — 20 dB in a second and a half — rather than a
    /// duration, so it is not one of the durations that go to zero, and it belongs to the meter
    /// rather than to this table.
    /// </summary>
    private static int AtFullLength(Move move) => move switch
    {
        Move.AnsweringAPress => 150,
        Move.EnteringOrLeaving => 250,
        Move.Travelling => 300,
        _ => throw new ArgumentOutOfRangeException(nameof(move)),
    };
}
