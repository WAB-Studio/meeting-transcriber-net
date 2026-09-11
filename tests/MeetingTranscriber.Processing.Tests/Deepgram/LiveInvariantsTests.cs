using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// What a real Deepgram response has to hold, proved against committed responses and without
/// spending anything.
/// </summary>
/// <remarks>
/// <para>
/// This is the half of <c>deepgram-live</c> that would otherwise only ever run on a call somebody
/// paid for. It is here rather than in <c>MeetingTranscriber.Cli.Tests</c> because the rule is
/// here: <c>docs/layout.md</c> says <c>MeetingTranscriber.Cli</c> holds no rule of its own, and a
/// suite proving a rule from the other side of a project boundary is how a rule ends up living
/// wherever its first caller happened to be. The confirmation, the ceiling arithmetic and the
/// refusals are the command line's and stay in <c>LiveDeepgramTests</c>.
/// </para>
/// <para>
/// What it reads is <c>tests/fixtures/deepgram/</c>: real responses with every word replaced from a
/// closed vocabulary and every timing, confidence and channel number left as sent. Nothing here
/// touches the network and nothing here spends.
/// </para>
/// </remarks>
public sealed class LiveInvariantsTests
{
    /// <summary>
    /// Declared here rather than on <see cref="DeepgramFixtures"/> for the reason that type's own
    /// remarks give: xunit's analyzer crashes on a <c>MemberData</c> whose member lives in another
    /// assembly, and a crashed analyzer is a warning CI fails on.
    /// </summary>
    public static TheoryData<string> Fixtures => new(DeepgramFixtures.All);

    /// <summary>
    /// Every committed response holds everything a live call is checked for.
    /// </summary>
    /// <remarks>
    /// It is also what stops a rule being written that reads a speaker label's list position as the
    /// provider's speaker number. All five fixtures number their speakers contiguously from zero,
    /// so such a rule would go green here and fail for the first time on a paid call — which is why
    /// <see cref="LiveInvariants"/> checks what the provider decides and nothing the parser built.
    /// </remarks>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public void Every_committed_response_holds_every_invariant(string fixture)
    {
        var read = Read(fixture);

        var verdict = LiveInvariants.Of(read, Sent(fixture, read));

        verdict.Broken.ShouldBeEmpty();
        verdict.Held.ShouldBeTrue();
        verdict.Turns.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// A response describing audio nobody sent is the failure only a real call can produce, and the
    /// one worth every other rule being left out for.
    /// </summary>
    [Fact]
    public void A_response_whose_length_is_not_the_audio_s_is_named()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);
        var sent = Sent(DeepgramFixtures.TwoChannelShort, read) with
        {
            Length = read.Audio + Duration.FromSeconds(120),
        };

        var verdict = LiveInvariants.Of(read, sent);

        verdict.Held.ShouldBeFalse();
        verdict.Broken.Count.ShouldBe(1);
        verdict.Broken[0].ShouldContain($"{read.Audio}");
        verdict.Broken[0].ShouldContain($"{sent.Length}");
        verdict.Broken[0].ShouldContain(sent.File.Name);
    }

    /// <summary>
    /// A provider reports its own rounding of what it received and this end counts frames, so the
    /// two never agree to the millisecond. Comparing for equality would fail every real call.
    /// </summary>
    [Fact]
    public void A_length_a_second_out_is_not_a_failure()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);
        var sent = Sent(DeepgramFixtures.TwoChannelShort, read) with
        {
            Length = read.Audio + Duration.FromMilliseconds(900),
        };

        LiveInvariants.Of(read, sent).Held.ShouldBeTrue();
    }

    /// <summary>
    /// The response's two halves against each other, which nothing else compares: the parser holds
    /// an utterance's start against its own end and neither against the length it reports.
    /// </summary>
    [Fact]
    public void An_utterance_after_the_end_of_the_audio_is_named()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);
        var beyond = read with
        {
            Segments =
            [
                .. read.Segments,
                new SpeechSegment(
                    read.Audio + Duration.FromSeconds(30),
                    read.Audio + Duration.FromSeconds(35),
                    AudioChannel.Loopback,
                    "ch0:speaker_0",
                    "coati"),
            ],
        };

        var verdict = LiveInvariants.Of(beyond, Sent(DeepgramFixtures.TwoChannelShort, read));

        verdict.Held.ShouldBeFalse();
        verdict.Broken.Count.ShouldBe(1);
        verdict.Broken[0].ShouldContain("1 utterance(s)");
        verdict.Broken[0].ShouldContain($"{read.Audio}");
    }

    /// <summary>
    /// What a run in the wrong language produces, and what a live probe exists to catch: a response
    /// that came back whole, cost money and holds nobody.
    /// </summary>
    [Fact]
    public void A_response_nobody_was_heard_in_is_a_failure()
    {
        var read = Read(DeepgramFixtures.TwoChannelShort);

        var verdict = LiveInvariants.Of(
            read with { Segments = [] }, Sent(DeepgramFixtures.TwoChannelShort, read));

        verdict.Turns.ShouldBe(0);
        verdict.Held.ShouldBeFalse();
        verdict.Broken.Count.ShouldBe(1);
        verdict.Broken[0].ShouldContain("nothing was heard");
        verdict.Broken[0].ShouldContain("language");
    }

    /// <summary>
    /// A channel that carried nobody was transcribed and was charged for, so it is said — and it is
    /// not a reason to fail a run, because a meeting where one side never spoke is a meeting.
    /// </summary>
    [Fact]
    public void A_silent_channel_is_said_and_is_not_a_failure()
    {
        var read = Read(DeepgramFixtures.TwoChannelSilentMe);

        var verdict = LiveInvariants.Of(read, Sent(DeepgramFixtures.TwoChannelSilentMe, read));

        verdict.Held.ShouldBeTrue();
        verdict.Broken.ShouldBeEmpty();
        verdict.WorthSaying.Count.ShouldBe(1);
        verdict.WorthSaying[0].ShouldContain("channel 1");
        verdict.WorthSaying[0].ShouldContain("the microphone");
    }

    /// <summary>
    /// A rule and not an example: what a live check may compare is structure. A provider rewords
    /// itself between models and between days, and a check against its words is a check that goes
    /// red for a reason nobody can act on — on a call somebody paid for.
    /// </summary>
    [Fact]
    public void Nothing_here_reads_a_word_of_what_was_said()
    {
        var read = Read(DeepgramFixtures.TwoChannelLong);
        var sent = Sent(DeepgramFixtures.TwoChannelLong, read);
        var reworded = read with
        {
            Segments = [.. read.Segments.Select(segment => segment with { Text = "coati" })],
        };

        var said = LiveInvariants.Of(read, sent);
        var otherwise = LiveInvariants.Of(reworded, sent);

        otherwise.Turns.ShouldBe(said.Turns);
        otherwise.Broken.ShouldBe(said.Broken);
        otherwise.WorthSaying.ShouldBe(said.WorthSaying);
    }

    /// <summary>A committed response, read exactly as the command reads a paid one.</summary>
    private static DeepgramTranscript Read(string fixture) => DeepgramTranscriptParser.ParseFile(
        DeepgramFixtures.PathOf(fixture), DeepgramFixtures.ProfileOf(fixture));

    /// <summary>
    /// The audio that response is a transcription of. The file is named and never opened: what a
    /// fixture is missing is its audio, and every rule under test is about the response.
    /// </summary>
    private static LiveAudio Sent(string fixture, DeepgramTranscript read) => new(
        new FileInfo(fixture + ".wav"), DeepgramFixtures.ProfileOf(fixture), read.Audio);
}
