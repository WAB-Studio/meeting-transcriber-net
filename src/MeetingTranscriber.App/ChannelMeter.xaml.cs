using System.Globalization;

using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using Windows.Foundation;

namespace MeetingTranscriber.App;

/// <summary>
/// One channel's meter: the four layers, the scale under them, and the loudest the source has
/// reached. <c>docs/design.md</c> §The meter is what it is drawn from.
/// </summary>
/// <remarks>
/// It draws and it remembers one number. Everything it is asked about a channel arrives as a
/// <see cref="ChannelReading"/> from a project a build agent can run, and every word on it is set
/// by the window out of the catalogue — this control names no text of its own, which is what keeps
/// one component from having to know which language it is in.
/// </remarks>
public sealed partial class ChannelMeter : UserControl
{
    /// <summary>How tall the bar is.</summary>
    private const double BarHeight = 20;

    /// <summary>How far the retained peak stands proud of the track, at each end.</summary>
    private const double PeakStandsProud = 4;

    /// <summary>
    /// The segment pattern every layer is drawn in: 3 on, 3 off. All four use the same one, so the
    /// segments of every layer line up — which is the whole reason the layers are clipped rather
    /// than sized.
    /// </summary>
    private const double SegmentWidth = 3;

    /// <summary>The gap between one segment and the next.</summary>
    private const double SegmentGap = 3;

    /// <summary>
    /// The four layers, bottom to top, each with the brush it is drawn in — the order they paint
    /// in, so the level goes over the hot zone and what is past −12 goes over the level.
    /// </summary>
    private readonly (Canvas Layer, Brush Paint)[] _layers;

    /// <summary>
    /// The loudest this source has reached since the recording started, or nothing when it has
    /// reached nothing at all. <b>This is the meter's only memory</b> — nothing else on the
    /// component remembers anything, which is what keeps it showing now rather than a history.
    /// </summary>
    private float? _loudestSoFar;

    /// <summary>
    /// How full the bar was the last time anything drew it, so a resize puts it back where it stood
    /// instead of emptying it until the next reading arrives.
    /// </summary>
    private double? _asFullAs;

    public ChannelMeter()
    {
        InitializeComponent();

        // Resolved here and through the same call every other colour on this component goes
        // through, so all four are names OlivoTests can see. Held as brushes rather than as keys
        // because the segments are rebuilt on every resize and the lookup does not need repeating.
        _layers =
        [
            (TrackLayer, Painted("MeterTrackBrush")),
            (HotZoneLayer, Painted("HotZoneBrush")),
            (LevelLayer, Painted("OliveBrush")),
            (ClippedLayer, Painted("PeakBrush")),
        ];
    }

    /// <summary>
    /// Which channel this is, in the words the catalogue carries for it. Set by the window, because
    /// this control is one and the channels are two: a meter knows it is a meter and never which.
    /// </summary>
    public string ChannelName
    {
        get => WhichChannel.Text;
        set
        {
            WhichChannel.Text = value;

            // The bar is the thing carrying a value, so it is the thing that carries the name a
            // screen reader says. A TextBlock beside it announces itself and leaves the bar
            // unnamed, which is the one element on here somebody listening needs named.
            AutomationProperties.SetName(Bar, value);
        }
    }

    /// <summary>
    /// The loudest this source has reached, for the window to word. The number is the meter's and
    /// the sentence around it is the catalogue's, so neither has to know what the other is for.
    /// </summary>
    public float? LoudestSoFar => _loudestSoFar;

    /// <summary>What the window wrote about <see cref="LoudestSoFar"/>, or nothing.</summary>
    public string LoudestSoFarSaid
    {
        get => Peak.Text;
        set => Peak.Text = value;
    }

    /// <summary>What this channel has open, in whatever this machine called it.</summary>
    public string CapturingSaid
    {
        get => Capturing.Text;
        set => Capturing.Text = value;
    }

    /// <summary>
    /// How loud it is now, as the window worded it — a measurement where something arrived, and the
    /// catalogue's own sentence where nothing did. An empty bar and a bar nothing has drawn yet look
    /// the same, which is the whole reason silence gets words rather than a blank.
    /// </summary>
    public string LoudnessSaid
    {
        get => Level.Text;
        set => Level.Text = value;
    }

    /// <summary>
    /// Forgets the loudest moment, and everything else this meter is showing.
    /// </summary>
    /// <remarks>
    /// Called when a recording starts, because the retained peak is the peak of <em>this</em>
    /// meeting: one carried over from the meeting before would be a mark standing where nothing in
    /// what is being recorded ever reached, and it would be there before the first sample arrived.
    /// </remarks>
    public void ForgetTheLoudestMoment()
    {
        _loudestSoFar = null;
        Show(null);
    }


