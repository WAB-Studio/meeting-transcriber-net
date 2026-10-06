using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// ISC-217: what Windows draws for the installed application is its mark — the images the manifest
/// names, the transparent taskbar icon, the program's .ico — and never the WinUI template's grey
/// placeholder, which is what an installed build showed until these were rendered.
/// </summary>
/// <remarks>
/// Every image is decoded and asked for the palette's own olive, because a file that merely exists
/// at the right size is what the template was too. What none of this reaches is an installed copy:
/// which variant the shell picks, and how the title bar and the desktop shortcut look, is a person
/// looking at Start, the taskbar and the desktop.
/// </remarks>
public partial class ApplicationIconTests
{
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly int[] Scales = [100, 125, 150, 200, 400];

    /// <summary>The sizes the shell asks the taskbar and Start icon at, 100% to 400%.</summary>
    private static readonly int[] TargetSizes = [16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256];

    /// <summary>What each image the manifest may name measures at scale 100.</summary>
    private static readonly Dictionary<string, (int Width, int Height)> Sizes = new()
    {
        ["Square44x44Logo"] = (44, 44),
        ["Square150x150Logo"] = (150, 150),
        ["Wide310x150Logo"] = (310, 150),
        ["StoreLogo"] = (50, 50),
        ["SplashScreen"] = (620, 300),
    };

    /// <summary>
    /// The WinUI template's placeholder images, by SHA-256: the grey box with an X that the first
    /// installed build showed in Start and on the taskbar.
    /// </summary>
    private static readonly string[] TemplatePlaceholders =
    [
        "688f384ef8832609d1f4d5e7fa9f6e08a81a6f52bba2c67070e385a015dfb42b",
        "209a614557ff28be5a307c55ec17e2126f1ad34f54a88c5d4226441891c5892f",
        "0b2ecc4b0e160157f55e43d1cd9532751cfa265be816227681c633b9c69a9381",
        "0be34ccbed90a09a51d56abc1b9ab279154d454e43906057b45762ba00b38854",
        "7799103c81a408b8dfc465764a5a8e693ebe1048a2e0f4690ae467398390030a",
        "5a44b2f15b76ff4cfcb315e8b62b2ac1e4a1f616bbfee0b8be29f9e2e116172c",
        "4938ed44d7d63f44ca959eafb942be36459939a9c679bfcbf4f5c837fdf3475c",
    ];

    private static DirectoryInfo Assets => new(AppSources.At(Path.Combine("MeetingTranscriber.App", "Assets")).FullName);

    /// <summary>ISC-217.1: every scale and target size of every image the manifest names, and nothing else.</summary>
    [Fact]
    public void Every_image_the_manifest_names_is_there_at_every_scale_and_target_size()
    {
        var expected = new Dictionary<string, (int Width, int Height)>();
        foreach (var name in NamedImages())
        {
            Sizes.ShouldContainKey(name, $"the manifest names {name}, and this test does not know its size");
            var (width, height) = Sizes[name];
            foreach (var scale in Scales)
            {
                expected[$"{name}.scale-{scale}.png"] = (width * scale / 100, height * scale / 100);
            }
        }

        foreach (var size in TargetSizes)
        {
            expected[$"Square44x44Logo.targetsize-{size}_altform-unplated.png"] = (size, size);
            expected[$"Square44x44Logo.targetsize-{size}_altform-lightunplated.png"] = (size, size);
        }

        var present = Assets.EnumerateFiles("*.png").ToDictionary(file => file.Name, file => Png.Read(file.FullName));

        present.Keys.Order(StringComparer.Ordinal).ShouldBe(
            expected.Keys.Order(StringComparer.Ordinal),
            "every image the manifest names is rendered at each scale and target size, and an image no manifest "
            + "names is a leftover; run tools/MeetingTranscriber.Icons rather than adding or removing one by hand");

        foreach (var (name, png) in present)
        {
            (png.Width, png.Height).ShouldBe(expected[name], name);
        }
    }

