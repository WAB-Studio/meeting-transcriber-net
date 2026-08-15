using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli;

/// <summary>
/// What this machine can record, what it actually records, and what a recording that was cut off
/// is worth: the commands that answer whether the audio side of a meeting works here, before a
/// window is built over it.
/// </summary>
/// <remarks>
/// A capture from a prompt is not scaffolding for the recorder — it is how a capture gets
/// measured. Drift is stated over two hours, and two hours of clicking through a window is not a
/// measurement anybody repeats.
/// </remarks>
public static class AudioCommands
{
    /// <summary>What Windows offers here, and under which names.</summary>
    public static int Devices(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        arguments.EnsureNothingLeftOver();

        var playback = AudioDevices.Playback();
        Report.Line(output, "playback", playback.ToString());
        Report.Line(output, "id", playback.Id);

        var microphones = AudioDevices.Microphones();
        if (microphones.Count == 0)
        {
            Report.Line(output, "microphone", "none");
            return Cli.Ok;
        }

        foreach (var microphone in microphones)
        {
            Report.Line(output, "microphone", microphone.ToString());
            Report.Line(output, "id", microphone.Id);
        }

        return Cli.Ok;
    }

    /// <summary>
    /// Records both sources at once, saying what each one is and how loud it is while it runs, and
    /// reads each spool back into a file somebody can listen to. Ctrl+C stops it early and still
    /// reports; killing the process leaves the spools, which is the point of them.
    /// </summary>
    public static int Capture(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var folder = new DirectoryInfo(arguments.Required("--out"));
        var seconds = arguments.Number("--seconds", 0);
        var wanted = arguments.Optional("--microphone");
        var program = arguments.Optional("--process");
        arguments.EnsureNothingLeftOver();

        if (seconds == 0)
        {
            throw new UsageException("--seconds is needed, and it takes a whole number above zero.");
        }

        var playback = AudioDevices.Playback();
        var microphone = AudioDevices.Choose(AudioDevices.Microphones(), wanted);
        var follow = program is null ? null : AudioProcesses.Choose(AudioProcesses.Running(), program);

        using var session = CaptureSession.Start(folder, playback, microphone, follow);

        Report.Line(output, "folder", folder.FullName);
        Report.Line(output, "channel 0", session.Mode.ToString());

        if (session.FellBack is not null)
        {
            Report.Line(output, "fell back", session.FellBack);
        }

        foreach (var source in session.Sources)
        {
            Report.Line(output, $"{Name(source)} hears", source.Listening.Name);
            Report.Line(output, $"{Name(source)} format", source.Format.ToString());
            Report.Line(output, $"{Name(source)} opened", source.StartedAt.ToString());
        }

        Meter(session, seconds, output);
        session.Stop();

        foreach (var source in session.Sources)
        {
            Report.Line(
                output,
                $"{Name(source)} wrote",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{source.File.Name}, {Report.Offset(source.Packets.Covers)}, "
                    + $"{source.Bytes / 1024d / 1024d:0.0} MB, loudest {source.Loudest}"));

            Report.Line(output, $"{Name(source)} clock", Clocking(source.Packets));
            Report.Line(output, $"{Name(source)} played back", PlayedBack(source.File));
        }

