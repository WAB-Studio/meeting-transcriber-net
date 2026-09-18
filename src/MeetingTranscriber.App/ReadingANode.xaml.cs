using MeetingTranscriber.Domain.Knowledge;
using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace MeetingTranscriber.App;

/// <summary>
/// A node's story: everything the meetings filed under it — and under everything hanging off it —
/// settled, left to do and left open, oldest first.
/// </summary>
/// <remarks>
/// <para>
/// Reached from one place only: the filing chip on a meeting's own screen,
/// <see cref="ReadingAMeeting.NodeChosen"/>. Docs/design.md §Historia is the authority this was
/// built from, and the product decision on #133 settles why: reading a project's history has to be
/// something you go and do, and a panel inside a meeting could never answer <em>read the whole of
/// this client</em>.
/// </para>
/// <para>
/// There is no player and no transcript on it. What this screen is for is reading across meetings;
/// the minute beside each thing is what says which meeting to open to hear one, and the transcript
/// unfolds there, on <see cref="ReadingAMeeting"/>.
/// </para>
/// <para>
/// A control and not a window, the same shape <see cref="ReadingAMeeting"/> is and for the same
/// reason: this is the screen the meetings are on, with the meetings out of the way.
/// </para>
/// </remarks>
public sealed partial class ReadingANode : UserControl
{
    /// <summary>
    /// How much of a node's story this screen reads.
    /// </summary>
    /// <remarks>
    /// The whole of it is what somebody came here for, so a bound that stops at a page is a screen
    /// they cannot use; and past a few hundred statements <em>read the whole of this</em> has
    /// stopped being what anybody does in one sitting. One past it is read so the screen knows it
    /// was cut without counting the rest, which is the same trick every tool on the MCP server
    /// uses.
    /// </remarks>
    private const int HowMuchOfANodesStoryIsRead = 500;

    private CorpusFolder? _corpus;
    private UiLanguage _language;
    private Guid? _node;
    private readonly ScreenStatus _status = new();

    /// <summary>Somebody asked to go back to the meeting they came from.</summary>
    public event EventHandler? Left;

    /// <summary>Somebody opened one of the meetings in this node's story.</summary>
    public event EventHandler<Guid>? MeetingChosen;

    public ReadingANode() => InitializeComponent();

    /// <summary>Whether this screen is showing a node.</summary>
    public bool IsShowingANode => _node is not null;

    /// <summary>
    /// Hands over the corpus the nodes are in. Reads nothing: nothing is shown until a node is
    /// chosen.
    /// </summary>
    /// <exception cref="InvalidOperationException">It was opened twice.</exception>
    public void Open(CorpusFolder corpus)
    {
        ArgumentNullException.ThrowIfNull(corpus);

        if (_corpus is not null)
        {
            throw new InvalidOperationException("The node screen already has a corpus.");
        }

        _corpus = corpus;
    }

    /// <summary>Which language this screen is being read in.</summary>
    public void ReadIn(UiLanguage language)
    {
        _language = language;
        Bindings.Update();

        if (_node is { } node)
        {
            Draw(node);
        }
    }

    /// <summary>Opens one node's story.</summary>
    public void Show(Guid node)
    {
        _node = node;
        Draw(node);
    }

    /// <summary>Lets go of the node and of everything drawn about it.</summary>
    public void Close()
    {
        _node = null;
        _status.Nothing();
        StatusText.Text = string.Empty;
        ThePath.Children.Clear();
        TheStory.Children.Clear();
        CountText.Text = string.Empty;
    }

    private CorpusFolder Corpus() => _corpus
        ?? throw new InvalidOperationException(
            "The node screen was never given a corpus, so it has no nodes to read.");

    private Style Chrome(string named) => (Style)Root.Resources[named];

    /// <summary>
    /// What this screen says, in the language it is being read in. Every word on it comes through
    /// here, which is how a screen names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    private void Draw(Guid node)
    {
        _status.Nothing();

        if (Corpus().Folder is not { } folder)
        {
            _status.Says(UiTexts.TheCorpusCouldNotBeOpened, Corpus().Path);
            Render(null, [], hasMore: false);
            return;
        }

        try
        {
            using var context = CorpusDatabase.Open(folder);
            var path = new MeetingClassifying(context, TimeProvider.System).PathTo(node);
            var said = CorpusStatements.Under(context, node, HowMuchOfANodesStoryIsRead + 1);
            var (meetings, hasMore) = WholeMeetings(said);

            Render(path, meetings, hasMore);
        }
        catch (ClassificationException gone)
        {
            // A node somebody removed between the meeting screen drawing its chip and this press —
            // the same race ClassifyingAMeeting reports with the same sentence.
            _status.Says(UiTexts.ThatIsNoLongerHowItWas, gone.Message);
            Render(null, [], hasMore: false);
        }
        catch (Exception unreadable) when (ScreenFailures.Reportable(unreadable))
        {
            _status.Says(UiTexts.ThatDidNotGoThrough, unreadable.Message);
            Render(null, [], hasMore: false);
        }
    }

