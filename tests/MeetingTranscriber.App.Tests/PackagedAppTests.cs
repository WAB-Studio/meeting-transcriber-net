using System.Diagnostics;
using System.IO.Compression;
using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// What the signed <c>.msix</c> a person is handed actually contains, read off the package itself
/// rather than off the sources it was built from.
/// </summary>
/// <remarks>
/// <para>
/// This is the only check in this repository that looks at a built package. Everything else here
/// reads <c>src/</c>, which answers what was written and not what the packaging build did with it
/// — and the gap between those two is where a package that starts, shows its window and cannot
/// record lives.
/// </para>
/// <para>
/// <b>It skips when no package has been built, unless it is asked not to.</b> <c>dotnet build</c>
/// over the solution produces no <c>.msix</c>: the package appears only from a Release publish with
/// <c>GenerateAppxPackageOnBuild=true</c>, which is a person's command out of
/// <c>docs/packaging.md</c> and takes a code-signing certificate this repository does not carry.
/// CI never runs it, so on CI these skip. But a skipped test and a passing test read the same in a
/// summary line, so a green run proves nothing on its own — which is why setting
/// <see cref="Expected"/> turns the absence of a package from a skip into a failure. That is the
/// form <c>docs/packaging.md</c> tells a person to run: build the package, then ask the suite to
/// prove it, and be told when it could not rather than shown a green that meant nothing.
/// </para>
/// <para>
/// The alternative — a test that built its own package — would put minutes and a certificate into
/// every <c>dotnet test</c>, and would prove its own invocation rather than the command
/// <c>docs/packaging.md</c> tells a person to type.
/// </para>
/// <para>
/// <b>What none of these reaches.</b> They compare the package against the *manifest*, so a package
/// built a week ago from entirely different C# passes every one of them as long as
/// <c>Package.appxmanifest</c> has not moved — there is no staleness rule here, and the failure
/// messages do not claim one. That is why the packaging build and this run are one act in
/// <c>docs/packaging.md</c> and not two joined by memory. Nor does anything here start the package:
/// whether Windows really hands the microphone over on a packaged build is ISC-56's, and installing
/// it anywhere is #151.
/// </para>
/// </remarks>
public class PackagedAppTests
{
    /// <summary>
    /// The variable that says a package is meant to be there, so its absence is a failure.
    /// </summary>
    /// <remarks>
    /// An environment variable and not a trait, because the thing that knows is the shell the
    /// person is standing in one command after the publish — not the test project, which cannot
    /// tell a machine that never packaged from one that packaged and then deleted it.
    /// </remarks>
    private const string Expected = "MEETING_TRANSCRIBER_EXPECT_PACKAGE";

    /// <summary>
    /// The package these read, or a skip — or, under <see cref="Expected"/>, a failure.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Asked once per fact rather than once per class so the skip reads at the top of each, but it
    /// is one rule in one place: which file is the package under test is a decision this answers
    /// and nothing else does.
    /// </para>
    /// <para>
    /// Newest rather than only, because <c>AppPackages/</c> accumulates: a version bump makes a
    /// second folder and the one these read is the one that was just made. A clean checkout has no
    /// such folder at all, which is the ordinary state.
    /// </para>
    /// <para>
    /// <b><c>Dependencies/</c> is skipped, and that is not tidiness.</b> A sideload layout carries
    /// the Windows App SDK framework packages beside the application's own, one per architecture,
    /// and they are <c>.msix</c> files too — bigger, and stamped with whenever NuGet restored them
    /// rather than whenever this was packaged. Without the skip, a machine that restored a newer
    /// SDK after packaging would read Microsoft's framework package here and report that *this*
    /// application's manifest does not match it: a red that sends somebody to rebuild something
    /// already correct.
    /// </para>
    /// </remarks>
    private static FileInfo Packaged()
    {
        var packages = new DirectoryInfo(
            AppSources.At("MeetingTranscriber.App/AppPackages").FullName);

        var newest = packages.Exists
            ? packages
                .EnumerateFiles("*.msix", SearchOption.AllDirectories)
                .Where(file => !file.FullName.Contains(
                    $"{Path.DirectorySeparatorChar}Dependencies{Path.DirectorySeparatorChar}",
                    StringComparison.OrdinalIgnoreCase))
                .MaxBy(file => file.LastWriteTimeUtc)
            : null;

        if (newest is not null)
        {
            return newest;
        }

        const string Missing =
            "No .msix under src/MeetingTranscriber.App/AppPackages. Build one - see "
            + "docs/packaging.md - and run this again.";

        if (Environment.GetEnvironmentVariable(Expected) is { Length: > 0 })
        {
            throw new InvalidOperationException(
                $"{Missing} {Expected} is set, which says a package was meant to be here, so this "
                + "is a failure and not a skip: the packaging build did not run, did not finish, "
                + "or wrote somewhere else.");
        }

        Assert.Skip(Missing);
        throw new UnreachableException();
    }

