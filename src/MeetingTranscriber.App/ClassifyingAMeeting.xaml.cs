using System.Globalization;

using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;

using Windows.Foundation;
using Windows.System;

// `System.IO` is an implicit using and has a Path of its own, so the shape is aliased rather than
// qualified at each use — the same way `MainWindow` handles the two meanings of Duration.
using Path = Microsoft.UI.Xaml.Shapes.Path;

namespace MeetingTranscriber.App;

/// <summary>
/// The screen one meeting is filed from: which of the thirteen it was like, what it was work of,
/// who was on the other side, what it was about, and who was there.
/// </summary>
/// <remarks>
/// <para>
/// Nothing on it names anything the corpus stores. There is no tree drawn, no word for a node, a
/// role or a link, and no help panel: the columns are three sentences about the meeting and the
/// shapes are fourteen meetings said by name, so somebody who has never heard of a three-level
/// classification can file one.
/// </para>
/// <para>
/// It decides nothing about the meeting. What a shape opens is <see cref="MeetingShapes"/>'s, what
/// a draft would write is <see cref="MeetingFiling"/>'s, and what writing it does to a corpus is
/// <see cref="MeetingClassifying"/>'s — each of them in a project a build agent can run. What is
/// here is turning those answers into controls and the presses back into calls.
/// </para>
/// <para>
/// It opens no corpus it does not let go of, for the reason <see cref="MeetingsDrawer"/> gives, and
/// it holds no file: the meeting screen keeps the recording, paused, for as long as this one has
/// the window.
/// </para>
/// <para>
/// Two things are written before <em>Guardar</em>, and both are deliberate: naming a node and
/// naming a person. Those are corpus-wide vocabulary rather than facts about this meeting, so
/// <c>HumanLayer</c>'s own contract applies — a person's edits are separate acts — and a node held
/// in a draft would have no id, which makes every picker below it a special case. Everything else
/// is a draft until the act on the right.
/// </para>
/// </remarks>
public sealed partial class ClassifyingAMeeting : UserControl
{
    /// <summary>
    /// How thick the two glyphs on this screen are drawn, in the 24-unit box they are written in.
    /// </summary>
    /// <remarks>
    /// The artboards' own weight. It is here rather than in a style beside the geometry for the
    /// reason Olivo's <c>Pill</c> gives about the chevron in the picker: a <c>Geometry</c> in a
    /// <c>Setter</c> is one object handed to every <c>Path</c> that takes it, and only the first
    /// one draws — so geometry is written where it is drawn, and this is written with it.
    /// </remarks>
    private const double HowThickAGlyphIs = 2.4;

    /// <summary>How wide the chevron between two pills is, and the `+` at the end of a path.</summary>
    private const double HowWideAGlyphIs = 11;

    /// <summary>
    /// Which rows have been asked for one more level than they have chosen, by the column they are
    /// in and their position in it.
    /// </summary>
    /// <remarks>
    /// A screen's state and not the draft's, which is why it is here: a path holds the nodes
    /// somebody chose, and an empty picker waiting for the next one is a thing on the screen rather
    /// than something a save would write. It is emptied whenever the rows underneath it move.
    /// </remarks>
    private readonly HashSet<(MeetingNodeRole Role, int Row)> _deeper = [];

    /// <summary>
    /// Where the meetings are, handed over once by the window that holds this. Not a constructor
    /// parameter because the XAML declares this control, and what XAML constructs takes no
    /// arguments.
    /// </summary>
    private CorpusFolder? _corpus;

    private UiLanguage _language;

    /// <summary>The meeting on screen, or none when this control is not showing one.</summary>
    private Guid? _meeting;

    /// <summary>What was read the last time this screen drew, or nothing when the read refused.</summary>
    private MeetingAsClassified? _read;

    /// <summary>
    /// The filing as it stands on screen, which is the corpus's answer until somebody changes it.
    /// Nothing writes it back until the act on the right is pressed.
    /// </summary>
    private MeetingFiling _chosen = MeetingFiling.Nothing;

    private TextLine? _status;

    /// <summary>
    /// True while this screen is building its own controls, so a picker being set to what it
    /// already says is not read as somebody having chosen something.
    /// </summary>
    private bool _drawing;

    /// <summary>Which pill, if any, somebody is typing a new name into.</summary>
    private (MeetingNodeRole Role, int Row, int Level)? _naming;

    /// <summary>Which place for somebody the dialogue was opened from.</summary>
    private int? _namingSomebody;

    /// <summary>The organizations the dialogue offers, in the order it offers them.</summary>
    private IReadOnlyList<Node> _organizations = [];

