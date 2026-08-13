namespace MeetingTranscriber.Audio;

/// <summary>
/// One source's meter: how loud it has been since somebody last looked, and how loud it has ever
/// been. Blocks arrive on the capture thread and readings are taken on whichever thread is
/// showing them, so both go through one lock.
/// </summary>
/// <remarks>
/// Reading the recent level empties it, which is what makes a meter a meter: what a person wants
/// to see is the last second, and a peak that was never cleared would show the loudest moment of
/// the meeting for the rest of it.
/// </remarks>
public sealed class SourceMeter
{
    private readonly Lock gate = new();
    private float recent;
    private float loudest;

    /// <summary>The loudest anything has been since the capture started.</summary>
    public LevelReading Loudest
    {
        get
        {
            lock (gate)
            {
                return new LevelReading(loudest);
            }
        }
    }

    /// <summary>Takes in what one block of samples turned out to be.</summary>
    public void Add(LevelReading block)
    {
        lock (gate)
        {
            recent = MathF.Max(recent, block.Peak);
            loudest = MathF.Max(loudest, block.Peak);
        }
    }

    /// <summary>The loudest block since the previous call, and starts the next stretch.</summary>
    public LevelReading Read()
    {
        lock (gate)
        {
            var reading = new LevelReading(recent);
            recent = 0;
            return reading;
        }
    }
}
