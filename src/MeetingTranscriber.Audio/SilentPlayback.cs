using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Silence played into the endpoint being recorded, for as long as it is being recorded.
/// </summary>
/// <remarks>
/// <para>
/// Loopback captures what an endpoint is playing, and an endpoint that is playing nothing hands
/// over nothing at all — not silence, no packets. Channel 0 would then hold only the stretches
/// something happened to be playing, spliced end to end: a meeting where nobody shared their
/// screen for ten minutes comes back ten minutes short, with everything after it moved earlier
/// and nothing in the file saying so. That is a worse failure than a missing recording, because
/// it looks like a recording.
/// </para>
/// <para>
/// Something silent playing is what keeps the endpoint handing packets over, and silence mixed
/// into what the machine plays changes nothing about what anybody hears.
/// </para>
/// </remarks>
internal sealed class SilentPlayback : IDisposable
{
    private readonly MMDevice endpoint;
    private readonly WasapiOut output;

    private SilentPlayback(MMDevice endpoint, WasapiOut output)
    {
        this.endpoint = endpoint;
        this.output = output;
    }

    /// <summary>Starts silence on <paramref name="device"/> and keeps it going until disposed.</summary>
    public static SilentPlayback On(AudioDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        // Its own handle on the endpoint. NAudio hangs one audio client off an MMDevice and hands
        // the same one out again, so sharing the capture's device here would have the two of them
        // initialising a single client for opposite directions.
        var endpoint = AudioDevices.Open(device);
        WasapiOut? output = null;
        try
        {
            output = new WasapiOut(endpoint, AudioClientShareMode.Shared, useEventSync: false, latency: 100);
            output.Init(new SilenceProvider(endpoint.AudioClient.MixFormat));
            output.Play();
            return new SilentPlayback(endpoint, output);
        }
        catch
        {
            output?.Dispose();
            endpoint.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        output.Dispose();
        endpoint.Dispose();
    }
}
