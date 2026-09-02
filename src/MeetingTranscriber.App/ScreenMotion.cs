using System.Runtime.CompilerServices;

using MeetingTranscriber.Presentation;

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Animation;

using Windows.UI.ViewManagement;

namespace MeetingTranscriber.App;

/// <summary>
/// How a screen moves between two arrangements: what <see cref="Movement"/> says the move is
/// worth, applied to the one element that is coming or going.
/// </summary>
/// <remarks>
/// <para>
/// The split is deliberate and it is where the provable half of ISC-173.3 is. <b>How long</b> a
/// move takes, and whether the platform asked for none, is <see cref="Movement"/> in
/// <c>MeetingTranscriber.Presentation</c>, which references nothing and which a build agent runs
/// against both answers. <b>What actually moves</b> is here, needs a WinUI tree, and is what a
/// person or the UI probe sees.
/// </para>
/// <para>
/// <c>docs/design.md</c> §Movement: entering decelerates and leaving accelerates, which is the
/// platform's own grammar and reads as weight rather than as an effect. Nothing eases both ways,
/// nothing bounces and nothing overshoots — so a cubic ease in one direction each, and no
/// <c>EasingMode.EaseInOut</c>, <c>BounceEase</c>, <c>ElasticEase</c> or <c>BackEase</c> anywhere.
/// <c>OlivoTests</c> reads the screens for those and fails the build over one.
/// </para>
/// </remarks>
internal static class ScreenMotion
{
    /// <summary>
    /// What Windows was asked for, read fresh every time rather than kept. There is no event that
    /// says this changed, so a value cached when the window opened would go on animating for the
    /// rest of the session after somebody turned animations off — which is precisely the person
    /// the setting exists for.
    /// </summary>
    private static readonly UISettings TheseSettings = new();

    /// <summary>
    /// Where each element that has been moved is heading, and the storyboard taking it there.
    /// </summary>
    /// <remarks>
    /// A screen cannot read that off <c>Visibility</c>, and this exists because it tried. An
    /// element on its way out is still <c>Visible</c> for the whole 300 ms, so a second press
    /// inside that window asks "is it showing?", is told yes, concludes nothing needs to change and
    /// starts nothing — and then the first storyboard finishes and settles to where it was going.
    /// Pressing the drawer's button twice quickly left the recorder half collapsed with the drawer
    /// docked, which is a screen in neither of its two arrangements and no way back but a third
    /// press. What an element is doing is the thing that knows it, so it is kept here.
    /// <para>
    /// A <see cref="ConditionalWeakTable{TKey,TValue}"/> so an element that goes away takes its
    /// entry with it: this is static and outlives every window, and a dictionary keyed on controls
    /// would be a leak with a screen's whole tree hanging off it.
    /// </para>
    /// </remarks>
    private static readonly ConditionalWeakTable<FrameworkElement, Heading> Headings = new();

    /// <summary>Where one element is going, and what is taking it there.</summary>
    private sealed class Heading
    {
        public bool Arriving { get; set; }

        public Storyboard? Moving { get; set; }
    }

    /// <summary>How long each of the four moves is worth on this machine, as it stands now.</summary>
    public static Movement Now => new(TheseSettings.AnimationsEnabled);

