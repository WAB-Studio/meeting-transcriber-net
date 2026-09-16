using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// What a manifest declares the application is and may reach, read the same way wherever the
/// manifest came from.
/// </summary>
/// <remarks>
/// Two classes ask it of two different documents — <see cref="PackageManifestTests"/> of the
/// source manifest in <c>src/</c>, <see cref="PackagedAppTests"/> of the <c>AppxManifest.xml</c>
/// inside a built <c>.msix</c> — and the whole point of the second is that it agrees with the
/// first. Two readers would make that comparison a comparison of two readers.
/// </remarks>
internal static class PackageManifest
{
    /// <summary>The source manifest, which there is exactly one of.</summary>
    /// <remarks>
    /// One manifest, because this is about *the* package: a second is a second answer to what the
    /// application may reach, and which one it is installed from would be whichever the build
    /// picked. A sideload layout under <c>AppPackages/</c> holds no loose <c>.appxmanifest</c> —
    /// its generated one is <c>AppxManifest.xml</c>, inside the <c>.msix</c> — so there is nothing
    /// for <c>AppSources.With</c> to skip beyond the <c>obj</c> and <c>bin</c> it already does.
    /// Checked on a machine that had just run the packaging build.
    /// </remarks>
    internal static XDocument Source() =>
        XDocument.Load(AppSources.With(".appxmanifest").ShouldHaveSingleItem().FullName);

    /// <summary>
    /// The <c>Identity</c> element, which is what the package is called and who signed it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// There is no such element, which of a packaged manifest means the tooling wrote something
    /// this does not understand — a sentence rather than a <c>NullReferenceException</c> two
    /// dereferences later.
    /// </exception>
    internal static XElement IdentityOf(XDocument manifest) =>
        Only(manifest, "Identity");

