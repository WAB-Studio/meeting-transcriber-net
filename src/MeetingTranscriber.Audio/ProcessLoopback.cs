using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Opens what processes are playing rather than what a device is: one tree of them, or every
/// process on the machine bar this application's own tree.
/// </summary>
/// <remarks>
/// <para>
/// There is no endpoint behind this. Windows exposes a virtual device that is activated with the
/// processes to capture attached to the call, so nothing here goes through
/// <see cref="AudioDevices"/> — and the client it hands back cannot be asked what format it mixes
/// at either, which is why the caller says. Everything after that is an ordinary capture client,
/// which is the whole reason this file is small: it produces a client, and
/// <see cref="WasapiStream"/> drives it exactly as it drives a device's.
/// </para>
/// <para>
/// Windows offers include-this-tree and exclude-this-tree and nothing else, and those two are
/// exactly the two things channel 0 is ever asked for. A tree rather than a process suits following
/// one program, because the process a person points at owns the window and the audio comes out of a
/// child of it. Everything but this application's own tree is the whole machine.
/// </para>
/// <para>
/// The whole machine is obtained here rather than by putting a loopback on the playback endpoint,
/// and that is the difference this type exists for. An endpoint hands over nothing at all while
/// nothing is playing into it — not silence, no packets — so a meeting nobody played into for ten
/// minutes came back ten minutes short, with everything after it moved earlier. Keeping it awake
/// meant this application opening a playback stream and pushing silence into it for the length of
/// the meeting, which made recording the machine depend on being able to play through it: another
/// application holding the speakers in exclusive mode, a stuck audio service or a driver refusing
/// the format each cost channel 0 the stretches nobody happened to be playing through. Windows
/// documents what it hands a process loopback client whose targets are rendering nothing — silence
/// — so nothing has to be kept awake and no playback stream is opened.
/// </para>
/// <para>
/// What is still needed is the playback endpoint's <em>answer</em>: the virtual client will not say
/// what it mixes at, so the format is read off the default render endpoint before either mode is
/// activated, and a machine Windows names no playback device for has no channel 0. That is a
/// question asked of an endpoint rather than a stream played through one, which is the whole of the
/// difference — an application holding the speakers in exclusive mode refuses the second and not
/// the first, measured.
/// </para>
/// </remarks>
internal static class ProcessLoopback
{
    /// <summary>The build where <c>AUDIOCLIENT_ACTIVATION_TYPE_PROCESS_LOOPBACK</c> arrived.</summary>
    /// <remarks>
    /// Below it there is no such activation and the recording would find that out by failing. The
    /// application already declares a higher floor than this — its minimum is Windows 11, build
    /// 22000 — so on any machine it installs on this is true; what it earns is that the question is
    /// asked before a meeting starts rather than answered by a COM error in the middle of one, and
    /// that the unpackaged command line, which runs wherever .NET does, gets the same answer.
    /// Channel 0 is obtained this way whichever of the two it is, so below this build there is no
    /// channel 0 at all rather than one way in that is missing.
    /// </remarks>
    private const int FirstBuildWithIt = 20348;

    /// <summary>The device path that means "not a device: the processes in the parameters".</summary>
    private const string VirtualDevice = "VAD\\Process_Loopback";

