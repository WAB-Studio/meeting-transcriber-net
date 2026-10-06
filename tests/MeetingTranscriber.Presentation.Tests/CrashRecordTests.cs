namespace MeetingTranscriber.Presentation.Tests;

/// <summary>
/// O-20261006-09: the application writes down what it was told before it goes, so the next crash
/// names itself instead of being a fault in a native module.
/// </summary>
public class CrashRecordTests : IDisposable
{
    private static readonly DateTimeOffset At = new(2026, 10, 6, 8, 56, 47, 123, TimeSpan.Zero);

    private readonly DirectoryInfo _folder =
        Directory.CreateTempSubdirectory("crash-record-tests");

    public void Dispose() => _folder.Delete(recursive: true);

    private CrashRecord Record() => new(new FileInfo(Path.Combine(_folder.FullName, "in", CrashRecord.FileName)));

    private static Exception Thrown(Exception exception)
    {
        try
        {
            throw exception;
        }
        catch (Exception caught)
        {
            return caught;
        }
    }

    [Fact]
    public void An_entry_names_the_place_the_type_the_message_and_the_stack()
    {
        var record = Record();

        record.Write("MainWindow.OnActivated", Thrown(new ArgumentException("bad width")), At);

        var written = File.ReadAllText(record.Location.FullName);
        written.ShouldContain("2026-10-06T08:56:47.123Z  MainWindow.OnActivated");
        written.ShouldContain("System.ArgumentException: bad width");
        written.ShouldContain("   at MeetingTranscriber.Presentation.Tests.CrashRecordTests.Thrown");
    }

    [Fact]
    public void What_caused_it_is_written_too_but_what_the_exception_carried_is_not()
    {
        var record = Record();
        var secret = new InvalidOperationException("outer", Thrown(new IOException("inner")));
        secret.Data["key"] = "dg-secret-key";

        record.Write("x", Thrown(secret), At);

        var written = File.ReadAllText(record.Location.FullName);
        written.ShouldContain("--- caused by System.IO.IOException: inner");
        written.ShouldNotContain("dg-secret-key");
    }

    [Fact]
    public void A_long_message_is_cut()
    {
        var record = Record();

        record.Write("x", Thrown(new InvalidOperationException(new string('a', 5_000))), At);

        File.ReadAllText(record.Location.FullName).ShouldNotContain(new string('a', CrashRecord.LongestMessage + 1));
    }

    [Fact]
    public void The_same_exception_seen_twice_is_written_once()
    {
        var record = Record();
        var thrown = Thrown(new InvalidOperationException("once"));

        record.Write("guard", thrown, At);
        record.Write("Application.UnhandledException", thrown, At);

        File.ReadAllText(record.Location.FullName).Split("once").Length.ShouldBe(2);
    }

    [Fact]
    public void A_guarded_body_that_throws_is_recorded_and_still_throws()
    {
        var record = Record();

        Should.Throw<InvalidOperationException>(() =>
            record.Guard("OnMeters", () => throw new InvalidOperationException("tick")));

        File.ReadAllText(record.Location.FullName).ShouldContain("OnMeters");
    }

    [Fact]
    public void A_guarded_body_that_does_not_throw_writes_nothing()
    {
        var record = Record();

        record.Guard("OnMeters", () => { });

        record.Location.Exists.ShouldBeFalse();
    }

    [Fact]
    public void A_file_that_has_grown_too_long_starts_again()
    {
        var record = Record();
        record.Location.Directory!.Create();
        File.WriteAllText(record.Location.FullName, new string('x', (int)CrashRecord.LongestFile + 1));

        record.Write("x", Thrown(new InvalidOperationException("fresh")), At);

        File.ReadAllText(record.Location.FullName).ShouldNotContain("xxxx");
    }

    [Fact]
    public void A_record_that_cannot_be_written_does_not_throw()
    {
        // A directory where the file should be.
        var record = Record();
        Directory.CreateDirectory(record.Location.FullName);

        Should.NotThrow(() => record.Write("x", Thrown(new InvalidOperationException("nowhere")), At));
    }

    [Fact]
    public void It_is_not_in_the_corpus_folder()
    {
        CrashRecord.OfThisUser().Location.FullName.ShouldNotStartWith(
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "MeetingTranscriber") + Path.DirectorySeparatorChar);
    }
}
