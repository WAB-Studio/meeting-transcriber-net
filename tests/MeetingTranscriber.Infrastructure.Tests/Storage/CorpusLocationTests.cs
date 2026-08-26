using System.Diagnostics;
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
    public void With_nobody_having_chosen_the_corpus_is_directly_under_the_users_profile()
    {
        var expected = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            CorpusLocation.ApplicationFolderName);

        var location = CorpusLocation.OfThisUser();

        location.Fallback.FullName.ShouldBe(expected);
        location.Setting.FullName.ShouldBe(Path.Combine(expected, CorpusLocation.SettingName));
    }

    /// <summary>
    /// The load-bearing one, and the reason it is an assertion rather than a comment: the folder
    /// this application would put a corpus in has to be one that outlives the package, and every
    /// candidate reads alike everywhere except here.
    /// </summary>
    /// <remarks>
    /// Held against the folders Windows itself names rather than against the rule alone, so it
    /// still catches an application data folder that has been redirected somewhere the rule's own
    /// anchor would not reach. Both halves are asserted of each: that a corpus there would be
    /// refused, and that neither the corpus nor the file saying where it is is under it.
    /// </remarks>
    [Fact]
    public void No_folder_the_application_would_write_a_corpus_in_is_under_app_data()
    {
        var location = CorpusLocation.OfThisUser();

        foreach (var doomed in
            CorpusLocation.ApplicationDataOfThisUser().Append(CorpusLocation.PackageContainerOfThisUser()))
        {
            CorpusLocation.GoesWhenThePackageDoes(doomed)
                .ShouldBeTrue($"A corpus in '{doomed}' goes when the package does.");
            Under(location.Fallback.FullName, doomed)
                .ShouldBeFalse($"The corpus would be under '{doomed}'.");
            Under(location.Setting.FullName, doomed)
                .ShouldBeFalse($"The file saying where the corpus is would be under '{doomed}'.");
        }

        CorpusLocation.GoesWhenThePackageDoes(location.Fallback.FullName).ShouldBeFalse();
        CorpusLocation.GoesWhenThePackageDoes(location.Setting.Directory!.FullName).ShouldBeFalse();
    }

    /// <summary>
    /// Which folders are refused, rather than that the ones on this machine happen to be. On an
    /// ordinary profile every application data folder is already inside the profile's own, so a
    /// rule anchored only there passes every assertion above while leaving somebody whose
    /// application data was moved off the profile — a redirected folder, a roaming profile — with
    /// no protection at all. Their corpus would be the one that goes.
    /// </summary>
    [Fact]
    public void The_folders_refused_are_the_ones_Windows_itself_names()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        CorpusLocation.ApplicationDataOfThisUser().ShouldBe(
            [
                Path.Combine(profile, "AppData"),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            ],
            ignoreOrder: true);
    }

    /// <summary>
    /// And that being in the list is what refuses a folder, asked of a tree this machine does not
    /// have: a stand-in for the application data of somebody whose profile keeps it elsewhere.
    /// Asserting it against the real folders could not tell the two rules apart, because on this
    /// machine they name the same tree.
    /// </summary>
    [Fact]
    public void An_application_data_folder_kept_off_the_profile_is_refused_like_any_other()
    {
        using var elsewhere = new TemporaryFolder();
        var moved = new[] { Path.Combine(elsewhere.Folder.FullName, "UserData") };

        CorpusLocation.GoesWhenThePackageDoes(Path.Combine(moved[0], "Meetings"), moved)
            .ShouldBeTrue();
        CorpusLocation.GoesWhenThePackageDoes(
            Path.Combine(elsewhere.Folder.FullName, "Meetings"), moved).ShouldBeFalse();
    }

    /// <summary>
    /// The folder this branch itself fell back to until the packaging question was answered, which
    /// makes this the test that would have caught it. Nothing is created: the folder is refused
    /// before anything is asked of the disk, so a first corpus is never put there.
    /// </summary>
    [Fact]
    public void A_first_corpus_is_never_put_under_app_data()
    {
        using var elsewhere = new TemporaryFolder();
        var virtualized = new DirectoryInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            CorpusLocation.ApplicationFolderName));

        var resolved = new CorpusLocation(
            new FileInfo(Path.Combine(elsewhere.Folder.FullName, CorpusLocation.SettingName)),
            virtualized).Resolve();

        resolved.Refusal.ShouldBe(CorpusRefusal.GoesWhenThePackageDoes);
        resolved.Path.ShouldBe(virtualized.FullName);
        resolved.Folder.ShouldBeNull();
    }

    /// <summary>
    /// Built under this machine's own profile rather than written out literally, because that is
    /// what the check is anchored on — a literal <c>C:\Users\someone\...</c> is nobody's
    /// application data on the machine the test runs on, and asserting about it would prove
    /// nothing.
    /// </summary>
    /// <remarks>
    /// Two ways to the same loss, which is why one rule covers both. What is under the container
    /// an uninstall deletes outright. What is elsewhere under application data a packaged build
    /// never writes to at all: the writes are redirected into that same container, under a path
    /// that reads back exactly as it was asked for. The temp folder is in the list because it is
    /// <c>%LOCALAPPDATA%\Temp</c>, which is where a test would most easily put a corpus by
    /// accident.
    /// </remarks>
    [Fact]
    public void A_folder_under_the_users_application_data_goes_when_the_package_does()
    {
        var applicationData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData");
        var container = CorpusLocation.PackageContainerOfThisUser();

        foreach (var doomed in new[]
        {
            container,
            Path.Combine(container, "Publisher.App_1abc", "LocalState"),
            Path.Combine(container, "Publisher.App_1abc", "LocalCache", "Local", "MeetingTranscriber"),
            Path.Combine(container.ToUpperInvariant(), "PUBLISHER.APP_1ABC", "LOCALCACHE"),
            applicationData,
            Path.Combine(applicationData, "Local", CorpusLocation.ApplicationFolderName),
            Path.Combine(applicationData, "Roaming", CorpusLocation.ApplicationFolderName),
            Path.Combine(applicationData, "LocalLow", CorpusLocation.ApplicationFolderName),
            Path.Combine(applicationData, "Local", "Temp", "corpus"),
        })
        {
            CorpusLocation.GoesWhenThePackageDoes(doomed)
                .ShouldBeTrue($"'{doomed}' is under '{applicationData}'.");
        }
    }

    /// <summary>
    /// The other side of it, and the decoys are the point. A folder goes with the package for being
    /// under this user's own application data, not for being spelled like it: somebody whose corpus
    /// sits on another disk in a folder that happens to be spelled that way keeps it. The first is
    /// where the application actually puts one.
    /// </summary>
    [Fact]
    public void A_folder_outside_it_survives_the_package()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        foreach (var outside in new[]
        {
            Path.Combine(profile, CorpusLocation.ApplicationFolderName),
            Path.Combine(profile, "Documents", "Meetings"),
            Path.Combine(profile, "Packages", "MeetingTranscriber"),
            @"D:\archives\AppData\Local\Packages\Meetings",
            @"D:\archives\AppData\Roaming\Meetings",
            @"D:\Corpus",
        })
        {
            CorpusLocation.GoesWhenThePackageDoes(outside)
                .ShouldBeFalse($"'{outside}' is somebody's own folder.");
        }
    }

    /// <summary>
    /// Where a path is written and where it leads are two questions, and the uninstall answers the
    /// second. A folder on any disk that is a link into the container is deleted with the package
    /// exactly like a folder spelled that way, so a check that only read the spelling would hand
    /// somebody's paid responses to the next uninstall.
    /// </summary>
    [Fact]
    public void A_folder_that_only_leads_into_the_container_goes_with_it_too()
    {
        using var elsewhere = new TemporaryFolder();
        var link = Path.Combine(elsewhere.Folder.FullName, "corpus");
        var inside = Path.Combine(
            CorpusLocation.PackageContainerOfThisUser(),
            $"MeetingTranscriber.Fabricated_{Guid.NewGuid():n}",
            "LocalCache");

        // A junction rather than a symbolic link: a symbolic link needs a privilege a build agent
        // does not have. The target is never created, so no test writes into a real package's
        // folder — and a link into a folder that is not there is exactly what an uninstall leaves.
        Junction(link, inside);

        CorpusLocation.GoesWhenThePackageDoes(link).ShouldBeTrue();
        Naming(elsewhere, link).Resolve().Refusal.ShouldBe(CorpusRefusal.GoesWhenThePackageDoes);
    }

    /// <summary>
    /// One link was followed and the next was not, so a corpus reached through a disk somebody
    /// moved and then through a folder somebody else moved was accepted while its files were in
    /// the container all along. Three reviewers found it independently.
    /// </summary>
    [Fact]
    public void A_folder_that_leads_into_the_container_through_another_link_goes_with_it_too()
    {
        using var elsewhere = new TemporaryFolder();
        var first = Path.Combine(elsewhere.Folder.FullName, "corpus");
        var second = Path.Combine(elsewhere.Folder.FullName, "company-data");
        var inside = Path.Combine(
            CorpusLocation.PackageContainerOfThisUser(),
            $"MeetingTranscriber.Fabricated_{Guid.NewGuid():n}",
            "LocalCache");

        // Neither target is ever created, so nothing is written inside a real package's folder.
        Junction(second, inside);
        Junction(first, second);

        CorpusLocation.GoesWhenThePackageDoes(first).ShouldBeTrue();
        Naming(elsewhere, first).Resolve().Refusal.ShouldBe(CorpusRefusal.GoesWhenThePackageDoes);
    }

    /// <summary>
    /// What following a chain costs if nothing remembers where it has been: two links pointing at
    /// each other, which is a start-up that never finishes rather than a wrong answer. The
    /// assertion is that it answers at all.
    /// </summary>
    [Fact]
    public void A_loop_of_links_is_answered_rather_than_followed()
    {
        using var elsewhere = new TemporaryFolder();
        var here = Path.Combine(elsewhere.Folder.FullName, "here");
        var there = Path.Combine(elsewhere.Folder.FullName, "there");

        Junction(here, there);
        Junction(there, here);

        CorpusLocation.GoesWhenThePackageDoes(here).ShouldBeFalse();
    }

    [Fact]
    public void The_corpus_opens_where_the_setting_says()
    {
        using var moved = new TemporaryFolder();
        var corpus = Corpus(moved);
        using var elsewhere = new TemporaryFolder();
        var location = At(elsewhere);

        location.Choose(corpus);

        var resolved = location.Resolve();
        resolved.Refusal.ShouldBeNull();
        resolved.Folder!.FullName.ShouldBe(corpus.FullName);
    }

    /// <summary>
    /// Read back through a second <see cref="CorpusLocation"/> over the same file, so what is
    /// proved is what is on disk and not what the object that wrote it remembers.
    /// </summary>
    [Fact]
    public void The_same_folder_opens_again_the_next_time_the_application_starts()
    {
        using var moved = new TemporaryFolder();
        var corpus = Corpus(moved);
        using var elsewhere = new TemporaryFolder();

        At(elsewhere).Choose(corpus);

        At(elsewhere).Resolve().Folder!.FullName.ShouldBe(corpus.FullName);
    }

    [Fact]
    public void A_folder_that_does_not_answer_is_refused_naming_it()
    {
        using var elsewhere = new TemporaryFolder();
        var gone = Path.Combine(elsewhere.Folder.FullName, "on-a-disk-nobody-plugged-in");
        var location = Naming(elsewhere, gone);

        var resolved = location.Resolve();

        resolved.Refusal.ShouldBe(CorpusRefusal.FolderDoesNotAnswer);
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
    /// Where a folder is is asked before anything else, so a corpus that is really there and really
    /// opens is still refused for being somewhere an uninstall would take it.
    /// </summary>
    /// <remarks>
    /// A real folder under this user's real <c>%LOCALAPPDATA%</c>, named for this test and removed
    /// again at the end. Nowhere else can carry it: the rule is about that tree in particular, and
    /// a fabricated one on another disk is a folder no uninstall would ever touch. It stands beside
    /// the package container rather than inside it — a fabricated package family in a folder
    /// Windows owns is litter a failed cleanup leaves somewhere nobody would look, and the
    /// redirected half of the rule is the half worth probing against a corpus that is really there.
    /// </remarks>
    [Fact]
    public void A_corpus_under_the_users_application_data_is_refused_though_it_is_there_and_whole()
    {
        using var elsewhere = new TemporaryFolder();
        var doomed = new DirectoryInfo(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            $"{CorpusLocation.ApplicationFolderName}.Fabricated_{Guid.NewGuid():n}"));
        doomed.Create();

        try
        {
            // Migrating is what puts a corpus in the folder, which is what makes the refusal below
            // about where it is rather than about there being nothing there.
            using (CorpusDatabase.OpenMigrated(doomed))
            {
            }

            CorpusDatabase.HoldsACorpus(doomed).ShouldBeTrue();

            var resolved = Naming(elsewhere, doomed.FullName).Resolve();

            resolved.Refusal.ShouldBe(CorpusRefusal.GoesWhenThePackageDoes);
            resolved.Path.ShouldBe(doomed.FullName);
            resolved.Folder.ShouldBeNull();
        }
        finally
        {
            CorpusDatabase.ClearPoolsFor(doomed);
            try
            {
                doomed.Delete(recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Same call TemporaryFolder makes, for the same two Windows refusals.
            }
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
    /// The gap an adversarial review found, and the only route left to the failure the whole
    /// refusal is built against. Somebody drags their corpus folder somewhere else in Explorer
    /// rather than through the application. There is no setting to refuse — nobody ever chose —
    /// so resolution lands on the folder the application would have used, which is now empty.
    /// </summary>
    /// <remarks>
    /// What stops it is that resolution never says "open this" without also saying whether there
    /// is a corpus there. Making one is then something whoever opens it was told about, not
    /// something nothing objected to — so an empty list is always preceded by a sentence.
    /// </remarks>
    [Fact]
    public void Somewhere_the_application_would_put_a_corpus_says_whether_one_is_there_yet()
    {
        using var elsewhere = new TemporaryFolder();
        var location = At(elsewhere);

        var firstRun = location.Resolve();
        firstRun.Refusal.ShouldBeNull();
        firstRun.HoldsACorpus.ShouldBeFalse();

        location.Fallback.Create();
        using (CorpusDatabase.OpenMigrated(location.Fallback))
        {
        }

        try
        {
            location.Resolve().HoldsACorpus.ShouldBeTrue();

            // And the drag: the folder goes, and the next start says there is nothing there rather
            // than opening it as though there had never been anything.
            CorpusDatabase.ClearPoolsFor(location.Fallback);
            Directory.Delete(location.Fallback.FullName, recursive: true);

            var afterTheDrag = location.Resolve();
            afterTheDrag.HoldsACorpus.ShouldBeFalse();
            afterTheDrag.Path.ShouldBe(location.Fallback.FullName);
        }
        finally
        {
            CorpusDatabase.ClearPoolsFor(location.Fallback);
        }
    }

    /// <summary>
    /// A corpus somebody moved through the application is a folder that answers and holds one, and
    /// it says so — the other half of the signal above, which no caller should have to infer from
    /// the absence of a refusal.
    /// </summary>
    [Fact]
    public void A_corpus_the_setting_names_says_it_is_already_there()
    {
        using var moved = new TemporaryFolder();
        var corpus = Corpus(moved);
        using var elsewhere = new TemporaryFolder();
        var location = At(elsewhere);

        location.Choose(corpus);

        location.Resolve().HoldsACorpus.ShouldBeTrue();
    }

    /// <summary>
    /// The pointer is written beside itself and moved into place, so a machine that stops during
    /// the write leaves the corpus somebody already has still pointed at.
    /// </summary>
    [Fact]
    public void Recording_where_the_corpus_is_leaves_no_half_written_pointer()
    {
        using var moved = new TemporaryFolder();
        var corpus = Corpus(moved);
        using var elsewhere = new TemporaryFolder();
        var location = At(elsewhere);

        location.Choose(corpus);

        File.ReadAllText(location.Setting.FullName).ShouldBe(corpus.FullName);
        location.Setting.Directory!.EnumerateFiles()
            .Select(file => file.Name)
            .ShouldBe([CorpusLocation.SettingName]);
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
    /// <remarks>
    /// Moved and not copied, and that is the assertion rather than an incidental choice: a copy
    /// would leave the files where they were, so a path that had been stored absolute would still
    /// find them and this would go green over the one mistake it exists to catch. Only the old
    /// folder ceasing to exist makes a stale path fail.
    /// </remarks>
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

        // The folder cannot move while a pooled connection holds the database open, and it cannot
        // move while anything else has a file under it open either — see Folders for the second.
        CorpusDatabase.ClearPoolsFor(origin);
        Folders.MoveWaitingOutWhoeverHasIt(origin, destination);

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
            + "under this user's application data.");
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

    /// <summary>
    /// A directory junction, made the one way that needs no privilege a build agent lacks. .NET
    /// can create a symbolic link and not a junction, and a symbolic link needs Developer Mode or
    /// an elevated process — neither of which a test may assume.
    /// </summary>
    private static void Junction(string link, string target)
    {
        using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{link}\" \"{target}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new InvalidOperationException("cmd.exe did not start.");

        mklink.WaitForExit();
        mklink.ExitCode.ShouldBe(0, $"mklink said: {mklink.StandardError.ReadToEnd()}");
    }

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

    /// <summary>
    /// A corpus in a folder of this test's own. Not <c>TemporaryCorpus</c>, which makes one under
    /// <c>Path.GetTempPath()</c> — <c>%LOCALAPPDATA%\Temp</c> on Windows, inside the one tree this
    /// rule refuses — so a corpus there would prove the refusal and never the thing being asserted.
    /// </summary>
    private static DirectoryInfo Corpus(TemporaryFolder folder)
    {
        using (CorpusDatabase.OpenMigrated(folder.Folder))
        {
        }

        return folder.Folder;
    }

    /// <summary>Whether this path is that folder or inside it, asked of paths and not of disks.</summary>
    private static bool Under(string path, string root)
    {
        var relative = Path.GetRelativePath(Path.GetFullPath(root), Path.GetFullPath(path));

        return !Path.IsPathRooted(relative)
            && relative != ".."
            && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
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
            // Not Path.GetTempPath(), which on Windows is %LOCALAPPDATA%\Temp — inside the one
            // tree this whole class is about a corpus never being in. Every test below would prove
            // the refusal and nothing else. What is left that is short, writable and outside it is
            // the folder the test binary runs from, which is build output and goes with it.
            Folder = new DirectoryInfo(Path.Combine(
                AppContext.BaseDirectory, "corpus-location", Guid.NewGuid().ToString("n")[..8]));

            CorpusLocation.GoesWhenThePackageDoes(Folder.FullName).ShouldBeFalse(
                $"'{Folder.FullName}' is where these tests put a corpus, and a corpus there is "
                + "refused for being under this user's application data. Whatever is being asserted "
                + "below, that is what would be proved instead.");

            Folder.Create();
        }

        public DirectoryInfo Folder { get; }

        public void Dispose()
        {
            // Without this a pooled connection still holds a corpus made in here and the delete
            // fails. Only this folder's, for the reason TemporaryCorpus gives.
            CorpusDatabase.ClearPoolsFor(Folder);

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