        return Cli.Ok;
    }

    /// <summary>
    /// What a folder of blocks holds, for a recording nobody got to stop: how much of it survived,
    /// what the machine cut off, and a file of each source to listen to.
    /// </summary>
    /// <remarks>
    /// It reports and writes beside what is there, and removes nothing. A spool may be the only
    /// copy of a meeting, so what happens to one is a person's decision and not a command's.
    /// </remarks>
    public static int Spool(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var folder = new DirectoryInfo(arguments.Required("--in"));
        arguments.EnsureNothingLeftOver();

        if (!folder.Exists)
        {
            throw new CommandException($"There is no folder at '{folder.FullName}'.");
        }

        Report.Line(output, "folder", folder.FullName);

        var found = 0;
        foreach (var channel in new[] { AudioChannel.Loopback, AudioChannel.Microphone })
        {
            var blocks = BlockSpool.FileFor(folder, channel);
            if (!blocks.Exists)
            {
                Report.Line(output, $"ch{CapturedAudio.IndexOf(channel)}", $"no {blocks.Name}");
                continue;
            }

            found++;
            using var spool = SpoolReader.Open(blocks);
            Report.Line(output, $"ch{CapturedAudio.IndexOf(channel)} format", spool.Format.ToString());
            Report.Line(output, $"ch{CapturedAudio.IndexOf(channel)} recovered", PlayedBack(blocks));
        }

        return found == 0
            ? throw new CommandException(
                $"'{folder.FullName}' holds no spool, so there is no recording in it to recover.")
            : Cli.Ok;
    }

    /// <summary>
    /// Reads one source's spool back into a file somebody can listen to, in the format its device
    /// handed over. It is a diagnostic and not the recording — the two sources become one pair of
    /// channels on the shared timeline, and that is a different file — but it is read through the
    /// same path a recovery takes, so every capture is a run of the code a crash will depend on.
    /// </summary>
    private static string PlayedBack(FileInfo blocks)
    {
        var wav = new FileInfo(Path.ChangeExtension(blocks.FullName, ".wav"));
        var replayed = BlockSpool.ToWav(blocks, wav);
        var cut = replayed.Discarded > 0
            ? string.Create(CultureInfo.InvariantCulture, $", {replayed.Discarded} bytes discarded")
            : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture, $"{wav.Name}, {replayed.Blocks} blocks{cut}");
    }

    /// <summary>
    /// What the device said about its own packets, which is the measurement the two files are not.
    /// The two numbers that matter are how much of the meeting it counted and never handed over,
    /// and how far apart it read one packet from the next: instants stamped where the application
    /// collected them would read as a burst of no time at all and then the whole of one poll.
    /// </summary>
    private static string Clocking(PacketTally packets) => string.Create(
        CultureInfo.InvariantCulture,
        $"{packets.Packets} packets, {packets.Closest.Milliseconds} to {packets.Furthest.Milliseconds} ms apart, "
        + $"{packets.Lost.Milliseconds} ms lost, {packets.Unvouched} unvouched");

    /// <summary>
    /// One line a second, so what is being written is visible while it is being written rather
    /// than after. It ends early on Ctrl+C, and on a source that stopped by itself — carrying on
    /// past that would be reporting levels for a stream that is no longer there.
    /// </summary>
    private static void Meter(CaptureSession session, int seconds, TextWriter output)
    {
        using var interrupted = new ManualResetEventSlim(initialState: false);

        void Interrupt(object? sender, ConsoleCancelEventArgs pressed)
        {
            // Without this the process dies where it stands. The spool survives that — every block
            // in it is whole — but nothing would report what was recorded or read it back, and the
            // run is a measurement rather than only a recording.
            pressed.Cancel = true;
            interrupted.Set();
        }

        Console.CancelKeyPress += Interrupt;
        try
        {
            for (var second = 1; second <= seconds; second++)
            {
                if (interrupted.Wait(TimeSpan.FromSeconds(1)))
                {
                    Report.Line(output, "stopped", "Ctrl+C");
                    return;
                }

                Report.Line(
                    output,
                    Report.Offset(Duration.FromMilliseconds(second * 1000L)),
                    string.Join("   ", session.Sources.Select(source => $"{Name(source)} {source.Level()}")));

                if (session.Sources.Any(source => source.HasEnded))
                {
                    return;
                }
            }
        }
        finally
        {
            Console.CancelKeyPress -= Interrupt;
        }
    }

    /// <summary>
    /// A source under its channel number, which is the contract everything downstream reads —
    /// Deepgram included — and not the name of the device it happens to be.
    /// </summary>
    private static string Name(CaptureSource source) => $"ch{CapturedAudio.IndexOf(source.Channel)}";
}
