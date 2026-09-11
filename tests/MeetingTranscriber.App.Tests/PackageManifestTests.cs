using System.Xml.Linq;

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
/// What it does not reach is the run: whether Windows really hands the device over on a packaged
/// build is a person starting an installed copy and pressing record, which is what ISC-56's
/// verification line now says is still owed.
/// </para>
/// </remarks>
public class PackageManifestTests
{
    /// <summary>
    /// Every capability the package declares, spelled the way the manifest spells it — prefix and
    /// all.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The prefix travels with the name because it is the namespace, and the namespace is the
    /// question. <c>rescap:Capability</c> is restricted and is what the install prompt reads out;
    /// the same word without the prefix is a different declaration Windows answers differently, and
    /// a package that lost it is broken in a way nothing else here would see. Reading
    /// <c>LocalName</c> alone would let exactly that through, which is the mistake this is written
    /// against.
    /// </para>
    /// <para>
    /// Every child of <c>&lt;Capabilities&gt;</c> is read, not the kinds named in the list below, so
    /// a kind nobody has used yet — a custom capability, a device this application does not touch —
    /// arrives as a failure rather than as nothing. <c>Single</c> and <c>Elements</c> do the rest:
    /// a manifest with no <c>&lt;Capabilities&gt;</c> throws rather than passing over an empty set,
    /// and the comment now inside the element is an <c>XComment</c>, which <c>Elements</c> skips.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<string> Declared()
    {
        // One manifest, because this is about *the* package: a second is a second answer to what
        // the application may reach, and which one it is installed from would be whichever the
        // build picked.
        var manifest = AppSources.With(".appxmanifest").ShouldHaveSingleItem();

        return
        [
            .. XDocument.Load(manifest.FullName)
                .Root!
                .Elements()
                .Single(element => element.Name.LocalName == "Capabilities")
                .Elements()
                .Select(Spelled)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>One declaration as the manifest writes it: <c>prefix:LocalName Name</c>.</summary>
    private static string Spelled(XElement element)
    {
        var prefix = element.GetPrefixOfNamespace(element.Name.Namespace);

        return (prefix is null ? string.Empty : $"{prefix}:")
            + element.Name.LocalName
            + $" {element.Attribute("Name")?.Value}";
    }

    [Fact]
    public void The_application_declares_the_microphone_it_records_on() =>
        Declared()
            .ShouldBe(
                [
                    "DeviceCapability microphone",
                    "rescap:Capability runFullTrust",
                    "systemai:Capability systemAIModels",
                ],
                "channel 1 is the microphone, and a packaged identity that has not declared it is "
                + "refused at IAudioClient activation, so pressing record fails on every install. "
                + "If a capability is missing here, put it back in Package.appxmanifest; if one is "
                + "here that the package does not declare, say on this list what the application "
                + "does with it and what somebody is agreeing to at install. systemAIModels is on "
                + "this list unexplained — nothing in this repository names a Windows AI model — "
                + "and pinning it is not agreeing with it.");
}
