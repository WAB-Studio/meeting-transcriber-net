using System.Runtime.CompilerServices;

using MeetingTranscriber.Domain.Artifacts;
using MeetingTranscriber.Domain.Audio;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Artifacts;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Infrastructure.Tests.Storage;

/// <summary>
/// Which folder the corpus is in, which is the question nothing in the application could answer
/// before this and every screen has to have answered before it can show anything.
/// </summary>
/// <remarks>
/// The claim these are all really about is the one no test on a build agent can run: that a corpus
/// an installed build wrote is still there after the package is uninstalled. What is provable
/// without a package is everything that decides where the writing happens — so these stand at the
/// folder rather than at the uninstall, and the one about the source tree stands at the API that
/// would quietly send it somewhere else.
/// </remarks>
public class CorpusLocationTests
{
    private static readonly UtcTimestamp When =
        UtcTimestamp.From(new DateTimeOffset(2026, 8, 18, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public void With_nobody_having_chosen_the_corpus_is_under_the_users_own_application_data()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CorpusLocation.ApplicationFolderName);

        var location = CorpusLocation.OfThisUser();

        location.Fallback.FullName.ShouldBe(expected);
        location.Setting.FullName.ShouldBe(Path.Combine(expected, CorpusLocation.SettingName));
    }

    /// <summary>
    /// The load-bearing one, and the reason it is an assertion rather than a comment: the folder
    /// this application would put a corpus in has to be one that outlives the package, and the two
    /// candidates read alike everywhere except here.
    /// </summary>
    [Fact]
    public void The_folder_the_application_falls_back_to_is_not_inside_the_package_container()
    {
        CorpusLocation.InsideThePackageContainer(CorpusLocation.OfThisUser().Fallback.FullName)
            .ShouldBeFalse();
    }

    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\Packages\Publisher.App_1abc\LocalCache\Local\MeetingTranscriber")]
    [InlineData(@"C:\Users\someone\AppData\Local\Packages\Publisher.App_1abc\LocalState")]
    [InlineData(@"C:\Users\someone\AppData\Local\Packages")]
    [InlineData(@"c:\users\someone\appdata\local\packages\publisher.app_1abc\localcache")]
    public void A_folder_inside_the_package_container_goes_when_the_package_does(string path) =>
        CorpusLocation.InsideThePackageContainer(path).ShouldBeTrue();

    /// <summary>
    /// The other side of it, and the decoys are the point: what makes a folder the package's is
    /// the three segments in that order under the profile, not the word <c>Packages</c> appearing
    /// in the path.
    /// </summary>
    [Theory]
    [InlineData(@"C:\Users\someone\AppData\Local\MeetingTranscriber")]
    [InlineData(@"C:\Users\someone\AppData\Roaming\Packages\MeetingTranscriber")]
    [InlineData(@"C:\Packages\MeetingTranscriber")]
    [InlineData(@"D:\Corpus")]
    public void A_folder_outside_it_survives_the_package(string path) =>
        CorpusLocation.InsideThePackageContainer(path).ShouldBeFalse();

    [Fact]
    public void The_corpus_opens_where_the_setting_says()
    {
        using var corpus = new TemporaryCorpus();
        Migrated(corpus);
        using var elsewhere = new TemporaryFolder();
        var location = At(elsewhere);

        location.Choose(corpus.Root);

        var resolved = location.Resolve();
        resolved.Refusal.ShouldBeNull();
        resolved.Folder!.FullName.ShouldBe(corpus.Root.FullName);
    }

    /// <summary>
    /// Read back through a second <see cref="CorpusLocation"/> over the same file, so what is
    /// proved is what is on disk and not what the object that wrote it remembers.
    /// </summary>
    [Fact]
    public void The_same_folder_opens_again_the_next_time_the_application_starts()
    {
        using var corpus = new TemporaryCorpus();
        Migrated(corpus);
        using var elsewhere = new TemporaryFolder();

        At(elsewhere).Choose(corpus.Root);

        At(elsewhere).Resolve().Folder!.FullName.ShouldBe(corpus.Root.FullName);
    }

