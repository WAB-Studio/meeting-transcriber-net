using System.Runtime.InteropServices;

using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What this machine offers to record from, and how a name somebody typed becomes one of them.
/// </summary>
/// <remarks>
/// Enumeration and choice are separate on purpose: which endpoints exist is the machine's answer
/// and changes when somebody unplugs a headset, while which one was meant is a rule, and a rule
/// gets tested. <see cref="Choose"/> is that rule and it touches no device.
/// </remarks>
public static class AudioDevices
{
    /// <summary>Every microphone Windows currently has active.</summary>
    public static IReadOnlyList<AudioDevice> Microphones() => Endpoints(DataFlow.Capture);

    /// <summary>
    /// The endpoint the machine is playing through, which is the one full loopback listens to.
    /// There is exactly one at a time: it is where Windows sends sound, not a choice this
    /// application makes.
    /// </summary>
    public static AudioDevice Playback() => Ask(() =>
    {
        using var enumerator = new MMDeviceEnumerator();
        if (!enumerator.HasDefaultAudioEndpoint(DataFlow.Render, Role.Console))
        {
            throw new AudioCaptureException(
                "Windows names no playback device, so there is nothing for channel 0 to listen to.");
        }

        using var endpoint = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
        return new AudioDevice(endpoint.ID, endpoint.FriendlyName, IsDefault: true);
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
    /// already in. Asked of the playback endpoint because that is where the mix goes; a capture that
    /// follows a process has no endpoint of its own to ask.
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
                        return new AudioDevice(endpoint.ID, endpoint.FriendlyName, endpoint.ID == standardId);
                    }
                }),
        ];
    });

    /// <summary>
    /// Anything the audio stack refuses, said as an answer rather than as an HRESULT. A machine
    /// whose audio service is not running throws from the first call, and "0x80070490" is not
    /// something anybody acts on.
    /// </summary>
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
