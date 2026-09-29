using MeetingTranscriber.Domain.Jobs;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Processing.Summaries;

/// <summary>What one call to <see cref="SummarisingAMeeting.SummariseAsync"/> came to.</summary>
public enum SummaryOutcome
{
    /// <summary>
    /// The provider answered, and the door filed what it said — accepted, or refused for good. The
    /// door already moved the job; there is nothing left for a caller to do.
    /// </summary>
    Filed = 1,

    /// <summary>The provider ran and gave back nothing an extraction can be read from.</summary>
    DidNotAnswer = 2,

    /// <summary>
    /// Nothing was sent: the provider is not there, or would not say its version, or the corpus
    /// could not give what a call needed before anything was sent.
    /// </summary>
    NotSent = 3,
}

/// <summary>What one call to <see cref="SummarisingAMeeting.SummariseAsync"/> came to, and why.</summary>
/// <param name="Said">The machine's own English, set for everything but <see cref="SummaryOutcome.Filed"/>.</param>
/// <param name="Failure">
/// Set for <see cref="SummaryOutcome.NotSent"/> alone — <see cref="SummaryOutcome.DidNotAnswer"/> is
/// the runner's to turn into a failure or a retry, and a <see cref="SummaryOutcome.Filed"/> job
/// already carries whatever the door decided.
/// </param>
/// <param name="RunId">The run the door filed, set for <see cref="SummaryOutcome.Filed"/> alone.</param>
public sealed record SummaryEnded(SummaryOutcome Outcome, string? Said, JobFailure? Failure = null, Guid? RunId = null);

/// <summary>
/// One <see cref="MeetingTranscriber.Domain.Jobs.JobKind.Extract"/> job the runner has already
/// started: sending the meeting to a provider, and filing whatever comes of it.
/// </summary>
/// <remarks>
/// <para>
/// <b>This never moves the job itself.</b> <see cref="ExtractionIntake.Receive"/> is what moves it
/// on a <see cref="SummaryOutcome.Filed"/> outcome — accepted or refused for good, the door already
/// decided which — and the runner is what moves it on <see cref="SummaryOutcome.DidNotAnswer"/> or
/// <see cref="SummaryOutcome.NotSent"/>, because only the runner knows how many times this job has
/// already been tried.
/// </para>
/// <para>
/// <b>Only the caller's own cancellation is let out.</b> The corpus is read once, before anything is
/// sent, on a context this disposes before ever awaiting a provider — a provider call is not
/// something a database connection should sit open across. Anything the provider or the door throws
/// that is not <paramref name="stopping"/> ending the call becomes <see cref="SummaryOutcome.DidNotAnswer"/>,
/// carrying its message: a call that is money spent already reads as an answer worth acting on
/// rather than an exception a caller has to know to catch.
/// </para>
/// <para>
/// <b>Two calls in flight on one job at once are not this method's to prevent.</b> Nothing here
/// takes a lease the way <c>TranscribingAMeeting</c>'s own remarks describe one being held for a
/// whole pump pass — a caller that starts two calls for the same job without that discipline is not
/// one this method can defend against, the same way <see cref="ExtractionIntake.Receive"/> trusts
/// the transaction a caller opens around <em>its</em> own moves. What a second such call meets is
/// never silent corruption: the corpus's own write lock serialises the two doors, and a job already
/// moved past <c>Running</c> by the first refuses the second loudly.
/// </para>
/// </remarks>
public static class SummarisingAMeeting
{
    /// <summary>
    /// Sends an extraction job and turns what happens into a <see cref="SummaryEnded"/>.
    /// </summary>
    /// <param name="root">The corpus.</param>
    /// <param name="jobId">The <see cref="JobKind.Extract"/> job this attempt is for.</param>
    /// <param name="provider">What actually reaches the summariser.</param>
    /// <param name="clock">Where every timestamp this writes comes from.</param>
    /// <param name="stopping">Cancels the call.</param>
    /// <exception cref="InvalidOperationException">
    /// The job is missing, is not a summary, or has not been started. Nothing is sent.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// <paramref name="stopping"/> ended the call itself. Nothing about the job has moved.
    /// </exception>
    public static async Task<SummaryEnded> SummariseAsync(
        DirectoryInfo root,
        Guid jobId,
        ISummaryProvider provider,
        TimeProvider clock,
        CancellationToken stopping = default)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(clock);

        MeetingInput prepared;
        using (var context = CorpusDatabase.Open(root))
        {
            var job = context.ProcessingJobs.FirstOrDefault(row => row.Id == jobId);
            if (job is null || job.Kind != JobKind.Extract || job.State != JobState.Running)
            {
                throw new InvalidOperationException(
                    $"Job {jobId} is not a summary that has been started, so nothing is sent for it.");
            }

            try
            {
                prepared = MeetingInput.Prepare(context, job.MeetingId);
            }
            catch (InvalidOperationException exception)
            {
                return new SummaryEnded(SummaryOutcome.NotSent, exception.Message, JobFailure.CorpusRefused);
            }
        }

        try
        {
            var request = new ExtractionRequest(
                prepared, ExtractionInstructions.ToExtract, ExtractionInstructions.Schema, null);
            var answer = await provider.ExtractAsync(request, stopping).ConfigureAwait(false);

            return answer is SummaryProviderAnswer.Extracted extracted
                ? FileAsync(root, jobId, provider, extracted, prepared.Hash, clock)
                : NotExtracted(answer);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return new SummaryEnded(SummaryOutcome.DidNotAnswer, exception.Message);
        }
    }

    private static SummaryEnded FileAsync(
        DirectoryInfo root,
        Guid jobId,
        ISummaryProvider provider,
        SummaryProviderAnswer.Extracted extracted,
        string inputHash,
        TimeProvider clock)
    {
        using var context = CorpusDatabase.Open(root);
        var attempt = new ExtractionIntake.ExtractionAttempt(
            jobId,
            provider.Name,
            extracted.ProviderVersion,
            extracted.Model,
            ExtractionInstructions.Version,
            inputHash,
            extracted.Output,
            extracted.SessionId);

        var received = ExtractionIntake.Receive(context, attempt, UtcTimestamp.From(clock.GetUtcNow()));
        return new SummaryEnded(SummaryOutcome.Filed, null, RunId: received.RunId);
    }

    /// <summary>What a <see cref="SummaryProviderAnswer"/> that is not <c>Extracted</c> becomes.</summary>
    private static SummaryEnded NotExtracted(SummaryProviderAnswer answer) => answer switch
    {
        SummaryProviderAnswer.DidNotAnswer didNotAnswer => new SummaryEnded(SummaryOutcome.DidNotAnswer, didNotAnswer.Said),
        SummaryProviderAnswer.NotAvailable notAvailable =>
            new SummaryEnded(SummaryOutcome.NotSent, notAvailable.Said, JobFailure.NoSummariserOnThisMachine),
        _ => throw new ArgumentOutOfRangeException(
            nameof(answer), answer, $"{answer.GetType()} is Extracted, and files rather than becoming a SummaryEnded here."),
    };
}