    [Fact]
    public void The_package_carries_the_identity_the_manifest_declares()
    {
        using var msix = ZipFile.OpenRead(Packaged().FullName);

        var declared = PackageManifest.IdentityOf(PackageManifest.Source());
        var carried = PackageManifest.IdentityOf(Manifest(msix));

        // One message for the three that come off the manifest, because any of them differing
        // means the same thing and the same fix. It does not say the package is *older* - nothing
        // here compares a timestamp to anything, and a message that named an ordering it had not
        // checked would be a diagnosis this cannot make. A suffixed Name is the one difference
        // that names its own cause: PackageIdentity.props is imported only under
        // Configuration == Debug, so a suffix here means the publish was not a Release one.
        const string Differs =
            "the package in AppPackages was built from a different manifest than the one now in "
            + "src/MeetingTranscriber.App/Package.appxmanifest. Build the package again - see "
            + "docs/packaging.md - or find out which of the two moved.";

        Value(carried, "Name").ShouldBe(Value(declared, "Name"), Differs);
        Value(carried, "Publisher").ShouldBe(Value(declared, "Publisher"), Differs);
        Value(carried, "Version").ShouldBe(Value(declared, "Version"), Differs);

        // Not off the manifest: the source declares no ProcessorArchitecture at all. The packaging
        // build writes it from -p:Platform=x64, so this is the one attribute here that is a fact
        // about the command rather than about the document beside it.
        Value(carried, "ProcessorArchitecture").ShouldBe(
            "x64",
            "the packaging build is -p:Platform=x64 against a win-x64 publish profile; anything "
            + "else here is a package built for an architecture nobody chose.");
    }

    [Fact]
    public void The_package_declares_exactly_the_capabilities_the_manifest_does()
    {
        using var msix = ZipFile.OpenRead(Packaged().FullName);

        PackageManifest.CapabilitiesOf(Manifest(msix))
            .ShouldBe(
                PackageManifest.Declared(),
                "what an install prompt reads out to somebody is the package's list and not the "
                + "source's. The two differing means the package was built from a different "
                + "manifest; build it again - see docs/packaging.md.");
    }

    [Fact]
    public void The_package_needs_the_windows_app_runtime_from_outside_itself()
    {
        using var msix = ZipFile.OpenRead(Packaged().FullName);

        PackageManifest.DependenciesOf(Manifest(msix))
            .ShouldBe(
                ["Microsoft.WindowsAppRuntime.2 >= 2.3.1.0"],
                "this list is what somebody on the receiving end has to have before the package "
                + "will install at all, and docs/packaging.md is written off it: a dependency that "
                + "arrives or leaves here changes what they are handed beside the .msix, and "
                + "Add-AppxPackage answers a missing one with 0x80073CF3 and no name. If this is "
                + "empty, WindowsAppSDKSelfContained was turned on and the document's hand-off "
                + "section is now telling people to carry something that is already inside.");
    }

