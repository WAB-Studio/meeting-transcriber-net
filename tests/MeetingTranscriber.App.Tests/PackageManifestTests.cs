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
}