    [Fact]
    public void A_folder_that_is_not_there_is_refused_naming_it()
    {
        using var elsewhere = new TemporaryFolder();
        var gone = Path.Combine(elsewhere.Folder.FullName, "on-a-disk-nobody-plugged-in");
        var location = Naming(elsewhere, gone);

        var resolved = location.Resolve();

        resolved.Refusal.ShouldBe(CorpusRefusal.FolderIsNotThere);
        resolved.Path.ShouldBe(gone);
        resolved.Folder.ShouldBeNull();
    }

    /// <summary>
    /// The failure the whole refusal is here to stop. Somebody's corpus is on a disk that is not
    /// plugged in, and the application answers by making a new empty one — after which they are
    /// looking at no meetings and nothing is wrong.
    /// </summary>
    [Fact]
    public void A_folder_that_is_not_there_never_becomes_a_second_empty_corpus()
    {
        using var elsewhere = new TemporaryFolder();
        var gone = new DirectoryInfo(Path.Combine(elsewhere.Folder.FullName, "unplugged"));
        var location = Naming(elsewhere, gone.FullName);

        location.Resolve().Refusal.ShouldNotBeNull();

        Directory.Exists(gone.FullName).ShouldBeFalse();
        CorpusDatabase.HoldsACorpus(location.Fallback).ShouldBeFalse();
        Directory.Exists(location.Fallback.FullName).ShouldBeFalse();
    }

    [Fact]
    public void A_folder_with_no_corpus_in_it_is_refused_rather_than_filled_with_a_new_one()
    {
        using var elsewhere = new TemporaryFolder();
        using var empty = new TemporaryFolder();

        var resolved = Naming(elsewhere, empty.Folder.FullName).Resolve();

        resolved.Refusal.ShouldBe(CorpusRefusal.NoCorpusInTheFolder);
        resolved.Path.ShouldBe(empty.Folder.FullName);
        File.Exists(Path.Combine(empty.Folder.FullName, CorpusDatabase.DatabaseName)).ShouldBeFalse();
    }

    /// <summary>
    /// A <c>corpus.db</c> of no bytes is what a create cut off part way leaves, and it is neither
    /// a corpus nor nothing: SQLite will not put it into WAL, so the migration that would make it
    /// a corpus is refused as a write to a read-only database.
    /// </summary>
    [Fact]
    public void A_corpus_file_of_no_bytes_is_not_a_corpus()
    {
        using var elsewhere = new TemporaryFolder();
        using var halfMade = new TemporaryFolder();
        File.WriteAllBytes(Path.Combine(halfMade.Folder.FullName, CorpusDatabase.DatabaseName), []);

        Naming(elsewhere, halfMade.Folder.FullName).Resolve().Refusal
            .ShouldBe(CorpusRefusal.NoCorpusInTheFolder);
    }

    /// <summary>
    /// The container is refused before anything else is asked, so a corpus that is really there
    /// and really opens is still refused for being somewhere an uninstall would take it.
    /// </summary>
    [Fact]
    public void A_corpus_inside_the_package_container_is_refused_though_it_is_there_and_whole()
    {
        using var elsewhere = new TemporaryFolder();
        using var container = new TemporaryFolder();
        var inside = new DirectoryInfo(Path.Combine(
            container.Folder.FullName, "AppData", "Local", "Packages", "Publisher.App_1abc", "LocalCache"));
        inside.Create();
        // Migrating is what puts a corpus in the folder, which is what makes the refusal below
        // about where it is rather than about it not being there.
        using (CorpusDatabase.OpenMigrated(inside))
        {
        }

        CorpusDatabase.HoldsACorpus(inside).ShouldBeTrue();
        try
        {
            var resolved = Naming(elsewhere, inside.FullName).Resolve();

            resolved.Refusal.ShouldBe(CorpusRefusal.InsideThePackageContainer);
            resolved.Path.ShouldBe(inside.FullName);
        }
        finally
        {
            CorpusDatabase.ClearPoolsFor(inside);
        }
    }

