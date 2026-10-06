using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// A PNG of one screen as the desktop shows it, popups included, for the half of a screen the tree
/// cannot say: a control behind another, a panel the wrong colour, a heading clipped, a list open.
/// </summary>
/// <remarks>
/// <para>
/// A copy of the pixels of the desktop and not a print of the window. Asking the window to print
/// itself was the first shape because it needs no window in front, and it cannot see what the
/// platform opens in a window of its own — a list, a flyout, a tooltip — which is most of what a
/// picture of a control is wanted for. It was also the path that printed black frames. Now that
/// <see cref="Foreground"/> keeps the window in front the copy is of the screen, and what is
/// photographed is what a person looking at the machine would see.
/// </para>
/// <para>
/// The rectangle is the screen's, grown to cover its popups and clipped to the virtual desktop —
/// a list that opens past the edge of a monitor is photographed as far as there is a desktop.
/// </para>
/// <para>
/// What it still needs is for the window to have drawn. A window whose automation tree is already
/// complete can still copy as a title bar over a black rectangle, because the frame is the
/// desktop compositor's and the inside is an island that has not composed yet. That was measured,
/// not guessed: the first version of this waited a quarter of a second for unrelated reasons, and
/// taking the wait out turned the recorder into a black rectangle with a title on it. So the
/// inside is what is checked, and checking it is also what catches a minimised window and a copy
/// that quietly came back flat — all of which are otherwise a correctly sized PNG that looks like
/// an artifact and is believed.
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
    internal static (byte[] Png, string Size) Of(AutomationElement window, IReadOnlyList<IntPtr> popups)
    {
        var handle = AppWindows.Handle(window);

        if (!Native.GetWindowRect(handle, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            throw new ProbeFailed(
                $"The window \"{ElementWords.Name(window)}\" has no size to photograph.");
        }

        var area = Covering(rect, popups);
        var inside = Within(Inside(handle, rect, window), rect, area);

        var pixels = Patience.Until(ToDraw, () =>
        {
            var taken = Copy(area);

            return Drawn(taken, area.Width, inside) ? taken : null;
        }) ?? throw new ProbeFailed(
            $"The desktop under \"{ElementWords.Name(window)}\" copied as nothing but a frame around "
            + $"one flat colour for {ToDraw.TotalSeconds:0} seconds. The window is minimised, "
            + "covered by something that is not part of the application, or it never drew.");

        var picture = BitmapSource.Create(
            area.Width,
            area.Height,
            96,
            96,
            // Bgr32 and not Bgra32: a desktop copy comes back with zero in every alpha byte, and
            // a PNG written from those is a perfectly transparent picture of nothing.
            PixelFormats.Bgr32,
            palette: null,
            pixels,
            area.Width * 4);

        var png = new PngBitmapEncoder();
        png.Frames.Add(BitmapFrame.Create(picture));

        using var encoded = new MemoryStream();
        png.Save(encoded);

        return (encoded.ToArray(), $"{area.Width}x{area.Height}");
    }

    /// <summary>The screen's rectangle grown to hold each popup, and then cut to the desktop.</summary>
    private static Native.Rect Covering(Native.Rect screen, IReadOnlyList<IntPtr> popups)
    {
        var area = screen;
        foreach (var popup in popups)
        {
            if (Native.GetWindowRect(popup, out var one) && one.Width > 0 && one.Height > 0)
            {
                area.Left = Math.Min(area.Left, one.Left);
                area.Top = Math.Min(area.Top, one.Top);
                area.Right = Math.Max(area.Right, one.Right);
                area.Bottom = Math.Max(area.Bottom, one.Bottom);
            }
        }

        var left = Native.GetSystemMetrics(Native.SmXVirtualScreen);
        var top = Native.GetSystemMetrics(Native.SmYVirtualScreen);
        var right = left + Native.GetSystemMetrics(Native.SmCxVirtualScreen);
        var bottom = top + Native.GetSystemMetrics(Native.SmCyVirtualScreen);

        area.Left = Math.Max(area.Left, left);
        area.Top = Math.Max(area.Top, top);
        area.Right = Math.Min(area.Right, right);
        area.Bottom = Math.Min(area.Bottom, bottom);

        return area.Width > 0 && area.Height > 0
            ? area
            : throw new ProbeFailed(
                "The window is entirely off the desktop, so there is nothing to photograph.");
    }

    /// <summary>
    /// Where the window's own content is within the window's rectangle. The frame is excluded
    /// because the frame always draws — it is the compositor's, not the application's — so a
    /// picture judged whole would be judged by the one part of it that is never blank.
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

    /// <summary>
    /// The inside said in the picture's own pixels, and cut to what the picture holds: the window
    /// may begin before the picture does when it is partly off the desktop.
    /// </summary>
    private static Native.Rect Within(Native.Rect inside, Native.Rect window, Native.Rect area)
    {
        var shifted = new Native.Rect
        {
            Left = Math.Max(inside.Left + window.Left - area.Left, 0),
            Top = Math.Max(inside.Top + window.Top - area.Top, 0),
            Right = Math.Min(inside.Right + window.Left - area.Left, area.Width),
            Bottom = Math.Min(inside.Bottom + window.Top - area.Top, area.Height),
        };

        return shifted.Width > 0 && shifted.Height > 0
            ? shifted
            : throw new ProbeFailed("The inside of the window is off the desktop, so there is nothing to photograph.");
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
    /// The desktop's pixels over <paramref name="area"/>. Everything that can go wrong in here is
    /// the machine saying no to the probe itself and is a failure: no device context, no room for a
    /// bitmap, a copy Windows refused, half the rows handed back.
    /// </summary>
    private static byte[] Copy(Native.Rect area)
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
            bitmap = Native.CreateCompatibleBitmap(desktop, area.Width, area.Height);
            if (memory == IntPtr.Zero || bitmap == IntPtr.Zero)
            {
                throw new ProbeFailed(
                    $"There was no room for a {area.Width}x{area.Height} picture of the screen.");
            }

            replaced = Native.SelectObject(memory, bitmap);

            if (!Native.BitBlt(
                memory,
                0,
                0,
                area.Width,
                area.Height,
                desktop,
                area.Left,
                area.Top,
                Native.SrcCopy | Native.CaptureBlt))
            {
                throw new ProbeFailed(
                    $"Windows would not copy the desktop ({Marshal.GetLastWin32Error()}). A locked "
                    + "workstation or a secure desktop in front is the usual reason.");
            }

            // GDI will not read out a bitmap that is still selected into a device context.
            Native.SelectObject(memory, replaced);
            replaced = IntPtr.Zero;

            var header = new Native.BitmapInfoHeader
            {
                biSize = (uint)Marshal.SizeOf<Native.BitmapInfoHeader>(),
                biWidth = area.Width,

                // Negative, so the rows arrive top down and match what every image format means
                // by the first row.
                biHeight = -area.Height,
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Native.BI_RGB,
            };

            var pixels = new byte[area.Width * 4 * area.Height];
            var lines = Native.GetDIBits(
                memory,
                bitmap,
                0,
                (uint)area.Height,
                pixels,
                ref header,
                Native.DIB_RGB_COLORS);

            if (lines != area.Height)
            {
                throw new ProbeFailed($"Only {lines} of {area.Height} rows of the screen came back.");
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
