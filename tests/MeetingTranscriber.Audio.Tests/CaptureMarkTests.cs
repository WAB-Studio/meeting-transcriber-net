namespace MeetingTranscriber.Audio.Tests;

/// <summary>
/// The mark held over the folder a recording is being written into: what it says while a recording
/// is being started, what it says once whatever was holding it is gone, who takes it, and that a
/// session handed one lets it go on every way out that is not a recording.
/// </summary>
/// <remarks>
/// The middle one is the whole bet, and it costs more here than it does for a save. A capture runs
/// for the length of a meeting, so it is the mark most likely to be left stranded by a machine that
/// died — and a stranded one that meant something by being there would hold its folder out of the
/// sweep forever, which is the phantom meeting the sweep exists to end, kept alive by the crash
/// instead. Every test below that would pass over an implementation reading <c>File.Exists</c> is
/// worth nothing to that, and the ones that would fail over one say so in their own words.
/// </remarks>
public sealed class CaptureMarkTests : IDisposable
{
    private readonly DirectoryInfo root = new(Path.Combine(
        Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));

    public CaptureMarkTests() => root.Create();

    /// <summary>A folder no capture has ever run in has nothing to say about one.</summary>
    [Fact]
    public void A_folder_no_capture_has_touched_is_not_being_recorded_into()
    {
        var folder = root.CreateSubdirectory("daily");

        CaptureMark.IsHeldIn(folder).ShouldBeFalse();
        Mark(folder).Exists.ShouldBeFalse();

        // And a folder that is not there at all is not a capture either, which is what a sweep asks
        // of a corpus that has never recorded anything. Claiming one is refused by naming it.
        var nowhere = new DirectoryInfo(Path.Combine(root.FullName, "nowhere"));

        CaptureMark.IsHeldIn(nowhere).ShouldBeFalse();
        Should.Throw<AudioCaptureException>(() => CaptureMark.Take(nowhere))
            .Message.ShouldContain(nowhere.FullName);
    }

    /// <summary>
    /// While a capture holds the folder, anything looking at that folder is told so — which for a
    /// folder holding no block is the only thing there is to tell.
    /// </summary>
    [Fact]
    public void A_capture_that_is_running_holds_the_folder_and_refuses_a_second_one()
    {
        var folder = root.CreateSubdirectory("daily");

        using (CaptureMark.Take(folder))
        {
            CaptureMark.IsHeldIn(folder).ShouldBeTrue();

            var refused = Should.Throw<AudioCaptureException>(() => CaptureMark.Take(folder));
            refused.Message.ShouldContain("already recording into");
            refused.Message.ShouldContain(folder.FullName);
        }

        // Let go of, and the folder is anybody's again — including the next recording's.
        CaptureMark.IsHeldIn(folder).ShouldBeFalse();
        CaptureMark.Take(folder).Dispose();
    }

    /// <summary>
    /// The two marks are two facts. A folder being saved says nothing about a capture and a folder
    /// being captured says nothing about a save, and neither refuses the other its own name.
    /// </summary>
    /// <remarks>
    /// One file for both would be the whole defect this pair exists to avoid: a save's mark drawn
    /// over a capture reads as a meeting still happening, and a capture's drawn over a save reads
    /// as one to sweep. What keeps them apart is two names, and this is where that is held.
    /// </remarks>
    [Fact]
    public void A_capture_and_a_save_are_two_marks_and_neither_answers_for_the_other()
    {
        var folder = root.CreateSubdirectory("daily");

        using (CaptureMark.Take(folder))
        {
            SavingMark.IsHeldIn(folder).ShouldBeFalse();

            using (SavingMark.Take(folder))
            {
                CaptureMark.IsHeldIn(folder).ShouldBeTrue();
                SavingMark.IsHeldIn(folder).ShouldBeTrue();
            }

            CaptureMark.IsHeldIn(folder).ShouldBeTrue();
        }

        SavingMark.IsHeldIn(folder).ShouldBeFalse();
        CaptureMark.IsHeldIn(folder).ShouldBeFalse();

        // Two files on disk and not one, which is what makes the two answers independent.
        folder.GetFiles().Select(file => file.Name)
            .ShouldBe([CaptureMark.FileName, SavingMark.FileName], ignoreOrder: true);
    }

    /// <summary>
    /// ISC-156.1's mechanism. A mark left lying by a capture that ended badly is a file nothing is
    /// holding, and a file nothing is holding says no capture is running.
    /// </summary>
    /// <remarks>
    /// This is the test that goes red over a mark whose meaning is its existence: the file is
    /// really there, asserted before the question is asked, and the answer is still no. What that
    /// buys is the folder still being swept after a crash — a stranded mark that held it would be
    /// the meeting nobody had surviving forever, which is the defect and not the fix.
    /// </remarks>
    [Fact]
    public void A_mark_a_capture_left_behind_holds_the_folder_for_nobody()
    {
        var folder = root.CreateSubdirectory("daily");
        CaptureMark.Take(folder).Dispose();

        var mark = Mark(folder);
        mark.Exists.ShouldBeTrue("a capture that ended leaves its mark on disk, and this test is about that file");

        CaptureMark.IsHeldIn(folder).ShouldBeFalse();

        // Asked twice with nothing in between, because a reader that cleared the mark on its way
        // past would answer the first one honestly and the second one for the wrong reason.
        CaptureMark.IsHeldIn(folder).ShouldBeFalse();
        mark.Refresh();
        mark.Exists.ShouldBeTrue();
    }