    /// <summary>
    /// Where this parts company with the language preference beside it, which reads an unreadable
    /// file as nobody having chosen. The costs are nothing alike: one is a preference somebody
    /// re-picks in a click, the other is a corpus of meetings on another disk that the application
    /// would walk away from.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("corpus")]
    [InlineData(@"..\corpus")]
    [InlineData("not a path at all")]
    public void A_setting_saying_nothing_usable_is_refused_and_not_read_as_nobody_having_chosen(
        string written)
    {
        using var elsewhere = new TemporaryFolder();
        var location = At(elsewhere);
        File.WriteAllText(location.Setting.FullName, written);

        var resolved = location.Resolve();

        resolved.Refusal.ShouldBe(CorpusRefusal.SettingSaysNothingUsable);
        resolved.Path.ShouldBe(location.Setting.FullName);
        CorpusDatabase.HoldsACorpus(location.Fallback).ShouldBeFalse();
    }

    [Fact]
    public void With_no_setting_at_all_the_folder_the_application_keeps_its_own_data_in_is_the_answer()
    {
        using var elsewhere = new TemporaryFolder();
        var location = At(elsewhere);

        var resolved = location.Resolve();

        resolved.Refusal.ShouldBeNull();
        resolved.Folder!.FullName.ShouldBe(location.Fallback.FullName);
    }

    /// <summary>
    /// One rule, asked twice. A folder written down and then refused at every start afterwards is
    /// a corpus somebody can neither reach nor correct.
    /// </summary>
    [Fact]
    public void A_folder_the_next_start_would_refuse_cannot_be_recorded_as_where_the_corpus_is()
    {
        using var elsewhere = new TemporaryFolder();
        using var empty = new TemporaryFolder();
        var location = At(elsewhere);

        Should.Throw<ArgumentException>(() => location.Choose(empty.Folder));

        File.Exists(location.Setting.FullName).ShouldBeFalse();
    }

    /// <summary>
    /// The corpus moved somewhere else and every path the corpus recorded still reaches its file.
    /// A second temp folder rather than a second drive, which no build agent has — what the claim
    /// turns on is that a stored path is relative to whichever folder the corpus is opened as, and
    /// that is what changes here.
    /// </summary>
    [Fact]
    public void A_corpus_moved_somewhere_else_keeps_every_path_it_recorded()
    {
        // Not TemporaryCorpus, whose disposal deletes the folder this one has to move instead.
        using var before = new TemporaryFolder();
        using var after = new TemporaryFolder();
        var origin = new DirectoryInfo(Path.Combine(before.Folder.FullName, "corpus"));
        var destination = new DirectoryInfo(Path.Combine(after.Folder.FullName, "corpus"));
        string transcript;
        string response;
        origin.Create();

        using (var context = CorpusDatabase.OpenMigrated(origin))
        {
            var meeting = Recorded(context);
            transcript = Written(context, meeting, "transcript.md", ArtifactKind.Transcript, "a rendering");
            response = Written(context, meeting, "deepgram.json", ArtifactKind.DeepgramResponse, "{\"paid\":true}");
        }

        // The folder cannot move while a pooled connection still holds the database open.
        CorpusDatabase.ClearPoolsFor(origin);
        Directory.Move(origin.FullName, destination.FullName);

        try
        {
            using var elsewhere = new TemporaryFolder();
            var location = At(elsewhere);
            location.Choose(destination);

            var resolved = location.Resolve().Folder.ShouldNotBeNull();
            using var context = CorpusDatabase.Open(resolved);

            ArtifactReconciler.Check(context, verifyContents: true).ShouldBeEmpty();
            File.ReadAllText(CorpusFiles.Locate(resolved, transcript).FullName).ShouldBe("a rendering");
            File.ReadAllText(CorpusFiles.Locate(resolved, response).FullName).ShouldBe("{\"paid\":true}");
        }
        finally
        {
            CorpusDatabase.ClearPoolsFor(destination);
        }
    }

