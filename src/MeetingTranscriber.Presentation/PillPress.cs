namespace MeetingTranscriber.Presentation;

/// <summary>
/// Whether a press on a drop-down's pill closes the list it opened, or opens it.
/// </summary>
/// <remarks>
/// <c>long.MinValue</c> once stood for "never dismissed" and the elapsed time against it overflowed
/// to a number below the window, so every press on a fresh pill was read as the one that had just
/// dismissed the list and no pill opened under a pointer. That is why "never" is <c>null</c> here
/// and why the comparison cannot overflow for any tick.
/// </remarks>
public static class PillPress
{
    /// <summary>
    /// How long after the list was light-dismissed a press on the pill is still read as the press
    /// that dismissed it.
    /// </summary>
    /// <remarks>
    /// One press reaches two places: the popup, which closes, and the pill underneath it. The
    /// platform decides which hears it first. If the pill hears it second, it finds the list already
    /// closed and would open it again. 300 ms covers the two arrivals of one press and stops short of
    /// a second press that somebody means.
    /// </remarks>
    public static readonly TimeSpan JustDismissed = TimeSpan.FromMilliseconds(300);

    /// <summary>
    /// Whether this press closes the list, or is the press that already did, and so must not open it.
    /// </summary>
    /// <param name="open">Whether the list was open when the press came.</param>
    /// <param name="dismissedAt">
    /// <c>Environment.TickCount64</c> at the last light-dismiss that no press has answered yet, or
    /// <c>null</c> where there was none.
    /// </param>
    /// <param name="now"><c>Environment.TickCount64</c> at this press.</param>
    public static bool ClosesTheList(bool open, long? dismissedAt, long now)
    {
        if (open)
        {
            return true;
        }

        if (dismissedAt is not { } at || at > now)
        {
            return false;
        }

        // With at <= now the true gap is in [0, 2^64), and the difference of the two as unsigned
        // numbers is exactly that gap, so nothing overflows whatever the ticks are.
        return unchecked((ulong)now - (ulong)at) < (ulong)JustDismissed.TotalMilliseconds;
    }
}
