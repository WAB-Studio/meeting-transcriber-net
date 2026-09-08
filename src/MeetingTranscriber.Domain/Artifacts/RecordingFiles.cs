namespace MeetingTranscriber.Domain.Artifacts;

/// <summary>
/// What a file inside a spool folder is, told from its name alone.
/// </summary>
/// <remarks>
/// <para>
/// This is the question asked of a file <em>inside a spool folder</em>, and it is not
/// <see cref="ArtifactOrigin"/>. <see cref="ArtifactOrigin"/> is what the corpus stores about an
/// artifact it holds; this is what a scan of the spool can tell from a name. The two disagree
/// about <c>audio.wav</c> on purpose and both are right: the meeting's copy is a source because
/// nothing on the machine can make it again, and the spool's copy sits beside the blocks it was
/// poured out of.
/// </para>
/// <para>
/// It answers for nothing under <c>meetings/</c>.
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
    /// Not the meeting's. <c>MeetingManifest.FileName</c> in the infrastructure project is the same
    /// string and a different file — one is produced from the corpus every time and may be
    /// replaced, this one is written once and is the only record of which meeting a folder of
    /// blocks belongs to. docs/corpus.md says so under a heading of its own, and the two are
    /// deliberately not unified.
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
/// <c>corpus check</c> as recordings to recover.
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

    /// <summary>What the mark held from the folder being claimed until the last device goes is called.</summary>
    public const string CaptureMark = "capture.mark";

    /// <summary>What the mark held while a list, a keep or an export reads the blocks through is called.</summary>
    public const string ReadingMark = "reading.mark";

    /// <summary>What the mark held while a finish writes the meeting down is called.</summary>
    public const string SavingMark = "saving.mark";

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
            // Above the PlaybackExtension arm, and load-bearing: audio.wav would answer through
            // that arm too, with the same answer today and no reason it has to stay the same one.
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