    private const int ProcessLoopbackActivation = 1;
    private const int IncludeTargetProcessTree = 0;
    private const int ExcludeTargetProcessTree = 1;
    private const ushort VariantIsBlob = 65;

    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    /// <summary>Whether this Windows can capture what a process is playing at all.</summary>
    internal static bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstBuildWithIt);

    /// <summary>
    /// An audio client carrying what <paramref name="process"/> and its children are playing, not
    /// yet initialised. Throws <see cref="AudioCaptureException"/> when this machine will not give
    /// one, which stops the recording rather than widening it.
    /// </summary>
    internal static AudioClient Following(AudioProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        return Obtained((uint)process.Id, IncludeTargetProcessTree, $"the audio of {process}");
    }

    /// <summary>
    /// An audio client carrying everything this machine is playing, wherever it comes out, not yet
    /// initialised.
    /// </summary>
    /// <remarks>
    /// Every process on the machine except this application's own tree, which is the only way
    /// Windows has of being asked for all of it — and excluding our own is what it would say
    /// anyway, so nothing this application ever plays can land in somebody's meeting. It is not one
    /// endpoint's audio: a machine playing through speakers and a headset at once puts both in,
    /// where recording a device put in whichever of them Windows was calling the default.
    /// </remarks>
    internal static AudioClient EverythingThisMachinePlays() =>
        Obtained((uint)Environment.ProcessId, ExcludeTargetProcessTree, "this machine's audio");

    /// <summary>
    /// The activation both ways in are made through. <paramref name="what"/> is how the thing being
    /// captured is named in everything this can throw, and it is the caller's because only the
    /// caller knows whether a process id is the program being followed or the one being left out.
    /// </summary>
    private static AudioClient Obtained(uint processId, int tree, string what)
    {
        if (!IsAvailable)
        {
            throw new AudioCaptureException(
                $"This Windows is build {Environment.OSVersion.Version.Build}, and capturing "
                + $"{what} needs {FirstBuildWithIt} or later.");
        }

        // On a thread of our own, in the multi-threaded apartment, because the activation answers on
        // a callback: an apartment that delivers calls through a message pump would have to pump it,
        // and this waits instead of pumping. The window's thread is exactly such an apartment.
        AudioClient? client = null;
        ExceptionDispatchInfo? refused = null;

        var activating = new Thread(() =>
        {
            try
            {
                client = Activate(processId, tree, what);
            }
            catch (Exception no)
            {
                refused = ExceptionDispatchInfo.Capture(no);
            }
        })
        {
            IsBackground = true,
            Name = $"activating {what}",
        };

        activating.SetApartmentState(ApartmentState.MTA);
        activating.Start();
        activating.Join();

        // Thrown as it was caught, and never widened into a sentence about this machine's audio
        // in general. What a person acts on is which of the two channel 0 could not be opened as
        // and what Windows said about that, and an interop defect wrapped into something broader
        // would read as a machine that cannot record rather than as the bug it is.
        refused?.Throw();

        return client!;
    }

    private static AudioClient Activate(uint processId, int tree, string what)
    {
        var wanted = new ActivationParameters
        {
            Type = ProcessLoopbackActivation,
            TargetProcessId = processId,
            Mode = tree,
        };

        var parameters = Marshal.AllocHGlobal(Marshal.SizeOf<ActivationParameters>());
        var variant = Marshal.AllocHGlobal(Marshal.SizeOf<BlobVariant>());

        try
        {
            Marshal.StructureToPtr(wanted, parameters, fDeleteOld: false);
            Marshal.StructureToPtr(
                new BlobVariant
                {
                    Type = VariantIsBlob,
                    Size = Marshal.SizeOf<ActivationParameters>(),
                    Data = parameters,
                },
                variant,
                fDeleteOld: false);

            var handler = new Activation();
            var wantedInterface = AudioClientId;

            Marshal.ThrowExceptionForHR(
                ActivateAudioInterfaceAsync(VirtualDevice, in wantedInterface, variant, handler, out var attempt));

            if (!handler.Answered(CaptureLoop.StopsWithin))
            {
                // Left undisposed on purpose, and it is the one place here that leaks. The callback
                // may still be coming, and setting an event somebody has already disposed would
                // take the process down from a COM thread this code does not own.
                //
                // A wedge and not a refusal, which is the whole of what this type says about it:
                // a refusal is an answer, and this is the case where none came — so it is reported
                // as the one thing nothing here can do anything about rather than as Windows having
                // said no. The deadline is the one every wait on a device shares, so this and the
                // ask around it are the same number rather than two that could disagree.
                throw AudioDeviceWedgedException.NoAnswerFrom(what);
            }

            handler.Dispose();

            try
            {
                attempt.GetActivateResult(out var result, out var activated);
                Marshal.ThrowExceptionForHR(result);

                return new AudioClient((IAudioClient)activated);
            }
            finally
            {
                Marshal.ReleaseComObject(attempt);
            }
        }
        catch (COMException no)
        {
            throw new AudioCaptureException($"Windows would not hand over {what}: {no.Message}", no);
        }
        finally
        {
            Marshal.FreeHGlobal(variant);
            Marshal.FreeHGlobal(parameters);
        }
    }

    [DllImport("Mmdevapi.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
    private static extern int ActivateAudioInterfaceAsync(
        string deviceInterfacePath,
        in Guid wantedInterface,
        IntPtr activationParameters,
        [MarshalAs(UnmanagedType.Interface)] IActivateAudioInterfaceCompletionHandler handler,
        [MarshalAs(UnmanagedType.Interface)] out IActivateAudioInterfaceAsyncOperation attempt);

    /// <summary>
    /// What Windows calls back on when the activation is over. It carries no result: the operation
    /// handed back by the call is the same one passed here, and asking it is where the answer is.
    /// </summary>
    private sealed class Activation : IActivateAudioInterfaceCompletionHandler, IDisposable
    {
        private readonly ManualResetEventSlim over = new(initialState: false);

        public void ActivateCompleted(IActivateAudioInterfaceAsyncOperation attempt) => over.Set();

        public void Dispose() => over.Dispose();

        internal bool Answered(TimeSpan within) => over.Wait(within);
    }

    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandler
    {
        void ActivateCompleted(
            [MarshalAs(UnmanagedType.Interface)] IActivateAudioInterfaceAsyncOperation attempt);
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceAsyncOperation
    {
        void GetActivateResult(
            out int result,
            [MarshalAs(UnmanagedType.IUnknown)] out object activated);
    }

    /// <summary>
    /// AUDIOCLIENT_ACTIVATION_PARAMS, whose one union member is the process loopback one. Written
    /// flat rather than as a union because there is nothing else in it to overlap with.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct ActivationParameters
    {
        public int Type;
        public uint TargetProcessId;
        public int Mode;
    }

    /// <summary>
    /// A PROPVARIANT holding a blob, which is the only shape the activation parameters travel in.
    /// The padding between the size and the pointer is the compiler's, and it is what makes this
    /// right on 64 bit as well as 32.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct BlobVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public int Size;
        public IntPtr Data;
    }
}
