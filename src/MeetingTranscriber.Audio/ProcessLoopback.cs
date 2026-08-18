using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;

using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MeetingTranscriber.Audio;

/// <summary>
/// Opens the audio of one process and the processes it started, rather than of a device.
/// </summary>
/// <remarks>
/// <para>
/// There is no endpoint behind this. Windows exposes a virtual device that is activated with the
/// process to follow attached to the call, so nothing here goes through <see cref="AudioDevices"/>
/// — and the client it hands back cannot be asked what format it mixes at either, which is why the
/// caller says. Everything after that is an ordinary capture client, which is the whole reason this
/// file is small: it produces a client, and <see cref="WasapiStream"/> drives it exactly as it
/// drives a device's.
/// </para>
/// <para>
/// The tree is always included. Windows offers only include-this-tree and exclude-this-tree, so
/// there is no mode that follows one process alone — which suits the problem, because the process a
/// person points at is a window and the audio comes out of a child of it.
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
    /// </remarks>
    private const int FirstBuildWithIt = 20348;

    /// <summary>The device path that means "not a device: the process in the parameters".</summary>
    private const string VirtualDevice = "VAD\\Process_Loopback";

    private const int ProcessLoopbackActivation = 1;
    private const int IncludeTargetProcessTree = 0;
    private const ushort VariantIsBlob = 65;

    private static readonly Guid AudioClientId = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");

    /// <summary>Whether this Windows can follow a process at all.</summary>
    internal static bool IsAvailable => OperatingSystem.IsWindowsVersionAtLeast(10, 0, FirstBuildWithIt);

    /// <summary>
    /// An audio client carrying what <paramref name="process"/> and its children are playing, not
    /// yet initialised. Throws <see cref="AudioCaptureException"/> when this machine will not give
    /// one — which is the answer that sends the capture back to the whole machine's loopback.
    /// </summary>
    internal static AudioClient For(AudioProcess process)
    {
        ArgumentNullException.ThrowIfNull(process);

        if (!IsAvailable)
        {
            throw new AudioCaptureException(
                $"This Windows is build {Environment.OSVersion.Version.Build}, and following one "
                + $"program's audio needs {FirstBuildWithIt} or later.");
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
                client = Activate(process);
            }
            catch (Exception no)
            {
                refused = ExceptionDispatchInfo.Capture(no);
            }
        })
        {
            IsBackground = true,
            Name = $"activating {process}",
        };

        activating.SetApartmentState(ApartmentState.MTA);
        activating.Start();
        activating.Join();

        // Thrown as it was caught, and never widened into "this machine cannot follow a process".
        // A capture reads that answer as permission to record everything instead — so an interop
        // defect wrapped in it would come back as a wider recording that looks like a supported
        // fallback, which is the one outcome nobody would go looking for.
        refused?.Throw();

        return client!;
    }

    private static AudioClient Activate(AudioProcess process)
    {
        var wanted = new ActivationParameters
        {
            Type = ProcessLoopbackActivation,
            TargetProcessId = (uint)process.Id,
            Mode = IncludeTargetProcessTree,
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
                // A wedge and not a refusal, which is the whole of what this type says about it: an
                // answer that never came is what the session reads before deciding whether to fall
                // back to the whole machine's audio, and reported as a refusal this would open a
                // second device while the callback for the first is still outstanding. The deadline
                // is the one every wait on a device shares, so this and the ask around it are the
                // same number rather than two that could disagree.
                throw AudioDeviceWedgedException.NoAnswerFrom($"audio of {process}");
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
            throw new AudioCaptureException(
                $"Windows would not hand over the audio of {process}: {no.Message}", no);
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
