namespace MeetingTranscriber.Domain.Meetings;

/// <summary>
/// The row that asks who is using the application, as the facts that decide what it does: whether
/// it is still asking, whether it may be typed in, and whether there is an answer to keep.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than beside the window for the reason <c>RecorderScreen</c> is beside no window: a
/// screen's rules are the half of it a build agent can run, and a window that decided this for
/// itself would leave "the question is on screen exactly while nobody has answered" true only for
/// as long as nobody edited the four conditions it is made of. Nothing in it is a corpus, a file
/// or a control — four facts in, three answers out.
/// </para>
/// <para>
/// In <c>Domain</c> and not in <c>MeetingTranscriber.Presentation</c>, where it first landed, for
/// the rule <c>docs/layout.md</c> gives about where a screen's rules go: this one is about the
/// person the corpus flags as me, and the answer becomes that person's
/// <see cref="Person.DisplayName"/>, two files from here.
/// </para>
/// <para>
/// The row is the asking and the answer at once, and that is the whole design: there is one field
/// whether it has been answered or not, and what changes is the sentence under it. A first-run
/// screen that asked and went would have to be a screen; a field that appeared only once could not
/// be edited afterwards, which is the other half of what the answer is for.
/// </para>
/// </remarks>
/// <param name="CorpusIsReachable">
/// Whether the answer has anywhere to be kept. A corpus that was refused leaves the whole row dead
/// rather than offering a press that would fail: the refusal is already on screen, naming the
/// folder, and that is where somebody has to go first.
/// </param>
/// <param name="SomebodyHasSaid">Whether this corpus already carries an answer.</param>
/// <param name="Typed">What is in the field right now, exactly as it was typed.</param>
/// <param name="BeingKept">
/// Whether an answer is on its way to the corpus. It is the state the row would not otherwise
/// have, and without it a second press lands while the first is still running — two writes that
/// each find nobody has answered, and two people who both are.
/// </param>
public sealed record WhoIsUsingThisRow(
    bool CorpusIsReachable,
    bool SomebodyHasSaid,
    string Typed,
    bool BeingKept)
{
    /// <summary>The row before anybody has been asked anything.</summary>
    public static readonly WhoIsUsingThisRow Unread =
        new(CorpusIsReachable: false, SomebodyHasSaid: false, Typed: "", BeingKept: false);

    /// <summary>
    /// Whether the question is still being put. It is what makes this a question rather than a
    /// label, and it goes the moment there is an answer — a sentence that stayed would keep asking
    /// something this install has settled.
    /// </summary>
    public bool IsAsking => CorpusIsReachable && !SomebodyHasSaid;

    /// <summary>Whether the field may be typed in.</summary>
    public bool FieldIsLive => CorpusIsReachable && !BeingKept;

    /// <summary>
    /// Whether there is an answer to keep. Blank is not one, and it is refused by the press being
    /// dead rather than by a sentence: an empty field has already said everything there is to say
    /// about it.
    /// </summary>
    public bool MayBeKept => FieldIsLive && Name.Length > 0;

    /// <summary>What would be kept: what was typed, without the spaces around it.</summary>
    public string Name => Typed.Trim();
}
