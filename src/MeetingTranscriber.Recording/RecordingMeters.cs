using MeetingTranscriber.Audio;
using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Recording;

/// <summary>
/// One channel of a meeting as it read the last time anybody looked: what it is capturing, how
/// loud that has been since the look before, and whether its device is still there.
/// </summary>
/// <remarks>
/// Everything on it is a moment and not a meeting. What a person watching a recording needs is
/// whether audio is arriving <em>now</em> — a microphone somebody muted, a program that stopped
/// playing, a device that was unplugged — and every one of those is invisible in a number
/// covering the whole meeting so far.
/// </remarks>
public sealed record ChannelReading
{
    /// <summary>
    /// How far down a meter still draws something. Decibels, and not the peak itself: speech
    /// arrives around a twentieth of full scale, so a bar drawn straight off the peak sits near
    /// the floor for a meeting that is recording perfectly well and says nothing to anybody.
    /// </summary>
    private const float Quietest = -60f;

    /// <summary>Which of the two channels this is.</summary>
    public required AudioChannel Channel { get; init; }

    /// <summary>
    /// What it is capturing, as it was capturing it when this was read — the device or the program
    /// — or nothing at all when it is capturing the whole machine. Read off the source rather than
    /// off the card beside the blocks, so a channel somebody moved to the whole machine says so
    /// from the moment it moves.
    /// </summary>
    /// <remarks>
    /// Nothing rather than a phrase, for the reason <see cref="Loudness"/> hands back nothing: a
    /// device's name and a program's are names this machine gave, and they read the same in every
    /// language. "Everything this machine plays" is a sentence, and a sentence belongs to the
    /// catalogue a screen names an entry in — so the one case that needs words hands back none.
    /// It is the ordinary case rather than a corner, since it is what a recording nobody pointed at
    /// a program is capturing.
    /// </remarks>
    /// <remarks>
    /// Nullable and required at once, which is not a contradiction: nothing is one of the answers,
    /// so it has to be sayable — and required is what keeps saying it apart from not saying
    /// anything. Without it a caller that forgot this field would compile into a reading claiming
    /// the recording is of everything this machine plays.
    /// </remarks>
    public required string? Capturing { get; init; }

    /// <summary>The loudest this channel was over the stretch this reading covers.</summary>
    public required LevelReading Level { get; init; }

    /// <summary>
    /// Whether its stream is over. Read while the meeting is still running, which is what makes it
    /// a device that stopped by itself rather than one somebody stopped — a source asked to stop is
    /// over too, and what keeps the two apart is that nothing meters a meeting being stopped.
    /// </summary>
    /// <remarks>
    /// That it stopped, and nothing about why. What the stream threw on its way out is whatever
    /// came out of the drain loop — a <see cref="System.Runtime.InteropServices.COMException"/>,
    /// the audio engine's own sentence, the filesystem's — and never a driver quoted, so carrying
    /// it on a reading would put framework English on a screen ISC-152 holds to being in both
    /// languages. It is in the sentence <see cref="CaptureSource.Finish"/> throws, which is what
    /// somebody diagnosing a meeting reads; a reading is what somebody recording one reads, and
    /// the words for that are the catalogue's.
    /// </remarks>
    public required bool Stopped { get; init; }

    /// <summary>Whether nothing at all arrived on this channel in the stretch just read.</summary>
    public bool IsSilent => Level.IsSilent;

    /// <summary>
    /// How loud it was, as a number a person reads, or nothing at all when nothing arrived.
    /// </summary>
    /// <remarks>
    /// Nothing rather than a word, and that is the point of it. A level is a measurement and reads
    /// the same in every language; having measured nothing is a sentence, and a sentence belongs to
    /// the catalogue a screen names an entry in. So the one case that needs words hands back none,
    /// and there is no English left on this record for a screen to print by accident.
    /// </remarks>
    public string? Loudness => IsSilent ? null : Level.ToString();

    /// <summary>
    /// How full the bar is, from nothing at <c>0</c> to full scale at <c>1</c>. Silence is
    /// nothing, and a source past full scale is full rather than more than full — a reading that
    /// clipped is something to see and not something to draw off the end of the meter.
    /// </summary>
    public double Meter => IsSilent
        ? 0
        : Math.Clamp((Level.Decibels - Quietest) / -Quietest, 0, 1);

