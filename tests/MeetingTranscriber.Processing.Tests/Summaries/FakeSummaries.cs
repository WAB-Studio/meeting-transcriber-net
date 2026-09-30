using MeetingTranscriber.Processing.Summaries;

namespace MeetingTranscriber.Processing.Tests.Summaries;

/// <summary>
/// A provider standing in for <see cref="ISummaryProvider"/> itself, so a caller of
/// <c>SummarisingAMeeting.SummariseAsync</c> can be proven without a real process — and, in
/// <c>SummarisingAMeetingTests.The_fake_and_Claude_Code_file_the_same_summary</c>, proven to answer
/// exactly like the real adapter over the same request.
/// </summary>
public sealed class FakeSummaries : ISummaryProvider
{
    private readonly Queue<SummaryProviderAnswer> _answers = new();
    private readonly List<ExtractionRequest> _requests = [];
    private bool _neverAnswers;

    public string Name => "fake";

    /// <summary>Every request this was handed, in the order it was handed them.</summary>
    public IReadOnlyList<ExtractionRequest> Requests => _requests;

    /// <summary>
    /// Queues one answer per call, in order. A call once every queued answer is spent throws —
    /// asking this for more than a test told it to expect is the test's own defect.
    /// </summary>
    public FakeSummaries Answering(params SummaryProviderAnswer[] answers)
    {
        ArgumentNullException.ThrowIfNull(answers);

        foreach (var answer in answers)
        {
            _answers.Enqueue(answer);
        }

        return this;
    }

    /// <summary>
    /// Every call waits on the cancellation token alone, the way a real run that never comes back
    /// would leave a caller waiting on the process instead of the queue above.
    /// </summary>
    public FakeSummaries ThatNeverAnswers()
    {
        _neverAnswers = true;
        return this;
    }

    public Task<SummaryAvailability> IsAvailableAsync(CancellationToken stopping) =>
        Task.FromResult(new SummaryAvailability(Availability.Answers, "fake 1", null));

    public async Task<SummaryProviderAnswer> ExtractAsync(ExtractionRequest request, CancellationToken stopping)
    {
        ArgumentNullException.ThrowIfNull(request);
        _requests.Add(request);

        if (_neverAnswers)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, stopping).ConfigureAwait(false);
        }

        if (_answers.Count == 0)
        {
            throw new InvalidOperationException(
                "FakeSummaries was asked for one more answer than it was given. Queue one more "
                + "with Answering(...) before the call that needs it.");
        }

        return _answers.Dequeue();
    }
}
