using System.Globalization;

using MeetingTranscriber.Domain.Meetings;
using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Meetings;
using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Presentation;
using MeetingTranscriber.Processing.Rendering;
using MeetingTranscriber.Recording;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

using Windows.System;

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
/// name goes through <see cref="RenamingSomebody.Rename"/> instead, which commits the rename on its
/// own and then renders each meeting it touches in a transaction of its own — this dialogue owns
/// none of that ladder and only reports what it answers.
/// </para>
/// <para>
/// A <see cref="RenderException"/> out of a correction is not the same answer as every other
/// refusal: the rename already landed by the time it can be thrown, so <c>OnSomebodyNamed</c> closes
/// on it exactly as it would a correction with nothing still owed, rather than cancelling — a
/// cancelled dialogue is what <c>ClassifyingAMeeting</c> and <c>SayingWhoIsWho</c> both read as
/// nothing having been written, which would no longer be true. Every other refusal keeps the
/// dialogue open, because the alternative is losing what somebody typed to a corpus that was locked
/// for a second.
/// </para>
/// <para>
/// There is a third answer, and only while adding: while the name is typed, up to three people the
/// corpus already holds under a name spelled nearly the same way are offered under the field, and
/// pressing one answers the dialogue with that person and writes nothing. <c>Guardar</c> still adds
/// the name as typed, so a name that is really somebody new is never refused for resembling
/// somebody else. Who is offered is <see cref="WhoTheyMightBe"/>'s and what it is read from is
/// <see cref="KnownPeople"/>'s; this only draws it, and is told by the screen that opened it what
/// that screen cannot take.
/// </para>
/// </remarks>
public sealed partial class AddingSomebody : ContentDialog
{
    private UiLanguage _language;
    private CorpusFolder? _corpus;
    private IReadOnlyList<Node> _organizations = [];
    private Person? _correcting;
    private Guid? _made;
    private PeopleAround _around = new([], []);
    private IReadOnlySet<Guid> _notOffered = new HashSet<Guid>();

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
        OpenedOver openedOver,
        UiLanguage language,
        XamlRoot over,
        IReadOnlyList<Node> organizations,
        Person? correcting)
    {
        ArgumentNullException.ThrowIfNull(corpus);
        ArgumentNullException.ThrowIfNull(openedOver);
        ArgumentNullException.ThrowIfNull(organizations);

        _corpus = corpus;
        _language = language;

        // The markup's one-time labels were evaluated when the page was built, before any language
        // was set, so they read Spanish under an English title. They are evaluated again now.
        Bindings.Update();

        _organizations = organizations;
        _correcting = correcting;
        _made = null;
        _around = new PeopleAround([], []);
        _notOffered = openedOver.NotOffered;
        SomebodyAlreadyHereList.Children.Clear();
        SomebodyAlreadyHereLine.Visibility = Visibility.Collapsed;

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

        if (correcting is null)
        {
            ReadWhoIsAround(openedOver);
        }

        // A dialogue declared in a screen's own markup is already in the window's tree and has its
        // root; one that is not would throw where it is shown, off a build with nothing wrong in it.
        if (XamlRoot is null)
        {
            XamlRoot = over;
        }

        await ShowAsync();
        return _made;
    }

    private void OnTheirNameTyped(object sender, TextChangedEventArgs e)
    {
        IsPrimaryButtonEnabled = !string.IsNullOrWhiteSpace(TheirNameBox.Text);
        OfferWhoTheyMightBe();
    }

    private void OnTheirOrganizationChosen(object sender, SelectionChangedEventArgs e) => OfferWhoTheyMightBe();

    /// <summary>
    /// Reads who the corpus holds once, when the dialogue opens, and lets go of the connection at
    /// once: what is ranked on every keystroke after this is in memory.
    /// </summary>
    private void ReadWhoIsAround(OpenedOver openedOver)
    {
        if (_corpus?.Folder is not { } folder)
        {
            return;
        }

        try
        {
            using var context = CorpusDatabase.OpenReadOnly(folder);
            _around = KnownPeople.Around(context, openedOver);
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            // The dialogue goes on with nobody to offer: adding somebody does not need the list.
            Say(TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message));
        }
    }

    /// <summary>
    /// Draws who the typed name might already be, or takes the line off when nobody is close. Only
    /// while adding: correcting a name is about somebody already chosen.
    /// </summary>
    private void OfferWhoTheyMightBe()
    {
        SomebodyAlreadyHereList.Children.Clear();

        if (_correcting is not null)
        {
            SomebodyAlreadyHereLine.Visibility = Visibility.Collapsed;
            return;
        }

        var chosen = TheirOrganization.SelectedIndex - 1;
        IReadOnlyCollection<Guid> organizations = chosen >= 0 && chosen < _organizations.Count
            ? [.. _around.Organizations, _organizations[chosen].Id]
            : _around.Organizations;

        var offered = WhoTheyMightBe.For(TheirNameBox.Text ?? string.Empty, _around.Known, organizations, _notOffered);

        foreach (var possible in offered)
        {
            // The one that earned the match when there is one, so two people of one name are told
            // apart by the organization this meeting stands under.
            var theirs = _around.Known.Single(known => known.Person.Id == possible.Person.Id).Organizations;
            var organization =
                _organizations.FirstOrDefault(node => theirs.Contains(node.Id) && organizations.Contains(node.Id))
                ?? _organizations.FirstOrDefault(node => theirs.Contains(node.Id));

            var pill = new Button
            {
                Style = (Style)Application.Current.Resources["PillButton"],
                Content = organization is null
                    ? possible.Person.DisplayName
                    : TextLine.Says(UiTexts.SomebodyAndWhereTheyBelong, possible.Person.DisplayName, organization.Name)
                        .In(_language),
            };

            var person = possible.Person.Id;
            pill.Click += (_, _) => AnswerWith(person);
            SomebodyAlreadyHereList.Children.Add(pill);
        }

        SomebodyAlreadyHereLine.Visibility = offered.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>
    /// Answers the dialogue with somebody the corpus already holds. Writes nothing: the person is
    /// already there, and the screen that asked puts them in the place it opened this from.
    /// </summary>
    private void AnswerWith(Guid person)
    {
        _made = person;
        IsPrimaryButtonEnabled = false;
        Hide();
    }

    /// <summary>
    /// Writes what the dialogue was asked for: a person the corpus does not have yet, or a
    /// correction of one it already does.
    /// </summary>
    private void OnSomebodyNamed(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        ArgumentNullException.ThrowIfNull(args);
        args.Cancel = !Commit();
    }

    /// <summary>
    /// Enter in the name box commits the dialogue the way the primary button does — the node pill
    /// on <c>ClassifyingAMeeting</c> commits on Enter too, and two pills on one screen should not
    /// commit differently. A dead primary button stays dead: an empty name is not committed.
    /// </summary>
    private void OnTheirNameKey(object sender, KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (e.Key is not VirtualKey.Enter)
        {
            return;
        }

        e.Handled = true;

        if (IsPrimaryButtonEnabled && Commit())
        {
            // Dead before it closes, so a second Enter before the dialogue is gone commits nothing.
            IsPrimaryButtonEnabled = false;
            Hide();
        }
    }

    /// <summary>
    /// Writes what the dialogue was asked for. True when the dialogue may close, false when it stays
    /// open having said why.
    /// </summary>
    private bool Commit()
    {
        var name = (TheirNameBox.Text ?? string.Empty).Trim();

        // The act is dead until there is a name, so an empty one is not something a person did.
        if (name.Length == 0 || _corpus?.Folder is not { } folder)
        {
            return false;
        }

        try
        {
            if (_correcting is { } correcting)
            {
                Guid? made;
                try
                {
                    made = RenamingSomebody.Rename(folder, correcting, name, TimeProvider.System)?.Id;
                }
                catch (RenderException)
                {
                    // RenamingSomebody.Rename's own contract: the rename itself always lands before
                    // this can be thrown, and only some of the meetings it touches are still owed a
                    // render — caught up by the next launch, which the exception says and
                    // this dialogue lets go of here. That is not this dialogue's
                    // failure to report: the corpus already reads the new name, so `made` closes the
                    // same way a rename with nothing still owed does. Falling to the catch below
                    // instead would tell whoever asked that nothing was written, while
                    // ClassifyingAMeeting and SayingWhoIsWho both redraw a `null` answer as though
                    // the dialogue never wrote anything — which is no longer true.
                    made = correcting.Id;
                }

                if (made is null)
                {
                    Say(TextLine.Says(UiTexts.ThatIsNoLongerHowItWas, correcting.DisplayName));
                    return false;
                }

                _made = made;
                return true;
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
            return true;
        }
        catch (Exception refused) when (ScreenFailures.Reportable(refused))
        {
            Say(TextLine.Says(UiTexts.ThatDidNotGoThrough, refused.Message));
            return false;
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