    /// <summary>
    /// Asks each of <paramref name="recording"/>'s sources what it reads, in channel order.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Reading a level empties it.</b> That is what makes a meter a meter — what somebody wants
    /// to see is the stretch since they last looked, not the loudest moment of the meeting — and it
    /// is why this is called from the one place that ticks and from nowhere else. A screen that
    /// asked again while redrawing would find the stretch since a moment ago, which is nothing, and
    /// print the muted-channel answer over a channel carrying a conversation.
    /// </para>
    /// <para>
    /// It is a projection and holds no rule, deliberately: it is the one part of this file that
    /// needs two open devices to run, so anything decided here would be decided where no probe
    /// reaches. What to do with what it read is <see cref="RecordingMeters.Of"/>'s, which needs
    /// nothing but the numbers.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ChannelReading> ReadFrom(MeetingRecording recording)
    {
        ArgumentNullException.ThrowIfNull(recording);

        return
        [
            .. recording.Sources
                .OrderBy(source => CapturedAudio.IndexOf(source.Channel))
                .Select(source => Of(
                    source.Listening, source.Level(), stopped: source.HasEnded)),
        ];
    }

    /// <summary>
    /// One channel's reading, built from what it is listening to rather than from a name somebody
    /// read off it — which is the whole of the rule, and the reason this is not four initialisers
    /// at the one call site that needs a device to reach.
    /// </summary>
    /// <param name="listening">What the channel has open, which also says which channel it is.</param>
    /// <param name="level">The loudest it has been since the look before.</param>
    /// <param name="stopped">Whether its stream is over.</param>
    public static ChannelReading Of(CaptureTarget listening, LevelReading level, bool stopped)
    {
        ArgumentNullException.ThrowIfNull(listening);

        return new ChannelReading
        {
            Channel = listening.Channel,

            // The name where it is one this machine gave, and nothing where it would be a sentence
            // this application wrote — see Capturing, which is where that costs somebody the
            // language they read in.
            Capturing = listening is CaptureTarget.TheWholeMachine ? null : listening.Name,
            Level = level,
            Stopped = stopped,
        };
    }
}

/// <summary>
/// What the recording screen shows beside the buttons while a meeting is being recorded: a meter
/// per channel, and the one warning about this machine that costs nothing to be sure of.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than beside the window for the reason <see cref="RecorderScreen"/> is: reaching a
/// WinUI tree needs a UI thread and a packaged host, and a rule living there is a rule nothing
/// runs. What the window keeps is setting labels from this.
/// </para>
/// <para>
/// It is built and not updated. Every field on it is what was true when it was built, which is why
/// there is no way to change one — a screen holding a meter it built a minute ago is a screen
/// showing a level that is not the level, and that is exactly the failure a meter exists against.
/// </para>
/// </remarks>
public sealed record RecordingMeters
{
    /// <summary>Nothing is being recorded, so there is nothing to meter and nothing to warn.</summary>
    public static RecordingMeters Nothing { get; } = new();

    /// <summary>The channels, in channel order, or none when no meeting is being recorded.</summary>
    public IReadOnlyList<ChannelReading> Channels { get; init; } = [];

    /// <summary>
    /// Whether the meeting is being played through something that puts it into the room, so the
    /// microphone is recording the other side a second time.
    /// </summary>
    /// <remarks>
    /// It is what the playback endpoint says it is and never a measurement of the echo: how much
    /// of channel 0 really comes back in on channel 1 is the audio engine's to measure, and what
    /// a screen should then do with that number is not decided.
    /// </remarks>
    public bool TheOthersAreHeardTwice { get; init; }

    /// <summary>Whether there is a meeting to show any of this for.</summary>
    public bool Showing => Channels.Count > 0;

    /// <summary>
    /// What one channel reads as, or nothing when there is no meeting — so a screen asks for the
    /// channel it has a row for instead of walking a list it cannot have the wrong length of.
    /// </summary>
    public ChannelReading? On(AudioChannel channel) =>
        Channels.FirstOrDefault(reading => reading.Channel == channel);

    /// <summary>
    /// The meters as they are for a screen in <paramref name="state"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A state with no meeting in it shows none of this, and that is the rule rather than a
    /// convenience. A meter left standing after a meeting ended is the last second of a recording
    /// that is over, and somebody reads it as a recording that is still going; the warning beside
    /// it is worse, because it is about a microphone that is not open.
    /// </para>
    /// <para>
    /// The endpoint is asked for here rather than carried by the recording, because the answer
    /// changes while the meeting runs. Windows moves what the machine plays through the moment
    /// somebody plugs a headset in, and a warning settled when the devices opened would tell that
    /// person the room could hear them for the rest of the hour — which is the one thing this
    /// warning is worth anything for not doing.
    /// </para>
    /// </remarks>
    /// <param name="state">What the screen is doing.</param>
    /// <param name="playback">
    /// The endpoint the meeting is coming out of now, or nothing when the machine would not say —
    /// which warns about nothing, the same as an endpoint that did not say what it is.
    /// </param>
    /// <param name="channels">What each channel last read as, in channel order.</param>
    public static RecordingMeters Of(
        RecorderState state, AudioDevice? playback, IReadOnlyList<ChannelReading> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);

        return state.IsRecording()
            ? new RecordingMeters
            {
                Channels = channels,
                TheOthersAreHeardTwice = playback?.PlaysIntoTheRoom ?? false,
            }
            : Nothing;
    }
}
