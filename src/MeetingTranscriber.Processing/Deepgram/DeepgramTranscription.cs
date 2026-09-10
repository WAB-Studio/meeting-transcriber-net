using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// One call to Deepgram, with the user's key, that writes what came back and reads none of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>It saves and does not parse, and the order is the whole of what it is for.</b> A response is
/// a paid artifact: the money is spent the moment Deepgram answers, so every byte reaches the
/// caller's stream before anything asks whether it is JSON, whether it has the channels the profile
/// promises, or whether this build can read it at all. A call that parsed first would throw a
/// response away for being unreadable by today's parser — which is exactly the artifact that cannot
/// be obtained again. Reading it is <see cref="DeepgramTranscriptParser"/>'s, afterwards, from what
/// was written.
/// </para>
/// <para>
/// The key is a <see cref="string"/> and reaches this from a front end that is already Windows.
/// This project is plain <c>net10.0</c> and names neither the type a key is stored under nor the
/// credential store it comes out of; <c>docs/layout.md</c> says why, and a naming test in
/// <c>MeetingTranscriber.Infrastructure.Tests</c> fails on a third file naming that type — in a
/// comment as readily as in code, which is why this sentence is spelled the long way round. The key
/// is held in no field here either: it is a parameter for the length of one call, put in one
/// header, and never in the URL, where it would be in the provider's request log and in every proxy
/// between here and there.
/// </para>
/// <para>
/// <b>Nothing here retries.</b> A call that may already have been charged for is not something this
/// application repeats on its own — that is the contract's own sentence about a job a restart found
/// running, and it holds a level lower too. A refusal comes back as one refusal.
/// </para>
/// </remarks>
public sealed class DeepgramTranscription
{
    /// <summary>
    /// How much of the response one read moves at most — <see cref="Stream.CopyTo(Stream)"/>'s own
    /// default, which is what this replaced.
    /// </summary>
    private const int ChunkBytes = 81_920;

    /// <summary>
    /// How much of a refusal's own body is quoted back. A 502 from something between here and
    /// Deepgram is commonly a page of HTML, and a person at a prompt should get the sentence rather
    /// than the page.
    /// </summary>
    private const int MostOfARefusalWorthQuoting = 400;

    private readonly HttpClient http;

    /// <param name="http">
    /// The client to send on, whose timeout is the caller's to set.
    /// <para>
    /// <b>Two things about that timeout, both of which cost money to find out.</b>
    /// <see cref="HttpClient.Timeout"/> covers the upload as well as the wait, and it defaults to
    /// 100 seconds: a 58-minute meeting is tens of megabytes going up and minutes of transcription
    /// coming back, so on the default every real call dies — as a
    /// <see cref="TaskCanceledException"/>, which reads like somebody pressed cancel. And it stops
    /// covering anything once the response headers arrive, because this call reads the body itself:
    /// a connection that goes quiet after Deepgram answers is not bounded by the client at all.
    /// What bounds it is the <see cref="CancellationToken"/> handed to
    /// <see cref="SendAsync(FileInfo, DeepgramRequest, string, Stream, CancellationToken)"/>, which
    /// is where a caller that has a deadline puts it. Nothing is enforced here: the client is the
    /// caller's, and a constructor refusing a short one would be wrong on the day somebody
    /// transcribes a thirty-second clip.
    /// </para>
    /// </param>
    public DeepgramTranscription(HttpClient http)
    {
        ArgumentNullException.ThrowIfNull(http);

        this.http = http;
    }

    /// <summary>
    /// Sends <paramref name="audio"/> under <paramref name="request"/> and writes every byte that
    /// comes back into <paramref name="response"/>.
    /// </summary>
    /// <param name="audio">
    /// The file to transcribe. Nothing here opens it as audio, so nothing here can tell whether it
    /// has the channels <paramref name="request"/>'s profile promises — the contract's rule that a
    /// profile disagreeing with its audio throws is the caller's to have satisfied before it spends
    /// money, and <c>AudioIntake</c> is where that is enforced for a file this application takes in.
    /// </param>
    /// <param name="response">
    /// Where the bytes go. <b>On every failure after the first byte it holds a prefix of a paid
    /// response, and discarding it is the caller's.</b> This call cannot unwind what it has already
    /// written into a stream somebody else opened, so a caller writing straight at
    /// <c>deepgram.json</c> would be left with a truncated artifact; whoever files one writes
    /// somewhere it can abandon and moves it into place on success.
    /// </param>
    /// <returns>
    /// How many bytes were written into <paramref name="response"/>, counted as they were copied.
    /// Not read off <c>Content-Length</c>: a transcription comes back chunked and carries none.
    /// </returns>
    /// <exception cref="DeepgramCallException">
    /// The provider refused the request, failed it, could not be reached, or answered and the
    /// answer did not arrive whole.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="cancellation"/> was cancelled. It is the only thing that bounds the read of
    /// the response body, so a caller with a deadline meets this rather than a refusal.
    /// </exception>
    public async Task<long> SendAsync(
        FileInfo audio,
        DeepgramRequest request,
        string key,
        Stream response,
        CancellationToken cancellation = default)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(response);

