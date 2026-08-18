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
/// <para>
/// Letting the gaps happen and writing each block's device position and clock timestamp, so the
/// timeline can put them back, is not the alternative to this — it is arriving anyway, because
/// drift is measured from those timestamps and a spool recovers by them. The two do different
/// jobs: the timestamps say where what arrived belongs, and this is what makes something arrive.
/// </para>
/// </remarks>
internal sealed class SilentPlayback : IDisposable
{
    private readonly MMDevice endpoint;
    private readonly WasapiOut output;
    private DeviceRelease? release;

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
            // Bounded like the release below it, and for the same reason: this runs while opening
            // the endpoint is already failing, and a driver wedged in either of these two would
            // hang a recording that has not started yet.
            DeviceRelease.LetGoOf("loopback silence", () =>
            {
                try
                {
                    output?.Dispose();
                }
                finally
                {
                    endpoint.Dispose();
                }
            });

            throw;
        }
    }

    /// <summary>
    /// Stops the silence and lets the endpoint go, and comes back whether or not the device
    /// answered.
    /// </summary>
    /// <remarks>
    /// Bounded for the same reason a captured device's release is, and it is the last of the three
    /// waits on the way out of a recording. This is a second handle on the endpoint channel 0 is
    /// recording, played into by a device thread of NAudio's own, so it is the same driver and the
    /// same way of not coming back — and it is reached after both sources have been let go of,
    /// which is where a meeting that is already on disk would be held up by a device nobody is
    /// listening to any more.
    /// </remarks>
    public void Dispose()
    {
        // Built once and waited on again, so a second call costs nothing and a release that came
        // back after its deadline still frees what it was holding.
        release ??= DeviceRelease.Of("loopback silence release", () =>
        {
            // A finally, so a playback that refuses to close still leaves its endpoint let go of:
            // the two are separate handles and only one of them is what refused. A playback that
            // wedges instead never reaches it, which is the same rule read the other way — nothing
            // a live thread is inside is anybody's to close.
            try
            {
                output.Dispose();
            }
            finally
            {
                endpoint.Dispose();
            }
        });

        release.Dispose();
    }
}
