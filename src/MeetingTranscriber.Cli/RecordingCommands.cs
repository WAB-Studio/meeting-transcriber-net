using System.Globalization;

using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Recording;

namespace MeetingTranscriber.Cli;

/// <summary>
/// Recording a meeting into a corpus from a prompt: the whole cycle — record, pause, resume, stop
/// — without a window to automate.
/// </summary>
/// <remarks>
/// This is the same service a record button presses, and the CLI adds what it always adds: the
/// arguments, a report and an exit code. It is where the ordering that needs two real devices to
/// go wrong actually gets exercised, since nothing on a build agent can open one.
/// </remarks>
public static class RecordingCommands
{
    /// <summary>
    /// Records a meeting into the corpus, optionally pausing partway, and stops — which finishes
    /// the recording and starts nothing.
    /// </summary>
    public static int Record(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var seconds = arguments.Number("--seconds", 0);
        var language = arguments.Required("--language");
        var wanted = arguments.Optional("--microphone");
        var program = arguments.Optional("--process");
        var pauseAt = arguments.Number("--pause-at", 0);
        var resumeAt = arguments.Number("--resume-at", 0);
        arguments.EnsureNothingLeftOver();

        if (seconds <= 0)
        {
            throw new UsageException("--seconds is needed, and it takes a whole number above zero.");
        }

        // Either both or neither, and both inside the recording. A pause the run never reaches, or
        // one it never comes out of, is a probe that reports a clean cycle it did not perform.
        if ((pauseAt > 0) != (resumeAt > 0))
        {
            throw new UsageException("--pause-at and --resume-at are given together or not at all.");
        }

        if (pauseAt > 0 && !(pauseAt < resumeAt && resumeAt < seconds))
        {
            throw new UsageException(
                $"--pause-at {pauseAt} and --resume-at {resumeAt} have to fall inside the recording "
                + $"and in that order: 0 < pause < resume < --seconds {seconds}.");
        }

        var playback = AudioDevices.Playback();
        var microphone = AudioDevices.Choose(AudioDevices.Microphones(), wanted);
        var follow = program is null ? null : AudioProcesses.Choose(AudioProcesses.Running(), program);

        using var context = corpus.Write();
        using var recording = MeetingRecording.Start(
            context, language, playback, microphone, follow, Clock.Now());

        Report.Line(output, "meeting", recording.MeetingId.ToString());
        Report.Line(output, "spool", recording.Prepared.Spool.FullName);
        Report.Line(output, "channel 0", recording.Card.Mode.ToString());

        foreach (var source in recording.Sources)
        {
            Report.Line(output, $"{Name(source.Channel)} hears", source.Listening.Name);
            Report.Line(output, $"{Name(source.Channel)} format", source.Format.ToString());
        }

        Meter(recording, seconds, pauseAt, resumeAt, output);

        var finished = recording.Stop(Clock.Now());

        Report.Line(output, "length", Report.Offset(finished.Length));
        Report.Line(output, "audio", finished.Audio.RelativePath);
        Report.Line(output, "sha256", finished.Audio.Sha256);
        Report.Line(
            output,
            "size",
            string.Create(CultureInfo.InvariantCulture, $"{finished.Audio.ByteSize / 1024d / 1024d:0.0} MB"));

        // Said out loud, every time, because it is the promise and not an omission: stopping
        // finished the recording and set nothing going.
        Report.Line(
            output,
            "queued",
            finished.Queued.Count == 0
                ? "nothing — transcribing is a separate press"
                : string.Join(", ", finished.Queued));

        return Cli.Ok;
    }

    /// <summary>
    /// Runs the meeting, showing what each source is hearing, and presses pause and resume at the
    /// seconds they were asked for.
    /// </summary>
    private static void Meter(
        MeetingRecording recording, int seconds, int pauseAt, int resumeAt, TextWriter output)
    {
        using var interrupted = new ManualResetEventSlim(initialState: false);

        var wholeMachine = WholeMachine.AtThePrompt(said =>
        {
            recording.RecordTheWholeMachine();
            Report.Line(said, "channel 0", $"{recording.Mode} — everything this machine plays");
        });

        void Interrupt(object? sender, ConsoleCancelEventArgs pressed)
        {
            // Ctrl+C is somebody stopping the meeting early, not the process dying: cancelled here
            // so that the recording is still finished and still reported.
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

                if (second == pauseAt)
                {
                    recording.Pause();
                    Report.Line(output, "paused", "the meeting's clock keeps running");
                }

                if (second == resumeAt)
                {
                    recording.Resume();
                    Report.Line(output, "resumed", string.Empty);
                }

                Report.Line(
                    output,
                    Report.Offset(Duration.FromMilliseconds(second * 1000L)),
                    string.Join(
                        "   ",
                        recording.Sources.Select(source => $"{Name(source.Channel)} {source.Level()}")));

                wholeMachine.Consider(recording.HeardNothingFromTheProgram(), output);

                if (recording.Sources.Any(source => source.HasEnded))
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

    /// <summary>What a channel is called in a report, which is what it is rather than its number.</summary>
    private static string Name(AudioChannel channel) =>
        channel == AudioChannel.Loopback ? "others" : "me";
}