    /// <summary>
    /// The trap the whole card is about, and the only form of it a test can stand at: the two ways
    /// of asking Windows for a folder read alike in a debugger and one of them is the package's own
    /// folder, which uninstalling deletes. Nothing the product is built out of may ask that way, so
    /// the source tree is swept rather than the behaviour argued about.
    /// </summary>
    [Fact]
    public void Nothing_the_application_is_built_out_of_asks_the_package_where_to_write()
    {
        var thisFile = ThisFile();
        var product = new DirectoryInfo(
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(thisFile)!, "..", "..", "..", "src")));
        const string PackageLocalFolder = "ApplicationData.Current";

        product.Exists.ShouldBeTrue($"'{product.FullName}' is where the product is.");

        var offenders = product
            .EnumerateFiles("*.cs", SearchOption.AllDirectories)
            .Concat(product.EnumerateFiles("*.xaml", SearchOption.AllDirectories))
            .Where(file => !IsBuildOutput(file))
            .Where(file => Code(file).Contains(PackageLocalFolder, StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(product.FullName, file.FullName))
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders.ShouldBeEmpty(
            $"These reach {PackageLocalFolder}, whose LocalFolder is "
            + @"%LOCALAPPDATA%\Packages\<family>\LocalCache — the folder uninstalling the package "
            + $"deletes. A corpus folder comes from {nameof(CorpusLocation)}, which asks "
            + $"{nameof(Environment)}.{nameof(Environment.GetFolderPath)} and refuses anything "
            + "inside the container.");
    }

    /// <summary>
    /// What the file does, with what it says about itself left out. The sweep has to run over the
    /// one file whose whole job is to name the API nothing may call — excusing that file instead
    /// would leave the sweep blind exactly where the mistake is most likely to be made.
    /// </summary>
    private static string Code(FileInfo file) => string.Join(
        Environment.NewLine,
        File.ReadLines(file.FullName)
            .Where(line => !line.TrimStart().StartsWith("//", StringComparison.Ordinal)));

    private static bool IsBuildOutput(FileInfo file)
    {
        var separator = Path.DirectorySeparatorChar;
        return file.FullName.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            || file.FullName.Contains($"{separator}bin{separator}", StringComparison.Ordinal);
    }

    /// <summary>
    /// This source file, from where it was compiled rather than from the working directory, the
    /// way <c>IsaDocument</c> and <c>TemporaryCorpusTests</c> find what they read.
    /// </summary>
    private static string ThisFile([CallerFilePath] string path = "") => Path.GetFullPath(path);

    /// <summary>A location whose setting and fallback are both inside a folder of this test's own.</summary>
    private static CorpusLocation At(TemporaryFolder folder) => new(
        new FileInfo(Path.Combine(folder.Folder.FullName, CorpusLocation.SettingName)),
        new DirectoryInfo(Path.Combine(folder.Folder.FullName, CorpusLocation.ApplicationFolderName)));

    /// <summary>The same, with the setting already written by hand — including what Choose refuses.</summary>
    private static CorpusLocation Naming(TemporaryFolder folder, string path)
    {
        var location = At(folder);
        File.WriteAllText(location.Setting.FullName, path);
        return location;
    }

    private static void Migrated(TemporaryCorpus corpus)
    {
        using var context = corpus.OpenMigrated();
    }

    private static string Written(
        CorpusDbContext context, Guid meeting, string name, ArtifactKind kind, string text) =>
        DurableArtifact.WriteText(
            context, meeting, kind, CorpusFiles.PathFor(meeting, name), When, text).RelativePath;

    private static Guid Recorded(CorpusDbContext context)
    {
        var meeting = new Meeting
        {
            Id = Guid.NewGuid(),
            Language = "es",
            StartedAt = When,
            SourceProfile = SourceProfile.Multichannel,
            CreatedAt = When,
            UpdatedAt = When,
        };

        context.Meetings.Add(meeting);
        context.SaveChanges();
        return meeting.Id;
    }

    /// <summary>
    /// A folder of this test's own, which is not a corpus. <c>TemporaryCorpus</c> is the one for
    /// corpora and makes one; what these need is somewhere to put a setting file, and somewhere a
    /// corpus is deliberately not.
    /// </summary>
    private sealed class TemporaryFolder : IDisposable
    {
        public TemporaryFolder()
        {
            Folder = new DirectoryInfo(Path.Combine(
                Path.GetTempPath(), "meeting-transcriber-tests", Guid.NewGuid().ToString("n")));
            Folder.Create();
        }

        public DirectoryInfo Folder { get; }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Folder.FullName, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // A leftover temp folder is not worth reddening a green test over, which is the
                // same call TemporaryCorpus makes and for the same two Windows refusals.
            }
        }
    }
}
