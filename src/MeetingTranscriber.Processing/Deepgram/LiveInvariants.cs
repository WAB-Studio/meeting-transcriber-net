using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;

namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>One audio file a live run would send, and what it is.</summary>
/// <param name="File">The file.</param>
/// <param name="Profile">
/// Decided by the file's own channel count and never asked for: two channels is what this
/// application records, one is a single track, and the contract is that a profile disagreeing with
/// its audio throws. A flag would let somebody pay to find that out.
/// </param>
/// <param name="Length">
/// How long it is, counted off the frames before anything is sent rather than read off the header,
/// because the header is a claim and this is the number a ceiling is spent against.
/// </param>
/// <remarks>
/// It holds a <see cref="FileInfo"/>, a <see cref="SourceProfile"/> and a <see cref="Duration"/>
/// and nothing else, which is why it comes here with the rules that read it rather than staying
/// behind with the command that fills it in: none of the three puts this project on a Windows
/// target framework, and a rule about a response that had to reach into the command line to name
/// what was sent would be a rule nothing outside the command line could ever use.
/// </remarks>
public sealed record LiveAudio(FileInfo File, SourceProfile Profile, Duration Length);

/// <summary>
/// What a real response came back as: what it broke, and what is worth saying about it that is not
/// a failure. The two are separate lists because a channel that carried nobody is a fact somebody
/// paid for and has to be told, and is not a reason to fail a run.
/// </summary>
/// <param name="Turns">
/// How many turns <see cref="MeetingTranscriber.Domain.Knowledge.Turns.Group"/> made of it.
/// </param>
public sealed record LiveVerdict(
    int Turns, IReadOnlyList<string> Broken, IReadOnlyList<string> WorthSaying)
{
    /// <summary>Whether the response held everything a live call is checked for.</summary>
    public bool Held => Broken.Count == 0;
}

/// <summary>
/// What has to be true of a real response, said as structure and never as wording. A provider
/// rewords itself between models and between days; what it may not do is come back describing
/// different audio from the audio that was sent.
/// </summary>
/// <remarks>
/// <para>
/// <b>It checks only what the provider decides, and deliberately restates nothing the parser has
/// already refused.</b> <see cref="DeepgramTranscriptParser"/> is the first invariant and the
/// loudest one: it refuses a response whose channel count is not the profile's, an utterance on a
/// channel that does not exist, an utterance that ends before it starts, and a channel whose whole
/// transcript and whose utterances disagree — each as a
/// <see cref="DeepgramResponseException"/> naming what it found. A check here for any of those
/// would be a check against the parser and would pass by construction.
/// </para>
/// <para>
/// The same goes for anything the parser builds rather than reads. A speaker label is
/// <see cref="SpeakerLabels.For"/>'s output, a channel's position is
/// <see cref="CapturedAudio.ChannelAt"/>'s, and the order of turns is
/// <see cref="Turns.Group"/>'s — asserting any of them here would be asserting that this build
/// calls the functions it calls, and one of them would go further and be wrong: a channel whose
/// speakers really are numbered 0 and 2 has a label list whose second entry is
/// <c>ch0:speaker_2</c>, because the parser drops a speaker whose every utterance is blank. A rule
/// that read the list position as the provider's number would fail on a correct response, and it
/// would fail for the first time on a call somebody had paid for.
/// </para>
/// <para>
/// What is left is the three things only a real call can settle, and they are the three below.
/// </para>
/// <para>
/// It lives here and not beside the command that calls it because it is a rule about what a
/// provider response has to hold, and <c>docs/layout.md</c> says three times that
/// <c>MeetingTranscriber.Cli</c> holds no rule of its own: every command is a call into the same
/// service the application calls, and what it adds is argument parsing, a report and an exit code.
/// It needs only <c>Domain</c> and this project, and it sits beside
/// <see cref="DeepgramTranscriptParser"/>, whose refusals the remarks above defer to.
/// </para>
/// </remarks>
public static class LiveInvariants
{
    /// <summary>
    /// The one tolerance there is. A provider reports its own rounding of the length it received
    /// and this end counts frames, so the two agree to about a second and never to the millisecond.
    /// </summary>
    private static readonly Duration Slack = Duration.FromSeconds(1);

    /// <summary>What a real response says, held against the audio that was sent.</summary>
    public static LiveVerdict Of(DeepgramTranscript answered, LiveAudio sent)
    {
        ArgumentNullException.ThrowIfNull(answered);
        ArgumentNullException.ThrowIfNull(sent);

        var broken = new List<string>();
        var turns = Turns.Group(answered.Segments);

        // Both lengths print as milliseconds rather than as a clock: a tolerance of one second is
        // not readable in h:mm:ss, and this is the one line somebody compares two numbers on.
        if (Difference(answered.Audio, sent.Length) > Slack)
        {
            broken.Add(
                $"the response says the audio is {answered.Audio} and '{sent.File.Name}' is "
                + $"{sent.Length}. That is not this file.");
        }

        // Held against the length this end counted rather than against the one the response
        // reports, so that a response whose reported duration is nonsense breaks one rule instead
        // of two. Nothing else compares the two at all: the parser holds an utterance's start
        // against its own end and neither against anything outside it.
        var beyond = answered.Segments
            .Where(segment => segment.End > sent.Length + Slack)
            .ToArray();
        if (beyond.Length > 0)
        {
            broken.Add(
                $"{beyond.Length} utterance(s) end after the audio does: the last ends at "
                + $"{beyond.Max(segment => segment.End)} and '{sent.File.Name}' is {sent.Length}.");
        }

        // Read off the segments and not off the grouping, deliberately. What a wrong language
        // produces is a response with nothing in it, which is a fact about what came back; routed
        // through Turns.Group a grouping regression would arrive as an accusation about the
        // language, and would arrive for the first time on a call somebody paid for.
        if (answered.Segments.All(segment => string.IsNullOrWhiteSpace(segment.Text)))
        {
            broken.Add(
                $"nothing was heard at all in {sent.Length} of audio. The usual cause is asking "
                + "for the wrong language.");
        }

        return new LiveVerdict(
            turns.Count,
            broken,
            [
                .. answered.SilentChannels.Select(channel =>
                    $"channel {channel.Index} ({Position(channel.Channel)}) carried nobody. It was "
                    + "transcribed and it was charged for; the usual cause is asking for the wrong "
                    + "language."),
            ]);
    }

    /// <summary>How far apart two lengths are, whichever way round they came.</summary>
    private static Duration Difference(Duration one, Duration other) =>
        one > other ? one - other : other - one;

    /// <summary>
    /// What a channel is, read off the value the transcript already carries. Nothing here decides
    /// which index is which: <see cref="CapturedAudio.ChannelAt"/> did that, once.
    /// </summary>
    private static string Position(AudioChannel? channel) => channel switch
    {
        AudioChannel.Loopback => "the loopback",
        AudioChannel.Microphone => "the microphone",
        _ => "the single track",
    };
}