    /// <remarks>
    /// That the signature is <em>there</em>, and not who made it. The thing worth asserting is that
    /// the signer's subject equals <c>&lt;Identity Publisher&gt;</c> — Windows refuses the package
    /// when they differ and says only that the publisher does not match, not which of the two is
    /// wrong, which is the hour <c>docs/packaging.md</c>'s first warning exists to save. Reading it
    /// means decoding the PKCS#7 inside <c>AppxSignature.p7x</c>, and <c>SignedCms</c> wants
    /// <c>System.Security.Cryptography.Pkcs</c> referenced in this project's file, which this
    /// change does not own. So the document says to read the signer with
    /// <c>Get-AuthenticodeSignature</c> rather than claiming this does it.
    /// </remarks>
    [Fact]
    public void The_package_is_signed()
    {
        using var msix = ZipFile.OpenRead(Packaged().FullName);

        Names(msix).Contains("AppxSignature.p7x").ShouldBeTrue(
            "an unsigned .msix cannot be installed by Add-AppxPackage at all, and the packaging "
            + "build produces one silently when -p:AppxPackageSigningEnabled=true is left off.");
    }

    [Fact]
    public void The_package_carries_its_own_runtime_its_own_sqlite_and_its_own_fonts()
    {
        using var msix = ZipFile.OpenRead(Packaged().FullName);
        var names = Names(msix);

        names.Contains("MeetingTranscriber.App.exe").ShouldBeTrue(
            "a package with no executable is a package that installs and starts nothing.");

        names.Contains("coreclr.dll").ShouldBeTrue(
            "the .NET runtime travels inside the package: `SelfContained` in win-x64.pubxml is "
            + "what puts it there, and without it the application needs a .NET install nobody on "
            + "the receiving end was asked for.");

        names.Contains("resources.pri").ShouldBeTrue(
            "WinUI resolves its resources through the package's resource index; a package without "
            + "one starts and draws nothing.");

        names.Contains("e_sqlite3.dll").ShouldBeTrue(
            "the corpus is SQLite, and this is the native half SQLitePCLRaw loads by name at the "
            + "first query. It is also the concrete thing the csproj's `PublishTrimmed` argument "
            + "is about: a trimmer sees nothing reach it, and what a trimmed package fails on is "
            + "somebody's first query against their own corpus.");

        // Both fonts, one reason: docs/design.md sets every text in one and every compared number
        // in the other, and a dropped CopyToOutputDirectory is a failure found by photographing a
        // window and by nothing else - the window renders whole, in Segoe UI.
        const string Fonts =
            "the fonts are carried, not assumed: Content alone indexes a font into resources.pri "
            + "and puts no file on disk, and ms-appx:/// has to reach a real one.";

        names.Contains("Assets/Fonts/SpaceGrotesk.ttf").ShouldBeTrue(Fonts);
        names.Contains("Assets/Fonts/JetBrainsMono.ttf").ShouldBeTrue(Fonts);
    }

    /// <summary>The manifest the package carries, which is not the one in <c>src/</c>.</summary>
    private static XDocument Manifest(ZipArchive msix)
    {
        var entry = msix.GetEntry("AppxManifest.xml")
            ?? throw new InvalidOperationException(
                "the package holds no AppxManifest.xml, which means it is not an .msix.");

        using var manifest = entry.Open();

        return XDocument.Load(manifest);
    }

    /// <summary>One attribute of an element the tooling wrote, or a sentence saying it is gone.</summary>
    private static string Value(XElement element, string attribute) =>
        element.Attribute(attribute)?.Value
            ?? throw new InvalidOperationException(
                $"<{element.Name.LocalName}> has no {attribute} attribute, so what the package "
                + "says about itself cannot be compared with what the source manifest does.");

    /// <summary>
    /// Every entry in the package, compared the way a package path is compared: entry names inside
    /// an <c>.msix</c> use <c>/</c> whatever the machine's separator is, and case is not part of
    /// the name. A <c>HashSet</c> and not a list, because <c>ShouldContain</c> over a sequence
    /// would compare ordinally and go red over a letter's case.
    /// </summary>
    private static HashSet<string> Names(ZipArchive msix) =>
        new(msix.Entries.Select(entry => entry.FullName), StringComparer.OrdinalIgnoreCase);
}
