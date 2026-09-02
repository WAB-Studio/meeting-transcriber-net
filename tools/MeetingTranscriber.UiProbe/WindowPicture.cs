using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// A PNG of one window, for the half of a screen the tree cannot say: a control behind another, a
/// panel the wrong colour, a heading clipped.
/// </summary>
/// <remarks>
/// <para>
/// Asked of the window rather than read off the desktop. Copying the pixels under the window was
/// tried first and is what most of the internet does; it produced a picture of whatever was in
/// front, because Windows refuses to let a background process raise a window and
/// <c>SetForegroundWindow</c> quietly returns false. <c>PW_RENDERFULLCONTENT</c> prints what the
/// window composed — Mica, the XAML island and all — with no need for it to be in front, so
/// <see cref="WriteTo"/> photographs a screen without disturbing which one a person is looking at.
/// </para>
/// <para>
/// What it does need is for the window to have drawn. A window whose automation tree is already
/// complete can still print as a title bar over a black rectangle, because the frame is the
/// desktop compositor's and the inside is an island that has not composed yet. That was measured,
/// not guessed: the first version of this waited a quarter of a second for unrelated reasons, and
/// taking the wait out turned the recorder into a black rectangle with a title on it. So the
/// inside is what is checked, and checking it is also what catches a minimised window and a print
/// that quietly rendered nothing — all three of which are otherwise a correctly sized PNG that
/// looks like an artifact and is believed.
/// </para>
/// </remarks>
internal static class WindowPicture
{
    private static readonly TimeSpan ToDraw = TimeSpan.FromSeconds(10);

    /// <summary>
    /// The PNG itself, and not a file. One host writes it under a name somebody chose and the
    /// other hands the bytes back inside the turn that asked for them, so a picture that could
    /// only exist as a path would have made the second one read what it had just written.
    /// </summary>
    internal static (byte[] Png, string Size) Of(AutomationElement window)
    {
        var handle = AppWindows.Handle(window);

        if (!Native.GetWindowRect(handle, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ProbeFailed(
                $"The window \"{ElementWords.Name(window)}\" has no size to photograph.");
        }

        var inside = Inside(handle, rect, window);

        // A refusal to print is inside the budget and not above it. Both ways this can fail are
        // the same fact — the window has not composed yet — and the retry loop was written for
        // only one of them: a print that comes back blank was asked again, and a print Windows
        // refused outright threw straight out of the wait that exists to absorb it. A screen busy
        // enough to refuse one print refuses it for a frame or two, so what that cost was the
        // whole verb, tree included, on a window that was about to be photographable.
        var refused = false;

        var pixels = Patience.Until(ToDraw, () =>
        {
            var taken = Copy(handle, rect);
            refused = taken is null;

            return taken is not null && Drawn(taken, rect.Width, inside) ? taken : null;
        }) ?? throw new ProbeFailed(
            $"The window \"{ElementWords.Name(window)}\" "
            + (refused
                ? $"refused to be printed for {ToDraw.TotalSeconds:0} seconds."
                : $"printed as a frame around one flat colour for {ToDraw.TotalSeconds:0} seconds.")
            + (Native.IsIconic(handle)
                ? " It is minimised."
                : " It is not minimised, so it never drew."));

        var picture = BitmapSource.Create(
            rect.Width,
            rect.Height,
            96,
            96,
            // Bgr32 and not Bgra32: a printed window comes back with zero in every alpha byte, and
            // a PNG written from those is a perfectly transparent picture of nothing.
            PixelFormats.Bgr32,
            palette: null,
            pixels,
            rect.Width * 4);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(picture));

        using var encoded = new MemoryStream();
        png.Save(encoded);

        return (encoded.ToArray(), $"{rect.Width}x{rect.Height}");
    }

