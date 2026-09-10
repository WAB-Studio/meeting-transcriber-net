using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Processing.Deepgram;

namespace MeetingTranscriber.Processing.Tests.Deepgram;

/// <summary>
/// What each profile asks the provider for. It is billable configuration, so it is spelled here
/// literally rather than rebuilt from the same rule the code uses.
/// </summary>
public class DeepgramRequestTests
{
    /// <summary>
    /// The whole of what goes on the wire, both profiles, written out. Red when an option is
    /// added, removed, reordered or respelled — which is what makes one billable configuration look
    /// like another one, and what would let the same call be paid for twice.
    /// </summary>
    [Fact]
    public void Each_profile_spells_the_whole_of_what_it_asks_for()
    {
        new DeepgramRequest(SourceProfile.Multichannel, "es").Options.ShouldBe(
            "model=nova-3&language=es&smart_format=true&utterances=true&diarize=true&multichannel=true");

        new DeepgramRequest(SourceProfile.Diarize, "es").Options.ShouldBe(
            "model=nova-3&language=es&smart_format=true&utterances=true&diarize=true");
    }

    /// <summary>
    /// The same ask twice is the same value, which is what lets a caller ask whether this exact
    /// billable configuration has already been paid for.
    /// </summary>
    [Fact]
    public void The_same_ask_is_the_same_value()
    {
        new DeepgramRequest(SourceProfile.Multichannel, "es")
            .ShouldBe(new DeepgramRequest(SourceProfile.Multichannel, "es"));

        new DeepgramRequest(SourceProfile.Multichannel, "es")
            .ShouldNotBe(new DeepgramRequest(SourceProfile.Diarize, "es"));
    }

    [Fact]
    public void The_options_are_the_query_of_the_url_it_is_sent_to()
    {
        new DeepgramRequest(SourceProfile.Diarize, "es").At().ToString().ShouldBe(
            "https://api.deepgram.com/v1/listen?"
            + new DeepgramRequest(SourceProfile.Diarize, "es").Options);
    }

    [Fact]
    public void A_language_with_nothing_in_it_is_not_a_language()
    {
        Should.Throw<ArgumentException>(() => new DeepgramRequest(SourceProfile.Diarize, "  "));
        new DeepgramRequest(SourceProfile.Diarize, " es ").Language.ShouldBe("es");
    }

    /// <summary>
    /// A profile the domain does not have never becomes a request. Both switches are read off
    /// <see cref="SourceProfiles"/>, so the refusal comes from the one place that knows which
    /// profiles exist rather than from a mapping copied into this project.
    /// </summary>
    [Fact]
    public void A_profile_the_domain_does_not_have_is_not_asked_for()
    {
        Should.Throw<AudioContractException>(() => new DeepgramRequest((SourceProfile)99, "es"));
    }
}