    /// <summary>
    /// Draws <paramref name="reading"/>, or the bar with nothing on it when there is nothing to
    /// draw. The track and the hot zone are there either way: the hot zone is visible even when
    /// nothing is arriving, so its colour is not something that appears out of nowhere on the day
    /// it clips.
    /// </summary>
    /// <remarks>
    /// It draws and it remembers, and it says nothing: the three lines of words on it are the
    /// window's, because a sentence is the catalogue's and this control would otherwise have to
    /// know which language it is in to say that nothing is arriving.
    /// </remarks>
    public void Show(ChannelReading? reading)
    {
        if (reading is { IsSilent: false })
        {
            _loudestSoFar = _loudestSoFar is { } loudest
                ? Math.Max(loudest, reading.Level.Decibels)
                : reading.Level.Decibels;
        }

        _asFullAs = reading is { IsSilent: false } ? reading.Meter : null;
        Draw();
    }

    /// <summary>
    /// Puts every layer where the level says by moving its clip and never its size. A sized layer
    /// re-tiles its own pattern and the segments walk as the level moves.
    /// </summary>
    private void Draw()
    {
        var width = Bar.ActualWidth;
        if (width <= 0)
        {
            return;
        }

        var hotFrom = MeterScale.Along(MeterScale.HotFrom) * width;
        var to = (_asFullAs ?? 0) * width;

        ShowOnly(TrackLayer, 0, width);
        ShowOnly(HotZoneLayer, hotFrom, width);

        // Nothing arriving means no level and no peak: a bar drawn to nothing is what says the
        // source is silent, and the two coloured layers are the ones that would otherwise claim
        // something was heard.
        ShowOnly(LevelLayer, 0, _asFullAs is null ? 0 : to);
        ShowOnly(ClippedLayer, hotFrom, _asFullAs is null ? hotFrom : Math.Max(hotFrom, to));

        RetainedPeak.Visibility = _loudestSoFar is null ? Visibility.Collapsed : Visibility.Visible;

        if (_loudestSoFar is { } loudest)
        {
            RetainedPeak.Height = BarHeight + (PeakStandsProud * 2);
            RetainedPeak.Margin = new Thickness(
                Math.Clamp(MeterScale.Along(loudest) * width, 0, Math.Max(0, width - RetainedPeak.Width)),
                -PeakStandsProud,
                0,
                -PeakStandsProud);
        }
    }

    /// <summary>
    /// Shows <paramref name="layer"/> only between <paramref name="from"/> and
    /// <paramref name="to"/>, in the bar's own pixels.
    /// </summary>
    private static void ShowOnly(Canvas layer, double from, double to) =>
        layer.Clip = new RectangleGeometry { Rect = new Rect(from, 0, Math.Max(0, to - from), BarHeight) };

    /// <summary>
    /// Lays the segments out again when the bar's width changes, and only then. For the life of
    /// that width they stand at fixed positions and what moves is the clip over them.
    /// </summary>
    private void OnBarResized(object sender, SizeChangedEventArgs e)
    {
        foreach (var (layer, paint) in _layers)
        {
            layer.Children.Clear();

            for (var x = 0d; x < e.NewSize.Width; x += SegmentWidth + SegmentGap)
            {
                var segment = new Rectangle
                {
                    Width = Math.Min(SegmentWidth, e.NewSize.Width - x),
                    Height = BarHeight,
                    Fill = paint,
                };

                Canvas.SetLeft(segment, x);
                layer.Children.Add(segment);
            }
        }

        Draw();
    }

    /// <summary>
    /// Writes the numbers under the bar at the fractions <see cref="MeterScale"/> puts them. The
    /// −12 is in pico and the 0 is in ink; the rest is the data rank's own tertiary.
    /// </summary>
    /// <remarks>
    /// Each is placed and then pulled back inside the bar it belongs to, which is why the layout
    /// runs in the middle: the last mark sits at the full width, so a number starting there would
    /// be drawn off the end, and how wide it is is not known until it has been measured.
    /// </remarks>
    private void OnScaleResized(object sender, SizeChangedEventArgs e)
    {
        ScaleUnderTheBar.Children.Clear();

        foreach (var mark in MeterScale.Marks)
        {
            var number = new TextBlock
            {
                Text = mark.ToString("0", CultureInfo.InvariantCulture),
                Style = (Style)Application.Current.Resources["DataText"],
                Foreground = Painted(InkOf(mark)),
            };

            Canvas.SetLeft(number, MeterScale.Along(mark) * e.NewSize.Width);
            ScaleUnderTheBar.Children.Add(number);
        }

        ScaleUnderTheBar.UpdateLayout();

        foreach (var number in ScaleUnderTheBar.Children.OfType<TextBlock>())
        {
            Canvas.SetLeft(number, Math.Max(
                0, Math.Min(Canvas.GetLeft(number), e.NewSize.Width - number.ActualWidth)));
        }
    }

    /// <summary>
    /// Which of the three inks a mark is written in. The two that carry information are coloured
    /// and the rest are data like any other — a scale where every number was coloured would be one
    /// where none of them meant anything.
    /// </summary>
    private static string InkOf(float mark) => mark switch
    {
        MeterScale.HotFrom => "PeakBrush",
        MeterScale.Loudest => "InkBrush",
        _ => "TertiaryTextBrush",
    };

    /// <summary>
    /// One of Olivo's brushes, by the key it is settled under. Every colour on this component comes
    /// through here, so there is nowhere on it a value could be chosen instead.
    /// </summary>
    private static Brush Painted(string key) => (Brush)Application.Current.Resources[key];
}
