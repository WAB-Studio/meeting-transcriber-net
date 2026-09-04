using System.Text.RegularExpressions;

namespace MeetingTranscriber.App.Tests;

/// <summary>
/// The meetings list re-reads itself every two seconds and rebuilds every card when it does. This
/// holds the structure that lets somebody keep their place across a rebuild they did not ask for.
/// </summary>
/// <remarks>
/// <para>
/// Read off the source, like every other check in this project: there is no
/// <c>ProjectReference</c> to the application, and a WinUI tree needs a UI thread and a packaged
/// host. So what runs here is the shape the fix is made of — every button carries an id, every id
/// is remembered, and the capture and the restore stand on the right sides of the rebuild — and
/// what runs by hand is whether the keyboard really comes back. Neither is the other's substitute:
/// the structure is what a later edit breaks silently, and this is where that shows up.
/// </para>
/// <para>
/// Every read goes through <see cref="SourceLines.StandsInACommentedLine"/>, because the rules
/// these hold are written down beside the code they govern and the sentence explaining a rule
/// names the thing the rule bans.
/// </para>
/// </remarks>
public sealed partial class KeepingAPlaceTests
{
    private static readonly string Drawer =
        File.ReadAllText(AppSources.At("MeetingTranscriber.App/MeetingsDrawer.xaml.cs").FullName);

    /// <summary>
    /// Every button this list builds is bound to a local, so the check below can see all of them.
    /// </summary>
    /// <remarks>
    /// A convention rather than a rule about behaviour, and it is here because it is what makes the
    /// rule about behaviour total. A button written straight into an <c>Add(new Button { … })</c>
    /// has no name to look for an id beside, so a guard over locals alone would pass over the one
    /// press somebody added carelessly — which is the whole failure that guard exists for.
    /// </remarks>
    [Fact]
    public void Every_press_this_list_draws_is_held_in_a_local()
    {
        var built = Occurrences("new Button").Count();
        var named = Buttons()
            .Matches(Drawer)
            .Count(match => !SourceLines.StandsInACommentedLine(Drawer, match.Index));

        // Without this both counts can be zero, which is exactly how a renamed constructor would
        // leave them and reads like a file with nothing wrong in it.
        built.ShouldBeGreaterThan(0, "no button is built in MeetingsDrawer, so this holds nothing.");

        named.ShouldBe(
            built,
            "MeetingsDrawer builds a button straight into the tree instead of into a local, so "
            + "nothing can check that it was given an id and a re-read will drop somebody's place "
            + "on it without anything looking wrong.");
    }

    /// <summary>
    /// Every button this list builds is given an id and remembered under it.
    /// </summary>
    /// <remarks>
    /// A sixth press added to a card without one is a press the next tick drops somebody's place
    /// on, and nothing about it looks wrong: it draws, it works, and only the person reading the
    /// list when the corpus changes ever finds out.
    /// </remarks>
    [Fact]
    public void Every_press_this_list_draws_is_one_it_can_find_again()
    {
        var unnamed = Buttons()
            .Matches(Drawer)
            .Where(match => !SourceLines.StandsInACommentedLine(Drawer, match.Index))
            .Select(match => match.Groups["press"].Value)
            .Where(press => !Occurrences($"KnownAs({press},").Any())
            .ToArray();

        unnamed.ShouldBeEmpty(
            "MeetingsDrawer builds presses it never hands to KnownAs, so they carry no id and a "
            + "re-read cannot give the keyboard back to them: " + string.Join("; ", unnamed));
    }

    /// <summary>
    /// Nothing sets an id anywhere but in <c>KnownAs</c>.
    /// </summary>
    /// <remarks>
    /// The other half of the rule above, and the failure it stops is the one that reads as working:
    /// an id set directly is one a tool can press and a redraw cannot find, because the press was
    /// never remembered under it. One act, one place.
    /// </remarks>
    [Fact]
    public void Nothing_on_this_list_carries_an_id_the_redraw_cannot_find()
    {
        var set = Occurrences("AutomationProperties.SetAutomationId(").ToArray();

        set.ShouldHaveSingleItem(
            "MeetingsDrawer sets an automation id somewhere other than in KnownAs, so a press "
            + "carries an id that nothing remembers it under.");

        Body("private void KnownAs(").ShouldContain(
            "AutomationProperties.SetAutomationId(",
            customMessage: "the one id this screen sets is set outside KnownAs, so the press it "
                + "names is never remembered and a re-read cannot give the keyboard back to it.");
    }