    /// <summary>
    /// Brings <paramref name="element"/> in or takes it out, over the length
    /// <paramref name="move"/> is worth. Whichever way it goes it ends up where it was going: on a
    /// machine asked for no animation it is simply already there.
    /// </summary>
    /// <remarks>
    /// The height is what travels, because that is what says the drawer rose rather than that
    /// something faded: the meetings take the room the recorder half gives up, and a reader
    /// follows the one into the other. Opacity goes with it so the words do not sit on top of each
    /// other at the ends of the move.
    /// <para>
    /// A move worth nothing is not an animation of no length — it is no animation. A storyboard of
    /// zero duration still finishes on a later turn of the loop, so the arrangement would arrive
    /// one frame after the press on the machine whose whole request was that nothing be waited
    /// for, and anything reading the screen straight afterwards would read the old one. Setting the
    /// end state and returning is what makes standing still exact.
    /// </para>
    /// </remarks>
    public static void ArriveOrLeave(FrameworkElement element, bool arriving, Move move)
    {
        ArgumentNullException.ThrowIfNull(element);

        var heading = Headings.GetOrCreateValue(element);

        // Whatever it was doing, it is doing this now. Stopped rather than left to finish, because
        // a storyboard that ended after the one replacing it began would drive the element back to
        // where the last press was going.
        heading.Moving?.Stop();
        heading.Moving = null;
        heading.Arriving = arriving;

        var milliseconds = Now.Milliseconds(move);

        if (milliseconds == 0)
        {
            Settle(element, arriving);
            return;
        }

        // Its own height, and not a number written down here: what the recorder half is worth is
        // whatever is in it at the moment it is asked, which changes with the language, the
        // pickers and whether a meeting is being saved.
        //
        // Laid out for real rather than measured by hand. `Measure` needs an available width, and
        // the width an element that is currently collapsed reports is nought — so measuring it
        // meant offering infinite width, under which every wrapping line in it comes out on one
        // line and the height to travel to is short by however much the text would have wrapped.
        // Making it visible and running a layout pass asks the same question with the real width
        // in it.
        element.MaxHeight = double.PositiveInfinity;
        element.Opacity = arriving ? 0 : 1;
        element.Visibility = Visibility.Visible;
        element.UpdateLayout();

        var full = element.ActualHeight;
        if (full <= 0)
        {
            Settle(element, arriving);
            return;
        }

        var travel = new DoubleAnimation
        {
            From = arriving ? 0 : full,
            To = arriving ? full : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EnableDependentAnimation = true,
            EasingFunction = new CubicEase
            {
                EasingMode = arriving ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };

        Storyboard.SetTarget(travel, element);
        Storyboard.SetTargetProperty(travel, nameof(FrameworkElement.MaxHeight));

        var fade = new DoubleAnimation
        {
            From = arriving ? 0 : 1,
            To = arriving ? 1 : 0,
            Duration = new Duration(TimeSpan.FromMilliseconds(milliseconds)),
            EasingFunction = new CubicEase
            {
                EasingMode = arriving ? EasingMode.EaseOut : EasingMode.EaseIn,
            },
        };

        Storyboard.SetTarget(fade, element);
        Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));

        var moving = new Storyboard { Children = { travel, fade } };

        // Settled at the end whichever way it went, so the element is never left holding the
        // ceiling the animation drove it to: one kept at the height it had when it arrived would
        // stop growing the next time anything in it got longer.
        //
        // And only by the storyboard that is still the current one. `Stop()` above raises
        // `Completed` on the one it interrupts, so without this the press that superseded a move
        // would be undone by the move it superseded, one frame after it started.
        moving.Completed += (_, _) =>
        {
            if (!ReferenceEquals(heading.Moving, moving))
            {
                return;
            }

            heading.Moving = null;

            // Stopped before it is settled, and this is the whole reason `Settle` works at all. A
            // storyboard's default `FillBehavior` is `HoldEnd`: when it finishes it goes on holding
            // the value it drove the property to, and a hold outranks anything the code sets
            // afterwards. So `MaxHeight` stayed pinned at whatever the move ended on — nought,
            // after a leave — and the next arrival measured under a nought ceiling, found no height
            // to travel to, and settled the recorder half visible and zero pixels tall. It never
            // came back. `Stop` releases the hold; `FillBehavior.Stop` on the animations would do
            // it too and would snap the property to its local value a frame before this runs, which
            // is a flash of the half at full height on its way out.
            //
            // `Stop` raises `Completed` again. The check above is what makes that turn harmless.
            moving.Stop();
            Settle(element, arriving);
        };

        heading.Moving = moving;
        moving.Begin();
    }

    /// <summary>
    /// Whether <paramref name="element"/> is on screen or on its way there — which is not the same
    /// question as whether it is visible, and is the one a screen deciding what to do next has.
    /// </summary>
    public static bool IsShowing(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        // Its own visibility until something has moved it, so the first reading of a screen that
        // has never been rearranged is what the markup says rather than a default.
        return Headings.TryGetValue(element, out var heading)
            ? heading.Arriving
            : element.Visibility == Visibility.Visible;
    }

    /// <summary>
    /// Puts <paramref name="element"/> in the arrangement it was going to, with nothing left over
    /// from having travelled there.
    /// </summary>
    private static void Settle(FrameworkElement element, bool arriving)
    {
        element.MaxHeight = double.PositiveInfinity;
        element.Opacity = arriving ? 1 : 0;
        element.Visibility = arriving ? Visibility.Visible : Visibility.Collapsed;
    }
}
