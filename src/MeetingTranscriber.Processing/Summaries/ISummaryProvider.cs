namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// What summarises a meeting, whichever engine is behind it. One implementation runs Claude Code
/// headless; a fake stands in for it in every test that is not about the adapter itself.
/// </summary>
/// <remarks>
/// <para>
/// Two calls and nothing more. Whether the engine is there at all is its own question, asked before
/// anything is sent and answered without spending — the settings screen asks it on every
/// <c>Show()</c>, and a run asks it again first (Decides 4, part two) so that a machine that lost
/// its sign-in reads as <em>did not answer</em> rather than being sent a prompt nobody will read.
/// Extracting is the one call that spends, and it takes exactly what a run is: the meeting, the
/// words asking for it, the shape the answer has to take, and — on a hand-back — the correction
/// being asked for.
/// </para>
/// <para>
/// Nothing here decides what a citation is or whether an extraction holds up. That is
/// <see cref="Summaries.ExtractionCheck"/>'s, over whatever bytes came back, so a provider never
/// knows it is being checked and cannot be written to pass the check rather than to answer honestly.
/// </para>
/// </remarks>
public interface ISummaryProvider
{
    /// <summary>What this provider is called, stored as <c>extraction_runs.provider</c>.</summary>
    string Name { get; }

    /// <summary>
    /// Whether this provider can be asked at all, asked for its own sake and never as a side effect
    /// of a run. Costs nothing: it is what the settings screen asks on every draw, so this cannot be
    /// the call that starts a summary.
    /// </summary>
    Task<SummaryAvailability> IsAvailableAsync(CancellationToken stopping);

    /// <summary>One extraction, or one correction of a refused one.</summary>
    Task<SummaryProviderAnswer> ExtractAsync(ExtractionRequest request, CancellationToken stopping);
}

/// <summary>Which of the three things <see cref="SummaryAvailability"/> can say.</summary>
public enum Availability
{
    /// <summary>The provider is there and said its version.</summary>
    Answers = 1,

    /// <summary>Nothing this machine can reach is the provider at all.</summary>
    NotOnThisMachine = 2,

    /// <summary>It is there, and asking it did not come back with an answer.</summary>
    DoesNotAnswer = 3,
}

/// <summary>
/// Whether a provider can be asked right now, and what it said about itself while being asked.
/// </summary>
/// <param name="Is">Which of the three this was.</param>
/// <param name="Version">What it reported itself as, when it answered.</param>
/// <param name="Said">
/// The machine's own English, when there is nothing else to go on. It goes to <c>last_error</c> and
/// never onto a screen as this application's own words — a screen chooses one of its own sentences
/// from <see cref="Availability"/> and folds this in as data, the way every machine message is.
/// </param>
public sealed record SummaryAvailability(Availability Is, string? Version, string? Said);

/// <summary>The shape an extraction's answer has to take, as a version and the document saying so.</summary>
/// <param name="Version">Stored as <c>extraction_runs.schema_version</c>, and read back by
/// <see cref="ExtractionReader.SchemaVersion"/> — the reader is the shape's only definition, and
/// this is that definition read out loud for whoever answers it.</param>
/// <param name="Document">The prose a provider is shown, so it can answer in the shape being asked for.</param>
public sealed record ExtractionSchema(string Version, string Document);

/// <summary>
/// What a provider is handed to correct a refused answer: the answer it gave, and what was wrong
/// with it.
/// </summary>
public sealed record SummaryCorrection(byte[] PreviousOutput, string WhatWasWrong);

/// <summary>
/// One call to a provider, whether the first attempt at a meeting or a hand-back of a refused one.
/// </summary>
/// <remarks>
/// <see cref="Instructions"/> and <see cref="Correction"/> are two views of the one fact — which
/// of the two this call is — and it is the caller's to keep them agreeing:
/// <c>ExtractionInstructions.ToExtract</c> paired with no correction, or <c>ToCorrect</c> paired
/// with one. Nothing here checks the pair, the way <see cref="SummaryProviderAnswer"/> two records
/// below is built so nothing downstream of <em>it</em> has to: a provider only ever writes what
/// this hands it, so a request built with the two disagreeing sends a corrected prompt over a
/// meeting it was never asked to correct, or the reverse. There is exactly one caller of
/// <see cref="ISummaryProvider.ExtractAsync"/> in this codebase, and it is what keeps this pair
/// honest.
/// </remarks>
/// <param name="Input">The meeting, prepared and hashed once so every reader of the answer checks
/// it against the same bytes that were sent.</param>
/// <param name="Instructions">What is being asked for: <c>ExtractionInstructions.ToExtract</c> the
/// first time, <c>ToCorrect</c> on a hand-back.</param>
/// <param name="Schema">The shape the answer has to take.</param>
/// <param name="Correction">What is being corrected, or nothing on a first attempt.</param>
public sealed record ExtractionRequest(
    MeetingInput Input, string Instructions, ExtractionSchema Schema, SummaryCorrection? Correction)
{
    /// <summary>
    /// The model the call asks for, as the provider's own alias, or nothing for the provider's
    /// default. An init property and not a fifth positional member: the command line's own request
    /// asks for no model, and every caller that does not choose one stays as it was.
    /// </summary>
    public string? Model { get; init; }
}

/// <summary>
/// What a call to a provider came back with: an extraction, a run that gave back nothing usable, or
/// a provider that was never really asked.
/// </summary>
/// <remarks>
/// Closed, the way <c>CaptureTarget</c> is: a private constructor so nothing outside this file can
/// add a fifth case, and four nested records so a caller pattern-matches on what actually
/// happened rather than reading a flag and a set of nullable fields that might disagree with it.
/// </remarks>
public abstract record SummaryProviderAnswer
{
    private SummaryProviderAnswer()
    {
    }

    /// <summary>The provider answered. What it said is unwrapped, and not yet checked against the meeting.</summary>
    /// <param name="Output">The bytes handed to <see cref="Summaries.ExtractionCheck"/>, whatever they turn out to be.</param>
    /// <param name="ProviderVersion">What <see cref="ISummaryProvider.IsAvailableAsync"/> reported for this run.</param>
    /// <param name="Model">The model that answered, when the provider says which one that was.</param>
    /// <param name="SessionId">The conversation the provider opened for this call, when it reports one.</param>
    public sealed record Extracted(byte[] Output, string ProviderVersion, string? Model, string? SessionId)
        : SummaryProviderAnswer;

    /// <summary>
    /// The provider ran and gave back nothing an extraction can be read from — it failed, timed
    /// out, or was stopped.
    /// </summary>
    public sealed record DidNotAnswer(string Said) : SummaryProviderAnswer;

    /// <summary>
    /// Nothing was sent: the provider is not there, or would not say its version, so asking it to
    /// extract anything would spend on a call already known to fail.
    /// </summary>
    public sealed record NotAvailable(string Said) : SummaryProviderAnswer;

    /// <summary>
    /// The run was refused before it started, because of a memory file Claude Code would have read
    /// into the summary. Nothing was sent, and retrying cannot change it: somebody has to move the
    /// file.
    /// </summary>
    /// <param name="Said">The machine's own English, naming the file.</param>
    /// <param name="File">The memory file, as a full path.</param>
    public sealed record MemoryInTheWay(string Said, string File) : SummaryProviderAnswer;
}
