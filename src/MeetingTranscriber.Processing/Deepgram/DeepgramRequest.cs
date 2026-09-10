using MeetingTranscriber.Domain.Audio;

namespace MeetingTranscriber.Processing.Deepgram;

/// <summary>
/// What one transcription asks the provider for: the model, the language, and the options the
/// profile decides.
/// </summary>
/// <remarks>
/// <para>
/// A value and not a call, so what each of the two profiles turns into is readable and testable
/// without a socket.
/// </para>
/// <para>
/// <b>Both switches the profile decides come off the domain's own table and neither is spelled
/// here.</b> <see cref="SourceProfiles.RequestsDiarization"/> says whether the provider is asked to
/// tell speakers apart, and <see cref="SourceProfiles.ChannelCount"/> says how many channels the
/// audio has — which is what <c>multichannel</c> means. A mapping written here instead would put a
/// third profile's wire behaviour in a file under <c>Processing</c> that nothing in
/// <c>Domain/Audio/</c> points at, and a profile the domain does not have would reach the provider
/// under whichever options the last one happened to want.
/// </para>
/// <para>
/// <b>The order of the options is fixed and is part of what this is.</b> Everything here is
/// billable configuration — the model, the language and the switches — so the same ask has to spell
/// itself the same way every time. A set that reordered itself would make one request look like a
/// different one, and the pair that decides whether a call has already been paid for is the audio's
/// hash and the billable configuration's.
/// </para>
/// <para>
/// <b>A switch is spelled only when it is on.</b> An absent switch and a switch set to false are
/// the same request to the provider and two different strings here, and one ask with two spellings
/// is one ask that can be paid for twice.
/// </para>
/// <para>
/// <c>smart_format</c> and not <c>punctuate</c>: the first turns on the second, and the committed
/// fixtures carry both <c>punctuated_word</c> and <c>results.paragraphs</c>, which is what
/// <c>smart_format=true</c> produces. Sending both would say the same thing twice.
/// <c>utterances=true</c> is what puts <c>results.utterances</c> in the response, which is the only
/// block <see cref="DeepgramTranscriptParser"/> reads speech out of — without it every fixture
/// shape this repository has would arrive with nothing the parser can use.
/// </para>
/// </remarks>
public sealed record DeepgramRequest
{
    /// <summary>
    /// The model every request here asks for. It is what the committed fixtures came back from —
    /// <c>metadata.model_info</c> names <c>general-nova-3</c> in all five — and what
    /// <c>UiTexts.TheEngineThatTranscribes</c> already says a person is paying for.
    /// </summary>
    public const string Model = "nova-3";

    /// <summary>The path under <see cref="Deepgram"/> that transcribes a file.</summary>
    private const string Listen = "v1/listen";

    public DeepgramRequest(SourceProfile profile, string language)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);

        Profile = profile;
        Language = language.Trim();
        Options = OptionsFor(profile, Language);
    }

    /// <summary>
    /// Where the provider is. One host, spelled once: Deepgram has no second origin and nothing
    /// here takes one as a parameter, because a caller that wanted to point somewhere else already
    /// has <see cref="HttpClient.BaseAddress"/> and needs no API of ours to do it.
    /// </summary>
    public static Uri Deepgram { get; } = new("https://api.deepgram.com/");

    public SourceProfile Profile { get; }

    public string Language { get; }

    /// <summary>The query string, ordered, exactly as it goes on the wire.</summary>
    public string Options { get; }

    /// <summary>Where this request is sent.</summary>
    public Uri At() => new(Deepgram, $"{Listen}?{Options}");

    private static string OptionsFor(SourceProfile profile, string language)
    {
        var options = new List<string>
        {
            $"model={Model}",
            $"language={Uri.EscapeDataString(language)}",
            "smart_format=true",
            "utterances=true",
        };

        if (profile.RequestsDiarization())
        {
            options.Add("diarize=true");
        }

        // What multichannel says is that the audio has more than one channel, which is the domain's
        // number rather than a second name for one profile.
        if (profile.ChannelCount() > 1)
        {
            options.Add("multichannel=true");
        }

        return string.Join('&', options);
    }
}
