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

        // What the endpoint declares itself to be, and what rests on it: the recording screen puts
        // a line beside the meters when the meeting is coming out into the room, because then the
        // microphone is recording the other side a second time. Reported here because this is the
        // command that says what this machine is, and it is where somebody checks that answer
        // against the device they are actually wearing.
        Report.Line(
            output,
            "form factor",
            playback.PlaysIntoTheRoom
                ? $"{playback.Kind} — the microphone hears the others twice"
                : playback.Kind.ToString());

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
    /// Records both sources at once, saying what each one is and how loud it is while it runs,
    /// reads each spool back into a file somebody can listen to, and makes the recording the two of
    /// them become. Ctrl+C stops it early and still reports; killing the process leaves the spools,
    /// which is the point of them.
    /// </summary>
    /// <remarks>
    /// Channel 0 following a program that plays nothing is offered the whole machine's audio while
    /// the run is going, and somebody takes that offer by pressing the key or by having named the
    /// second with <c>--whole-machine-at</c>. Neither is the recording deciding: the argument is
    /// how a measurement of what moving costs gets made without a person holding a key down for
    /// two hours, the way <c>--pause-at</c> is how pausing gets measured. It takes the offer and
    /// does not stand in for it — a second falling before the rule has said anything moves nothing
    /// and says so, since what it would otherwise measure is the one move nobody was offered.
    /// </remarks>
    public static int Capture(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var folder = new DirectoryInfo(arguments.Required("--out"));
        var seconds = arguments.Number("--seconds", 0);
        var wanted = arguments.Optional("--microphone");
        var program = arguments.Optional("--process");
        var wholeMachineAt = arguments.Number("--whole-machine-at", 0);
        var meeting = arguments.Optional("--meeting") is { } typed
            ? Arguments.Meeting(typed)
            : Guid.NewGuid();
        arguments.EnsureNothingLeftOver();

        if (seconds == 0)
        {
            throw new UsageException("--seconds is needed, and it takes a whole number above zero.");
        }

        // The offer is somebody's to take, so taking it from here needs a program to have been
        // named: what it would otherwise ask for is a move from the whole machine to the whole
        // machine, which is the run reporting a measurement it did not make.
        if (wholeMachineAt > 0 && program is null)
        {
            throw new UsageException(
                "--whole-machine-at takes the offer of the whole machine's audio, and there is no "
                + "offer without --process naming the program channel 0 is following.");
        }

        if (wholeMachineAt >= seconds)
        {
            throw new UsageException(
                $"--whole-machine-at {wholeMachineAt} falls outside a recording of {seconds} "
                + "seconds, so the move it asks for would never happen.");
        }

        // Refused here rather than reported an hour in. Channel 0 is offered the whole machine
        // only once it has been silent for as long as SilentProgram waits, so a second before that
        // names a moment at which there is nothing to take, and the run would record the meeting
        // and report a measurement it did not make.
        if (wholeMachineAt > 0 && wholeMachineAt * 1000L < SilentProgram.Waits.Milliseconds)
        {
            throw new UsageException(
                $"--whole-machine-at {wholeMachineAt} falls before the whole machine's audio can "
                + $"have been offered: channel 0 has to have heard nothing for {SilentProgram.Waits} "
                + "first, and what is not offered cannot be taken.");
        }

        var microphone = AudioDevices.Choose(AudioDevices.Microphones(), wanted);
        var follow = program is null ? null : AudioProcesses.Choose(AudioProcesses.Running(), program);

        // The session is let go of before anything reads its spools back, and the scope is what
        // says so: a recording still being written is a file this build refuses to read, which is
        // the same refusal that stops somebody being told a meeting still going on had ended.
        var spools = new List<(AudioChannel Channel, FileInfo Blocks)>();
        using (var session = CaptureSession.Start(folder, meeting, microphone, follow))
        {
            Report.Line(output, "folder", folder.FullName);
            Report.Line(output, "meeting", session.Card.MeetingId.ToString());
            Report.Line(output, "channel 0", session.Mode.ToString());

            foreach (var source in session.Sources)
            {
                Report.Line(output, $"{Name(source.Channel)} hears", source.Listening.Name);
                Report.Line(output, $"{Name(source.Channel)} format", source.Format.ToString());
                Report.Line(output, $"{Name(source.Channel)} opened", source.StartedAt.ToString());
            }

            Meter(session, seconds, wholeMachineAt, output);
            session.Stop();

            foreach (var source in session.Sources)
            {
                Report.Line(
                    output,
                    $"{Name(source.Channel)} wrote",
                    string.Create(
                        CultureInfo.InvariantCulture,
                        $"{source.File.Name}, {Report.Offset(source.Packets.Covers)}, "
                        + $"{source.Bytes / 1024d / 1024d:0.0} MB, loudest {source.Loudest}"));

                Report.Line(output, $"{Name(source.Channel)} clock", Clocking(source.Packets));
                spools.Add((source.Channel, source.File));
            }
        }

        // Before the files to listen to, and that order is not cosmetic: those hold every sample at
        // the rate its device ran, so two of them are several times the recording itself. A machine
        // with room for the meeting and not for the diagnostics beside it must end up with the
        // meeting.
        Materialise(folder, output);

        foreach (var (channel, blocks) in spools)
        {
            Report.Line(output, $"{Name(channel)} played back", PlayedBack(blocks));
        }

        return Cli.Ok;
    }

    /// <summary>
    /// What recordings nobody got to stop are waiting in the folder recordings are written into:
    /// which meeting each one is, when it started, what it was listening to and how much is there.
    /// </summary>
    /// <remarks>
    /// This is the start after a crash, without a window over it. It reads and removes nothing:
    /// what happens to a recording is <see cref="Recover"/>, and that takes somebody saying which
    /// of the three it is.
    /// </remarks>
    public static int Recordings(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var root = new DirectoryInfo(arguments.Required("--spool"));
        arguments.EnsureNothingLeftOver();

        Report.Line(output, "spool", root.FullName);

        var waiting = UnfinishedRecordings.In(root);
        if (waiting.Count == 0)
        {
            Report.Line(output, "waiting", "none");
            return Cli.Ok;
        }

        foreach (var recording in waiting)
        {
            Report.Line(output, "recording", recording.Folder.Name);
            Describe(recording, output);

            // Every recording, the way the corpus-aware listing does it, and last for the same
            // reason: a key that appears only to say there are none reads as its own absence
            // meaning the opposite of what it means on the other listing. This command names a
            // folder rather than a meeting, so the three are all there is to say — which of them
            // a corpus would shut is `recovery --corpus`, and it says so there.
            Report.Line(output, "choices", Choices(recording));
        }

        return Cli.Ok;
    }

    /// <summary>
    /// Which of the three this folder is open to, which is all of them unless a capture is still
    /// writing it.
    /// </summary>
    private static string Choices(UnfinishedRecording recording) =>
        recording.NothingToDecideYet is { } yet ? $"nothing yet — {yet}" : "keep, export or discard";

    /// <summary>
    /// What happens to one recording nobody stopped: it is kept — which is where the recording the
    /// two spools become is made — its sources are taken out to a folder, or it is thrown away.
    /// </summary>
    /// <remarks>
    /// One of the three has to be typed, and there is no default. A spool may be the only copy of
    /// a meeting that happened, so the command exists to carry a decision rather than to make one
    /// — which is the same reason nothing else in this program deletes one.
    /// </remarks>
    public static int Recover(Arguments arguments, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(output);

        var folder = new DirectoryInfo(arguments.Required("--in"));
        var keep = arguments.Flag("--keep");
        var into = arguments.Optional("--export");
        var discard = arguments.Flag("--discard");
        arguments.EnsureNothingLeftOver();

        if (new[] { keep, into is not null, discard }.Count(chosen => chosen) != 1)
        {
            throw new UsageException(
                "One of --keep, --export <directory> or --discard is needed, and only one. What "
                + "happens to a recording is a decision, so this command does not have a default.");
        }

        var recording = UnfinishedRecordings.At(folder);
        Report.Line(output, "folder", recording.Folder.FullName);
        Describe(recording, output);

        // Said before any of the three is reached. Keeping one goes through the file the two
        // sources become before it goes through `Keep`, so a meeting still being recorded would
        // otherwise fail there — on a block file, and under advice to run this again.
        recording.EnsureThereIsSomethingToDecide();

        if (discard)
        {
            recording.Discard();
            Report.Line(output, "thrown away", recording.Folder.FullName);
            return Cli.Ok;
        }

        if (into is not null)
        {
            foreach (var exported in recording.Export(new DirectoryInfo(into)))
            {
                Report.Line(output, $"{Name(exported.Channel)} taken out", Says(exported));
            }

            return Cli.Ok;
        }

        // A meeting is two sources on one timeline, so half of one is a folder somebody has to look
        // at rather than a recording to make half of. Either way every source that is there is
        // read through and reported below, which is what a person keeping one is owed.
        if (recording.Sources.Count == CapturedAudio.ChannelCount)
        {
            Materialise(recording.Folder, output);
        }
        else
        {
            Report.Line(output, "recording", $"not made: {MeetingAudio.FileName} needs both sources");
        }

        foreach (var survivor in recording.Keep())
        {
            Report.Line(output, $"{Name(survivor.Channel)} format", survivor.Format.ToString());
            Report.Line(output, $"{Name(survivor.Channel)} kept", Says(survivor));
        }

        return Cli.Ok;
    }

    /// <summary>
    /// Makes the recording the two spools become, and says what it turned out to be: how long it
    /// is, how loud each channel got over the whole of it, and what each source never delivered.
    /// </summary>
    /// <remarks>
    /// The last three are the numbers a person decides on. A recording whose microphone is silent
    /// throughout is one somebody has to be told about now, while the meeting is still fresh enough
    /// to hold again — not after it has been paid to be transcribed.
    /// </remarks>
    private static void Materialise(DirectoryInfo folder, TextWriter output)
    {
        Materialised recording;
        try
        {
            recording = MeetingAudio.Materialise(folder);
        }
        catch (AudioCaptureException cannot)
        {
            // The blocks are what a meeting is worth, and they are still there. Said here because
            // this is the first place a person meets the failure, and what they would otherwise
            // read is a sentence about a frame counter after an hour of recording.
            throw new AudioCaptureException(
                $"{cannot.Message} Every block is still in '{folder.FullName}': what could not be "
                + "made is the one file the two sources become, and 'recover --in <folder> --keep' "
                + "makes it again.",
                cannot);
        }

        Report.Line(
            output,
            "recording",
            string.Create(
                CultureInfo.InvariantCulture,
                $"{recording.File.Name}, {Report.Offset(recording.Length)}, {recording.Frames} frames, "
                + $"{recording.File.Length / 1024d / 1024d:0.0} MB"));

        foreach (var source in recording.Timeline.Sources)
        {
            Report.Line(
                output,
                $"{Name(source.Channel)} recorded",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"loudest {recording.Loudest(source.Channel)}, "
                    + $"{source.Missing.Milliseconds} ms missing, {source.Waited.Milliseconds} ms waited, "
                    // Never called measured. It is the device's label until the recording is long
                    // enough to say otherwise, and for a source whose counter was given up on it
                    // stays the label however long the meeting was.
                    + $"{source.MeasuredRate:0} Hz"
                    + $"{(source.CounterGivenUp ? ", counter given up on" : string.Empty)}"));
        }
    }

    /// <summary>
    /// What a recording says about itself before anything is decided: its card if it has one, and
    /// what each source's file holds.
    /// </summary>
    private static void Describe(UnfinishedRecording recording, TextWriter output)
    {
        // Said whichever of the two files was torn, and beside the card rather than instead of it:
        // a folder whose changes could not be read still names its meeting, and hiding that would
        // make a damaged line about one moment cost the account of the whole recording.
        if (recording.Unreadable is { } torn)
        {
            Report.Line(output, "unreadable", torn);
        }

        if (recording.Card is { } card)
        {
            Report.Line(output, "meeting", card.MeetingId.ToString());
            Report.Line(output, "started", card.StartedAt.ToStorage());
            Report.Line(output, "profile", card.Profile.ToWireName());
            Report.Line(output, "channel 0", card.Mode.ToString());

            foreach (var source in card.Sources)
            {
                Report.Line(output, $"{Name(source.Channel)} heard", source.Heard);
            }
        }
        else if (recording.Unreadable is null)
        {
            Report.Line(output, "meeting", $"unnamed, there is no {SpoolManifest.FileName} here");
        }

        // After the card and before the bytes, which is the order they happened in: the card says
        // what each channel opened on, and every line here says what one of them was moved to while
        // the meeting ran. A folder whose channel 0 was moved and did not say so would tell whoever
        // found it that their notifications are not in the file.
        foreach (var change in recording.Changed)
        {
            Report.Line(
                output,
                $"{Name(change.Channel)} moved",
                $"at {change.At.ToStorage()}, from '{change.WasHearing}' to '{change.Heard}'");
        }

        foreach (var source in recording.Sources)
        {
            Report.Line(
                output,
                $"{Name(source.Channel)} holds",
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"{source.Blocks.Name}, {source.Bytes / 1024d / 1024d:0.0} MB"));
        }
    }

    /// <summary>
    /// Reads one source's spool back into a file somebody can listen to, in the format its device
    /// handed over. It is a diagnostic and not the recording — the two sources become one pair of
    /// channels on the shared timeline, and that is a different file — but it is read through the
    /// same path a recovery takes, so every capture is a run of the code a crash will depend on.
    /// </summary>
    private static string PlayedBack(FileInfo blocks)
    {
        var replayed = BlockSpool.ToWav(blocks);

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{BlockSpool.PlaybackFor(blocks).Name}, {replayed.Blocks} blocks{Cut(replayed.Discarded)}");
    }

    /// <summary>What one source turned out to be worth, on the line a person reads it off.</summary>
    private static string Says(SurvivingSource survivor) => string.Create(
        CultureInfo.InvariantCulture,
        $"{survivor.Blocks} blocks, {Report.Offset(survivor.Covers)}, "
        + $"{survivor.Lost.Milliseconds} ms lost{Cut(survivor.Discarded)}");

    /// <summary>The same for a source somebody took out of the application.</summary>
    private static string Says(ExportedSource exported) => string.Create(
        CultureInfo.InvariantCulture,
        $"{exported.Wav.FullName}, {exported.Blocks} blocks{Cut(exported.Discarded)}");

    /// <summary>What the recording being cut off cost, said only when it cost something.</summary>
    private static string Cut(long discarded) => discarded > 0
        ? string.Create(CultureInfo.InvariantCulture, $", {discarded} bytes discarded")
        : string.Empty;

    /// <summary>
    /// What the source's packets said about themselves, which is the measurement the two files are
    /// not. The two numbers that matter are how much of the meeting was counted and never handed
    /// over, and how far apart the device read one packet from the next: instants stamped where the
    /// application collected them would read as a burst of no time at all and then the whole of one
    /// poll. The first of the two is the device's own count only while its counter can be laid out;
    /// past that it is the clock, which is what the rebuilt recording uses too.
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
    private static void Meter(
        CaptureSession session, int seconds, int wholeMachineAt, TextWriter output)
    {
        using var interrupted = new ManualResetEventSlim(initialState: false);

        var wholeMachine = WholeMachine.AtThePrompt(said =>
        {
            session.RecordTheWholeMachine();
            Report.Line(said, "channel 0", $"{session.Mode} — {session.On(AudioChannel.Loopback).Listening.Name}");
        });

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
                    string.Join("   ", session.Sources.Select(source => $"{Name(source.Channel)} {source.Level()}")));

                wholeMachine.Consider(session.HeardNothingFromTheProgram(), output);

                if (second == wholeMachineAt)
                {
                    wholeMachine.Take(output);
                }

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
    private static string Name(AudioChannel channel) => $"ch{CapturedAudio.IndexOf(channel)}";
}
