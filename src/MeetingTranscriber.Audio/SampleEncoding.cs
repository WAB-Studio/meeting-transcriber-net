namespace MeetingTranscriber.Audio;

/// <summary>How the bytes of one sample are to be read.</summary>
public enum SampleEncoding
{
    /// <summary>A signed integer, full scale at the widest value the width holds.</summary>
    Pcm = 1,

    /// <summary>A float, full scale at 1.0 and free to go past it.</summary>
    IeeeFloat = 2,
}
