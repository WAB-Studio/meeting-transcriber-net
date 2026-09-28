namespace MeetingTranscriber.Processing.Summaries;

/// <summary>
/// The words a provider is given to summarise a meeting: what is asked for, and the shape the
/// answer has to take.
/// </summary>
/// <remarks>
/// <para>
/// A public static class and not a provider's own constant. What is asked for and how it is asked
/// belong to nobody's adapter — a provider only writes what it is handed, and knows nothing of
/// summaries, citations or corrections. Two providers asking the same question two different ways
/// would be two contracts, and a corpus read back later could not tell which one an old run
/// answered.
/// </para>
/// <para>
/// <see cref="Schema"/>'s document is <see cref="ExtractionReader"/> read out loud rather than a
/// second definition of the shape: the reader is written once, by hand, and this holds it to an
/// example that has to read through it with no refusal — so the two cannot drift the way a document
/// a person maintains and a validator that reads bytes eventually do.
/// </para>
/// </remarks>
public static class ExtractionInstructions
{
    /// <summary>The only prompt version this build writes, stored as <c>extraction_runs.prompt_version</c>.</summary>
    public const string Version = "1";

    /// <summary>What is asked for on a first attempt.</summary>
    public static string ToExtract { get; } = """
        You are given one meeting's transcript as meeting.json and the shape your answer must take as
        schema.md. Read nothing else: you have no tools.

        Answer with one JSON object in exactly the shape schema.md describes, and nothing before or after it.

        - Write the abstract, the summary and every statement in the language the meeting was held in.
        - Every decision, action and open question cites the one turn it was said in: that turn's ordinal,
          its start_ms and its speaker_label exactly as meeting.json gives them, and quoted_text copied word
          for word from that turn's text.
        - Leave out anything you cannot cite that way. A statement no turn says does not belong in the answer.
        - participants lists the speaker_label of every voice that spoke, as meeting.json writes it.
        - meeting_id is the meeting_id meeting.json carries, and schema_version is "1".
        """;

    /// <summary>
    /// What is asked for on a hand-back of a refused answer: <c>ExtractionCorrection.WhatWasWrong</c>
    /// says what has to change, and this says how to use it.
    /// </summary>
    public static string ToCorrect { get; } = """
        You are given one meeting's transcript as meeting.json, the shape your answer must take as schema.md,
        the answer you gave before as previous-output.json, and what was wrong with it as what-was-wrong.md.
        Read nothing else: you have no tools.

        Answer with the corrected answer: one JSON object in exactly the shape schema.md describes, and
        nothing before or after it.

        - Where what-was-wrong.md says the shape is wrong, fix the shape and change nothing else.
        - Where it says to remove a statement, remove that statement. Do not cite it again from another turn
          and do not reword it: a statement the meeting does not support is left out.
        - Keep every statement it does not mention exactly as it was, and add none.
        """;

    /// <summary>
    /// One extraction, whole, that reads through <see cref="ExtractionReader.Read"/> with no
    /// refusal — the example a provider is shown, and what <c>ExtractionInstructionsTests</c> pins
    /// the shape against.
    /// </summary>
    /// <remarks>
    /// Declared ahead of <see cref="Schema"/>, which is not tidiness: a static property initialiser
    /// runs in declaration order, and <see cref="Schema"/>'s reads this one to build its document.
    /// </remarks>
    public static string Example { get; } = """
        {
          "schema_version": "1",
          "meeting_id": "00000000-0000-0000-0000-000000000000",
          "abstract": "El equipo revisó el lanzamiento de la campaña y quién sigue cada frente.",
          "summary": "Se repasó el estado de la campaña, se fijó una fecha para el envío del primer correo y quedó pendiente resolver el presupuesto de publicidad paga.",
          "participants": ["ch1:speaker_0", "ch0:speaker_0"],
          "decisions": [
            {
              "statement": "Lanzar la campaña el viernes.",
              "evidence": {
                "utterance_ordinal": 3,
                "start_ms": 61200,
                "end_ms": 63500,
                "speaker_label": "ch1:speaker_0",
                "quoted_text": "Lanzamos la campaña el viernes."
              }
            }
          ],
          "actions": [
            {
              "statement": "Mandar el primer correo de la campaña.",
              "due_date": "2026-10-02",
              "evidence": {
                "utterance_ordinal": 7,
                "start_ms": 150400,
                "end_ms": 153800,
                "speaker_label": "ch0:speaker_0",
                "quoted_text": "Yo mando el primer correo el jueves que viene."
              }
            }
          ],
          "open_questions": [
            {
              "question": "¿Con qué presupuesto se paga la publicidad?",
              "evidence": {
                "utterance_ordinal": 9,
                "start_ms": 188000,
                "end_ms": 190200,
                "speaker_label": "ch1:speaker_0",
                "quoted_text": "Todavía no sé con qué presupuesto pagamos la publicidad."
              }
            }
          ]
        }
        """;

    /// <summary>
    /// The document a provider is shown so it can answer in the shape <see cref="ExtractionReader"/>
    /// reads. The prose, a blank line, and then <see cref="Example"/>.
    /// </summary>
    public static ExtractionSchema Schema { get; } = new(
        ExtractionReader.SchemaVersion,
        """
        The answer is one JSON object with exactly these keys, and no others at any depth:

        - schema_version: the string "1".
        - meeting_id: the meeting's id, as a string.
        - abstract: one or two sentences saying what the meeting was for. Never empty.
        - summary: a longer account of the meeting. It may be empty.
        - participants: an array of speaker labels.
        - decisions: an array of objects with "statement" and "evidence".
        - actions: an array of objects with "statement", "due_date" and "evidence"; due_date is
          "YYYY-MM-DD" or null.
        - open_questions: an array of objects with "question" and "evidence".

        evidence is an object with "utterance_ordinal", "start_ms", "end_ms", "speaker_label" and
        "quoted_text": the cited turn's ordinal, start_ms and speaker_label exactly, an end_ms no earlier
        than start_ms (it may be where the quote ends inside the turn), and quoted_text copied from that
        turn's text. statement and question are never empty.

        For example:

        """
        + Example);
}
