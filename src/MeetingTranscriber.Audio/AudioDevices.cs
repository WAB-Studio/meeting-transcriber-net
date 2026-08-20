using System.Runtime.InteropServices;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What this machine offers to record from, and how a name somebody typed becomes one of them.
/// </summary>
/// <remarks>
/// <para>
/// Enumeration and choice are separate on purpose: which endpoints exist is the machine's answer
/// and changes when somebody unplugs a headset, while which one was meant is a rule, and a rule
/// gets tested. <see cref="Choose"/> is that rule and it touches no device.
/// </para>
/// <para>
/// Which is also why only one of the two is bounded. The two questions asked here on nobody else's
/// behalf — the microphones and the playback endpoint — go through <see cref="DeviceEnquiry"/>,
/// because either can sit inside a stuck audio service for as long as that service likes. The rule
/// waits on nothing and needs no deadline, and the two questions asked from inside somebody else's
/// deadline are the subject of the remark on <see cref="Ask"/>.
/// </para>
/// </remarks>
public static class AudioDevices
{
    /// <summary>
    /// Every microphone Windows currently has active, or a refusal when it will not say within the
    /// deadline every wait on this machine's audio shares.
    /// </summary>
    public static IReadOnlyList<AudioDevice> Microphones() =>
        DeviceEnquiry.Answering("the microphones on this machine", () => Endpoints(DataFlow.Capture));

    /// <summary>
    /// The endpoint the machine is playing through. There is exactly one at a time: it is where
    /// Windows sends sound, not a choice this application makes.
    /// </summary>
    /// <remarks>
    /// Nothing records it. Channel 0 is what this machine's processes play and comes off no
    /// endpoint, so what this is for is what an endpoint can still say about a meeting: whether
    /// what is played comes out into the room, where the microphone hears it a second time.
    /// <para>
    /// Bounded like the list of microphones, and it is the one asked most often: a meeting on
    /// screen asks it once a second, which is what <see cref="DeviceEnquiry"/> is written against.
    /// </para>
    /// </remarks>
    public static AudioDevice Playback() =>
        DeviceEnquiry.Answering("the device this machine plays through", PlayingThrough);

