using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// The name Windows knows this application by, worked out from the manifest in the repository
/// rather than written down here.
/// </summary>
/// <remarks>
/// An application user model id is <c>&lt;package family name&gt;!&lt;application id&gt;</c>, and
/// the family name is the identity's name and a hash of its publisher. Both halves are in
/// <c>Package.appxmanifest</c>, so deriving it means the day somebody changes the publisher or
/// splits out a second application, this tool follows instead of launching something that is no
/// longer there.
/// </remarks>
internal static class Aumid
{
    private static readonly XNamespace Foundation =
        "http://schemas.microsoft.com/appx/manifest/foundation/windows10";

    /// <summary>
    /// Crockford's base 32 as Windows spells it: lowercase, and without the four letters that
    /// are read as digits.
    /// </summary>
    private const string Base32 = "0123456789abcdefghjkmnpqrstvwxyz";

    internal static string OfTheApplicationIn(string manifestPath)
    {
        var manifest = XDocument.Load(manifestPath).Root
            ?? throw new ProbeFailed($"{manifestPath} is empty.");

        var identity = manifest.Element(Foundation + "Identity")
            ?? throw new ProbeFailed($"{manifestPath} has no <Identity>.");

        var application = manifest
            .Element(Foundation + "Applications")?
            .Elements(Foundation + "Application")
            .FirstOrDefault()
            ?? throw new ProbeFailed($"{manifestPath} declares no <Application>.");

        var name = Required(identity, "Name", manifestPath);
        var publisher = Required(identity, "Publisher", manifestPath);
        var id = Required(application, "Id", manifestPath);

        return $"{name}_{PublisherHash(publisher)}!{id}";
    }

    private static string Required(XElement element, string attribute, string manifestPath) =>
        element.Attribute(attribute)?.Value
        ?? throw new ProbeFailed($"{manifestPath}: <{element.Name.LocalName}> has no {attribute}.");

    /// <summary>
    /// The first eight bytes of the SHA-256 of the publisher in UTF-16, read as one number with a
    /// zero bit appended — sixty-five bits, which is thirteen base 32 digits exactly.
    /// </summary>
    private static string PublisherHash(string publisher)
    {
        var hash = SHA256.HashData(Encoding.Unicode.GetBytes(publisher));
        var value = new BigInteger(hash.AsSpan(0, 8), isUnsigned: true, isBigEndian: true) * 2;

        var digits = new char[13];
        for (var position = 0; position < digits.Length; position++)
        {
            var place = BigInteger.Pow(32, digits.Length - 1 - position);
            digits[position] = Base32[(int)(value / place % 32)];
        }

        return new string(digits);
    }
}