    /// <summary>
    /// A capture is what takes the mark, and it takes it before it opens anything: a session
    /// started over a folder somebody already holds is refused with nothing on disk to show for it.
    /// </summary>
    /// <remarks>
    /// The one probe of the wiring that runs on a machine with no sound card, and it runs there
    /// because it never reaches a device — which is also exactly what it is asserting. A session
    /// that took the mark after opening its sources would fail this on the spool files it left
    /// behind, and one that never took it at all would fail it on the refusal that never came.
    /// The device named is one no machine has, so nothing here can open even where there is a card.
    /// </remarks>
    [Fact]
    public void A_session_takes_the_mark_before_it_opens_a_device()
    {
        var folder = root.CreateSubdirectory(Guid.NewGuid().ToString());

        using (CaptureMark.Take(folder))
        {
            var refused = Should.Throw<AudioCaptureException>(() => CaptureSession.Start(
                folder,
                Guid.NewGuid(),
                new AudioDevice("{no-such-endpoint}", "A microphone no machine has", IsDefault: false)));

            refused.Message.ShouldContain("already recording into");
            refused.Message.ShouldContain(folder.FullName);
        }

        // Nothing was opened and nothing was written: no spool for either channel, and no card.
        folder.GetFiles().Select(file => file.Name)
            .ShouldBe([CaptureMark.FileName], ignoreOrder: true);
    }

    /// <summary>
    /// A session handed a claim and refused before it opens anything lets the claim go, so the
    /// folder is the sweep's again.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The half of the handoff that stops the fix being worse than the defect.
    /// <c>MeetingRecordings.Open</c> claims a meeting's folder and hands the claim to
    /// <see cref="CaptureSession.StartUnder"/>, which owns it from its first line — so every press
    /// that fails, which is the ordinary one where a microphone is refused, has to leave the folder
    /// unheld. A claim held by a session that never started keeps its meeting in the list forever,
    /// and no start can ever take it away.
    /// </para>
    /// <para>
    /// Refused on the argument rather than on a device, because a device is what a build agent does
    /// not have — and because the argument checks sit inside the <c>try</c> for exactly this
    /// reason. A build that moved them back above it, which is where they were and is the obvious
    /// tidy-up, fails here and nowhere else.
    /// </para>
    /// </remarks>
    [Fact]
    public void A_session_refused_the_claim_it_was_handed_lets_the_folder_go()
    {
        var folder = root.CreateSubdirectory(Guid.NewGuid().ToString());

        Should.Throw<ArgumentNullException>(() => CaptureSession.StartUnder(
            CaptureMark.Take(folder), Guid.NewGuid(), microphone: null!));

        CaptureMark.IsHeldIn(folder).ShouldBeFalse();
    }

    /// <summary>
    /// A session handed a claim over a folder that already holds a recording is refused, and the
    /// claim goes with the refusal.
    /// </summary>
    /// <remarks>
    /// "A recording is not written over another one" is decided in one place —
    /// <see cref="BlockSpool.EnsureNothingRecordedIn"/> — and both doors into
    /// <see cref="CaptureSession"/> ask it. On the one path anybody reaches
    /// <see cref="CaptureSession.StartUnder"/> by, the folder is a fresh <c>Guid</c> made
    /// microseconds earlier and this can never fire; it is asserted because the door is public, and
    /// a rule held by one caller's habit is not held at all.
    /// </remarks>
    [Fact]
    public void A_session_handed_a_claim_over_somebody_elses_recording_is_refused()
    {
        var folder = root.CreateSubdirectory(Guid.NewGuid().ToString());

        System.IO.File.WriteAllBytes(Path.Combine(folder.FullName, "loopback.blocks"), [1, 2, 3]);

        Should.Throw<AudioCaptureException>(() => CaptureSession.StartUnder(
                CaptureMark.Take(folder),
                Guid.NewGuid(),
                new AudioDevice("{no-such-endpoint}", "A microphone no machine has", IsDefault: false)))
            .Message.ShouldContain("loopback.blocks");

        CaptureMark.IsHeldIn(folder).ShouldBeFalse();
    }

    public void Dispose()
    {
        try
        {
            root.Delete(recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // A leftover temp directory is not worth failing a green test over.
        }
    }

    /// <summary>The mark's file, which this type deliberately does not hand anybody.</summary>
    private static FileInfo Mark(DirectoryInfo folder) =>
        new(Path.Combine(folder.FullName, CaptureMark.FileName));

}
