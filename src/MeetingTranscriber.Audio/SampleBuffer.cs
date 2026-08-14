namespace MeetingTranscriber.Audio;

/// <summary>
/// Samples one source has produced and the timeline has not handed on yet, in the order they
/// happened.
/// </summary>
/// <remarks>
/// It exists because the two sources are never level with each other: one packet arrives, is
/// resampled and is ready, and nothing can be written until the other source has covered the same
/// stretch. What waits here is that difference and nothing more — tens of milliseconds while both
/// sources are running — so it stays small without anything having to bound it.
/// </remarks>
internal sealed class SampleBuffer
{
    private short[] items = new short[4096];
    private int start;

    /// <summary>How many samples are waiting.</summary>
    internal int Count { get; private set; }

    /// <summary>Puts <paramref name="samples"/> at the end of the queue.</summary>
    internal void Add(ReadOnlySpan<short> samples)
    {
        if (samples.IsEmpty)
        {
            return;
        }

        Compact(samples.Length);
        samples.CopyTo(items.AsSpan(start + Count));
        Count += samples.Length;
    }

    /// <summary>
    /// Fills <paramref name="into"/> with the oldest samples and drops them. Asking for more than
    /// is there is a mistake in the caller's arithmetic rather than a short read.
    /// </summary>
    internal void Take(Span<short> into)
    {
        if (into.Length > Count)
        {
            throw new AudioCaptureException(
                $"{into.Length} samples were asked of a buffer holding {Count}.");
        }

        items.AsSpan(start, into.Length).CopyTo(into);
        start += into.Length;
        Count -= into.Length;

        if (Count == 0)
        {
            start = 0;
        }
    }

    /// <summary>Makes room for <paramref name="room"/> more samples after what is queued.</summary>
    private void Compact(int room)
    {
        if (start + Count + room <= items.Length)
        {
            return;
        }

        if (Count + room <= items.Length)
        {
            items.AsSpan(start, Count).CopyTo(items);
            start = 0;
            return;
        }

        var grown = new short[Math.Max(items.Length * 2, Count + room)];
        items.AsSpan(start, Count).CopyTo(grown);
        items = grown;
        start = 0;
    }
}