        // Opened before anything is sent, and by name: a path that is not there comes back as a
        // FileNotFoundException naming the file, which is a better answer than a call that was made
        // and had nothing to put in it.
        await using var content = audio.OpenRead();

        using var message = new HttpRequestMessage(HttpMethod.Post, request.At())
        {
            Content = new StreamContent(content)
            {
                Headers = { ContentType = new MediaTypeHeaderValue("audio/wav") },
            },
        };

        // Deepgram's own scheme, and the only place the key appears.
        message.Headers.Authorization = new AuthenticationHeaderValue("Token", key);

        HttpResponseMessage answered;
        try
        {
            // ResponseHeadersRead, so a response of several megabytes streams into the caller's
            // stream instead of being built in memory first.
            answered = await http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellation)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException noAnswer)
        {
            // One arm and not two. HttpRequestException covers a name that would not resolve, a
            // connection that was refused, and a connection that died while the audio was going up
            // or while this end waited for the headers — and the last of those is the biggest
            // window in the call, with a complete file already at the provider. Nothing on the
            // exception separates them, so nothing here claims to.
            throw new DeepgramCallException(
                "Deepgram could not be reached, or the connection did not survive the call. "
                + "Whether it charged for the request is not something this end can tell, so "
                + $"nothing is sent again on its own: {noAnswer.Message}",
                noAnswer);
        }
        catch (OperationCanceledException ranOut) when (ranOut.InnerException is TimeoutException)
        {
            // The client's own timeout and not somebody pressing cancel, told apart by the signal
            // .NET actually gives: HttpClient wraps its timeout as a cancellation carrying an inner
            // TimeoutException, and a token cancellation carries none. Asking instead whether the
            // caller's token is cancelled would turn a genuine timeout into a bare cancellation for
            // anybody who cancelled in the same instant.
            throw new DeepgramCallException(
                "Deepgram did not answer within the time this call was given. Whether it charged "
                + "for the request is not something this end can tell, so nothing is sent again on "
                + "its own.",
                ranOut);
        }

        using (answered)
        {
            if (!answered.IsSuccessStatusCode)
            {
                throw await RefusedAsync(answered, cancellation).ConfigureAwait(false);
            }

            await using var body = await answered.Content
                .ReadAsStreamAsync(cancellation)
                .ConfigureAwait(false);

            var written = await CopyAsync(body, response, cancellation).ConfigureAwait(false);
            await response.FlushAsync(cancellation).ConfigureAwait(false);
            return written;
        }
    }

    /// <summary>
    /// Copies <paramref name="from"/> into <paramref name="to"/>, counting what actually moved.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Not <see cref="Stream.CopyToAsync(Stream, CancellationToken)"/> with a length read
    /// afterwards, for two reasons that both matter. The count: a transcription comes back chunked,
    /// so <c>Content-Length</c> is absent and a number taken from it is zero after megabytes were
    /// written, while one taken from the destination's <see cref="Stream.Position"/> is wrong the
    /// other way for a destination that already held bytes and unavailable for one that cannot
    /// seek. What was copied is the only number this end knows.
    /// </para>
    /// <para>
    /// And the blame: only the read is guarded. A write that fails is the caller's disk, their
    /// handle or their quota, and telling them Deepgram truncated the response would send somebody
    /// to a status page over a full disk. <c>CopyToAsync</c> cannot tell the two apart, and this is
    /// the one class whose whole job is to say true things about what happened to a paid call.
    /// </para>
    /// </remarks>
    private static async Task<long> CopyAsync(Stream from, Stream to, CancellationToken cancellation)
    {
        var chunk = new byte[ChunkBytes];
        long written = 0;

        while (true)
        {
            int read;
            try
            {
                read = await from.ReadAsync(chunk, cancellation).ConfigureAwait(false);
            }
            catch (Exception cutOff) when (EndedTheBody(cutOff))
            {
                // The one failure where this end does know it was billed: the provider answered
                // 200, so it transcribed. What is in the caller's stream is a prefix of a paid
                // response and is worth nothing — filing it would put a truncated deepgram.json
                // where the artifact goes and cost somebody a second call to find out.
                throw new DeepgramCallException(
                    "Deepgram answered and the response did not arrive whole. It transcribed the "
                    + "audio and it charged for it, and what reached this call is a fragment: it "
                    + "is not a response and must not be filed as one.",
                    cutOff);
            }

            if (read == 0)
            {
                return written;
            }

            await to.WriteAsync(chunk.AsMemory(0, read), cancellation).ConfigureAwait(false);
            written += read;
        }
    }

    /// <summary>
    /// Whether this is the response body ending before it was finished, rather than the caller
    /// calling the whole thing off.
    /// </summary>
    /// <remarks>
    /// <b>A cancellation is deliberately not one of these, and the distinction was measured.</b> A
    /// connection genuinely dropped mid-body arrives as a plain <see cref="IOException"/>; a read
    /// killed by a token arrives as a <see cref="TaskCanceledException"/> whose <em>inner</em>
    /// exception is an <see cref="IOException"/>, so it does not match here and is not dressed up as
    /// a provider failure. The only token that can end this read is the caller's own — the client's
    /// timeout is finished once the headers are read — so a cancellation here is something they
    /// asked for and is handed back as itself. What they are owed is knowing that their stream now
    /// holds a prefix of a paid response, and that is said on
    /// <see cref="SendAsync(FileInfo, DeepgramRequest, string, Stream, CancellationToken)"/> where
    /// they are already reading.
    /// </remarks>
    private static bool EndedTheBody(Exception cutOff) => cutOff is HttpRequestException or IOException;

    /// <summary>
    /// What Deepgram said no with. Its body is JSON carrying <c>err_code</c> and <c>err_msg</c> on
    /// a refusal, and plain text or nothing on a failure — so the body is read as far as it goes and
    /// what cannot be read is left out rather than guessed at.
    /// </summary>
    private static async Task<DeepgramCallException> RefusedAsync(
        HttpResponseMessage answered, CancellationToken cancellation)
    {
        var said = await Said(answered, cancellation).ConfigureAwait(false);
        var status = (int)answered.StatusCode;

        var what = answered.StatusCode switch
        {
            HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden =>
                "Deepgram would not accept this machine's key. Nothing was transcribed and nothing "
                + "was charged. Nothing about the key is quoted here on purpose.",
            HttpStatusCode.PaymentRequired or HttpStatusCode.TooManyRequests =>
                "Deepgram would not take this request: the account behind this key is out of "
                + "credit or over its rate. Nothing was transcribed.",
            _ when status >= 500 =>
                "Deepgram failed the request on its own side. Whether it charged for it is not "
                + "something this end can tell, so nothing is sent again on its own.",
            _ =>
                "Deepgram refused the request. Nothing was transcribed.",
        };

        return new DeepgramCallException(
            said is null ? $"{what} ({status})" : $"{what} ({status}: {said})");
    }

    /// <summary>
    /// The sentence the provider put in the refusal, or null when there is none to be had.
    /// </summary>
    /// <remarks>
    /// <b>The <see langword="catch"/> is not a defensive habit and is not to be deleted as one.</b>
    /// The call was made with <see cref="HttpCompletionOption.ResponseHeadersRead"/>, so nothing of
    /// this body has been buffered: reading it here is a real read off the connection, and a
    /// connection that has already gone is exactly how a refusal arrives with nothing behind it. A
    /// refusal that cannot say why is still a refusal, and losing the status to an exception raised
    /// while fetching the explanation would be the worse answer.
    /// </remarks>
    private static async Task<string?> Said(HttpResponseMessage answered, CancellationToken cancellation)
    {
        string body;
        try
        {
            body = await answered.Content.ReadAsStringAsync(cancellation).ConfigureAwait(false);
        }
        catch (Exception unreadable) when (EndedTheBody(unreadable))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return Shortened(
                document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("err_msg", out var said)
                && said.ValueKind == JsonValueKind.String
                    ? said.GetString()
                    : body);
        }
        catch (JsonException)
        {
            return Shortened(body);
        }
    }

    private static string? Shortened(string? said)
    {
        var trimmed = said?.Trim();

        return trimmed is null || trimmed.Length <= MostOfARefusalWorthQuoting
            ? trimmed
            : trimmed[..MostOfARefusalWorthQuoting] + "…";
    }
}
