using System.Diagnostics;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Waiting for a screen to catch up, without a fixed pause written into any instruction.
/// </summary>
/// <remarks>
/// A window opens, a list fills, a card is built — none of it at the moment the press returned.
/// The alternative is a fixed pause in every script, which is both slower than this on the runs
/// where nothing was slow and still too short on the run where something was.
/// <para>
/// A script can write a <c>sleep</c>, and that is not the pause this argues against: it holds a
/// screen that changes by the second rather than waiting for one to change at all. Everything
/// that is waiting for something to happen is still here, and a <c>sleep</c> standing where a
/// <c>wait</c> belongs is a script that will not reproduce on a slower machine.
/// </para>
/// </remarks>
internal static class Patience
{
    private static readonly TimeSpan Between = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Asks again until <paramref name="look"/> finds something or the budget runs out, and
    /// returns null when it ran out. The caller says what "nothing there" means, because only the
    /// caller knows how to word it.
    /// </summary>
    internal static T? Until<T>(TimeSpan budget, Func<T?> look)
        where T : class
    {
        var spent = Stopwatch.StartNew();
        while (true)
        {
            var found = look();
            if (found is not null)
            {
                return found;
            }

            if (spent.Elapsed >= budget)
            {
                return null;
            }

            Thread.Sleep(Between);
        }
    }
}