    /// <summary>ISC-217.1: no image is the template's, and every one carries the mark's olive.</summary>
    [Fact]
    public void No_image_Windows_is_handed_is_a_template_placeholder()
    {
        var images = Assets.EnumerateFiles("*.png").Append(new FileInfo(IconFile())).ToArray();
        images.Length.ShouldBeGreaterThan(1);

        foreach (var image in images)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(image.FullName)));
            TemplatePlaceholders.ShouldNotContain(hash, $"{image.Name} is the WinUI template's placeholder");
        }

        var light = Palette.Of("Default");
        var dark = Palette.Of("Dark");
        foreach (var image in Assets.EnumerateFiles("*.png"))
        {
            var olive = image.Name.Contains("_altform-unplated", StringComparison.Ordinal) ? dark.Olive : light.Olive;
            Png.Read(image.FullName).Carries(olive).ShouldBeTrue($"{image.Name} does not carry the mark's olive");
        }
    }

    /// <summary>
    /// ISC-217.2: the taskbar's icon is drawn over nothing, in the ink of the theme whose taskbar it
    /// sits on — light arcs for a dark taskbar, dark arcs for a light one.
    /// </summary>
    [Fact]
    public void The_taskbar_mark_is_drawn_over_nothing_in_the_ink_of_the_taskbar_it_sits_on()
    {
        var themes = new[] { ("_altform-unplated", Palette.Of("Dark")), ("_altform-lightunplated", Palette.Of("Default")) };
        foreach (var (variant, palette) in themes)
        {
            foreach (var size in TargetSizes)
            {
                var name = $"Square44x44Logo.targetsize-{size}{variant}.png";
                var png = Png.Read(Path.Combine(Assets.FullName, name));

                foreach (var (x, y) in new[] { (0, 0), (size - 1, 0), (0, size - 1), (size - 1, size - 1) })
                {
                    png.Alpha(x, y).ShouldBe((byte)0, $"{name} has something behind the mark at ({x}, {y})");
                }

                png.Carries(palette.Ink).ShouldBeTrue($"{name} is not drawn in its theme's ink");
            }
        }
    }

    /// <summary>
    /// ISC-217.3 and ISC-217.4: the program carries the mark, so whatever Windows draws from the
    /// program rather than from the package has it.
    /// </summary>
    [Fact]
    public void The_program_carries_the_mark_at_every_size_the_shell_draws_it()
    {
        var entries = Ico.Read(IconFile());

        new[] { 16, 24, 32, 48, 256 }.ShouldBeSubsetOf(entries.Select(png => png.Width));
        foreach (var png in entries)
        {
            png.Width.ShouldBe(png.Height);
            png.Carries(Palette.Of("Default").Olive).ShouldBeTrue($"the {png.Width} px icon does not carry the mark's olive");
        }

        Project().Descendants("Content").Select(item => (string?)item.Attribute("Include"))
            .ShouldContain((string?)Project().Descendants("ApplicationIcon").Single().Value,
                "the window reads the .ico from the package at run time, so it has to be packaged as well as embedded");
    }

    /// <summary>ISC-217.3: every window the application opens is given the mark.</summary>
    [Fact]
    public void Every_window_the_application_opens_wears_the_mark()
    {
        var sources = AppSources.With(".cs").Select(file => File.ReadAllText(file.FullName)).ToArray();
        var opened = sources.Sum(source => WindowOpened().Matches(source).Count);
        var dressed = sources.Sum(source => Regex.Matches(source, @"\.AppWindow\.SetIcon\(TheMark\)").Count);

        opened.ShouldBeGreaterThan(0);
        dressed.ShouldBe(opened, "a window opened without SetIcon shows a generic icon in its title bar and in Alt+Tab");

        sources.ShouldContain(source => source.Contains($"\"{Path.GetFileName(IconFile())}\"", StringComparison.Ordinal));
    }

    /// <summary>ISC-217.4: the shortcut the install makes takes its icon from the program that carries the mark.</summary>
    [Fact]
    public void The_desktop_shortcut_takes_its_icon_from_the_program()
    {
        var shortcut = PackageManifest.Source().Descendants().Single(element => element.Name.LocalName == "Shortcut");

        ((string?)shortcut.Attribute("Icon")).ShouldBe(@"[{Package}]\MeetingTranscriber.App.exe");
        Project().Descendants("AssemblyName").ShouldBeEmpty("the shortcut names the program by the project's own name");
        File.Exists(IconFile()).ShouldBeTrue();
    }

    /// <summary>The mark rendered outside the window is the one the app bar draws inside it.</summary>
    [Fact]
    public void The_mark_rendered_is_the_one_the_app_bar_draws()
    {
        var svg = XDocument.Load(Path.Combine(Assets.FullName, "Mark.svg"));
        var drawn = svg.Descendants().Where(element => element.Name.LocalName == "path")
            .Select(path => ((string?)path.Attribute("d"), (string?)path.Attribute("stroke-width"), (string?)path.Attribute("stroke-linecap")))
            .ToArray();

        var window = XDocument.Load(AppSources.At(Path.Combine("MeetingTranscriber.App", "MainWindow.xaml")).FullName);
        var appBar = window.Descendants().Where(element => element.Name.LocalName == "Path"
                && ((string?)element.Attribute("Data"))?.Contains('a') == true
                && element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "Viewbox"))
            .Select(path => ((string?)path.Attribute("Data"), (string?)path.Attribute("StrokeThickness"), ((string?)path.Attribute("StrokeEndLineCap"))?.ToLowerInvariant()))
            .ToArray();

        appBar.Length.ShouldBe(2, "the app bar's mark is two arcs");
        drawn.ShouldBe(appBar, "Mark.svg and the app bar in MainWindow.xaml are one mark: redraw both, then run tools/MeetingTranscriber.Icons");
    }

    [GeneratedRegex(@"new \w+Window\(")]
    private static partial Regex WindowOpened();

    private static XDocument Project() =>
        XDocument.Load(AppSources.At(Path.Combine("MeetingTranscriber.App", "MeetingTranscriber.App.csproj")).FullName);

    private static string IconFile()
    {
        var named = Project().Descendants("ApplicationIcon").ShouldHaveSingleItem().Value;
        return AppSources.At(Path.Combine("MeetingTranscriber.App", named.Replace('\\', Path.DirectorySeparatorChar))).FullName;
    }

    /// <summary>Every image the window's own <c>VisualElements</c> and the package's properties name.</summary>
    private static IReadOnlyCollection<string> NamedImages()
    {
        string[] attributes = ["Square150x150Logo", "Square44x44Logo", "Wide310x150Logo", "Image"];
        var manifest = PackageManifest.Source();
        return manifest.Descendants()
            .SelectMany(element => element.Attributes().Where(attribute => attributes.Contains(attribute.Name.LocalName)).Select(attribute => attribute.Value))
            .Concat(manifest.Descendants().Where(element => element.Name.LocalName == "Logo").Select(element => element.Value))
            .Select(path => Path.GetFileNameWithoutExtension(path.Replace('\\', '/')))
            .ToHashSet(StringComparer.Ordinal);
    }

    private sealed record Palette(uint Ink, uint Olive)
    {
        public static Palette Of(string theme)
        {
            var olivo = XDocument.Load(AppSources.At(Path.Combine("MeetingTranscriber.App", "Olivo.xaml")).FullName);
            var dictionary = olivo.Descendants()
                .Single(element => element.Name.LocalName == "ResourceDictionary" && (string?)element.Attribute(Xaml + "Key") == theme);

            uint Brush(string key) => Convert.ToUInt32(((string)dictionary.Elements()
                .Single(element => (string?)element.Attribute(Xaml + "Key") == key)
                .Attribute("Color")!).TrimStart('#'), 16);

            return new Palette(Brush("InkBrush"), Brush("OliveBrush"));
        }
    }

    /// <summary>
    /// The PNGs the renderer writes — eight-bit, RGB or RGBA, not interlaced — decoded far enough to
    /// read a pixel. Anything else is refused rather than misread.
    /// </summary>
    private sealed class Png(int width, int height, byte[] rgba)
    {
        public int Width => width;

        public int Height => height;

        public byte Alpha(int x, int y) => rgba[(((y * width) + x) * 4) + 3];

        /// <summary>Whether a fully opaque pixel is <paramref name="colour"/>, give or take rounding.</summary>
        public bool Carries(uint colour)
        {
            var (r, g, b) = ((int)(colour >> 16) & 0xFF, (int)(colour >> 8) & 0xFF, (int)colour & 0xFF);
            for (var i = 0; i < rgba.Length; i += 4)
            {
                if (rgba[i + 3] == 255 && Math.Abs(rgba[i] - r) <= 2 && Math.Abs(rgba[i + 1] - g) <= 2 && Math.Abs(rgba[i + 2] - b) <= 2)
                {
                    return true;
                }
            }

            return false;
        }

        public static Png Read(string path) => Decode(File.ReadAllBytes(path));

        public static Png Decode(ReadOnlySpan<byte> bytes)
        {
            bytes[..8].SequenceEqual((ReadOnlySpan<byte>)[0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10]).ShouldBeTrue("not a PNG");
            int width = 0, height = 0, channels = 0;
            using var compressed = new MemoryStream();
            for (var at = 8; at < bytes.Length;)
            {
                var length = BinaryPrimitives.ReadInt32BigEndian(bytes[at..]);
                var type = System.Text.Encoding.ASCII.GetString(bytes.Slice(at + 4, 4));
                var data = bytes.Slice(at + 8, length);
                if (type == "IHDR")
                {
                    width = BinaryPrimitives.ReadInt32BigEndian(data);
                    height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
                    data[8].ShouldBe((byte)8, "bit depth");
                    channels = data[9] switch { 6 => 4, 2 => 3, var other => throw new InvalidDataException($"colour type {other}") };
                    data[12].ShouldBe((byte)0, "interlace");
                }
                else if (type == "IDAT")
                {
                    compressed.Write(data);
                }

                at += 12 + length;
            }

            compressed.Position = 0;
            using var inflated = new MemoryStream();
            using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
            {
                zlib.CopyTo(inflated);
            }

            var raw = inflated.ToArray();
            var stride = width * channels;
            var pixels = new byte[stride * height];
            for (var y = 0; y < height; y++)
            {
                var filter = raw[y * (stride + 1)];
                for (var x = 0; x < stride; x++)
                {
                    var left = x >= channels ? pixels[(y * stride) + x - channels] : 0;
                    var up = y > 0 ? pixels[((y - 1) * stride) + x] : 0;
                    var corner = x >= channels && y > 0 ? pixels[((y - 1) * stride) + x - channels] : 0;
                    var value = raw[(y * (stride + 1)) + 1 + x];
                    pixels[(y * stride) + x] = (byte)(value + filter switch
                    {
                        0 => 0,
                        1 => left,
                        2 => up,
                        3 => (left + up) / 2,
                        4 => Paeth(left, up, corner),
                        _ => throw new InvalidDataException($"filter {filter}"),
                    });
                }
            }

            if (channels == 4)
            {
                return new Png(width, height, pixels);
            }

            var rgba = new byte[width * height * 4];
            for (var i = 0; i < width * height; i++)
            {
                pixels.AsSpan(i * 3, 3).CopyTo(rgba.AsSpan(i * 4));
                rgba[(i * 4) + 3] = 255;
            }

            return new Png(width, height, rgba);
        }

        private static int Paeth(int a, int b, int c)
        {
            var p = a + b - c;
            var (pa, pb, pc) = (Math.Abs(p - a), Math.Abs(p - b), Math.Abs(p - c));
            return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
        }
    }

    /// <summary>An .ico whose every image is a PNG, read as those images.</summary>
    private static class Ico
    {
        public static IReadOnlyList<Png> Read(string path)
        {
            var bytes = File.ReadAllBytes(path);
            BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)).ShouldBe((ushort)1, "an .ico is type 1");
            var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
            return Enumerable.Range(0, count).Select(i =>
            {
                var entry = bytes.AsSpan(6 + (16 * i));
                var length = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
                var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
                return Png.Decode(bytes.AsSpan(offset, length));
            }).ToArray();
        }
    }
}