    /// <summary>
    /// Where the window's own content is within the picture. The frame is excluded because the
    /// frame always draws — it is the compositor's, not the application's — so a picture judged
    /// whole would be judged by the one part of it that is never blank.
    /// </summary>
    private static Native.Rect Inside(IntPtr handle, Native.Rect rect, AutomationElement window)
    {
        var origin = default(Native.Point);
        if (!Native.GetClientRect(handle, out var client) || !Native.ClientToScreen(handle, ref origin))
        {
            throw new ProbeFailed(
                $"The window \"{ElementWords.Name(window)}\" would not say where its inside is.");
        }

        var left = Math.Max(origin.X - rect.Left, 0);
        var top = Math.Max(origin.Y - rect.Top, 0);
        var inside = new Native.Rect
        {
            Left = left,
            Top = top,
            Right = Math.Min(left + client.Width, rect.Width),
            Bottom = Math.Min(top + client.Height, rect.Height),
        };

        return inside.Width > 0 && inside.Height > 0
            ? inside
            : throw new ProbeFailed(
                $"The window \"{ElementWords.Name(window)}\" has no inside to photograph.");
    }

    private static bool Drawn(byte[] pixels, int width, Native.Rect inside)
    {
        var first = (inside.Top * width * 4) + (inside.Left * 4);

        for (var row = inside.Top; row < inside.Bottom; row++)
        {
            for (var column = inside.Left; column < inside.Right; column++)
            {
                var at = (row * width * 4) + (column * 4);
                if (pixels[at] != pixels[first]
                    || pixels[at + 1] != pixels[first + 1]
                    || pixels[at + 2] != pixels[first + 2])
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The window's pixels, or null when Windows refused to print it — which is something to ask
    /// again rather than something to report, and what tells the two apart is the budget above
    /// running out. Everything else in here is the machine saying no to the probe itself and stays
    /// a failure: no device context, no room for a bitmap, half the rows handed back.
    /// </summary>
    private static byte[]? Copy(IntPtr handle, Native.Rect rect)
    {
        var desktop = Native.GetDC(IntPtr.Zero);
        var memory = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var replaced = IntPtr.Zero;

        try
        {
            if (desktop == IntPtr.Zero)
            {
                throw new ProbeFailed("Windows would not hand over a device context to draw into.");
            }

            memory = Native.CreateCompatibleDC(desktop);
            bitmap = Native.CreateCompatibleBitmap(desktop, rect.Width, rect.Height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                throw new ProbeFailed(
                    $"There was no room for a {rect.Width}x{rect.Height} picture of the window.");
            }

            replaced = Native.SelectObject(memory, bitmap);

            if (!Native.PrintWindow(handle, memory, Native.PW_RENDERFULLCONTENT))
            {
                return null;
            }

            // GDI will not read out a bitmap that is still selected into a device context.
            Native.SelectObject(memory, replaced);
            replaced = IntPtr.Zero;

            var header = new Native.BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<Native.BitmapInfoHeader>(),
                biWidth = rect.Width,

                // Negative, so the rows arrive top down and match what every image format means
                // by the first row.
                biHeight = -rect.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Native.BI_RGB,
            };

            var pixels = new byte[rect.Width * 4 * rect.Height];
            var lines = Native.GetDIBits(
                memory,
                bitmap,
                0,
                (uint)rect.Height,
                pixels,
                ref header,
                Native.DIB_RGB_COLORS);

            if (lines != rect.Height)
            {
                throw new ProbeFailed($"Only {lines} of {rect.Height} rows of the window came back.");
            }

            return pixels;
        }
        finally
        {
            if (replaced != IntPtr.Zero)
            {
                Native.SelectObject(memory, replaced);
            }

            if (bitmap != IntPtr.Zero)
            {
                Native.DeleteObject(bitmap);
            }

            if (memory != IntPtr.Zero)
            {
                Native.DeleteDC(memory);
            }

            if (desktop != IntPtr.Zero)
            {
                Native.ReleaseDC(IntPtr.Zero, desktop);
            }
        }
    }
}
