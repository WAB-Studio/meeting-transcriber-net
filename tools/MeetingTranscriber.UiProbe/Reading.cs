using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// Reading one property off an element that may not be there any more.
/// </summary>
/// <remarks>
/// An element can go between being found and being read — a card rebuilt, a popup dismissed, a
/// window closed. Every read in this tool goes through here, so that a tree with a blank in it is
/// what happens instead of a tree that stops half way.
/// </remarks>
internal static class Reading
{
    /// <summary>What the element said, or null if it is gone.</summary>
    internal static T? Of<T>(Func<T?> read)
        where T : class
    {
        try
        {
            return read();
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }

    /// <summary>
    /// Three-valued on purpose: an element that went away is neither enabled nor disabled, and a
    /// line saying "disabled" about one that simply vanished would be a wrong story told
    /// confidently.
    /// </summary>
    internal static bool? Flag(Func<bool> read)
    {
        try
        {
            return read();
        }
        catch (ElementNotAvailableException)
        {
            return null;
        }
    }
}