    private static AudioDevice PlayingThrough() => Ask(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console))
        {
            throw new AudioCaptureException(
                "Windows names no playback device, so there is nothing for channel 0 to listen to.");
        }

        using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        return Described(endpoint, isDefault: true);
    });

    /// <summary>
    /// The device <paramref name="wanted"/> names, out of the ones there are. A name matches
    /// whole or in part and ignores case, because the names Windows builds are long and a person
    /// types the word they recognise; two devices answering to the same word is refused rather
    /// than resolved, since picking one of them is picking somebody's microphone for them.
    /// </summary>
    /// <param name="devices">What there is to choose from.</param>
    /// <param name="wanted">A name, an id, or nothing at all to take the default.</param>
    public static AudioDevice Choose(IReadOnlyList<AudioDevice> devices, string? wanted)
    {
        ArgumentNullException.ThrowIfNull(devices);

        if (devices.Count == 0)
        {
            throw new AudioCaptureException("This machine has no active microphone.");
        }

        if (string.IsNullOrWhiteSpace(wanted))
        {
            return Only(devices.Where(device => device.IsDefault).ToArray(), devices);
        }

        var named = devices
            .Where(device =>
                device.Id.Equals(wanted, StringComparison.OrdinalIgnoreCase)
                || device.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (named.Length == 0)
        {
            named = devices
                .Where(device => device.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        return named.Length switch
        {
            1 => named[0],
            0 => throw new AudioCaptureException(
                $"No microphone here is called '{wanted}'. There is {Names(devices)}."),
            _ => throw new AudioCaptureException(
                $"'{wanted}' names {named.Length} microphones: {Names(named)}. Say which by its id."),
        };
    }

    /// <summary>
    /// The format the audio engine is mixing at, which is the format everything being played is
    /// already in. Asked of the playback endpoint because that is where the mix goes; channel 0 has
    /// no endpoint of its own to ask, either way it is obtained.
    /// </summary>
    internal static WaveFormat EngineFormat() => Ask(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console))
        {
            throw new AudioCaptureException(
                "Windows names no playback device, so nothing says what the machine is mixing at.");
        }

        using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        using var client = endpoint.AudioClient;
        return client.MixFormat;
    });

    /// <summary>Reopens a device the machine described earlier, ready to be captured from.</summary>
    internal static MMDevice Open(AudioDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);

        return Ask(() =>
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.GetDevice(device.Id);
        });
    }

    private static AudioDevice Only(AudioDevice[] standard, IReadOnlyList<AudioDevice> devices) =>
        standard.Length switch
        {
            1 => standard[0],
            0 => throw new AudioCaptureException(
                $"Windows names no default microphone. Name one of {Names(devices)}."),
            _ => throw new AudioCaptureException(
                $"{standard.Length} microphones call themselves the default: {Names(standard)}. Name one."),
        };

    private static IReadOnlyList<AudioDevice> Endpoints(DataFlow flow) => Ask<IReadOnlyList<AudioDevice>>(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        using MMDevice? standard = enumerator.HasDefaultAudioEndpoint(flow, Role.Console)
            ? enumerator.GetDefaultAudioEndpoint(flow, Role.Console)
            : null;
        var standardId = standard?.ID;

        return
        [
            .. enumerator
                .EnumerateAudioEndPoints(flow, DeviceState.Active)
                .Select(endpoint =>
                {
                    using (endpoint)
                    {
                        return Described(endpoint, endpoint.ID == standardId);
                    }
                }),
        ];
    });

    /// <summary>
    /// One endpoint as this application holds it: what it is called, what reopens it, and what it
    /// says it is.
    /// </summary>
    /// <remarks>
    /// The kind is asked for here, where the endpoint is already open, rather than looked up again
    /// later off an id — a device somebody unplugged between the two would answer the second
    /// question with a throw on a path that had nothing to do with it.
    /// </remarks>
    private static AudioDevice Described(MMDevice endpoint, bool isDefault) =>
        new(endpoint.ID, endpoint.FriendlyName, isDefault) { Kind = KindOf(endpoint) };

    /// <summary>
    /// What an endpoint declares itself to be, or <see cref="EndpointKind.Unsaid"/> when it holds
    /// no such answer.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The property is optional and the store hands back whatever type the driver wrote, so the
    /// absence, a value that is not a number, and a value nothing here can even read all come back
    /// as nothing having been said. This feeds a warning beside a meter: an endpoint that would not
    /// answer is a line not shown, and never a device that will not open.
    /// </para>
    /// <para>
    /// The catch is around the read and not around the value, which is where the guard has to be:
    /// reading a property of a type the library does not map throws out of the read itself, before
    /// anything here can look at what came back. It is not a <see cref="COMException"/>, so it goes
    /// past the wrapping every other question to the audio stack gets — and what it would take down
    /// is the window's constructor, over a line beside a meter.
    /// </para>
    /// </remarks>
    private static EndpointKind KindOf(MMDevice endpoint)
    {
        try
        {
            var properties = endpoint.Properties;
            if (!properties.Contains(PropertyKeys.PKEY_AudioEndpoint_FormFactor))
            {
                return EndpointKind.Unsaid;
            }

            // Windows writes it as a four byte unsigned number, and a driver is free to write a
            // signed one. Both widths are read; a number too big to be an int comes out negative,
            // which is one Windows never named and so is nothing having been said.
            return properties[PropertyKeys.PKEY_AudioEndpoint_FormFactor].Value switch
            {
                uint declared => EndpointKinds.Of(unchecked((int)declared)),
                int declared => EndpointKinds.Of(declared),
                _ => EndpointKind.Unsaid,
            };
        }
        catch (Exception unreadable) when (unreadable is COMException or NotImplementedException or ArgumentException)
        {
            return EndpointKind.Unsaid;
        }
    }

    /// <summary>
    /// Anything the audio stack refuses, said as an answer rather than as an HRESULT. A machine
    /// whose audio service is not running throws from the first call, and "0x80070490" is not
    /// something anybody acts on.
    /// </summary>
    /// <remarks>
    /// A refusal and never a deadline, though it is the one funnel every question in this file goes
    /// through and so the obvious place to put one. <see cref="Open"/> and <see cref="EngineFormat"/>
    /// are called from inside a <see cref="DeviceOpen"/> ask that is already counting, and a
    /// deadline here would be a second one over the same device — which is the thing a device
    /// costing exactly one deadline exists to prevent. The two questions that are nobody else's
    /// ask are bounded where they are asked instead, and a third one added here is bounded the
    /// same way: at the method that asks it, and never at this funnel. Nothing goes red if it is
    /// not, which is the one thing this arrangement cannot enforce.
    /// </remarks>
    private static T Ask<T>(Func<T> question)
    {
        try
        {
            return question();
        }
        catch (COMException refused)
        {
            throw new AudioCaptureException(
                $"Windows would not answer about its audio devices: {refused.Message}", refused);
        }
    }

    private static string Names(IReadOnlyList<AudioDevice> devices) =>
        string.Join(", ", devices.Select(device => $"'{device.Name}'"));
}
