namespace MeetingTranscriber.App.Tests;

/// <summary>
/// What the packaged application is allowed to reach, spelled out here so a capability cannot
/// arrive or leave without somebody saying why.
/// </summary>
/// <remarks>
/// <para>
/// A capability is the one part of this application whose absence nothing else catches. The
/// microphone list draws from <c>AudioDevices.Microphones</c>, which never activates a device, so
/// an install with no <c>microphone</c> declaration starts normally and looks right; the refusal
/// arrives on the press, as <c>E_ACCESSDENIED</c> out of <c>IAudioClient</c> activation, whatever
/// the Windows privacy pages say. Somebody installs the application, presses record, and it fails
/// — every time, on every machine — and what it leaves behind is a stub at
/// <c>MeetingStage.Recording</c>, which <c>MeetingScreen.ItMayBeFiled</c> then refuses to let
/// anybody file. That is the first step of the product, and the whole of the fix is one line of
/// XML, which is exactly the kind of line that gets lost in a merge.
/// </para>
/// <para>
/// So the assertion is a whole set and not a contains: a capability <em>arriving</em> is as much a
/// thing to answer as one leaving. A packaged identity asking for more than it needs is what an
/// install prompt reads out to somebody, and nothing else in this repository would notice one
/// being added.
/// </para>
/// <para>
/// This reads the source manifest. Whether the package that was actually built carries what the
/// source says is <see cref="PackagedAppTests"/>, which reads a built <c>.msix</c> through the
/// same <see cref="PackageManifest"/> — so what it compares is two documents and never two
/// readers.
/// </para>
/// <para>
/// What neither of them reaches is the run: whether Windows really hands the device over on a
/// packaged build is a person starting an installed copy and pressing record, which is what
/// ISC-56's verification line now says is still owed.
/// </para>
/// </remarks>
public class PackageManifestTests
{
    [Fact]
    public void The_application_declares_the_microphone_it_records_on() =>
        PackageManifest.Declared()
            .ShouldBe(
                // The namespace and not the prefix, for the reason PackageManifest.CapabilitiesOf
                // gives: this list is compared against one the MSIX tooling writes, and a prefix is
                // a document's spelling rather than what it means. Sorted ordinally over the whole
                // string, which is why the restricted namespace comes first - it is the longer URI
                // and '/' sorts below '}'.
                [
                    "{http://schemas.microsoft.com/appx/manifest/foundation/windows10"
                    + "/restrictedcapabilities}Capability runFullTrust",
                    "{http://schemas.microsoft.com/appx/manifest/foundation/windows10}"
                    + "DeviceCapability microphone",
                ],
                "channel 1 is the microphone, and a packaged identity that has not declared it is "
                + "refused at IAudioClient activation, so pressing record fails on every install. "
                + "If a capability is missing here, put it back in Package.appxmanifest; if one is "
                + "here that the package does not declare, say on this list what the application "
                + "does with it and what somebody is agreeing to at install.");

    /// <summary>
    /// The two faces used from outside the window, each with a name on the user's <c>PATH</c>.
    /// </summary>
    /// <remarks>
    /// A whole set and not a contains, for the same reason the capabilities above are: an alias
    /// <em>arriving</em> is a name this install takes over on that user's <c>PATH</c> from whatever
    /// already answered to it, and nothing else in this repository would notice one being added. One
    /// leaving is the other half — an MCP client's config file names the executable and nothing
    /// else, so an alias that goes takes the server with it. That the two executables are really in
    /// the built package, and that the alias and the face agree at all, are
    /// <see cref="PackagedAppTests"/> and <c>MeetingTranscriber.App.csproj</c>'s own two
    /// <c>&lt;Error&gt;</c>s.
    /// </remarks>
    [Fact]
    public void The_application_declares_an_alias_for_each_face_used_from_outside_the_window() =>
        PackageManifest.AliasesOf(PackageManifest.Source())
            .ShouldBe(
                // The `.exe` on the alias is ST_ExecutableNoPath's requirement; what a person types
                // is `meeting-transcriber`.
                [
                    "meeting-transcriber-mcp.exe → meeting-transcriber-mcp.exe",
                    "meeting-transcriber.exe → meeting-transcriber.exe",
                ],
                "inside an MSIX the install directory is read-only and its path changes with every "
                + "version, so an alias is the only way a prompt or an MCP client's config file "
                + "reaches either of these. If one is missing here, put it back in "
                + "Package.appxmanifest; if one is here that no face answers, it is a name this "
                + "install takes on the user's PATH for an executable that is not there.");

    /// <summary>
    /// That the window is still the first <c>&lt;Application&gt;</c> the manifest declares.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every other assertion here is over a sorted set, precisely so that document order is nobody's
    /// business. This one is the exception, and it exists because something outside this suite reads
    /// it: <c>tools/MeetingTranscriber.UiProbe/ApplicationId.cs</c> activates the <em>first</em>
    /// <c>&lt;Application&gt;</c>, which was the only one until the two faces each needed one of
    /// their own — <c>MakeAppx</c> refuses two <c>windows.appExecutionAlias</c> extensions under one
    /// application.
    /// </para>
    /// <para>
    /// So an alphabetical tidy-up or a merge that put <c>Cli</c> above <c>App</c> would have every
    /// probe session launch a console face as *the window*, and then fail on a window that never
    /// appears. The whole list is asserted rather than only its head, because a face added without
    /// an alias is the other thing this document can be got wrong by, and both are one line to fix.
    /// </para>
    /// </remarks>
    [Fact]
    public void The_window_is_the_first_application_the_manifest_declares() =>
        PackageManifest.ApplicationIdsOf(PackageManifest.Source())
            .ShouldBe(
                ["App", "Cli", "Mcp"],
                "tools/MeetingTranscriber.UiProbe/ApplicationId.cs activates the first "
                + "<Application> in this manifest, so the window has to be first and the two "
                + "console faces after it. If this is red because the order moved, the probe is "
                + "about to start a console face and wait for a window that never appears; if it "
                + "is red because an Id arrived, say here what that application is and check "
                + "MeetingTranscriber.App.csproj publishes what it names.");
}
