using System.Xml.Linq;
using SkiaSharp;
using Svg.Skia;

namespace MeetingTranscriber.Icons;

/// <summary>
/// Renders every image the package hands Windows, and the program's .ico, from
/// <c>Assets/Mark.svg</c> and the two palettes in <c>Olivo.xaml</c>. Every <c>.png</c> at the top of
/// <c>Assets/</c> is deleted first, so nothing drawn by hand or left by a template survives a run.
/// </summary>
/// <remarks>
/// Three looks, because the mark has to read on whatever Windows puts behind it:
/// <list type="bullet">
/// <item>plated — papel behind the light theme's arcs, for the tiles, the splash, the store logo
/// and the .ico, which Windows draws on a background nobody here chooses;</item>
/// <item><c>altform-unplated</c> — transparent, the dark theme's arcs, which is what Windows draws
/// on a dark taskbar and Start;</item>
/// <item><c>altform-lightunplated</c> — transparent, the light theme's arcs, for a light one.</item>
/// </list>
/// </remarks>
internal static class Program
{
    private static readonly int[] Scales = [100, 125, 150, 200, 400];

    /// <summary>The sizes the shell asks the taskbar and Start icon at, 100% to 400%.</summary>
    private static readonly int[] TargetSizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];

    private static readonly int[] IcoSizes = [16, 20, 24, 32, 40, 48, 64, 256];

    private static int Main()
    {
        var app = Path.Combine(RepositoryRoot(), "src", "MeetingTranscriber.App");
        var assets = Path.Combine(app, "Assets");
        var mark = File.ReadAllText(Path.Combine(assets, "Mark.svg"));
        var light = Palette.Read(Path.Combine(app, "Olivo.xaml"), "Default");
        var dark = Palette.Read(Path.Combine(app, "Olivo.xaml"), "Dark");

        foreach (var stale in Directory.EnumerateFiles(assets, "*.png"))
        {
            File.Delete(stale);
        }

        var plated = new Look(mark, light, light.Paper);
        var unplated = new Look(mark, dark, Background: null);
        var lightUnplated = new Look(mark, light, Background: null);

        // The share of the shorter side the mark's 24-unit box takes. The arcs fill about two thirds
        // of that box, so 1.0 leaves a sixth of margin each way and a tile at 0.8 sits the mark
        // well inside the plate.
        foreach (var scale in Scales)
        {
            int At(int size) => size * scale / 100;
            Write(assets, $"Square44x44Logo.scale-{scale}.png", plated.Draw(At(44), At(44), 1.0));
            Write(assets, $"Square150x150Logo.scale-{scale}.png", plated.Draw(At(150), At(150), 0.8));
            Write(assets, $"Wide310x150Logo.scale-{scale}.png", plated.Draw(At(310), At(150), 0.8));
            Write(assets, $"StoreLogo.scale-{scale}.png", plated.Draw(At(50), At(50), 1.0));
            Write(assets, $"SplashScreen.scale-{scale}.png", plated.Draw(At(620), At(300), 0.6));
        }

        // Larger than the plated share, because nothing frames it: at 16 px the arcs are 13 px tall.
        foreach (var size in TargetSizes)
        {
            Write(assets, $"Square44x44Logo.targetsize-{size}_altform-unplated.png", unplated.Draw(size, size, 1.2));
            Write(assets, $"Square44x44Logo.targetsize-{size}_altform-lightunplated.png", lightUnplated.Draw(size, size, 1.2));
        }

        Write(assets, "MeetingTranscriber.ico", Ico.Of(IcoSizes.Select(size => plated.Draw(size, size, 1.1))));
        return 0;
    }

    private static void Write(string folder, string name, byte[] bytes)
    {
        File.WriteAllBytes(Path.Combine(folder, name), bytes);
        Console.WriteLine(name);
    }

    private static string RepositoryRoot()
    {
        for (var folder = new DirectoryInfo(Environment.CurrentDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "MeetingTranscriber.slnx")))
            {
                return folder.FullName;
            }
        }

        throw new InvalidOperationException("Run this from inside the repository: no MeetingTranscriber.slnx above the current folder.");
    }
}

/// <summary>The three colours a look takes from one theme of <c>Olivo.xaml</c>.</summary>
internal sealed record Palette(SKColor Ink, SKColor Olive, SKColor Paper)
{
    public static Palette Read(string olivo, string theme)
    {
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var dictionary = XDocument.Load(olivo).Descendants()
            .Single(element => element.Name.LocalName == "ResourceDictionary" && (string?)element.Attribute(x + "Key") == theme);

        SKColor Brush(string key) => SKColor.Parse((string)dictionary.Elements()
            .Single(element => (string?)element.Attribute(x + "Key") == key)
            .Attribute("Color")!);

        return new Palette(Brush("InkBrush"), Brush("OliveBrush"), Brush("PaperBrush"));
    }
}

/// <summary>The mark in one theme's colours, over a background or over nothing.</summary>
internal sealed record Look(string Mark, Palette Colours, SKColor? Background)
{
    /// <summary>
    /// A PNG of <paramref name="width"/> by <paramref name="height"/> with the mark's box centred
    /// and <paramref name="share"/> of the shorter side across.
    /// </summary>
    public byte[] Draw(int width, int height, double share)
    {
        using var svg = new SKSvg();
        var picture = svg.FromSvg(Recoloured()) ?? throw new InvalidOperationException("Mark.svg did not parse.");

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(Background ?? SKColors.Transparent);

        var box = (float)(Math.Min(width, height) * share);
        canvas.Translate((width - box) / 2, (height - box) / 2);
        canvas.Scale(box / picture.CullRect.Width, box / picture.CullRect.Height);
        canvas.Translate(-picture.CullRect.Left, -picture.CullRect.Top);
        canvas.DrawPicture(picture);

        using var image = surface.Snapshot();
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return png.ToArray();
    }

    private string Recoloured()
    {
        var document = XDocument.Parse(Mark);
        Stroke(document, "ink", Colours.Ink);
        Stroke(document, "olive", Colours.Olive);
        return document.ToString();
    }

    private static void Stroke(XDocument document, string id, SKColor colour) =>
        document.Descendants().Single(element => (string?)element.Attribute("id") == id)
            .SetAttributeValue("stroke", $"#{colour.Red:X2}{colour.Green:X2}{colour.Blue:X2}");
}

/// <summary>
/// An .ico whose every image is a PNG, which every Windows this application installs on reads and
/// the compiler embeds as it is.
/// </summary>
internal static class Ico
{
    public static byte[] Of(IEnumerable<byte[]> pngs)
    {
        var images = pngs.ToArray();
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Length);

        var offset = 6 + (16 * images.Length);
        foreach (var png in images)
        {
            var side = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
            writer.Write((byte)(side >= 256 ? 0 : side));
            writer.Write((byte)(side >= 256 ? 0 : side));
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write(png.Length);
            writer.Write(offset);
            offset += png.Length;
        }

        foreach (var png in images)
        {
            writer.Write(png);
        }

        return stream.ToArray();
    }
}