    public ClassifyingAMeeting() => InitializeComponent();

    /// <summary>The meeting was filed, and this screen is done with it.</summary>
    public event EventHandler<Guid>? Filed;

    /// <summary>Somebody asked to go back to the meeting without filing it.</summary>
    public event EventHandler? Left;

    /// <summary>Whether this screen is showing a meeting.</summary>
    public bool IsOpen => _meeting is not null;

    /// <summary>
    /// Hands over the corpus the meetings are in. Reads nothing: nothing is shown until a meeting
    /// is chosen.
    /// </summary>
    /// <exception cref="InvalidOperationException">It was opened twice.</exception>
    public void Open(CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        if (_corpus is not null)
        {
            throw new InvalidOperationException("The classification screen already has a corpus.");
        }

        _corpus = corpus;
    }

    /// <summary>
    /// Which language this screen is being read in.
    /// </summary>
    /// <remarks>
    /// It draws again and it keeps the draft. A language is not an answer about this meeting, and
    /// somebody who changed it half way through filing one would otherwise lose what they had said.
    /// </remarks>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();

        if (_meeting is not null)
        {
            Render();
        }
    }

    /// <summary>Opens one meeting: reads it, and shows what it is filed under now.</summary>
    public void Show(Guid meetingId)
    {
        _meeting = meetingId;
        Draw(theDraftToo: true);
    }

    /// <summary>Lets go of the meeting and of everything drawn about it.</summary>
    public void Close()
    {
        _meeting = null;
        _read = null;
        _status = null;
        _chosen = MeetingFiling.Nothing;
        _deeper.Clear();
        _naming = null;
        _namingSomebody = null;
        _organizations = [];

        TheShapes.Children.Clear();
        Columns.Children.Clear();
        Columns.ColumnDefinitions.Clear();
        Who.Children.Clear();
        WhichMeetingText.Text = string.Empty;
        StatusText.Text = string.Empty;
        StatusText.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// What this screen says, in the language it is being read in. Every word on it comes through
    /// here, which is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>What one of the fourteen shapes is called.</summary>
    /// <remarks>
    /// By name only, and nothing on this screen explains what one fills: that is seen when it is
    /// chosen, which is what #105 settled. The last arm stops rather than substituting, so a shape
    /// added later and given no name here cannot be shown to somebody as another one.
    /// </remarks>
    private static UiText Named(MeetingShape shape) => shape switch
    {
        MeetingShape.Class => UiTexts.TheShapeClass,
        MeetingShape.CasualCatchUp => UiTexts.TheShapeCasualCatchUp,
        MeetingShape.InterviewAsCandidate => UiTexts.TheShapeInterviewAsCandidate,
        MeetingShape.InterviewAsInterviewer => UiTexts.TheShapeInterviewAsInterviewer,
        MeetingShape.TwoProjects => UiTexts.TheShapeTwoProjects,
        MeetingShape.SellingToAClient => UiTexts.TheShapeSellingToAClient,
        MeetingShape.TeamMeeting => UiTexts.TheShapeTeamMeeting,
        MeetingShape.Conference => UiTexts.TheShapeConference,
        MeetingShape.BetweenTwoCompanies => UiTexts.TheShapeBetweenTwoCompanies,
        MeetingShape.HumanResources => UiTexts.TheShapeHumanResources,
        MeetingShape.RecurringOneToOne => UiTexts.TheShapeRecurringOneToOne,
        MeetingShape.Daily => UiTexts.TheShapeDaily,
        MeetingShape.AfterSalesSupport => UiTexts.TheShapeAfterSalesSupport,
        MeetingShape.FilledByHand => UiTexts.TheShapeFilledByHand,
        _ => throw new InvalidOperationException($"This screen has no name for the shape '{shape}'."),
    };

    /// <summary>What one of the three columns is called.</summary>
    /// <remarks>
    /// Plain Spanish about the meeting and never the name of the role: <em>es trabajo de</em> and
    /// not <em>work of</em>. The last arm stops for the reason above, and it is what fires the day
    /// a fourth way of relating a meeting to a node joins the closed vocabulary.
    /// </remarks>
    private static UiText Heading(MeetingNodeRole role) => role switch
    {
        MeetingNodeRole.WorkOf => UiTexts.ItIsWorkOf,
        MeetingNodeRole.Counterpart => UiTexts.TheOtherSide,
        MeetingNodeRole.About => UiTexts.ItIsAbout,
        _ => throw new InvalidOperationException($"This screen has no heading for the role '{role}'."),
    };

    /// <summary>What one of the two toggles on a person's row says.</summary>
    /// <remarks>
    /// The variable is <c>named</c> and the two above are <c>shape</c> and <c>role</c>, and that is
    /// not style: the checks that hold these tables to their enums find a table by the expression
    /// it switches on, one per file, so two tables over one name would be read as one.
    /// </remarks>
    private static UiText Badge(MeetingPersonRole named) => named switch
    {
        MeetingPersonRole.Attended => UiTexts.TheyWereThere,
        MeetingPersonRole.Subject => UiTexts.TheMeetingIsAboutThisPerson,
        _ => throw new InvalidOperationException($"This screen has no badge for the role '{named}'."),
    };

    private static Brush Painted(string key) => (Brush)Application.Current.Resources[key];

    /// <summary>
    /// A stroked glyph, built where it is drawn.
    /// </summary>
    /// <remarks>
    /// In code and not off a style, for the reason Olivo's own comment above <c>Pill</c> gives: the
    /// value of a <c>Data</c> setter is one <c>Geometry</c> object handed to every <c>Path</c> that
    /// takes the style, and only the first one draws.
    /// </remarks>
    private static Path Glyph(string brush, params Point[][] strokes)
    {
        var drawing = new PathGeometry();

        foreach (var run in strokes)
        {
            var stroke = new PathFigure { StartPoint = run[0], IsClosed = false, IsFilled = false };

            foreach (var point in run.Skip(1))
            {
                stroke.Segments.Add(new LineSegment { Point = point });
            }

            drawing.Figures.Add(stroke);
        }

        return new Path
        {
            Data = drawing,
            Stroke = Painted(brush),
            StrokeThickness = HowThickAGlyphIs,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Stretch = Stretch.Uniform,
            Width = HowWideAGlyphIs,
            Height = HowWideAGlyphIs,
            IsHitTestVisible = false,
        };
    }

    /// <summary>The mark between two pills of one path, which says one is inside the other.</summary>
    private static Path Chevron() =>
        Glyph("TertiaryTextBrush", [new Point(9, 5), new Point(16, 12), new Point(9, 19)]);

    /// <summary>The mark on a press that opens one more place to fill in.</summary>
    private static Path Plus() => Glyph(
        "TertiaryTextBrush",
        [new Point(12, 5), new Point(12, 19)],
        [new Point(5, 12), new Point(19, 12)]);

    private CorpusFolder Corpus() => _corpus
        ?? throw new InvalidOperationException(
            "The classification screen was never given a corpus, so it has no meeting to file.");

    private Style Chrome(string named) => (Style)Root.Resources[named];

    /// <summary>
    /// Reads the meeting again and puts it back on the controls.
    /// </summary>
    /// <param name="theDraftToo">
    /// Whether what is on screen is replaced by what the corpus holds. Only when the meeting
    /// changed: naming a node or a person reads the corpus again for the pickers' sake, and taking
    /// the draft back with it would throw away everything the person had answered so far.
    /// </param>
    private void Draw(bool theDraftToo)
    {
        _read = null;
        _status = null;

        if (theDraftToo)
        {
            _chosen = MeetingFiling.Nothing;
            _deeper.Clear();
            _naming = null;
        }

        if (_meeting is not { } meetingId)
        {
            Render();
            return;
        }

        if (Corpus().Folder is not { } folder)
        {
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
        }
        else
        {
            try
            {
                using var context = CorpusDatabase.Open(folder);
                _read = new MeetingClassifying(context, TimeProvider.System).Of(meetingId);
            }
            catch (MeetingStageException gone)
            {
                _status = TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            }
            catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
            {
                _status = TextLine.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
            }
        }

        if (theDraftToo && _read is { } read)
        {
            _chosen = read.Chosen;
        }

        Render();
    }

    /// <summary>Puts the draft and what was read onto the controls.</summary>
    private void Render()
    {
        _drawing = true;

        try
        {
            TheShapes.Children.Clear();
            Columns.Children.Clear();
            Columns.ColumnDefinitions.Clear();
            Who.Children.Clear();

            StatusText.Text = _status?.In(_language) ?? string.Empty;
            StatusText.Visibility = _status is null ? Visibility.Collapsed : Visibility.Visible;

            if (_read is not { } read)
            {
                WhichMeetingText.Text = string.Empty;
                SaveButton.IsEnabled = false;
                UnclassifyButton.IsEnabled = false;
                return;
            }

            SaveButton.IsEnabled = true;
            UnclassifyButton.IsEnabled = true;
            WhichMeetingText.Text = WhichMeeting(read.Meeting);

            TheShapesOnOffer();
            TheColumns(read);
            ThePeople(read);
        }
        finally
        {
            _drawing = false;
        }
    }

    /// <summary>
    /// Which meeting this is: its name and when it was. A meeting nobody has named is the moment on
    /// its own rather than the moment after a gap.
    /// </summary>
    private static string WhichMeeting(Meeting meeting) =>
        string.IsNullOrWhiteSpace(meeting.Title)
            ? ScreenNumbers.At(meeting.StartedAt)
            : ScreenNumbers.Beside(meeting.Title, ScreenNumbers.At(meeting.StartedAt));

    // ── The fourteen ──────────────────────────────────────────────────────────────────────────

    private void TheShapesOnOffer()
    {
        // In the order the vocabulary declares them, which is §5.3's own row order. The artboard
        // draws a different one; a second ordering written into this screen would be a second thing
        // to keep in step with that page.
        foreach (var shape in Enum.GetValues<MeetingShape>())
        {
            var chip = new Button { Content = In(Named(shape)), Style = Chrome(HowAChipIsDrawn(shape)) };

            chip.Click += (_, _) => ChooseTheShape(shape);
            TheShapes.Children.Add(chip);
        }
    }

    /// <summary>
    /// Which of the three ways a chip is drawn this one takes: chosen, ordinary, or the one that
    /// fills nothing and so wears the empty control's ring.
    /// </summary>
    private string HowAChipIsDrawn(MeetingShape shape)
    {
        if (_chosen.Shape == shape)
        {
            return "ChipChosen";
        }

        return shape is MeetingShape.FilledByHand ? "ChipFilledByHand" : "Chip";
    }

    /// <summary>
    /// Choosing a shape replaces what is on screen and writes nothing.
    /// </summary>
    /// <remarks>
    /// The card's <em>what each one fills is seen when it is chosen</em> only reads true if
    /// choosing changes the screen. What is lost is a draft; the corpus is where it was either way,
    /// and pressing the shape that was already lit re-opens its empty places.
    /// </remarks>
    private void ChooseTheShape(MeetingShape shape)
    {
        _chosen = MeetingFiling.AsShapedBy(shape);
        _deeper.Clear();
        _naming = null;
        Render();
    }

    // ── The three columns ─────────────────────────────────────────────────────────────────────

    private void TheColumns(MeetingAsClassified read)
    {
        foreach (var role in Enum.GetValues<MeetingNodeRole>())
        {
            var column = new StackPanel { Spacing = 8 };

            column.Children.Add(new TextBlock
            {
                Text = In(Heading(role)),
                Style = Chrome("ColumnHeading"),
            });

            var paths = _chosen.Column(role);

            if (paths.Count == 0)
            {
                // What stands where the pills would be. *Agregar* is under every column at all
                // times whatever this says, because a column a shape opened empty is one somebody
                // still has to be able to fill by hand — which is the whole of what the fourteenth
                // shape is for.
                column.Children.Add(new TextBlock
                {
                    Text = In(UiTexts.NothingElse),
                    Style = Chrome("NothingHere"),
                });
            }

            for (var row = 0; row < paths.Count; row++)
            {
                column.Children.Add(APath(read, role, row, paths[row]));
            }

            var add = new Button { Content = In(UiTexts.Add), Style = Chrome("AnOptionalPress") };
            add.Click += (_, _) => AddAPath(role);
            column.Children.Add(add);

            Columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(column, Columns.ColumnDefinitions.Count - 1);
            Columns.Children.Add(column);
        }
    }

    /// <summary>One row of a column: the pills down the tree, and the press that opens one more.</summary>
    private UIElement APath(MeetingAsClassified read, MeetingNodeRole role, int row, ChosenPath path)
    {
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };

        // A row always shows at least one pill: a place a shape opened has nothing chosen in it and
        // is still somewhere to answer.
        var levels = Math.Max(1, path.Nodes.Count + (_deeper.Contains((role, row)) ? 1 : 0));

        for (var level = 0; level < levels; level++)
        {
            if (level > 0)
            {
                line.Children.Add(Chevron());
            }

            line.Children.Add(APill(read, role, row, path, level));
        }

        // Only where the tree has room. What a node of each class holds is Node.Holds's, which is
        // also what caps the depth — so a topic never grows a child and nothing here counts levels.
        if (levels == path.Nodes.Count
            && Deepest(read, path) is { } deepest
            && Node.Holds(deepest.Kind) is not null)
        {
            var deeper = new Button { Style = Chrome("AddALevel"), Content = Plus() };

            // A glyph with no name is nothing to a screen reader.
            AutomationProperties.SetName(deeper, In(UiTexts.AddALevel));

            deeper.Click += (_, _) => OpenOneMoreLevel(role, row);
            line.Children.Add(deeper);
        }

        return line;
    }

    /// <summary>
    /// One pill: a picker over what may stand at this level, or the field a new name is typed into.
    /// </summary>
    /// <remarks>
    /// A picker and not a label with a press beside it. What stands at a level is chosen where it
    /// is shown, and Olivo's own drop-down already draws it as the pill the artboard draws.
    /// </remarks>
    private UIElement APill(MeetingAsClassified read, MeetingNodeRole role, int row, ChosenPath path, int level)
    {
        if (_naming == (role, row, level))
        {
            return AName(read, role, row, path, level);
        }

        var offered = WhatMayStandAt(read, path, level);
        var picker = new ComboBox
        {
            Style = Chrome("Picker"),
            PlaceholderText = In(UiTexts.NoneOfThese),
            ItemsSource = (string[])
            [
                In(UiTexts.NoneOfThese),
                .. offered.Select(node => node.Name),
                In(UiTexts.NameANewOne),
            ],
        };

        var here = level < path.Nodes.Count ? path.Nodes[level] : (Guid?)null;
        var standing = offered.Select((node, at) => (node.Id, At: at)).FirstOrDefault(found => found.Id == here);

        picker.SelectedIndex = here is null ? -1 : standing.At + 1;

        picker.SelectionChanged += (_, _) =>
        {
            if (_drawing)
            {
                return;
            }

            var picked = picker.SelectedIndex;

            if (picked < 0)
            {
                return;
            }

            if (picked == offered.Count + 1)
            {
                _naming = (role, row, level);
                Render();
                return;
            }

            // Nothing chosen empties this pill and everything to the right of it, because what a
            // deeper pill offered was the children of this one.
            PutAt(role, row, level, picked == 0 ? null : offered[picked - 1].Id);
        };

        return picker;
    }

    /// <summary>
    /// What may stand at one level of a path: every root at the first, and the children of the pill
    /// to the left after that.
    /// </summary>
    private static IReadOnlyList<Node> WhatMayStandAt(MeetingAsClassified read, ChosenPath path, int level)
    {
        if (level == 0)
        {
            return [.. read.Tree.Where(node => node.ParentId is null)];
        }

        return level <= path.Nodes.Count
            ? [.. read.Tree.Where(node => node.ParentId == path.Nodes[level - 1])]
            : [];
    }

    private static Node? Deepest(MeetingAsClassified read, ChosenPath path) =>
        path.Deepest is { } node ? read.Tree.FirstOrDefault(found => found.Id == node) : null;

    private void AddAPath(MeetingNodeRole role)
    {
        _chosen = _chosen.With(role, [.. _chosen.Column(role), ChosenPath.Empty]);
        Render();
    }

    private void OpenOneMoreLevel(MeetingNodeRole role, int row)
    {
        _deeper.Add((role, row));
        Render();
    }

    /// <summary>Puts a node at one level of a path, and empties everything below it.</summary>
    private void PutAt(MeetingNodeRole role, int row, int level, Guid? node)
    {
        var paths = _chosen.Column(role).ToList();
        var kept = paths[row].Nodes.Take(level).ToList();

        if (node is { } chosen)
        {
            kept.Add(chosen);
        }

        paths[row] = new ChosenPath(kept);

        _deeper.Remove((role, row));
        _naming = null;
        _chosen = _chosen.With(role, paths);
        Render();
    }

    // ── Naming something the corpus does not have yet ──────────────────────────────────────────

    /// <summary>The field a new name is typed into, standing where its pill was.</summary>
    private UIElement AName(MeetingAsClassified read, MeetingNodeRole role, int row, ChosenPath path, int level)
    {
        var typing = new TextBox { Style = Chrome("Naming") };

        typing.Loaded += (_, _) => typing.Focus(FocusState.Programmatic);
        typing.LostFocus += (_, _) => NameANode(read, role, row, path, level, typing.Text);

        typing.KeyDown += (_, pressed) =>
        {
            if (pressed.Key is VirtualKey.Enter)
            {
                pressed.Handled = true;
                NameANode(read, role, row, path, level, typing.Text);
            }
        };

        return typing;
    }

    /// <summary>
    /// Writes a node somebody named and puts it in the pill they named it from.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Written now and not at <em>Guardar</em>. A node is corpus-wide vocabulary rather than a fact
    /// about this meeting, so <c>HumanLayer</c>'s own contract applies; and a node held in a draft
    /// has no id, which makes every picker below it a special case. The cost is an organization
    /// somebody named and walked away from, which <c>HumanLayer.Remove</c> exists for the day that
    /// is worth a screen.
    /// </para>
    /// <para>
    /// What class it is placed as is fixed by where it stands and is never asked: an organization
    /// at the top, and whatever the pill to its left holds below that. Asking somebody
    /// <em>organization or body of work?</em> is a technical name on a screen the card says has
    /// none, so a body of work belonging to nobody in particular has no way in from here — which is
    /// a screen for the tree and not this one.
    /// </para>
    /// </remarks>
    private void NameANode(
        MeetingAsClassified read,
        MeetingNodeRole role,
        int row,
        ChosenPath path,
        int level,
        string? typed)
    {
        if (_drawing || _naming != (role, row, level))
        {
            return;
        }

        var name = (typed ?? string.Empty).Trim();
        var parent = level == 0 ? null : read.Tree.FirstOrDefault(node => node.Id == path.Nodes[level - 1]);
        var kind = parent is null ? NodeKind.Organization : Node.Holds(parent.Kind);

        // Nothing typed puts the pill back to what it was, and so does a place with nothing under
        // it — which the press that opened this one cannot produce and is checked anyway.
        if (name.Length == 0 || kind is not { } placed)
        {
            _naming = null;
            Render();
            return;
        }

        if (Corpus().Folder is not { } folder)
        {
            _status = TextLine.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
            _naming = null;
            Render();
            return;
        }

        Guid made;

        try
        {
            using var context = CorpusDatabase.Open(folder);
            var human = new HumanLayer(context, TimeProvider.System);

            made = parent is null
                ? human.Root(placed, name).Id
                : human.Under(parent, placed, name).Id;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status = TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
            _naming = null;
            Render();
            return;
        }

        // The tree again and not the draft: what somebody has answered so far is still on screen.
        _naming = null;
        Draw(theDraftToo: false);
        PutAt(role, row, level, made);
    }

    // ── Who was on it ─────────────────────────────────────────────────────────────────────────

    private void ThePeople(MeetingAsClassified read)
    {
        Who.Children.Add(new TextBlock { Text = In(UiTexts.Who), Style = Chrome("ColumnHeading") });

        // The person using this install is drawn first and is not a place: they are on every
        // meeting and stored on none, so there is no picker on their name and no way to take them
        // off. A row saying the owner of the corpus was at their own meeting says nothing.
        if (read.Me is { } me)
        {
            Who.Children.Add(TheOneUsingThis(read, me));
        }

        for (var slot = 0; slot < _chosen.Somebody.Count; slot++)
        {
            Who.Children.Add(APlaceForSomebody(read, slot, _chosen.Somebody[slot]));
        }

        var add = new Button { Content = In(UiTexts.AddSomebody), Style = Chrome("AnOptionalPress") };
        add.Click += (_, _) => AddAPlaceForSomebody();
        Who.Children.Add(add);
    }

    private UIElement TheOneUsingThis(MeetingAsClassified read, Person me)
    {
        var row = ARowOfPeople();

        var name = new Border
        {
            Style = Chrome("TheirName"),
            Child = new TextBlock { Text = me.DisplayName, Style = Chrome("TheirNameSays") },
        };

        Grid.SetColumn(name, 0);
        row.Children.Add(name);
        row.Children.Add(WhereTheyBelonged(read, me.Id));
        return row;
    }

    private UIElement APlaceForSomebody(MeetingAsClassified read, int slot, ChosenPerson person)
    {
        var row = ARowOfPeople();

        // Everybody but the one using this install, who is drawn above and cannot be a place: two
        // rows for one person is a meeting naming somebody twice.
        var offered = read.Everybody
            .Where(found => found.Person.Id != read.Me?.Id)
            .Select(found => found.Person)
            .ToArray();

        var picker = new ComboBox
        {
            Style = Chrome("Picker"),
            PlaceholderText = In(UiTexts.NoneOfThese),
            ItemsSource = (string[])
            [
                In(UiTexts.NoneOfThese),
                .. offered.Select(found => found.DisplayName),
                In(UiTexts.NameANewOne),
            ],
        };

        var standing = offered
            .Select((found, at) => (found.Id, At: at))
            .FirstOrDefault(found => found.Id == person.PersonId);

        picker.SelectedIndex = person.PersonId is null ? -1 : standing.At + 1;

        picker.SelectionChanged += async (_, _) =>
        {
            if (_drawing || picker.SelectedIndex < 0)
            {
                return;
            }

            if (picker.SelectedIndex == offered.Length + 1)
            {
                await AskWhoTheyAre(read, slot);
                return;
            }

            // Nothing chosen is how somebody comes off this meeting. The place stays and names
            // nobody, which files nothing.
            PutSomebodyIn(slot, picker.SelectedIndex == 0 ? null : offered[picker.SelectedIndex - 1].Id);
        };

        Grid.SetColumn(picker, 0);
        row.Children.Add(picker);

        var badges = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };

        // Both of them, and both pressable. One badge would leave somebody filing §5.3 row 10 by
        // hand unable to say that the person the meeting is about was never in the room.
        foreach (var named in Enum.GetValues<MeetingPersonRole>())
        {
            var badge = new Button
            {
                Content = In(Badge(named)),
                Style = Chrome(person.Carries(named) ? "BadgeOn" : "Badge"),
            };

            badge.Click += (_, _) => Flip(slot, named);
            badges.Children.Add(badge);
        }

        Grid.SetColumn(badges, 1);
        row.Children.Add(badges);

        if (person.PersonId is { } who)
        {
            row.Children.Add(WhereTheyBelonged(read, who));
        }

        return row;
    }

    /// <summary>The three places a person's row lays out in: their name, the two badges, where they
    /// belonged.</summary>
    private static Grid ARowOfPeople()
    {
        var row = new Grid { ColumnSpacing = 9 };

        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        return row;
    }

    /// <summary>
    /// Where somebody belonged the day of the meeting, at the end of their row.
    /// </summary>
    /// <remarks>
    /// That day and never today, which is the card's sixth decision and the reason an affiliation
    /// carries a period at all: hiring tomorrow somebody interviewed today must not rewrite this
    /// meeting. Which spells held then is <c>Affiliation.Held</c>'s, asked where the meeting is
    /// read.
    /// </remarks>
    private UIElement WhereTheyBelonged(MeetingAsClassified read, Guid person)
    {
        var belonged = read.Everybody.FirstOrDefault(found => found.Person.Id == person)?.Belonged ?? [];

        var line = new TextBlock
        {
            Text = ScreenNumbers.Beside([.. belonged.Select(Spelt)]),
            Style = Chrome("WhereTheyBelonged"),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Grid.SetColumn(line, 2);
        return line;
    }

    /// <summary>
    /// One spell: where, and since when. The organization's name is the whole of the "where" — an
    /// affiliation carries a node and a period and nothing else, so the artboard's second word has
    /// no row behind it. A corpus that never learned the date says only the name.
    /// </summary>
    private string Spelt((Node Organization, UtcTimestamp? Since) spell) => spell.Since is { } since
        ? ScreenNumbers.Beside(
            spell.Organization.Name,
            UiTexts.SinceTheYear.In(_language, ScreenNumbers.Year(since)))
        : spell.Organization.Name;

    private void AddAPlaceForSomebody()
    {
        _chosen = _chosen with
        {
            Somebody = [.. _chosen.Somebody, new ChosenPerson(null, Attended: true, Subject: false)],
        };

        Render();
    }

    private void PutSomebodyIn(int slot, Guid? person)
    {
        var slots = _chosen.Somebody.ToList();
        slots[slot] = slots[slot] with { PersonId = person };
        _chosen = _chosen with { Somebody = slots };
        Render();
    }

    private void Flip(int slot, MeetingPersonRole named)
    {
        var slots = _chosen.Somebody.ToList();
        slots[slot] = slots[slot].Flipped(named);
        _chosen = _chosen with { Somebody = slots };
        Render();
    }

    // ── Adding a person ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the one dialogue this screen has, over the place it was asked from.
    /// </summary>
    /// <remarks>
    /// The second of the two <c>docs/design.md</c> §Notices allows, and the list is closed — which
    /// is why naming a node is done on the pill and not in a third one.
    /// </remarks>
    private async Task AskWhoTheyAre(MeetingAsClassified read, int slot)
    {
        _namingSomebody = slot;
        TheirNameBox.Text = string.Empty;
        TheirYearBox.Text = string.Empty;
        DialogueStatusText.Text = string.Empty;
        DialogueStatusText.Visibility = Visibility.Collapsed;

        // Nobody is added without a name, and the act says so by being dead rather than by refusing
        // afterwards: a form that takes a press and answers with a complaint is a form that asked
        // for the press.
        AddingSomebody.IsPrimaryButtonEnabled = false;

        _organizations = [.. read.Tree.Where(node => node.ParentId is null && node.Kind is NodeKind.Organization)];

        TheirOrganization.ItemsSource = (string[])
        [
            In(UiTexts.NoneOfThese),
            .. _organizations.Select(node => node.Name),
        ];

        TheirOrganization.SelectedIndex = 0;

        // A dialogue declared in this screen's own markup is already in the window's tree and has
        // its root; one that is not would throw where it is shown, off a build with nothing wrong
        // in it and no test in this repository that opens a window.
        if (AddingSomebody.XamlRoot is null)
        {
            AddingSomebody.XamlRoot = Root.XamlRoot;
        }

        await AddingSomebody.ShowAsync();

        // Whatever happened, the pill that opened this is showing *Nombrar uno nuevo…* as though it
        // were an answer. Drawing again puts it back to whoever is in the place now, which is the
        // person just written or nobody at all.
        Render();
    }

    private void OnTheirNameTyped(object sender, TextChangedEventArgs e) =>
        AddingSomebody.IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(TheirNameBox.Text);

    /// <summary>
    /// Writes somebody the corpus does not have yet, and puts them in the place that asked.
    /// </summary>
    /// <remarks>
    /// The dialogue stays open on a refusal, because the alternative is losing three fields
    /// somebody typed to a corpus that was locked for a second.
    /// </remarks>
    private void OnSomebodyNamed(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var name = (TheirNameBox.Text ?? string.Empty).Trim();

        // The act is dead until there is a name, so an empty one is not something a person did.
        // A corpus that would not open is, and it is the one the sentence below is about.
        if (name.Length == 0 || _namingSomebody is not { } slot)
        {
            args.Cancel = true;
            return;
        }

        if (Corpus().Folder is not { } folder)
        {
            args.Cancel = true;
            SayInTheDialogue(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
            return;
        }

        var chosen = TheirOrganization.SelectedIndex - 1;
        var organization = chosen >= 0 && chosen < _organizations.Count ? _organizations[chosen] : null;

        Guid made;

        try
        {
            using var context = CorpusDatabase.Open(folder);
            var human = new HumanLayer(context, TimeProvider.System);
            var person = human.Add(name);

            if (organization is not null)
            {
                // No year is what Affiliation already means by no start — as far back as this
                // corpus goes — and not a guess at one.
                human.Join(person, organization, TheFirstOfTheYear(TheirYearBox.Text));
            }

            made = person.Id;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            args.Cancel = true;
            SayInTheDialogue(UiTexts.ThatDidNotGoThrough, refused.Message);
            return;
        }

        _namingSomebody = null;
        Draw(theDraftToo: false);
        PutSomebodyIn(slot, made);
    }

    private void SayInTheDialogue(UiText what, string about)
    {
        DialogueStatusText.Text = TextLine.Says(what, about).In(_language);
        DialogueStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The first instant of the year somebody typed, or nothing when they typed nothing readable.
    /// </summary>
    /// <remarks>
    /// A year and not a date, because that is the whole of what this screen shows about a period —
    /// and a start read back at a finer grain than it was asked for would be an invention.
    /// </remarks>
    private static UtcTimestamp? TheFirstOfTheYear(string? typed) =>
        int.TryParse((typed ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
        && year is >= 1 and <= 9999
            ? UtcTimestamp.From(new DateTimeOffset(year, 1, 1, 0, 0, 0, TimeSpan.Zero))
            : null;

    // ── The two answers ───────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Somebody said this meeting is filed under nothing.
    /// </summary>
    /// <remarks>
    /// It writes nothing. What it does is empty the screen, and the act on the right is still what
    /// files it — which is what keeps this in the ordinary neutral slot honestly rather than past
    /// the gap where a press that loses something sits.
    /// </remarks>
    private void OnLeaveItUnclassified(object sender, RoutedEventArgs e)
    {
        _chosen = MeetingFiling.Nothing;
        _deeper.Clear();
        _naming = null;
        Render();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (_meeting is not { } meeting || Corpus().Folder is not { } folder)
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            new MeetingClassifying(context, TimeProvider.System).Save(meeting, _chosen);
        }
        catch (MeetingStageException gone)
        {
            _status = TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            Render();
            return;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            _status = TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message);
            Render();
            return;
        }

        Close();
        Filed?.Invoke(this, meeting);
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        // Nothing is written on the way out, and nothing was written on the way in: a draft
        // abandoned leaves the meeting exactly as it was found.
        Close();
        Left?.Invoke(this, EventArgs.Empty);
    }
}