    /// <summary>Puts everything that was read onto the controls.</summary>
    private void Render(NodePath? read, IReadOnlyList<IReadOnlyList<Statement>> meetings, bool hasMore)
    {
        StatusText.Text = _status.In(_language);
        ThePath.Children.Clear();
        TheStory.Children.Clear();

        if (read is not { } path)
        {
            CountText.Text = string.Empty;
            return;
        }

        DrawThePath(path);

        CountText.Text = meetings.Count.ToString(UiLanguages.Culture(_language));

        if (meetings.Count == 0)
        {
            TheStory.Children.Add(new TextBlock
            {
                Text = In(UiTexts.NothingHasBeenSaidAboutThisYet),
                Style = Chrome("NothingSaidYet"),
            });

            return;
        }

        foreach (var meeting in meetings)
        {
            TheStory.Children.Add(MeetingCard(meeting));
        }

        if (hasMore)
        {
            TheStory.Children.Add(new TextBlock
            {
                Text = In(UiTexts.ThereIsMoreThanThisScreenShows),
                Style = Chrome("ThereIsMore"),
            });
        }
    }

    /// <summary>
    /// The path down the tree, as pills: every node but the last opens that node's own story, and
    /// the last is shown rather than pressed — you are already reading it.
    /// </summary>
    private void DrawThePath(NodePath path)
    {
        for (var index = 0; index < path.Nodes.Count; index++)
        {
            var here = path.Nodes[index];

            if (index == path.Nodes.Count - 1)
            {
                ThePath.Children.Add(new Border
                {
                    Style = Chrome("LastNode"),
                    Child = new TextBlock { Text = here.Name, Style = Chrome("PathPillSays") },
                });

                continue;
            }

            var node = here.Id;
            var pill = new Button
            {
                Style = Chrome("PathNode"),
                Content = new TextBlock { Text = here.Name, Style = Chrome("PathPillSays") },
            };

            pill.Click += (_, _) => Show(node);
            ThePath.Children.Add(pill);
        }
    }

    /// <summary>
    /// The read cut to whole meetings — one run per meeting, in the order the read returned
    /// them — so a card is never missing some of what its meeting settled.
    /// </summary>
    /// <remarks>
    /// One run per meeting because the read orders by <c>started_at, meeting_id</c> before
    /// anything else, so one meeting's rows are always contiguous; grouping by
    /// <see cref="Statement.MeetingId"/> and nothing more is what that ordering buys.
    /// <see cref="HowMuchOfANodesStoryIsRead"/> bounds rows and not meetings, so a cut can land
    /// inside one — the read brings one row past the bound for exactly this: where that row
    /// shares a meeting with the last run kept, that meeting is not whole here and its run is
    /// dropped rather than shown half full.
    /// </remarks>
    private static (IReadOnlyList<IReadOnlyList<Statement>> Meetings, bool HasMore) WholeMeetings(
        IReadOnlyList<Statement> said)
    {
        var hasMore = said.Count > HowMuchOfANodesStoryIsRead;

        var runs = said
            .Take(HowMuchOfANodesStoryIsRead)
            .GroupBy(statement => statement.MeetingId)
            .Select(run => (IReadOnlyList<Statement>)[.. run])
            .ToList();

        if (hasMore && runs.Count > 0 && said[HowMuchOfANodesStoryIsRead].MeetingId == runs[^1][0].MeetingId)
        {
            runs.RemoveAt(runs.Count - 1);
        }

        return (runs, hasMore);
    }

    private UIElement MeetingCard(IReadOnlyList<Statement> meeting)
    {
        var first = meeting[0];
        var named = !string.IsNullOrWhiteSpace(first.Title);
        var meetingId = first.MeetingId;

        var heading = new Button
        {
            Style = Chrome("MeetingHeadingButton"),
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = named ? first.Title : In(UiTexts.AMeetingNobodyHasNamed),
                        Style = Chrome("MeetingHeadingText"),
                    },
                    new TextBlock
                    {
                        Text = ScreenNumbers.At(first.StartedAt),
                        Style = Chrome("MeetingDateText"),
                    },
                },
            },
        };

        heading.Click += (_, _) => MeetingChosen?.Invoke(this, meetingId);

        var inside = new StackPanel { Spacing = 12 };
        inside.Children.Add(heading);

        foreach (var statement in meeting)
        {
            inside.Children.Add(OneStatement(statement));
        }

        return new Border { Style = Chrome("NodeCard"), Child = inside };
    }

    private UIElement OneStatement(Statement statement)
    {
        var row = new Grid { ColumnSpacing = 10 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var bullet = new Ellipse
        {
            Style = Bullet(statement.Kind),
            VerticalAlignment = VerticalAlignment.Center,
        };
        Grid.SetColumn(bullet, 0);
        row.Children.Add(bullet);

        var said = new TextBlock { Text = statement.Says, Style = Chrome("Said") };
        Grid.SetColumn(said, 1);
        row.Children.Add(said);

        var at = new TextBlock
        {
            Text = ScreenNumbers.Long(statement.At),
            Style = Chrome("SaidAt"),
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetColumn(at, 2);
        row.Children.Add(at);

        return row;
    }

    /// <summary>
    /// Which bullet a section's things take. The table is closed and the last arm stops rather
    /// than substituting, for <see cref="ReadingAMeeting"/>'s own reason: a kind added to
    /// <see cref="LeftKind"/> and not given a bullet here would otherwise be drawn under another
    /// section's colour. Each arm reaches <see cref="Chrome"/> by its own literal key rather than
    /// through a name computed once above, which is what lets the same sweep that catches every
    /// other misspelt key on this screen catch one here too.
    /// </summary>
    private Style Bullet(LeftKind kind) => kind switch
    {
        LeftKind.Decision => Chrome("BulletDecision"),
        LeftKind.Action => Chrome("BulletAction"),
        LeftKind.Question => Chrome("BulletQuestion"),
        _ => throw new InvalidOperationException($"This screen has no bullet for the section '{kind}'."),
    };

    private void OnBack(object sender, RoutedEventArgs e) => Left?.Invoke(this, EventArgs.Empty);
}
