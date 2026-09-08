namespace MeetingTranscriber.Domain.Artifacts;

/// <summary>
/// What a file inside a spool folder is, told from its name alone.
/// </summary>
/// <remarks>
/// <para>
/// The question asked of a file <em>inside a spool folder</em>, and it answers for nothing under
/// <c>meetings/</c>. <see cref="ArtifactKind"/> is the same folder's other vocabulary and is not
/// this one: it names a file the corpus holds a row for, so <see cref="Blocks"/>,
/// <see cref="Poured"/> and <see cref="Card"/> are <see cref="ArtifactKind.SpoolBlock"/>,
/// <see cref="ArtifactKind.Audio"/> and <see cref="ArtifactKind.Manifest"/> seen from the disk side,
/// and <see cref="Changes"/> and <see cref="Mark"/> have no kind at all because they are never rows.
/// A file role that can be a row has to be added to both.
/// </para>
/// <para>
/// The two disagree about <c>audio.wav</c> on purpose, and the disagreement is the reason these
/// members are not called <c>Source</c> and <c>Derived</c>:
/// <c>Artifacts.OriginOf(ArtifactKind.Audio)</c> is <see cref="ArtifactOrigin.Source"/> because
/// nothing on the machine can make the meeting's copy again, while the spool's copy sits beside the
/// blocks it was poured out of. Both are right for their own question, and one pair of words over
/// two enums a file apart is a trap no doc reaches, because a doc does not show up in a completion
/// list.
/// </para>
/// </remarks>
public enum SpoolFile
{
    /// <summary>
    /// A name no recording of this application writes.
    /// </summary>
    /// <remarks>
    /// The answer is load-bearing rather than a default: somebody else's file that happens to be
    /// under the spool gets the honest answer — a file with no row that may be the only copy —
    /// rather than a sentence telling its owner it is part of a recording.
    /// </remarks>
    Unknown = 0,

    /// <summary>One source's blocks, which are the audio itself until something pours them out.</summary>
    Blocks = 1,

    /// <summary>
    /// The <em>spool's</em> card: what the recording wrote down about itself when the devices
    /// opened.
    /// </summary>
    /// <remarks>
    /// Not the meeting's. <c>MeetingManifest.FileName</c> is the same string and a different file,
    /// and the two are deliberately not unified — docs/corpus.md says why under a heading of its
    /// own, and <c>MeetingManifest.FileName</c> carries the warning where the wrong edit would be
    /// typed.
    /// </remarks>
    Card = 2,

    /// <summary>What somebody moved while the recording was running.</summary>
    /// <remarks>
    /// An answer of its own rather than the card's, because it holds what the card cannot: the card
    /// is written once and says what each channel opened on, so a channel somebody moved to the
    /// whole machine an hour in is only written down here. The record carries no meeting id at all,
    /// so a sentence about which meeting the blocks belong to is false of this file.
    /// </remarks>
    Changes = 3,

    /// <summary>What was poured back out of a folder's blocks so something can play it.</summary>
    Poured = 4,

    /// <summary>A mark some process held over the folder.</summary>
    /// <remarks>
    /// It holds no bytes and nothing ever reads whether it is there, so nothing may be concluded
    /// from finding one except that some process had it open at some point. docs/corpus.md says
    /// nothing ever clears the one a crashed capture, read or save leaves.
    /// </remarks>
    Mark = 5,
}

/// <summary>
/// The names a recording writes into its own folder, in the one project everything that spells them
/// can see.
/// </summary>
/// <remarks>
/// <para>
/// Here rather than in the audio engine because the engine writes these names and the reconciler
/// reads them back off the disk, and docs/layout.md forbids an edge between those two projects in
/// either direction. So the name lives in the project both of them reference, and each side defines
/// its own constant from this one so the compiler proves they agree. docs/corpus.md is this same
/// table in prose.
/// </para>
/// <para>
/// Spelling them twice is what put <c>saving.mark</c> and then <c>capture.mark</c> into
/// <c>corpus check</c> as recordings to recover, once each and a day apart. A name added here and
/// nowhere else is still that defect, which is what
/// <c>RecordingFileNamesTests.Every_name_the_engine_declares_is_one_the_corpus_can_place</c> is for.
/// </para>
/// </remarks>
public static class RecordingFiles
{
    /// <summary>What the spool's card is called.</summary>
    /// <remarks>See <see cref="SpoolFile.Card"/> for why this is not the meeting's card.</remarks>
    public const string Card = "manifest.json";

