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

        // Behind one deadline, like the two ways a capture opens and like the release below: all of
        // it is synchronous COM into the same driver, and a wedge in any of it is an application
        // that freezes at the moment somebody pressed record rather than one that will not close.
        return DeviceOpen.Answering("loopback silence", () =>
        {
            // Its own handle on the endpoint, so that letting this go and letting the capture go
            // are two decisions rather than one: the capture opens the same device for the opposite
            // direction and disposes it on its own schedule, and one MMDevice between them would be
            // a handle closed under whichever of the two was still using it.
            var endpoint = AudioDevices.Open(device);
            WasapiOut? output = null;
            try
            {
                // Asked for, read once and let go of. Every ask activates a client of its own —
                // NAudio holds no reference to it and an MMDevice disposed later takes none of them
                // with it — so one read this way and left behind is a handle on the machine's
                // playback that nothing frees until the process ends.
                WaveFormat mixing;
                using (var mixer = endpoint.AudioClient)
                {
                    mixing = mixer.MixFormat;
                }

                output = new WasapiOut(endpoint, AudioClientShareMode.Shared, useEventSync: false, latency: 100);
                output.Init(new SilenceProvider(mixing));
                output.Play();
                return new SilentPlayback(endpoint, output);
            }
            catch
            {
                // The playback first and the endpoint under it, in the order Dispose uses: the two
                // are separate handles, so one that refuses to close still leaves the other let go
                // of, and one that wedges never reaches the line below it.
                DeviceRelease.LetGoOf(output);
                DeviceRelease.LetGoOf(endpoint);

                throw;
            }
        });
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
            // A playback that refuses to close still leaves its endpoint let go of: the two are
            // separate handles and only one of them is what refused. A playback that wedges instead
            // never reaches the line below, which is the same rule read the other way — nothing a
            // live thread is inside is anybody's to close.
            DeviceRelease.LetGoOf(output);
            DeviceRelease.LetGoOf(endpoint);
        });

        release.Dispose();
    }
}
