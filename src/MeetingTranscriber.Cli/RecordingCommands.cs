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
    /// What a start after a crash finds waiting in the corpus, and — when somebody names one and
    /// says which of the three — what happens to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the start the application performs, with no window over it: the same list and the
    /// same three choices, so what a person can do by hand and what a screen will do are one path.
    /// Named after the corpus rather than after a folder, which is what makes recovering here mean
    /// the meeting rather than a file beside the blocks.
    /// </para>
    /// <para>
    /// With no <c>--meeting</c> it lists and does nothing, which is what a start is: the listing
    /// never blocks anything and never removes anything, and <c>record</c> works with every one of
    /// these still sitting there undecided.
    /// </para>
    /// </remarks>
    public static int Recovery(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var corpus = Corpus.At(arguments);
        var named = arguments.Optional("--meeting");
        var keep = arguments.Flag("--keep");
        var into = arguments.Optional("--export");
        var discard = arguments.Flag("--discard");
        arguments.EnsureNothingLeftOver();

        var chosen = new[] { keep, into is not null, discard }.Count(picked => picked);
        if (named is null)
        {
            if (chosen > 0)
            {
                throw new UsageException(
                    "--meeting <id> says which recording is being decided about. Without it this "
                    + "lists what is waiting and does nothing to any of it.");
            }

            using var listing = corpus.Read();
            return List(WaitingRecordings.In(listing), output);
        }

        if (chosen != 1)
        {
            throw new UsageException(
                "One of --keep, --export <directory> or --discard is needed, and only one. What "
                + "happens to a recording is a decision, so this command does not have a default.");
        }

        if (!Guid.TryParse(named, out var meetingId))
        {
            throw new UsageException($"'{named}' is not a meeting id.");
        }

        using var context = corpus.Write();
        var recording = WaitingRecordings.In(context).FirstOrDefault(waiting => waiting.MeetingId == meetingId)
            ?? throw new CommandException(
                $"No recording of meeting {meetingId} is waiting to be decided about in "
                + $"'{corpus.Root.FullName}'. Run this without --meeting to see what is.");

        Report.Line(output, "folder", recording.Folder.FullName);

        if (discard)
        {
            recording.Spooled.Discard();
            Report.Line(output, "thrown away", recording.Folder.FullName);
            return Cli.Ok;
        }

        if (into is not null)
        {
            foreach (var exported in recording.Spooled.Export(new DirectoryInfo(into)))
            {
                Report.Line(output, $"{Name(exported.Channel)} taken out", exported.Wav.FullName);
            }

            return Cli.Ok;
        }

        var recovered = WaitingRecordings.Recover(context, recording, Clock.Now());

        Report.Line(output, "meeting", recovered.MeetingId.ToString());
        Report.Line(output, "length", Report.Offset(recovered.Length));
        Report.Line(output, "audio", recovered.Audio.RelativePath);
        Report.Line(output, "sha256", recovered.Audio.Sha256);
        Report.Line(
            output,
            "size",
            string.Create(CultureInfo.InvariantCulture, $"{recovered.Audio.ByteSize / 1024d / 1024d:0.0} MB"));

        return Cli.Ok;
    }

    /// <summary>
    /// What is waiting, one recording at a time: which meeting it is, how long it is, what
    /// survived in each of its sources, and which of the three choices is open to it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The blocks are read through, once per recording, which is what the length and what survived
    /// are read off. That is a pass over every byte and it is the right cost here: somebody typed
    /// this and is waiting for the answer, and the answer is what they decide on — a recording
    /// whose microphone caught nothing is one to be told about before it is kept, not after.
    /// </para>
    /// <para>
    /// A recording still being written is the one exception, because its files cannot be read
    /// while a capture holds them. It says so instead, which is the honest answer about a meeting
    /// that is still happening.
    /// </para>
    /// </remarks>
    private static int List(IReadOnlyList<WaitingRecording> waiting, TextWriter output)
    {
        if (waiting.Count == 0)
        {
            Report.Line(output, "waiting", "none");
            return Cli.Ok;
        }

        foreach (var recording in waiting)
        {
            Report.Line(
                output,
                "meeting",
                recording.MeetingId?.ToString() ?? $"unnamed, in '{recording.Folder.Name}'");
            Report.Line(
                output,
                "started",
                recording.Meeting?.StartedAt.ToStorage()
                    ?? recording.Spooled.Card?.StartedAt.ToStorage()
                    ?? "unknown");

            if (recording.Running)
            {
                Report.Line(output, "length", "still being recorded, so its blocks cannot be read yet");
            }
            else
            {
                var survived = recording.Read();
                Report.Line(output, "length", Report.Offset(survived.Length));

                foreach (var source in survived.Sources)
                {
                    Report.Line(output, $"{Name(source.Channel)} survived", Says(source));
                }
            }

            Report.Line(output, "choices", Choices(recording));
        }

        return Cli.Ok;
    }

    /// <summary>What one source turned out to be worth, on the line a person reads it off.</summary>
    private static string Says(SurvivingSource survivor) => string.Create(
        CultureInfo.InvariantCulture,
        $"{survivor.Blocks} blocks, {Report.Offset(survivor.Covers)}, {survivor.Lost.Milliseconds} ms lost{CutOff(survivor.Discarded)}");

    /// <summary>What the last write cost, said only when it cost something.</summary>
    private static string CutOff(long discarded) => discarded > 0
        ? string.Create(CultureInfo.InvariantCulture, $", {discarded} bytes cut off the end")
        : string.Empty;

    /// <summary>
    /// Which of the three this recording is open to, and where a folder this command cannot
    /// address is decided about instead.
    /// </summary>
    /// <remarks>
    /// A recording is named here by its meeting, so a folder nothing says the meeting of is one
    /// this command cannot be pointed at. Saying so beside it is the difference between an
    /// instruction and a listing that offers two choices and then refuses both.
    /// </remarks>
    private static string Choices(WaitingRecording recording)
    {
        if (recording.Unrecoverable is not { } why)
        {
            return "keep, export or discard";
        }

        return recording.MeetingId is null
            ? $"'recover --in {recording.Folder.FullName}' — this command names a recording by its "
                + $"meeting, and {why}"
            : $"export or discard — it cannot become a meeting: {why}";
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
