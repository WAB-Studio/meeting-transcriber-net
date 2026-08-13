using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Cli;

/// <summary>
/// What this machine can record and what it actually records: the two commands that answer
/// whether the audio side of a meeting works here, before a window is built over it.
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
    /// Records both sources at once, saying what each one is and how loud it is while it runs.
    /// Ctrl+C stops it early with both files finished, which is not what killing the process
    /// does.
    /// </summary>
    public static int Capture(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var folder = new DirectoryInfo(arguments.Required("--out"));
        var seconds = arguments.Number("--seconds", 0);
        var wanted = arguments.Optional("--microphone");
        arguments.EnsureNothingLeftOver();

        if (seconds == 0)
        {
            throw new UsageException("--seconds is needed, and it takes a whole number above zero.");
        }

        var playback = AudioDevices.Playback();
        var microphone = AudioDevices.Choose(AudioDevices.Microphones(), wanted);

        using var session = CaptureSession.Start(folder, playback, microphone);

        Report.Line(output, "folder", folder.FullName);
        Report.Line(output, "channel 0", session.Mode.ToString());
        foreach (var source in session.Sources)
        {
            Report.Line(output, $"{Name(source)} device", source.Device.Name);
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
                    $"{source.File.Name}, {Report.Offset(source.Recorded)}, "
                    + $"{source.Bytes / 1024d / 1024d:0.0} MB, loudest {source.Loudest}"));
        }

        return Cli.Ok;
    }

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
            // Without this the process dies where it stands, and a WAV whose header was never
            // written back is a recording nothing will open.
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
