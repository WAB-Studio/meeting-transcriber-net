using System.IO;
using System.Xml;
using System.Xml.Linq;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The application a package manifest declares, which is the half of an application user model id
/// that is written down rather than derived.
/// </summary>
/// <remarks>
/// <para>
/// An application user model id is <c>&lt;package family name&gt;!&lt;application id&gt;</c>. The
/// family name used to be computed here — the identity's name, an underscore, and the first eight
/// bytes of the SHA-256 of the publisher in UTF-16, doubled, in Crockford's base 32 as Windows
/// spells it. It is not computed any more, because <see cref="Repository"/> now asks Windows what
/// it has registered and Windows answers with the family name. Deriving something a system will
/// simply say is a second implementation of that system, and this one was thirty lines of it.
/// </para>
/// <para>
/// What is left is the half Windows does not offer without an asynchronous call: which application
/// inside the package to activate. The first <c>&lt;Application&gt;</c>, because this package
/// declares one, and a package that declared two would need somebody to say which. Which manifest
/// is <see cref="Repository"/>'s to say, and it is now a registered layout's rather than whichever
/// build wrote one last.
/// </para>
/// </remarks>
internal static class ApplicationId
{
    private static readonly XNamespace Foundation =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    internal static string DeclaredIn(string manifestPath)
    {
        var manifest = Opened(manifestPath).Root
            ?? throw new ProbeFailed($"{manifestPath} is empty.");

        var application = manifest
            .Element(Foundation + "Applications")?
            .Elements(Foundation + "Application")
            .FirstOrDefault()
            ?? throw new ProbeFailed($"{manifestPath} declares no <Application>.");

        return Required(application, "Id", manifestPath);
    }

    /// <summary>
    /// The manifest read as a manifest is this tool's business; the file being unreadable is not.
    /// It is the manifest of a layout Windows has registered, so the two ordinary ways this fails
    /// are a build writing it in the same moment and a build that stopped half way through it —
    /// neither of which is news about a screen, and both of which would otherwise come back as a
    /// stack trace.
    /// </summary>
    private static XDocument Opened(string manifestPath)
    {
        try
        {
            return XDocument.Load(manifestPath);
        }
        catch (Exception unreadable) when (unreadable is IOException or XmlException)
        {
            throw new ProbeFailed(
                $"{manifestPath} could not be read: {unreadable.Message} A build writing it right "
                + "now is the usual reason, and that one only needs letting finish. A build that "
                + "stopped part way through it is the other, and that one needs building again.");
        }
    }

    private static string Required(XElement element, string attribute, string manifestPath) =>
        element.Attribute(attribute)?.Value
        ?? throw new ProbeFailed($"{manifestPath}: <{element.Name.LocalName}> has no {attribute}.");
}
