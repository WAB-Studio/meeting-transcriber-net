namespace MeetingTranscriber.Recording;

/// <summary>
/// What the words under the meters are written from: the loudest each channel read since they were
/// last written, rewritten at most four times a second.
/// </summary>
/// <remarks>
/// <para>
/// The bar is drawn from every reading and falls on its own, but the words cannot be: a reading
/// empties the level, so the stretch between two words reads silent, and <em>nada</em> written from
/// it would stand over a conversation that is going on. So the words keep the loudest reading of a
/// quarter of a second (<see cref="ChannelReading.Loudest"/> decides which is louder) and are
/// rewritten when that has passed.
/// </para>
/// <para>
/// A rule in this project and not a field of the window, for the reason <see cref="RecorderScreen"/>
/// is: a build agent can run it. The quarter of a second is measured on a clock the caller hands in
/// and never counted in ticks, because a timer ticks late exactly when the screen is busy and a
/// count would stretch the stretch then.
/// </para>
/// </remarks>
public sealed class MeterWords
{
    /// <summary>How long the words keep one reading: as fast as a number under a bar can change and still be read.</summary>
    public const long WindowMilliseconds = 250;

    private IReadOnlyList<ChannelReading> _heard = [];
    private long _writtenAt;
    private bool _started;

    /// <summary>
    /// What the words were last written from, or nothing until the first quarter of a second of a
    /// meeting is over — when whoever draws them has only the reading just taken to go on.
    /// </summary>
    public IReadOnlyList<ChannelReading> Said { get; private set; } = [];

    /// <summary>
    /// Takes a reading, and says whether the words were rewritten by it.
    /// </summary>
    /// <param name="read">What each channel just read as.</param>
    /// <param name="nowMilliseconds">A monotonic clock, in milliseconds.</param>
    public bool Take(IReadOnlyList<ChannelReading> read, long nowMilliseconds)
    {
        ArgumentNullException.ThrowIfNull(read);

        if (!_started)
        {
            _started = true;
            _writtenAt = nowMilliseconds;
        }

        _heard =
        [
            .. read.Select(now => _heard.FirstOrDefault(before => before.Channel == now.Channel) is { } before
                ? ChannelReading.Loudest(before, now)
                : now),
        ];

        if (nowMilliseconds - _writtenAt < WindowMilliseconds)
        {
            return false;
        }

        Said = _heard;
        _heard = [];
        _writtenAt = nowMilliseconds;
        return true;
    }

    /// <summary>
    /// Drops everything, so the next meeting starts with nothing standing from the last one.
    /// </summary>
    public void Forget()
    {
        _heard = [];
        Said = [];
        _started = false;
    }
}