    /// <summary>What the log of mid-recording changes is called.</summary>
    public const string Changes = "changes.jsonl";

    /// <summary>What the recording the blocks become is called.</summary>
    /// <remarks>
    /// One name covers <c>spool/&lt;id&gt;/audio.wav</c> and <c>meetings/&lt;id&gt;/audio.wav</c>,
    /// because the meeting's copy is filed from the spool's under the same name.
    /// <see cref="WhatIsInASpoolFolder"/> answers for the first of those two and not the second.
    /// </remarks>
    public const string Recording = "audio.wav";

    /// <summary>What one source's blocks end in.</summary>
    /// <remarks>
    /// Matched by extension here rather than by composing each channel's name, because composing it
    /// is where an unknown channel is refused — <c>BlockSpool.FileFor</c> asks
    /// <c>CapturedAudio.IndexOf</c> before it names a file, and it stays the only thing that turns a
    /// channel into a file name.
    /// </remarks>
    public const string BlocksExtension = ".blocks";

    /// <summary>What a file poured back out of blocks ends in.</summary>
    /// <remarks>By extension for the reason <see cref="BlocksExtension"/> gives.</remarks>
    public const string PlaybackExtension = ".wav";

    /// <summary>What <c>CaptureMark</c> is called. What it means is on that type.</summary>
    public const string CaptureMark = "capture.mark";

    /// <summary>What <c>ReadingMark</c> is called. What it means is on that type.</summary>
    public const string ReadingMark = "reading.mark";

    /// <summary>What <c>SavingMark</c> is called. What it means is on that type.</summary>
    public const string SavingMark = "saving.mark";

    /// <summary>What a write that has not been confirmed yet is called, while it is being made.</summary>
    /// <remarks>
    /// The one name here that is not confined to a spool folder: the corpus writes every artifact
    /// through it. It is here because a recording is materialised into the folder the reconciler's
    /// sweep walks, so the engine writing the name and the sweep deleting on sight of it are one
    /// rule that used to be two spellings with nothing checking they agreed — and that one deletes
    /// audio when it drifts. What the suffix <em>claims</em>, and why the copy a replace sets aside
    /// may never wear it, stays on <c>CorpusFiles.UnfinishedSuffix</c>, which is the rule about
    /// stored paths and is the infrastructure's.
    /// </remarks>
    public const string UnfinishedSuffix = ".partial";

    /// <summary>
    /// What a removal calls the folder it moves a recording into before it takes it away, in front
    /// of the recording's own folder name.
    /// </summary>
    /// <remarks>
    /// A name and not a <see cref="Guid"/>, deliberately: a unique one would be impossible to find
    /// again, which is what would turn a machine dying inside a discard into rubbish nobody can
    /// identify rather than something a person can see and delete.
    /// </remarks>
    public const string BeingRemovedPrefix = ".removing-";

    /// <summary>What <paramref name="fileName"/> is, for a file directly inside a spool folder.</summary>
    /// <remarks>
    /// Case-insensitively, for the reason <c>CorpusFiles.PathComparer</c> gives: the corpus is a
    /// folder on a Windows filesystem and two spellings are one file. Telling them apart exactly
    /// here would be a second answer to a question that one already settled.
    /// </remarks>
    public static SpoolFile WhatIsInASpoolFolder(string fileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        if (Named(fileName, Recording))
        {
            // Answers for the constant and not for how it happens to be spelled. Today Recording
            // ends in PlaybackExtension, so the arm below would give the same answer — but the
            // engine writes whatever this constant says, and matched only by extension the day it
            // stops ending in .wav the recording a folder's blocks were poured into would be
            // reported as a file with no row that may be the only copy of something. That is this
            // card's own defect, and the test that reads the constant is what holds it.
            return SpoolFile.Poured;
        }

        if (Named(fileName, Card))
        {
            return SpoolFile.Card;
        }

        if (Named(fileName, Changes))
        {
            return SpoolFile.Changes;
        }

        if (Named(fileName, CaptureMark) || Named(fileName, ReadingMark) || Named(fileName, SavingMark))
        {
            return SpoolFile.Mark;
        }

        if (Ends(fileName, BlocksExtension))
        {
            return SpoolFile.Blocks;
        }

        return Ends(fileName, PlaybackExtension) ? SpoolFile.Poured : SpoolFile.Unknown;
    }

    private static bool Named(string fileName, string name) =>
        fileName.Equals(name, StringComparison.OrdinalIgnoreCase);

    private static bool Ends(string fileName, string extension) =>
        fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase);
}
