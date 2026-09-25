using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Meetings;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// The one rule that decides what pressing stop sets going, over each of the three answers somebody
/// can have settled beforehand.
/// </summary>
/// <remarks>
/// A pure test and no corpus anywhere, which is the point of the rule being a type of its own: what
/// a stop queues is a decision, and a decision with a database under it is one nobody can read off
/// a test name. Where the answer comes from is <c>CorpusSettingsTests</c>' and what is done with it
/// is <c>MeetingRecordingsTests</c>'.
/// </remarks>
public class WhatStoppingStartsTests
{
    [Fact]
    public void Nothing_is_queued_when_nothing_was_settled() =>
        WhatStoppingStarts.For(AfterARecording.DoNothing).ShouldBeEmpty();

    [Fact]
    public void Transcribing_is_queued_when_transcribing_was_settled() =>
        WhatStoppingStarts.For(AfterARecording.Transcribe).ShouldBe([JobKind.Transcribe]);

    /// <summary>
    /// Summarising is not queued at the stop even when it was settled.
    /// </summary>
    /// <remarks>
    /// The meeting has no transcription for a summary to be made from, so an <c>Extract</c> queued
    /// here would be a job whose input does not exist — the one thing <c>JobStates</c> cannot
    /// describe. What the second half of that answer means is read by nothing yet:
    /// <c>JobRunner</c>, which finishes a transcription, queues no summary when one lands, because
    /// nothing in this product summarises yet.
    /// </remarks>
    [Fact]
    public void Summarising_is_not_queued_at_the_stop_even_when_it_was_settled() =>
        WhatStoppingStarts
            .For(AfterARecording.TranscribeAndSummarise)
            .ShouldBe([JobKind.Transcribe]);

    /// <summary>
    /// Every answer somebody can settle is one this rule has an answer for.
    /// </summary>
    /// <remarks>
    /// The three cases above are the three members today, and this is what says so: a fourth added
    /// to <see cref="AfterARecording"/> would otherwise fall through the ternary and queue nothing,
    /// which is the quietest possible way for a paid answer to stop being honoured.
    /// </remarks>
    [Fact]
    public void Every_answer_somebody_can_settle_is_one_this_rule_answers_for() =>
        Enum.GetValues<AfterARecording>().Length.ShouldBe(
            3,
            "AfterARecording has a member WhatStoppingStarts has never been asked about, so what a "
            + "stop does with it is whatever the ternary happens to do.");
}
