using MeetingTranscriber.Domain.Artifacts;

namespace MeetingTranscriber.Domain.Tests.Artifacts;

/// <summary>
/// The names the audio engine writes into a spool folder and the corpus reads back off the disk,
/// with no project edge between the two — so this is where what each name means is settled.
/// </summary>
public class RecordingFilesTests
{
    [Theory]
    [InlineData("manifest.json", SpoolFile.Card)]
    [InlineData("changes.jsonl", SpoolFile.Changes)]
    [InlineData("loopback.blocks", SpoolFile.Blocks)]
    [InlineData("microphone.blocks", SpoolFile.Blocks)]
    [InlineData("audio.wav", SpoolFile.Poured)]
    [InlineData("loopback.wav", SpoolFile.Poured)]
    [InlineData("microphone.wav", SpoolFile.Poured)]
    [InlineData("capture.mark", SpoolFile.Mark)]
    [InlineData("reading.mark", SpoolFile.Mark)]
    [InlineData("saving.mark", SpoolFile.Mark)]
    public void Every_file_a_recording_writes_is_named_for_what_it_is(string name, SpoolFile what) =>
        RecordingFiles.WhatIsInASpoolFolder(name).ShouldBe(what);

    /// <summary>
    /// The one arm the theory above cannot see: dropping the exact check leaves <c>audio.wav</c>
    /// answering <see cref="SpoolFile.Poured"/> through the <c>.wav</c> arm anyway, and the two stop
    /// agreeing the moment either one is meant to say something different.
    /// </summary>
    [Fact]
    public void The_recording_is_named_before_the_extension_it_shares_with_the_playback_files() =>
        RecordingFiles.WhatIsInASpoolFolder(RecordingFiles.Recording).ShouldBe(SpoolFile.Poured);

    /// <summary>
    /// What stops <em>"if they are the only copy of that audio"</em> being printed over somebody's
    /// stray file: a name no recording writes is not one of a recording's files.
    /// </summary>
    [Theory]
    [InlineData("deepgram.json")]
    [InlineData("transcript.md")]
    [InlineData("utterances.jsonl")]
    [InlineData("summary.md")]
    [InlineData("notes.txt")]
    [InlineData("README")]
    public void A_name_no_recording_writes_is_not_one_of_its_files(string name) =>
        RecordingFiles.WhatIsInASpoolFolder(name).ShouldBe(SpoolFile.Unknown);

    /// <summary>
    /// One row per comparison that can be seen from outside. The exact-<c>Recording</c> arm is not
    /// among them: <c>audio.wav</c> reaches the same answer through the extension arm however that
    /// one is compared, so no case here can tell whether it ran.
    /// </summary>
    [Theory]
    [InlineData("SAVING.MARK", SpoolFile.Mark)]
    [InlineData("Loopback.Blocks", SpoolFile.Blocks)]
    [InlineData("Audio.WAV", SpoolFile.Poured)]
    [InlineData("Manifest.JSON", SpoolFile.Card)]
    [InlineData("Changes.JSONL", SpoolFile.Changes)]
    public void Case_is_not_what_tells_two_of_these_apart(string name, SpoolFile what) =>
        RecordingFiles.WhatIsInASpoolFolder(name).ShouldBe(what);

    /// <summary>
    /// A write that never finished is not one of a recording's files. The reconciler answers it
    /// before it ever asks this — <c>CorpusFiles.IsUnfinished</c> is the first branch of the walk —
    /// so what matters here is only that this does not claim it as something else.
    /// </summary>
    [Fact]
    public void A_write_that_never_finished_is_not_a_file_a_recording_left() =>
        RecordingFiles
            .WhatIsInASpoolFolder(RecordingFiles.Recording + RecordingFiles.UnfinishedSuffix)
            .ShouldBe(SpoolFile.Unknown);

    /// <summary>
    /// The disagreement the member names rest on, pinned rather than only documented. One file is
    /// called <c>audio.wav</c> in two places and the two answers are opposite on purpose: under
    /// <c>meetings/</c> nothing on the machine can make it again, and in the spool it sits beside
    /// the blocks it was poured out of. Somebody tidying the two into agreement breaks this.
    /// </summary>
    [Fact]
    public void The_meetings_copy_and_the_spools_copy_of_one_name_answer_opposite_ways()
    {
        RecordingFiles.WhatIsInASpoolFolder(RecordingFiles.Recording).ShouldBe(SpoolFile.Poured);
        ArtifactKind.Audio.OriginOf().ShouldBe(ArtifactOrigin.Source);
    }

    [Fact]
    public void Nothing_is_not_a_file_name()
    {
        Should.Throw<ArgumentException>(() => RecordingFiles.WhatIsInASpoolFolder(null!));
        Should.Throw<ArgumentException>(() => RecordingFiles.WhatIsInASpoolFolder("  "));
    }
}
