using System.Net;
using System.Security.Cryptography;
using System.Text;

using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// The call, against a provider that is not there. Offline: every answer comes from
/// <see cref="FakeDeepgram"/>, which reads the committed fixtures and opens no socket.
/// </summary>
public sealed class DeepgramTranscriptionTests : IDisposable
{
    private const string Key = "not-a-real-key-0123456789";

    private readonly DirectoryInfo folder = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public DeepgramTranscriptionTests() => folder.Create();

    /// <summary>
    /// Declared here rather than on <see cref="DeepgramFixtures"/> for the reason that type's own
    /// remarks give: xunit's analyzer crashes on a <c>MemberData</c> whose member lives in another
    /// assembly, and a crashed analyzer is a warning CI fails on.
    /// </summary>
    public static TheoryData<string> Fixtures => new(DeepgramFixtures.All);

    /// <summary>
    /// A meeting this application recorded goes up as two channels, and what comes back reads as
    /// two channels. Red when <c>OptionsFor</c> stops adding <c>multichannel</c>.
    /// </summary>
    [Fact]
    public async Task A_two_channel_meeting_goes_up_as_multichannel_and_comes_back_readable()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.TwoChannelShort);

        var (_, bytes) = await CallAsync(fake, SourceProfile.Multichannel);

        var query = fake.Asked!.RequestUri!.Query;
        query.ShouldContain("multichannel=true");
        query.ShouldContain("diarize=true");

        var transcript = DeepgramTranscriptParser.Parse(
            Encoding.UTF8.GetString(bytes), SourceProfile.Multichannel);
        transcript.Channels.Count.ShouldBe(2);
        transcript.Segments.ShouldNotBeEmpty();
    }

    /// <summary>
    /// A single-track file goes up without <c>multichannel</c>. Red when the switch is added
    /// unconditionally — the defect that sends a one-track file up as two and is refused by the
    /// provider after the audio has already gone.
    /// </summary>
    [Fact]
    public async Task A_single_track_file_goes_up_as_diarize_and_comes_back_readable()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.SingleTrackDiarized);

        var (_, bytes) = await CallAsync(fake, SourceProfile.Diarize, OneTrack());

        var query = fake.Asked!.RequestUri!.Query;
        query.ShouldNotContain("multichannel");
        query.ShouldContain("diarize=true");

        var transcript = DeepgramTranscriptParser.Parse(
            Encoding.UTF8.GetString(bytes), SourceProfile.Diarize);
        transcript.Channels.Count.ShouldBe(1);
        transcript.Segments.ShouldNotBeEmpty();
    }

    /// <summary>
    /// Every committed response, under the profile it was transcribed under. Asked of the
    /// inventory, so a sixth fixture is covered by adding it there and nowhere else.
    /// </summary>
    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task Every_committed_response_comes_back_whole_under_its_own_profile(string fixture)
    {
        var profile = DeepgramFixtures.ProfileOf(fixture);
        using var fake = FakeDeepgram.Answering(fixture);

        var (_, bytes) = await CallAsync(fake, profile);

        var transcript = DeepgramTranscriptParser.Parse(Encoding.UTF8.GetString(bytes), profile);
        transcript.Channels.Count.ShouldBe(profile.ChannelCount());
    }

    /// <summary>
    /// Red the day somebody moves the key into the query string, which is where it would reach the
    /// provider's own request log and every proxy in between.
    /// </summary>
    [Fact]
    public async Task The_key_travels_in_one_header_and_appears_nowhere_else()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.TwoChannelShort);

        await CallAsync(fake, SourceProfile.Multichannel);

        fake.Asked!.Headers.Authorization!.Scheme.ShouldBe("Token");
        fake.Asked!.Headers.Authorization!.Parameter.ShouldBe(Key);
        fake.Asked!.RequestUri!.ToString().ShouldNotContain(Key);
        fake.Asked!.Headers
            .Where(header => header.Key != "Authorization")
            .SelectMany(header => header.Value)
            .ShouldAllBe(value => !value.Contains(Key));
    }

    /// <summary>
    /// Red the day anything here normalises, re-serialises or trims a paid response.
    /// </summary>
    [Fact]
    public async Task The_bytes_that_come_back_are_written_exactly_as_they_arrived()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.TwoChannelShort);

        var (_, bytes) = await CallAsync(fake, SourceProfile.Multichannel);

        Sha256Of(bytes).ShouldBe(Sha256Of(
            File.ReadAllBytes(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort))));
    }

    /// <summary>
    /// Red the day the count is read off <c>Content-Length</c>, which is null here and null in
    /// production and would report zero after megabytes were written.
    /// </summary>
    [Fact]
    public async Task What_was_written_is_what_the_call_says_it_wrote()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.TwoChannelShort);

        var (written, bytes) = await CallAsync(fake, SourceProfile.Multichannel);

        written.ShouldBe(
            new FileInfo(DeepgramFixtures.PathOf(DeepgramFixtures.TwoChannelShort)).Length);
        written.ShouldBe(bytes.Length);
    }

    /// <summary>
    /// The one that guards the whole point of the class: a response this build cannot read is
    /// still money that was spent, so it is written whole and judged afterwards.
    /// </summary>
    [Fact]
    public async Task A_response_this_build_cannot_read_is_still_written_whole()
    {
        using var fake = FakeDeepgram.AnsweringWith(HttpStatusCode.OK, "not json at all");

        var (written, bytes) = await CallAsync(fake, SourceProfile.Multichannel);

        written.ShouldBe(15);
        Encoding.UTF8.GetString(bytes).ShouldBe("not json at all");
    }

    /// <summary>Red when the refusal starts quoting what it sent.</summary>
    [Fact]
    public async Task A_key_the_provider_will_not_take_says_so_without_repeating_it()
    {
        using var fake = FakeDeepgram.AnsweringWith(
            HttpStatusCode.Unauthorized,
            """{"err_code":"INVALID_AUTH","err_msg":"Token is invalid or expired"}""");

        var refused = await ShouldRefuseAsync(fake, SourceProfile.Multichannel);

        refused.Message.ShouldContain("401");
        refused.Message.ShouldContain("Token is invalid or expired");
        refused.Message.ShouldNotContain(Key);
    }

    /// <summary>
    /// Red the day a certainty is put back on an arm that cannot have one: a connection that
    /// dropped mid-upload leaves Deepgram holding a whole file it may already have transcribed.
    /// </summary>
    [Fact]
    public async Task A_provider_that_could_not_be_reached_promises_nothing_about_the_charge()
    {
        using var fake = FakeDeepgram.Unreachable();

        var refused = await ShouldRefuseAsync(fake, SourceProfile.Multichannel);

        refused.InnerException.ShouldBeOfType<HttpRequestException>();
        refused.Message.ShouldContain("is not something this end can tell");
        refused.Message.ShouldNotContain("nothing was charged");
    }

    /// <summary>
    /// Red when the <see cref="TaskCanceledException"/> arm is removed and a timeout surfaces as a
    /// bare cancellation, which reads like somebody pressed cancel.
    /// </summary>
    [Fact]
    public async Task A_call_that_ran_out_of_time_promises_nothing_about_the_charge()
    {
        using var fake = FakeDeepgram.NeverAnswering();

        var refused = await ShouldRefuseAsync(
            fake, SourceProfile.Multichannel, timeout: TimeSpan.FromMilliseconds(200));

        refused.Message.ShouldContain("did not answer within the time this call was given");
        refused.Message.ShouldContain("is not something this end can tell");
    }

    /// <summary>
    /// The other half of the same guard. Red when
    /// <c>when (!cancellation.IsCancellationRequested)</c> comes off and somebody pressing cancel
    /// is reported as the provider timing out.
    /// </summary>
    [Fact]
    public async Task A_call_somebody_cancelled_is_a_cancellation_and_not_a_provider_failure()
    {
        using var fake = FakeDeepgram.NeverAnswering();
        using var cancelling = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Should.ThrowAsync<OperationCanceledException>(
            () => CallAsync(fake, SourceProfile.Multichannel, cancellation: cancelling.Token));
    }

    /// <summary>
    /// Red when the 5xx arm is folded into the plain refusal arm. A 500 after the audio was
    /// accepted may have been billed, and telling somebody it was not is the one lie worth a
    /// branch.
    /// </summary>
    [Fact]
    public async Task A_failure_on_the_providers_own_side_promises_nothing_about_the_charge()
    {
        using var fake = FakeDeepgram.AnsweringWith(HttpStatusCode.InternalServerError, "");

        var refused = await ShouldRefuseAsync(fake, SourceProfile.Multichannel);

        refused.Message.ShouldContain("500");
        refused.Message.ShouldContain("is not something this end can tell");
    }

    /// <summary>
    /// Red the day the copy is left uncaught and a truncated paid response leaves this call looking
    /// like a success — a <c>deepgram.json</c> holding a prefix of JSON, and a second paid call to
    /// find out.
    /// </summary>
    [Fact]
    public async Task A_response_that_stopped_partway_is_never_offered_as_a_response()
    {
        using var fake = FakeDeepgram.AnsweringPartOf(DeepgramFixtures.TwoChannelShort, 4096);
        using var written = new MemoryStream();

        var refused = await Should.ThrowAsync<DeepgramCallException>(
            () => SendAsync(fake, SourceProfile.Multichannel, written));

        refused.Message.ShouldContain("did not arrive whole");
        refused.Message.ShouldContain("charged");

        // Assignable and not exact: .NET's HTTP/2 path raises HttpIOException, which is an
        // IOException and is what the call promises to have handled.
        refused.InnerException.ShouldBeAssignableTo<IOException>();

        // The other half of the contract, and the reason the sentence says "must not be filed":
        // the prefix is in the caller's stream and this call cannot take it back.
        written.ToArray().Length.ShouldBe(4096);
    }

    /// <summary>
    /// A disk that fills up while a response is being written is not Deepgram truncating it, and is
    /// not reported as one. Red the day the copy guards the write as well as the read: whoever met
    /// that message would go and look at a provider status page over a full disk.
    /// </summary>
    [Fact]
    public async Task A_destination_that_will_not_take_the_bytes_is_not_blamed_on_the_provider()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.TwoChannelShort);
        using var full = new NoRoom();

        var refused = await Should.ThrowAsync<IOException>(
            () => SendAsync(fake, SourceProfile.Multichannel, full));

        refused.ShouldNotBeOfType<DeepgramCallException>();
        refused.Message.ShouldBe(NoRoom.WhatADiskSays);
    }

    /// <summary>
    /// The body read is bounded by the caller's token and by nothing else — <c>HttpClient.Timeout</c>
    /// is finished once the headers are read. So a connection that goes quiet after Deepgram
    /// answered ends when the caller says so, as the cancellation they asked for rather than as a
    /// provider failure, and the fragment is in their stream where the contract says it is.
    /// </summary>
    /// <remarks>
    /// Red when the copy's guard goes back to a bare type test. A read killed by a token arrives as
    /// a <c>TaskCanceledException</c> whose inner exception is an <c>IOException</c>, so
    /// <c>is HttpRequestException or IOException</c> does not see it — and without this test
    /// nothing in the suite reaches a stall that happens after the headers.
    /// </remarks>
    [Fact]
    public async Task A_body_that_goes_quiet_ends_when_the_caller_says_so()
    {
        using var fake = FakeDeepgram.AnsweringAndGoingQuiet(DeepgramFixtures.TwoChannelShort, 4096);
        using var written = new MemoryStream();
        using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var stopped = await Should.ThrowAsync<OperationCanceledException>(
            () => SendAsync(fake, SourceProfile.Multichannel, written, giveUp.Token));

        written.ToArray().Length.ShouldBe(4096);
    }

    /// <summary>
    /// A file that is not there is answered before anything is sent, so nobody pays for a call that
    /// had nothing to put in it. Red the day the request is built and dispatched first, when the
    /// same missing file becomes a refusal from the provider after a round trip.
    /// </summary>
    [Fact]
    public async Task A_file_that_is_not_there_is_answered_before_the_call_is_made()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.TwoChannelShort);
        var missing = new FileInfo(Path.Combine(folder.FullName, "no-such-meeting.wav"));

        var lost = await Should.ThrowAsync<FileNotFoundException>(
            () => CallAsync(fake, SourceProfile.Multichannel, missing));

        lost.FileName.ShouldBe(missing.FullName);
        fake.Asked.ShouldBeNull();
    }

    /// <summary>Red the day the audio goes up as multipart, which this endpoint does not take.</summary>
    [Fact]
    public async Task The_audio_is_the_body_and_not_a_form()
    {
        using var fake = FakeDeepgram.Answering(DeepgramFixtures.TwoChannelShort);
        var audio = BothChannels();

        await CallAsync(fake, SourceProfile.Multichannel, audio);

        fake.Asked!.Method.ShouldBe(HttpMethod.Post);
        fake.Asked!.Content!.Headers.ContentType!.MediaType.ShouldBe("audio/wav");
        fake.Sent.ShouldBe(File.ReadAllBytes(audio.FullName));
    }

    public void Dispose()
    {
        try
        {
            folder.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp folder is not worth failing a green test over.
        }
    }

    private static string Sha256Of(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    /// <remarks>
    /// <paramref name="cancellation"/> is a nullable rather than a defaulted
    /// <see cref="CancellationToken"/>, and that is xunit's rule rather than a preference: xUnit1051
    /// refuses a call that takes a token and is handed none, so what a test does not name resolves
    /// here to the test's own token — which is what the rest of this repository passes.
    /// </remarks>
    private async Task<(long Written, byte[] Bytes)> CallAsync(
        FakeDeepgram fake,
        SourceProfile profile,
        FileInfo? audio = null,
        TimeSpan? timeout = null,
        CancellationToken? cancellation = null)
    {
        using var written = new MemoryStream();

        var count = await SendAsync(fake, profile, written, cancellation, audio, timeout);

        return (count, written.ToArray());
    }

    /// <summary>
    /// The call itself, writing where the test can still read afterwards. The tests about what is
    /// left in the caller's stream when the call throws need that; the rest go through
    /// <see cref="CallAsync"/>, which hands back the bytes and disposes it.
    /// </summary>
    private async Task<long> SendAsync(
        FakeDeepgram fake,
        SourceProfile profile,
        Stream into,
        CancellationToken? cancellation = null,
        FileInfo? audio = null,
        TimeSpan? timeout = null)
    {
        using var client = fake.Client(timeout);

        return await new DeepgramTranscription(client).SendAsync(
            audio ?? BothChannels(),
            new DeepgramRequest(profile, "es"),
            Key,
            into,
            cancellation ?? TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// The same call, for the tests that are about what it refuses with. Spelled apart from
    /// <see cref="CallAsync"/> because they want the exception rather than the tuple.
    /// </summary>
    private async Task<DeepgramCallException> ShouldRefuseAsync(
        FakeDeepgram fake, SourceProfile profile, TimeSpan? timeout = null) =>
        await Should.ThrowAsync<DeepgramCallException>(() => CallAsync(fake, profile, timeout: timeout));

    /// <summary>
    /// A two-channel WAV, which is what a meeting this application recorded is. Nothing on this
    /// side of the call reads a byte of it, so the levels and the length are whatever is cheapest
    /// to write — but the channel count is not free to be wrong, because it is half of what the
    /// profile in the same call is claiming.
    /// </summary>
    private FileInfo BothChannels() => Wav(0.5f, 0.25f);

    /// <summary>A single-track WAV, which is what <see cref="SourceProfile.Diarize"/> is for.</summary>
    private FileInfo OneTrack() => Wav(0.5f);

    private FileInfo Wav(params float[] levels) => ForeignWav.Steady(
        new FileInfo(Path.Combine(folder.FullName, $"{Guid.NewGuid():n}.wav")),
        rate: 48_000,
        frames: 480,
        levels);

    /// <summary>A caller's stream that cannot take what it is handed, the way a full disk cannot.</summary>
    private sealed class NoRoom : Stream
    {
        internal const string WhatADiskSays = "There is not enough space on the disk.";

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new IOException(WhatADiskSays);
    }
}
