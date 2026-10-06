using MeetingTranscriber.Audio;

namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// ISC-220.3: the microphone and what channel 0 followed are kept, so choosing them once is enough.
/// </summary>
public class SourcesChosenLastTimeTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("recorder-sources-");

    [Fact]
    public void Nothing_was_chosen_until_something_is() => Kept().Read().ShouldBeNull();

    [Fact]
    public void What_was_chosen_is_what_comes_back()
    {
        var microphone = new AudioDevice("{mic}", "A microphone", IsDefault: true);

        Kept().KeepTheMicrophone(microphone);
        Kept().KeepTheSource(RecorderSource.TheWholeMachine);

        // A second reader, because the point is what is on disk and not what is in memory.
        Kept().Read().ShouldBe(new LastSources("{mic}", TheWholeMachine: true, ProgramName: null));

        Kept().KeepTheSource(RecorderSource.Following(new AudioProcess(99, "teams.exe", StartedBy: 1)));

        Kept().Read().ShouldBe(new LastSources("{mic}", TheWholeMachine: false, ProgramName: "teams.exe"));
    }

    [Fact]
    public void The_first_choice_makes_the_place_it_is_kept()
    {
        var file = new FileInfo(Path.Combine(_folder.FullName, "never", "made", "recorder-sources.json"));

        new SourcesChosenLastTime(file).KeepTheSource(RecorderSource.TheWholeMachine);

        new SourcesChosenLastTime(file).Read()!.TheWholeMachine.ShouldBeTrue();
    }

    [Fact]
    public void Choosing_one_thing_leaves_the_other_as_it_was_kept()
    {
        Kept().KeepTheSource(RecorderSource.Following(new AudioProcess(1, "teams.exe", StartedBy: 1)));
        Kept().KeepTheMicrophone(new AudioDevice("{mic}", "A microphone", IsDefault: false));

        Kept().Read().ShouldBe(new LastSources("{mic}", TheWholeMachine: false, ProgramName: "teams.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("3")]
    public void Something_unreadable_reads_as_nothing_chosen(string written)
    {
        var kept = Kept();
        File.WriteAllText(kept.Location.FullName, written);

        kept.Read().ShouldBeNull();
    }

    public void Dispose()
    {
        _folder.Delete(recursive: true);
        GC.SuppressFinalize(this);
    }

    private SourcesChosenLastTime Kept() =>
        new(new FileInfo(Path.Combine(_folder.FullName, "recorder-sources.json")));
}
