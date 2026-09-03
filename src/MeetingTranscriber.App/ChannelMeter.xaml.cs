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

    /// <summary>
    /// Whether the source behind this bar is gone. Kept rather than passed down to each of the two
    /// things it changes, because one of them is the scale, which is laid out on resize and not on
    /// a reading: a bar that read it off the reading alone would put the two coloured numbers back
    /// under a dead channel the first time the window was made wider.
    /// </summary>
    private bool _died;

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
    /// Which channel this is, in the words the catalogue carries for it. Set from outside, because
    /// this control is one and the channels are two: a meter knows it is a meter and never which.
    /// </summary>
    /// <remarks>
    /// Nothing on this control draws it, and it reaches the automation tree and nowhere else. The
    /// words are the row above's — the chip, the role and the picker — because
    /// <c>docs/design.md</c> §Where it goes pins the meter to the control that chooses its source.
    /// But the bar is the thing carrying a value, so it is the thing that has to carry the name a
    /// screen reader says: a TextBlock beside it announces itself and leaves the one element on
    /// here that somebody listening needs named unnamed.
    /// </remarks>
    public string ChannelName
    {
        get => AutomationProperties.GetName(Bar);
        set => AutomationProperties.SetName(Bar, value);
    }

    /// <summary>
    /// The loudest this source has reached, for the window to word, or nothing where there is no
    /// peak to say. The number is the meter's and the sentence around it is the catalogue's, so
    /// neither has to know what the other is for.
    /// </summary>
    /// <remarks>
    /// Nothing while the source is dead, and that is one rule rather than two. The mark on the bar
    /// and the words beside it are the same peak said two ways — <c>docs/design.md</c> §The three
    /// states takes both off a source that died — so the answer that draws the mark is the answer
    /// the window words, and they cannot come apart into a bar with no mark under a line reading
    /// <c>pico −6.1</c>. What it is not is forgotten: the meeting's loudest moment is still there
    /// and comes back with the channel, because it is the peak of <em>this</em> meeting and the
    /// meeting did not stop.
    /// </remarks>
    public float? LoudestSoFar => _died ? null : _loudestSoFar;

    /// <summary>What the window wrote about <see cref="LoudestSoFar"/>, or nothing.</summary>
    public string LoudestSoFarSaid
    {
        get => Peak.Text;
        set => Peak.Text = value;
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
    /// <para>
    /// It draws and it remembers, and it says nothing: the three lines of words on it are the
    /// window's, because a sentence is the catalogue's and this control would otherwise have to
    /// know which language it is in to say that nothing is arriving.
    /// </para>
    /// <para>
    /// The exception the hot zone has is a source that died, which is the one condition that is not
    /// a meter state — <c>docs/design.md</c> §The three states. There the bar keeps the bare track
    /// and loses the hot zone, and that difference is the whole point of it: no signal is a source
    /// still there hearing nothing, and the hot zone under it is the standing promise that it would
    /// colour if what arrived ever clipped. A source that is not there makes no such promise, so
    /// the colour goes with it.
    /// </para>
    /// </remarks>
    public void Show(ChannelReading? reading)
    {
        if (reading is { IsSilent: false })
        {
            _loudestSoFar = _loudestSoFar is { } loudest
                ? Math.Max(loudest, reading.Level.Decibels)
                : reading.Level.Decibels;
        }

        // Asked before anything is drawn, because it decides three of the four layers, the retained
        // peak and both coloured numbers under them.
        var died = reading is { Stopped: true };
        var moved = died != _died;
        _died = died;

        _asFullAs = reading is { IsSilent: false, Stopped: false } ? reading.Meter : null;
        Draw();

        // The level's ink follows what it is saying. Where the source is alive it is a measurement
        // and reads in tinta, because it is what the row is about; where the source is gone the
        // words in its place are when it was cut off, and that is the thing on the row wanting
        // attention. `docs/design.md` §The three states puts it in pico for exactly that reason,
        // and this is one rank in two inks rather than the artboard's second size — the same
        // correction the peak beside it already stands as.
        Level.Foreground = Painted(died ? "PeakBrush" : "InkBrush");

        // Only where the answer moved. The scale is a canvas of text laid out by hand, so building
        // it again every second for as long as a dead device stays dead would be a screen doing
        // layout once a second to arrive at what was already on it.
        if (moved)
        {
            PaintTheScale(ScaleUnderTheBar.ActualWidth);
        }
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

        // The bare track and nothing else for a source that died, which is the one thing on this
        // component that is not a meter state. Everything below is then already nothing — a dead
        // source has no level — so this line is the whole of the difference.
        ShowOnly(HotZoneLayer, hotFrom, _died ? hotFrom : width);

        // Nothing arriving means no level and no peak: a bar drawn to nothing is what says the
        // source is silent, and the two coloured layers are the ones that would otherwise claim
        // something was heard.
        ShowOnly(LevelLayer, 0, _asFullAs is null ? 0 : to);
        ShowOnly(ClippedLayer, hotFrom, _asFullAs is null ? hotFrom : Math.Max(hotFrom, to));

        // Off LoudestSoFar and not off the field behind it, which is what makes the mark and the
        // words the window writes beside it one answer: a source that died has no peak to say, and
        // that is decided in one place rather than at each of the two things that draw it.
        RetainedPeak.Visibility = LoudestSoFar is null ? Visibility.Collapsed : Visibility.Visible;

        if (LoudestSoFar is { } loudest)
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
    private void OnScaleResized(object sender, SizeChangedEventArgs e) =>
        PaintTheScale(e.NewSize.Width);

    /// <summary>Writes the scale out at <paramref name="width"/>.</summary>
    /// <remarks>
    /// Called on a resize and on the source dying or coming back, because those are the two things
    /// that move it: where each number goes, and which ink it is in.
    /// </remarks>
    private void PaintTheScale(double width)
    {
        if (width <= 0)
        {
            return;
        }

        ScaleUnderTheBar.Children.Clear();

        foreach (var mark in MeterScale.Marks)
        {
            var number = new TextBlock
            {
                Text = mark.ToString("0", CultureInfo.InvariantCulture),
                Style = (Style)Application.Current.Resources["DataText"],
                Foreground = Painted(InkOf(mark)),
            };

            Canvas.SetLeft(number, MeterScale.Along(mark) * width);
            ScaleUnderTheBar.Children.Add(number);
        }

        ScaleUnderTheBar.UpdateLayout();

        foreach (var number in ScaleUnderTheBar.Children.OfType<TextBlock>())
        {
            Canvas.SetLeft(number, Math.Max(
                0, Math.Min(Canvas.GetLeft(number), width - number.ActualWidth)));
        }
    }

    /// <summary>
    /// Which of the three inks a mark is written in. The two that carry information are coloured
    /// and the rest are data like any other — a scale where every number was coloured would be one
    /// where none of them meant anything.
    /// </summary>
    /// <remarks>
    /// A dead source has neither of the two. What −12 and 0 say is where this bar's colours would
    /// change, and a bar that is no longer measuring anything has no such place: leaving them lit
    /// under it would be the scale going on describing a meter that is not running.
    /// <c>docs/design.md</c> §The three states is where that is decided.
    /// </remarks>
    private string InkOf(float mark) => mark switch
    {
        _ when _died => "TertiaryTextBrush",
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
