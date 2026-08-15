using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;

namespace MeetingTranscriber.Audio;

/// <summary>
/// What is running on this machine, and how a name somebody typed becomes one of it.
/// </summary>
/// <remarks>
/// <para>
/// Split the same way <see cref="AudioDevices"/> is: what is running is the machine's answer and
/// changes between two keystrokes, while which program was meant is a rule, and a rule gets
/// tested. <see cref="Choose"/> is that rule and it asks the machine nothing.
/// </para>
/// <para>
/// The rule that matters is the last one. A meeting in a browser is a dozen processes of the same
/// name, and the audio comes out of a child the person never heard of — so what a typed name has
/// to resolve to is the root of that tree, and every match whose parent is also a match is one of
/// its children rather than another candidate.
/// </para>
/// </remarks>
public static class AudioProcesses
{
    private const uint SnapshotOfProcesses = 0x00000002;
    private static readonly IntPtr NoSnapshot = new(-1);

    /// <summary>Every process this session can see, with the id of whatever started each one.</summary>
    /// <remarks>
    /// Taken from a snapshot rather than from <c>Process.GetProcesses</c> because the parent id is
    /// the whole point and .NET does not expose it. A process that ends between the snapshot and
    /// the capture is a refusal from the audio stack, not something to guard here.
    /// </remarks>
    public static IReadOnlyList<AudioProcess> Running()
    {
        var snapshot = CreateToolhelp32Snapshot(SnapshotOfProcesses, 0);
        if (snapshot == NoSnapshot)
        {
            throw new AudioCaptureException(
                "Windows would not say what is running, so there is no program to follow.",
                new Win32Exception(Marshal.GetLastWin32Error()));
        }

        try
        {
            var running = new List<AudioProcess>();
            var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };

            for (var more = Process32FirstW(snapshot, ref entry); more; more = Process32NextW(snapshot, ref entry))
            {
                running.Add(new AudioProcess(
                    (int)entry.ProcessId,
                    Path.GetFileNameWithoutExtension(entry.ExeFile),
                    (int)entry.ParentProcessId));
            }

            return running;
        }
        finally
        {
            CloseHandle(snapshot);
        }
    }

    /// <summary>
    /// The program <paramref name="wanted"/> names, out of the ones running. A number is a process
    /// id and is taken as one; anything else is a name, matched whole or in part and ignoring case,
    /// and reduced to the roots of the trees it hit. Two roots left is refused rather than resolved:
    /// picking one is picking which meeting gets recorded.
    /// </summary>
    /// <param name="processes">What there is to choose from.</param>
    /// <param name="wanted">A process id, or a name whole or in part.</param>
    public static AudioProcess Choose(IReadOnlyList<AudioProcess> processes, string wanted)
    {
        ArgumentNullException.ThrowIfNull(processes);

        if (string.IsNullOrWhiteSpace(wanted))
        {
            throw new AudioCaptureException("Say which program to follow, by name or by process id.");
        }

        if (int.TryParse(wanted, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            return processes.FirstOrDefault(process => process.Id == id)
                ?? throw new AudioCaptureException($"Nothing is running as process {id}.");
        }

        var named = processes
            .Where(process => process.Name.Equals(wanted, StringComparison.OrdinalIgnoreCase))
            .ToArray();

        if (named.Length == 0)
        {
            named = processes
                .Where(process => process.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        var roots = Roots(named);

        return roots.Length switch
        {
            1 => roots[0],
            0 => throw new AudioCaptureException($"Nothing running here is called '{wanted}'."),
            _ => throw new AudioCaptureException(
                $"'{wanted}' names {roots.Length} programs: {Names(roots)}. Say which by its process id."),
        };
    }

    /// <summary>
    /// The matches that nothing else in the match started. A tree is followed from its root, so a
    /// child among the candidates is not a candidate — it is part of the answer already.
    /// </summary>
    private static AudioProcess[] Roots(AudioProcess[] named)
    {
        var ids = named.Select(process => process.Id).ToHashSet();
        return [.. named.Where(process => !ids.Contains(process.StartedBy))];
    }

    private static string Names(IReadOnlyList<AudioProcess> processes) =>
        string.Join(", ", processes.Select(process => process.ToString()));

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    /// <summary>
    /// PROCESSENTRY32W. Every field is here whether it is read or not: the structure is walked by
    /// its declared size, and one left out moves every field after it.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClass;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }
}