    /// <summary>
    /// The capture reads the old buttons and the restore reads the new ones, which is an order.
    /// </summary>
    /// <remarks>
    /// Every step of it fails silently when it moves, which is why all four are held rather than
    /// the two that look load-bearing. Hoisted above the loops, the restore looks an id up among no
    /// buttons at all. Moved below either clear, the capture reads a dictionary that has just been
    /// emptied and captures nothing. Either way the list goes on drawing correctly and nobody's
    /// place is ever kept again.
    /// </remarks>
    [Fact]
    public void The_place_is_taken_before_the_cards_go_and_given_back_once_they_are_all_there()
    {
        var render = Body("private void Render()");

        int At(string what)
        {
            var found = render.IndexOf(what, StringComparison.Ordinal);

            found.ShouldBeGreaterThanOrEqualTo(0, $"Render no longer carries `{what}`.");

            return found;
        }

        Occurrences("Cards.Children.Clear()").ShouldHaveSingleItem(
            "MeetingsDrawer empties the list in more than one place, so there is more than one "
            + "rebuild for a captured place to survive.");

        int[] order =
        [
            At("WhereTheyWere();"),
            At("_presses.Clear();"),
            At("Cards.Children.Clear();"),
            render.LastIndexOf("Cards.Children.Add(", StringComparison.Ordinal),
            At("PutThemBack(place);"),
        ];

        order.ShouldBe(
            order.Order().ToArray(),
            "Render takes somebody's place and gives it back in an order that keeps neither: the "
            + "capture has to read the presses of the last draw before either clear empties them, "
            + "and the restore has to run after the last card is on the list.");
    }

    /// <summary>
    /// This screen moves the keyboard from exactly one place.
    /// </summary>
    /// <remarks>
    /// It goes on drawing while it is collapsed behind the meeting screen and the classifier, so a
    /// second focus call here is a press taken off the screen somebody is actually on. What this
    /// holds is the spelling — <c>Focus(</c> — and the reason that is enough is that the one call
    /// there is stands behind a capture that already refused every press nobody can see.
    /// </remarks>
    [Fact]
    public void This_screen_takes_focus_in_one_place_only()
    {
        Occurrences(".Focus(").ShouldHaveSingleItem(
            "MeetingsDrawer moves the keyboard from more than one place, and it draws while it is "
            + "collapsed behind another screen — so one of them can take a press off the screen "
            + "somebody is on.");
    }

    /// <summary>Where <paramref name="what"/> stands in the drawer, ignoring prose about it.</summary>
    private static IEnumerable<int> Occurrences(string what)
    {
        for (var at = Drawer.IndexOf(what, StringComparison.Ordinal);
            at >= 0;
            at = Drawer.IndexOf(what, at + what.Length, StringComparison.Ordinal))
        {
            if (!SourceLines.StandsInACommentedLine(Drawer, at))
            {
                yield return at;
            }
        }
    }

    /// <summary>What one member of the drawer is made of, from its signature to its last line.</summary>
    /// <remarks>
    /// Held to the member and not to the file, because an order between four calls says nothing
    /// unless they are all in the same method — and because the house style writes the rule out in
    /// prose beside the code, so a sentence naming a call would otherwise be read as the call.
    /// The end is the first closing brace at the class's own indent, which is this member's.
    /// </remarks>
    private static string Body(string signature)
    {
        var opens = Drawer.IndexOf(signature, StringComparison.Ordinal);

        opens.ShouldBeGreaterThanOrEqualTo(0, $"MeetingsDrawer has no `{signature}`.");

        var closes = Drawer.IndexOf("\n    }", opens, StringComparison.Ordinal);

        closes.ShouldBeGreaterThan(opens, $"`{signature}` never ends.");

        return Drawer[opens..closes];
    }

    /// <summary>A button this screen builds into a card, by the local it is held in.</summary>
    [GeneratedRegex(@"var (?<press>\w+) = new Button")]
    private static partial Regex Buttons();
}
