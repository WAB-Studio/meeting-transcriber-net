using System.Diagnostics;
using System.Net;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// A provider that is not there: it records what it was asked and answers with a committed fixture,
/// a status, a body that stops partway, a body that goes quiet, or nothing at all.
/// </summary>
/// <remarks>
/// <para>
/// A message handler and not a listener on localhost. The rule is that nothing under test reaches
/// the network, and a loopback socket is still the network stack — with a port to bind, a race to
/// lose on a busy agent, and a firewall prompt on somebody's machine. A handler answers the same
/// question with none of that, and it can be asked what was sent, which a socket cannot without a
/// second parser.
/// </para>
/// <para>
/// It lives in this suite and not in <c>MeetingTranscriber.Testing</c>. One suite calls it, and a
/// fake with one caller moved into the shared project is a type every other suite has to read past.
/// The day a second suite needs it is the day it moves.
/// </para>
/// <para>
/// Every body it answers with comes from <c>tests/fixtures/deepgram/</c> through
/// <see cref="DeepgramFixtures"/>, or is a literal written in the test that uses it. Nothing here
/// opens a socket and nothing here reads a response the fixture set does not name.
/// </para>
/// </remarks>
internal sealed class FakeDeepgram : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer;

    private FakeDeepgram(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer) =>
        this.answer = answer;

    /// <summary>
    /// The last call, for its method, its URI and its headers. Its content has been disposed by the
    /// time a test reads this, which is why the body is kept separately as <see cref="Sent"/>.
    /// </summary>
    internal HttpRequestMessage? Asked { get; private set; }

    /// <summary>
    /// The bytes of the last call's body, read here because <see cref="Asked"/>'s content is
    /// disposed with the request and cannot be read back afterwards.
    /// </summary>
    internal byte[] Sent { get; private set; } = [];

    /// <summary>Answers every call with this committed fixture.</summary>
    internal static FakeDeepgram Answering(string fixture) => Serving(
        () => new Arriving(File.OpenRead(DeepgramFixtures.PathOf(fixture)), HowItEnds.Whole));

    /// <summary>
    /// Answers with a 200 and the first <paramref name="bytes"/> of a committed fixture, then drops
    /// the connection — a transcription that was paid for and did not arrive.
    /// </summary>
    internal static FakeDeepgram AnsweringPartOf(string fixture, int bytes) => Serving(
        () => new Arriving(new MemoryStream(FirstOf(fixture, bytes)), HowItEnds.Truncated));

    /// <summary>
    /// Answers with a 200 and the first <paramref name="bytes"/> of a committed fixture, then never
    /// sends another one and never closes — a load balancer holding a socket open, or a proxy that
    /// stopped forwarding. Nothing on the client bounds this: <c>HttpClient.Timeout</c> is done
    /// once the headers are read, so only the caller's own token ends it.
    /// </summary>
    internal static FakeDeepgram AnsweringAndGoingQuiet(string fixture, int bytes) => Serving(
        () => new Arriving(new MemoryStream(FirstOf(fixture, bytes)), HowItEnds.Quiet));

    /// <summary>Answers every call with this status and these bytes, whatever they are.</summary>
    internal static FakeDeepgram AnsweringWith(HttpStatusCode status, string body) => new(
        (_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) }));

    /// <summary>Does not answer at all, the way a machine with no route out does not.</summary>
    internal static FakeDeepgram Unreachable() => new(
        (_, _) => throw new HttpRequestException("No such host is known."));

    /// <summary>
    /// Takes the call and never comes back, so whatever timeout the client carries is what ends it.
    /// </summary>
    internal static FakeDeepgram NeverAnswering() => new(
        async (_, waiting) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, waiting);
            throw new UnreachableException();
        });

    /// <summary>
    /// A client over this handler. A minute by default, which is generous for an answer that comes
    /// off disk, and short on purpose where a test is about running out of time.
    /// </summary>
    internal HttpClient Client(TimeSpan? timeout = null) =>
        new(this) { Timeout = timeout ?? TimeSpan.FromMinutes(1) };

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Asked = request;
        Sent = request.Content is null
            ? []
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        return await answer(request, cancellationToken);
    }

    private static FakeDeepgram Serving(Func<Stream> body) => new(
        (_, _) => Task.FromResult(
            new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(body()) }));

    private static byte[] FirstOf(string fixture, int bytes)
    {
        using var file = File.OpenRead(DeepgramFixtures.PathOf(fixture));
        var first = new byte[bytes];
        file.ReadExactly(first);
        return first;
    }

    /// <summary>What a body does once it has handed over everything it was given.</summary>
    private enum HowItEnds
    {
        /// <summary>Ends, the way a whole response does.</summary>
        Whole,

        /// <summary>Drops, the way a connection cut mid-response does.</summary>
        Truncated,

        /// <summary>Neither. It stops sending and stays open.</summary>
        Quiet,
    }

    /// <summary>
    /// The response body as it really arrives: forward only, with no length in front of it, and
    /// ending the way it is told to.
    /// </summary>
    /// <remarks>
    /// <b>The seekability is the point.</b> <see cref="StreamContent"/> takes
    /// <c>Content-Length</c> off a stream that can seek, and a real transcription comes back
    /// chunked with no length at all. Handing a fixture over as a <see cref="FileStream"/> would
    /// give every test here a header to read, and a call counting bytes off that header instead of
    /// off the copy would pass all of them and return zero in production.
    /// </remarks>
    private sealed class Arriving(Stream behind, HowItEnds ends) : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <remarks>
        /// <see cref="HowItEnds.Quiet"/> cannot be done here: a synchronous read has no token to
        /// wait on, so blocking would wedge the test rather than ending it. It refuses instead, so
        /// that a day when the read arrives synchronously is a red test and never a hang.
        /// </remarks>
        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = behind.Read(buffer, offset, count);

            return read > 0 ? read : Ended();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await behind.ReadAsync(buffer, cancellationToken);
            if (read > 0)
            {
                return read;
            }

            if (ends is not HowItEnds.Quiet)
            {
                return Ended();
            }

            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }

        /// <summary>What comes back once there is nothing left behind this to hand over.</summary>
        private int Ended() => ends switch
        {
            HowItEnds.Whole => 0,
            HowItEnds.Truncated =>
                throw new IOException("The connection was closed before the response ended."),
            _ => throw new NotSupportedException(
                "A body that goes quiet has to be read asynchronously to be waited on."),
        };

        public override void Flush() => behind.Flush();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) =>
            throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                behind.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