    /// <summary>
    /// Every capability <paramref name="manifest"/> declares, spelled <c>{namespace}LocalName
    /// Name</c>, sorted ordinally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>The namespace and not the prefix.</b> A restricted capability — <c>runFullTrust</c> under
    /// <c>…/restrictedcapabilities</c> — is what the install prompt reads out; the same word in the
    /// foundation namespace is a different declaration Windows answers differently, and a package
    /// that lost the distinction is broken in a way nothing else here would see. Reading
    /// <c>LocalName</c> alone lets exactly that through. Reading the *prefix* would be no better in
    /// the other direction: a prefix is one document's spelling of a namespace, and the two
    /// documents this runs over have two authors — the source manifest is hand-written and the
    /// packaged one is emitted by the MSIX tooling. A tooling version that spelled the same
    /// namespace <c>rescap2:</c> would go red for a difference neither side chose, with a message
    /// telling somebody to rebuild a package that was already right. <c>XName</c>'s own
    /// <c>{namespace}local</c> form is the thing both documents actually agree on.
    /// </para>
    /// <para>
    /// Every child of <c>&lt;Capabilities&gt;</c> is read, not the kinds named in the callers'
    /// lists, so a kind nobody has used yet — a custom capability, a device this application does
    /// not touch — arrives as a failure rather than as nothing. The comment inside the element is
    /// an <c>XComment</c>, which <c>Elements</c> skips.
    /// </para>
    /// <para>
    /// Sorted here and not by whoever asked, for the same reason the prefix is not compared:
    /// document order is the writer's and neither writer chose it.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> CapabilitiesOf(XDocument manifest)
    {
        return
        [
            .. Only(manifest, "Capabilities")
                .Elements()
                .Select(element => $"{element.Name} {element.Attribute("Name")?.Value}")
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The packages <paramref name="manifest"/> needs to be on the machine already, spelled
    /// <c>Name &gt;= MinVersion</c>, sorted ordinally.
    /// </summary>
    /// <remarks>
    /// Not everything an application needs travels inside its package. A framework dependency is
    /// resolved at install time and its absence is the whole of the install failing, so which ones
    /// there are decides what somebody has to be handed beside the <c>.msix</c> —
    /// <c>docs/packaging.md</c> is written off this list and would otherwise drift from it
    /// silently.
    /// </remarks>
    internal static IReadOnlyList<string> DependenciesOf(XDocument manifest)
    {
        return
        [
            .. Only(manifest, "Dependencies")
                .Elements()
                .Where(element => element.Name.LocalName == "PackageDependency")
                .Select(element =>
                    $"{element.Attribute("Name")?.Value} >= {element.Attribute("MinVersion")?.Value}")
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// Every app execution alias <paramref name="manifest"/> declares, spelled
    /// <c>Executable → Alias</c>, sorted ordinally.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Both halves, because they are two separate things to get wrong: the executable is what the
    /// package has to carry, and the alias is the name that goes on the <c>PATH</c>.
    /// <c>Package.appxmanifest</c> says why there is an alias at all.
    /// </para>
    /// <para>
    /// By <c>{namespace}LocalName</c> and never by prefix, for the reason
    /// <see cref="CapabilitiesOf"/> gives at length. Here that has a second edge: <c>uap5</c> is in
    /// the manifest's <c>IgnorableNamespaces</c>, so a block written into the wrong namespace is
    /// silently dropped by the tooling rather than refused — which is a difference between the two
    /// documents, and invisible inside either one.
    /// </para>
    /// <para>
    /// Found by descending rather than by walking to one <c>Application</c>: the schema allows more
    /// than one shape and which one the tooling took is not what these facts are about. A manifest
    /// declaring no alias answers an empty list, so a caller fails on its own sentence rather than
    /// on a missing element here.
    /// </para>
    /// </remarks>
    internal static IReadOnlyList<string> AliasesOf(XDocument manifest)
    {
        return
        [
            .. AliasExtensionsIn(manifest)
                .SelectMany(extension => extension
                    .Descendants()
                    .Where(element => element.Name.LocalName == "ExecutionAlias")
                    .Select(alias =>
                        $"{extension.Attribute("Executable")?.Value} → "
                        + $"{alias.Attribute("Alias")?.Value}"))
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>
    /// The executables <paramref name="manifest"/> puts an alias over, each once.
    /// </summary>
    /// <remarks>
    /// What a fact about the *package* asks of the manifest: the set of executables that have to be
    /// inside it. Derived rather than written down again, so a face arriving is a face the packaged
    /// facts start checking without anybody remembering to add it to a list.
    /// </remarks>
    internal static IReadOnlyList<string> AliasedExecutablesIn(XDocument manifest)
    {
        return
        [
            .. AliasExtensionsIn(manifest)
                .Select(extension => extension.Attribute("Executable")?.Value ?? string.Empty)
                .Where(executable => executable.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Order(StringComparer.Ordinal),
        ];
    }

    /// <summary>Every <c>windows.appExecutionAlias</c> extension, wherever it sits.</summary>
    private static IEnumerable<XElement> AliasExtensionsIn(XDocument manifest) =>
        manifest.Descendants()
            .Where(element =>
                element.Name.LocalName == "Extension"
                && element.Attribute("Category")?.Value == "windows.appExecutionAlias");

    /// <summary>
    /// The <c>Id</c> of each <c>&lt;Application&gt;</c>, in the order the manifest declares them.
    /// </summary>
    /// <remarks>
    /// Order, which nothing else here reads, because one thing outside this suite depends on it:
    /// <c>tools/MeetingTranscriber.UiProbe/ApplicationId.cs</c> activates the <em>first</em>
    /// <c>&lt;Application&gt;</c>. Every other list in this class is sorted precisely so document
    /// order cannot matter; this one is the exception and says so.
    /// </remarks>
    internal static IReadOnlyList<string> ApplicationIdsOf(XDocument manifest)
    {
        return
        [
            .. Only(manifest, "Applications")
                .Elements()
                .Where(element => element.Name.LocalName == "Application")
                .Select(element => element.Attribute("Id")?.Value ?? string.Empty),
        ];
    }

    /// <summary>What the application declares, which is <see cref="CapabilitiesOf"/> the source.</summary>
    internal static IReadOnlyList<string> Declared() => CapabilitiesOf(Source());

    /// <summary>
    /// The one child of the root called <paramref name="localName"/>.
    /// </summary>
    /// <remarks>
    /// By local name, unlike the capabilities below it, and that is deliberate rather than the same
    /// rule applied twice differently: this is *where to look*, and being tolerant there means a
    /// manifest schema that moved its foundation namespace fails on the comparison, saying which
    /// declaration differs, instead of failing to find the element at all.
    /// </remarks>
    private static XElement Only(XDocument manifest, string localName) =>
        manifest.Root!.Elements().SingleOrDefault(element => element.Name.LocalName == localName)
            ?? throw new InvalidOperationException(
                $"the manifest has no single <{localName}> element, so what it declares there "
                + "cannot be read. If this is a packaged manifest, the MSIX tooling wrote a shape "
                + "this does not understand.");
}
