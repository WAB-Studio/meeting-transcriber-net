using System.Runtime.InteropServices;

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Windows saying that this machine is not offering what it was: a device arrived, one went, one
/// was switched off or on, or the default moved.
/// </summary>
/// <remarks>
/// <para>
/// It says only that something changed and never what. What there is now is
/// <see cref="AudioDevices.Microphones"/> and <see cref="AudioDevices.Playback"/>, which is an
/// answer with a deadline on it; carrying the id out of a callback would be a second, thinner
/// answer to the same question, and a screen holding one would eventually disagree with the list
/// under it. So the whole of what crosses this seam is: ask again.
/// </para>
/// <para>
/// This is what a picker that keeps up with the machine is made of, and it replaces the shape
/// somebody reaches for first — a timer. A screen asking every second what this machine has is
/// four questions a second nobody answered differently, each of them a synchronous call into the
/// audio service; and it is still a second late. It is also the only way to notice the machine
/// moving what it plays through, which is what says whether the room is hearing the other side of
/// the meeting twice.
/// </para>
/// <para>
/// One event and not one per kind of change, because Windows says one physical thing several ways:
/// a headset going in is an endpoint added, a state changed and a default moved, and each of them
/// means the same thing here. Telling them apart would only let a caller ask the same question
/// three times, which is what a caller has to collapse anyway.
/// </para>
/// <para>
/// The callback arrives on whatever thread the audio service uses, which is never the one a window
/// draws on. Marshalling it is the caller's: this raises <see cref="Changed"/> where it was told,
/// and a screen is the thing that knows what a dispatcher is.
/// </para>
/// <para>
/// Registering is bounded like every other question this application puts to the audio stack, for
/// the reason <see cref="DeviceEnquiry"/> gives: it is <c>CoCreateInstance</c> on a service that
/// can be wedged, made from a window's constructor while somebody is looking at it. What a
/// registration given up on costs is what <see cref="DeviceOpen"/> already documents for a device:
/// the thread may come back a moment later and register after all, and what it registered is then
/// something nothing out here can reach or unregister for the life of the process. Nothing reaches
/// a screen from it — no caller ever received this object to subscribe to it — and it is the same
/// bargain a device given up on already makes.
/// </para>
/// <para>
/// What no probe in <c>dotnet test</c> reaches is any of it: a build agent has no audio endpoint to
/// arrive or go, and nothing here can make one. Nothing else can either — switching an endpoint off
/// to stand in for unplugging one needs an elevated shell — so the claim this exists for stays open
/// until somebody plugs a microphone in on a packaged build.
/// </para>
/// </remarks>
public sealed class DeviceChanges : IMMNotificationClient, IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;

    /// <summary>
    /// Whether this has been let go of. It narrows the window in which a notification already on
    /// its way in reaches a caller that has gone, and it does not close it: the audio service's
    /// thread can pass this test and be pre-empted before it raises. What actually guarantees
    /// nothing reaches a closed window is the window's own answer to the same question, and this
    /// is here because it costs one read and makes letting go mean something at once.
    /// </summary>
    private volatile bool _closed;

    private DeviceChanges(MMDeviceEnumerator enumerator) => _enumerator = enumerator;

    /// <summary>
    /// This machine's devices are not what they were. Raised on the audio service's thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// Starts being told, or throws what every question to a wedged audio stack throws.
    /// </summary>
    /// <exception cref="AudioCaptureException">Windows refused, or never answered.</exception>
    public static DeviceChanges Listening() =>
        DeviceEnquiry.Answering(DeviceQuestion.DeviceChanges, () =>
        {
            var enumerator = new MMDeviceEnumerator();
            var listening = new DeviceChanges(enumerator);

            try
            {
                Refused(enumerator.RegisterEndpointNotificationCallback(listening));
            }
            catch
            {
                enumerator.Dispose();
                throw;
            }

            return listening;
        });

    /// <summary>Stops being told, and stops saying so whether or not that goes through.</summary>
    /// <remarks>
    /// The flag first and on this thread, because it is what a window closing actually needs. What
    /// follows is the audio service again — the same service that may be why anybody is closing the
    /// window — so it goes on a thread of its own and nothing waits for it. Bounding it instead
    /// would be five seconds of a window that will not close, which is the freeze
    /// <see cref="DeviceEnquiry"/> exists to end, moved to the one moment nobody is waiting for an
    /// answer; and there is nothing to do with the answer either way, since the last thing that
    /// could have read it is the window that has gone.
    /// </remarks>
    public void Dispose()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        var enumerator = _enumerator;
        var listening = this;

        new Thread(() =>
        {
            try
            {
                enumerator.UnregisterEndpointNotificationCallback(listening);
                enumerator.Dispose();
            }
            catch (COMException)
            {
                // Nowhere to say it: what would have read it is the window that has gone. The
                // enumerator is let go of by the process ending either way, and nothing this
                // object raises reaches anybody, because the flag above is already set.
            }
        })
        {
            IsBackground = true,
            Name = "no longer being told when this machine's devices change",
        }.Start();
    }

    /// <summary>A device was switched off, switched on, or otherwise changed what it is.</summary>
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Say();

    /// <summary>A device arrived — the microphone somebody just plugged in.</summary>
    public void OnDeviceAdded(string pwstrDeviceId) => Say();

    /// <summary>A device went.</summary>
    public void OnDeviceRemoved(string deviceId) => Say();

    /// <summary>
    /// Windows moved what it records or plays through by default — which is what plugging a headset
    /// in does, without any device arriving or going.
    /// </summary>
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Say();

    /// <summary>
    /// A device changed one of its properties, which is not a change to what this machine offers.
    /// </summary>
    /// <remarks>
    /// Deliberately silent. This fires for a volume, an icon and a driver writing to its own
    /// property store, none of which changes what is in a picker — and it fires often enough that
    /// answering it would put the enumeration this exists to stop back, keyed on noise instead of
    /// on a clock.
    /// </remarks>
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key)
    {
    }

    /// <summary>
    /// Turns the registration's own answer into the refusal every other question to the audio
    /// stack throws, so a screen has one thing to catch. It hands back an HRESULT where the rest
    /// of this library throws, and a code nobody looked at is a picker that quietly never updates.
    /// </summary>
    private static void Refused(int hresult)
    {
        if (hresult >= 0)
        {
            return;
        }

        var said = Marshal.GetExceptionForHR(hresult);

        throw new AudioCaptureException(
            $"Windows would not say when this machine's devices change: {said?.Message ?? $"0x{hresult:X8}"}",
            said ?? new COMException(null, hresult));
    }

    private void Say()
    {
        if (!_closed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }
}
