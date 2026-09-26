using System.Globalization;

using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MeetingTranscriber.App;

/// <summary>
/// The one dialogue two screens share: adding a person the corpus does not have yet, and
/// correcting the name of one it already does.
/// </summary>
/// <remarks>
/// <para>
/// Moved out of <c>ClassifyingAMeeting</c> whole, which is where it used to live for the reason its
/// own remark gave: the caller that would make a control of its own worth anything is the screen
/// that names voices, and that screen was not built. It is built now, so this moved — an
/// abstraction for a caller that does not exist costs more than the duplication it saved, and a
/// second one is exactly what a third caller would have grown.
/// </para>
/// <para>
/// The write is its own and takes one of two paths. Adding a person is <see cref="HumanLayer.Add"/>
/// and <see cref="HumanLayer.Join(Person, Node, UtcTimestamp?)"/>, each of which saves, and a
/// refusal on the second would leave the first on disk with nothing pointing at it — so the two are
/// wrapped in one transaction here, the only opening of the corpus this file makes. Correcting a
/// name goes through <see cref="RenamingSomebody.Rename"/> instead, which opens its own transaction
/// around a rename and every render it forces, and a refusal there takes the rename back with it;
/// this dialogue owns none of that ladder and only reports what it answers.
/// </para>
/// <para>
/// The dialogue stays open on a refusal either way, because the alternative is losing what somebody
/// typed to a corpus that was locked for a second.
/// </para>
/// </remarks>
public sealed partial class AddingSomebody : ContentDialog
{
    private UiLanguage _language;
    private CorpusFolder? _corpus;
    private IReadOnlyList<Node> _organizations = [];
    private Person? _correcting;
    private Guid? _made;

    public AddingSomebody() => InitializeComponent();

    /// <summary>
    /// What this dialogue says, in the language it is being asked in. Every word on it comes
    /// through here, which is how it names what it says without carrying the words.
    /// </summary>
    public string In(UiText text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.In(_language);
    }

    /// <summary>
    /// Opens the dialogue over one place: adding a person when <paramref name="correcting"/> is
    /// nothing, or correcting theirs when it names somebody.
    /// </summary>
    /// <returns>
    /// The id of the person it wrote — theirs again when it was a correction — or nothing when the
    /// dialogue was left without writing anything.
    /// </returns>
    public async Task<Guid?> AskAsync(
        CorpusFolder corpus,
        UiLanguage language,
        XamlRoot over,
        IReadOnlyList<Node> organizations,
        Person? correcting)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(organizations);

        _corpus = corpus;
        _language = language;
        _organizations = organizations;
        _correcting = correcting;
        _made = null;

        Title = In(correcting is null ? UiTexts.AddSomebody : UiTexts.AboutThisPerson);
        TheirNameBox.Text = correcting?.DisplayName ?? string.Empty;
        TheirYearBox.Text = string.Empty;
        DialogueStatusText.Text = string.Empty;
        DialogueStatusText.Visibility = Visibility.Collapsed;

        // Nobody is added without a name, and the act says so by being dead rather than by
        // refusing afterwards: a form that takes a press and answers with a complaint is a form
        // that asked for the press. Correcting one opens with the name already in the box, so it
        // opens alive.
        IsPrimaryButtonEnabled = correcting is not null;

        // Where somebody belongs is not what this dialogue edits when it is correcting a name. An
        // affiliation is about a person across years and the screens that open this are each about
        // one meeting, so the two fields come off rather than standing there doing nothing.
        var asking = correcting is null ? Visibility.Visible : Visibility.Collapsed;
        TheirOrganizationLine.Visibility = asking;
        TheirYearBox.Visibility = asking;

        TheirOrganization.ItemsSource = (string[])
        [
            In(UiTexts.NoneOfThese),
            .. organizations.Select(node => node.Name),
        ];

        TheirOrganization.SelectedIndex = 0;

        // A dialogue declared in a screen's own markup is already in the window's tree and has its
        // root; one that is not would throw where it is shown, off a build with nothing wrong in it.
        if (XamlRoot is null)
        {
            XamlRoot = over;
        }

        await ShowAsync();
        return _made;
    }

    private void OnTheirNameTyped(object sender, TextChangedEventArgs e) =>
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(TheirNameBox.Text);

    /// <summary>
    /// Writes what the dialogue was asked for: a person the corpus does not have yet, or a
    /// correction of one it already does.
    /// </summary>
    private void OnSomebodyNamed(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);

        var name = (TheirNameBox.Text ?? string.Empty).Trim();

        // The act is dead until there is a name, so an empty one is not something a person did.
        if (name.Length == 0 || _corpus?.Folder is not { } folder)
        {
            args.Cancel = true;
            return;
        }

        try
        {
            if (_correcting is { } correcting)
            {
                var renamed = RenamingSomebody.Rename(folder, correcting, name, TimeProvider.System);

                if (renamed is null)
                {
                    args.Cancel = true;
                    Say(TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, correcting.DisplayName));
                    return;
                }

                _made = correcting.Id;
                return;
            }

            using var context = CorpusDatabase.Open(folder);
            using var writing = context.Database.BeginTransaction();

            var human = new HumanLayer(context, TimeProvider.System);
            var person = human.Add(name);

            var chosen = TheirOrganization.SelectedIndex - 1;
            var organization = chosen >= 0 && chosen < _organizations.Count ? _organizations[chosen] : null;

            if (organization is not null)
            {
                // No year is what Affiliation already means by no start — as far back as this
                // corpus goes — and not a guess at one.
                human.Join(person, organization, TheFirstOfTheYear(TheirYearBox.Text));
            }

            writing.Commit();
            _made = person.Id;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            args.Cancel = true;
            Say(TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message));
        }
    }

    private void Say(TextLine line)
    {
        DialogueStatusText.Text = line.In(_language);
        DialogueStatusText.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// The first instant of the year somebody typed, or nothing when they typed nothing readable.
    /// </summary>
    /// <remarks>
    /// A year and not a date, because that is the whole of what this dialogue asks about a period —
    /// and a start read back at a finer grain than it was asked for would be an invention. The
    /// instant is <see cref="ScreenNumbers.TheStartOfTheYear"/>'s and is deliberately not built
    /// here: what makes it right is that it is the exact inverse of the way the year is read back
    /// out beside the person, and two halves of one round trip written in two places is how one of
    /// them comes to be a midnight in the wrong zone.
    /// </remarks>
    private static UtcTimestamp? TheFirstOfTheYear(string? typed) =>
        int.TryParse((typed ?? string.Empty).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var year)
        && year is >= 1 and <= 9999
            ? ScreenNumbers.TheStartOfTheYear(year)
            : null;
}
