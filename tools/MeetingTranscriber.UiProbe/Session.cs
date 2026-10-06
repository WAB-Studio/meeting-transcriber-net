using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows.Automation;

namespace MeetingTranscriber.UiProbe;

/// <summary>What one screen was, at the moment it was looked at.</summary>
/// <remarks>
/// Both artifacts, and neither of them written down. Where they go is the host's: the command line
/// files them under a name somebody chose, the server hands them straight back inside the turn
/// that asked. A core that wrote files would have made the second host read what it had just
/// caused to be written, which is a round trip through the disk to move bytes between two methods.
/// </remarks>
internal sealed record Screen(string Tree, byte[] Picture, string Size);

/// <summary>
/// What a start is refused for before anything of anybody's is touched, and the answers it worked
/// out on the way: which checkout and package this is, and whether the copy of the tool answering
/// is the one the checkout describes.
/// </summary>
/// <remarks>
/// Separate from <see cref="Session.Open"/> because of what sits between them in both hosts: the
/// probe's home, which makes a folder and runs a migration in it. A start into a checkout with
/// nothing registered, or out of a published copy that is owed a publish, is the commonest refusal
/// this tool gives — and it should not have made a folder or run a migration on its way to saying
/// so. Neither answer costs anything to carry: <c>Repository.Around</c>
/// asks Windows for every package this user has, so asking it twice would be both slow and a chance
/// for the two answers to differ.
/// </remarks>
internal sealed record Bearings(Repository Repository, ProbeBuild Probe)
{
    internal static Bearings Taken()
    {
        // Which checkout and which package first, because nothing below can be said without them,
        // and then this tool's own age: an answer out of a build older than the sources behind it
        // is a stale answer about the window, about the registration and about staleness itself.
        var repository = Repository.Around();

        var probe = ProbeBuild.Running(repository);
        probe.MustNotPredateItsSources();

        return new Bearings(repository, probe);
    }
}

/// <summary>
/// One running application, and everything that can be done to it.
/// </summary>
/// <remarks>
/// <para>
/// This is what both hosts are hosts over. It says nothing about how it was asked and nothing
/// about who is listening: every verb answers with what it did — a screen, the name of the window
/// arrived at, or nothing — and the host decides whether that becomes a file, a line of output or
/// a turn.
/// </para>
/// <para>
/// It owns the application, so disposing it closes what it started. That is the whole of the
/// lifetime for the command line, which opens one and walks a script; the server opens one and
/// keeps it across turns, which is what <see cref="MustStillBeUsable"/> is for.
/// </para>
/// </remarks>
internal sealed class Session : IDisposable
{
    /// <summary>
    /// A hedge, and named as one. A screen answers a press on its own thread and the answer is
    /// not there when <c>Invoke</c> returns; this makes the common small change likely to have
    /// landed and guarantees nothing. <see cref="Wait"/> is the only thing in this tool that
    /// synchronises, and a press whose effect is about to be looked at needs one.
    /// </summary>
    private static readonly TimeSpan HedgeAfterAPress = TimeSpan.FromMilliseconds(250);

    private static readonly TimeSpan ForAList = TimeSpan.FromSeconds(5);

    /// <summary>Enough of a list to say what was there instead, rather than every entry of it.</summary>
    private const int EnoughOfAList = 24;

    /// <summary>
    /// How far one list is asked before the answer is taken for a provider that will not end.
    /// Every list this drives is a picker somebody reads down.
    /// </summary>
    private const int MostItems = 2000;

    private static readonly TimeSpan ForAScreen = TimeSpan.FromSeconds(15);

    /// <summary>How long a launch has to say which home it resolved, once it has a window.</summary>
    private static readonly TimeSpan ToBeConfirmed = TimeSpan.FromSeconds(10);

    /// <summary>How long <see cref="Hover"/> holds the pointer, which is long enough for a tooltip.</summary>
    private static readonly TimeSpan HeldOver = TimeSpan.FromSeconds(2);

    /// <summary>How often <see cref="Hover"/> reads the cursor.</summary>
    private static readonly TimeSpan Sampled = TimeSpan.FromMilliseconds(50);

    /// <summary>How long <see cref="Hover"/> lets the window answer a move before it reads the cursor.</summary>
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(100);

    /// <summary>The pause between two moves of a drag, so the application sees a drag and not a jump.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(20);

    /// <summary>How many moves a drag is made of.</summary>
    private const int Steps = 12;

    private readonly LaunchedApp _app;

    private readonly Freshness _freshness;

    private readonly ProbeBuild _probe;

    private Session(LaunchedApp app, Freshness freshness, ProbeBuild probe)
    {
        _app = app;
        _freshness = freshness;
        _probe = probe;
    }

    /// <summary>
    /// Which application this is driving, and what it is running — plus anything about this
    /// session somebody has to know before they use it. Today that is only a refused leash, and
    /// it goes here because this line is the one thing both hosts say when an application opens.
    /// </summary>
    internal string StartedAs =>
        $"{_app.AppUserModelId} is process {_app.ProcessId}, from {_app.RunningFrom}"
        + (_app.Unleashed is null ? string.Empty : Environment.NewLine + _app.Unleashed);

    /// <summary>
    /// Starts the application and waits for it to have a window with something on it. The
    /// freshness check is inside the opening rather than beside it: a window of the wrong build
    /// reads exactly like a window, so nothing may be done to one before it has been refused.
    /// </summary>
    internal static Session Open(Bearings bearings, ProbeCorpus home)
    {
        ArgumentNullException.ThrowIfNull(bearings);
        ArgumentNullException.ThrowIfNull(home);

        var repository = bearings.Repository;

        home.ForgetWhatWasReported();
        var app = LaunchedApp.Start(repository.AppUserModelId, home.LaunchArguments);

        try
        {
            // Whose window is this, and is it this old — in that order, because the second
            // question has no meaning until the first one is answered.
            repository.MustBeWhatWindowsStarted(app.RunningFrom);

            var freshness = Freshness.Of(repository, app.RunningFrom);
            freshness.MustNotPredateTheCode();

            app.OpenAWindow();

            // The home the application resolved is the one it was asked for, or nothing is done to
            // it. A launch line that did not arrive leaves the application on the owner's own
            // corpus and nothing on its screen says so; the report is the only place the
            // difference shows, and its absence is refused here, before any verb exists.
            _ = Patience.Until(ToBeConfirmed, () => home.HasBeenConfirmed() ? home : null)
                ?? throw new ProbeFailed(
                    $"The application did not report running in {home.Folder}, so it was started "
                    + "without being told its home and would be driving the owner's own corpus. It "
                    + "was closed and nothing was done to it.");

            // In front from the first moment, and not at the first verb: the window a person sees
            // open behind a browser is the one that was never going to be pressed into.
            Foreground.Hold(app.Windows, app.Windows.Active());

            return new Session(app, freshness, bearings.Probe);
        }
        catch
        {
            // An application started and then refused is still an application running.
            app.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Asked again before every instruction, because a session outlives a build and a publish. The
    /// order is the whole of it: this tool, then whether there is an application, then whether the
    /// window is old. This tool first because a copy older than its own sources is stale about the
    /// window, about the application and about staleness itself — the one refusal that has to be
    /// said before any other can be believed.
    /// </summary>
    internal void MustStillBeUsable()
    {
        _probe.MustNotPredateItsSources();

        if (_app.HasGone)
        {
            throw new ProbeFailed(
                "The application is not running any more — it was closed, or it crashed"
                + $"{_app.ExitedWith}. Start it again.");
        }

        _freshness.MustNotPredateTheCode();
    }

    /// <summary>Whether there is still an application here at all.</summary>
    internal bool HasGone => _app.HasGone;

    /// <summary>
    /// The screen the verb is about, with its popups, after the window has been brought in front.
    /// Every verb starts here and none of them asks the window for itself: two verbs disagreeing
    /// about which window they acted on is the failure <see cref="AppWindows"/> exists against, and
    /// one disagreeing about whether it was in front is the same failure said differently.
    /// </summary>
    private IReadOnlyList<AutomationElement> Front()
    {
        var window = _app.Windows.Active();
        Foreground.Hold(_app.Windows, window);

        return _app.Windows.Scope(window);
    }

    /// <summary>
    /// Both artifacts come off one screen, and the picture is a copy of the desktop over it: the
    /// window is brought in front first, so what is photographed is the screen and not whatever
    /// was covering it, and its popups are part of the copy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The tree is read first, and the order is the whole of why: the two halves fail
    /// independently — a window can print nothing but its frame while its tree reads whole, which
    /// is what <see cref="ScreenWouldNotBePhotographed"/> was measured against — and taking the
    /// picture first threw the readable half away on the way out.
    /// </para>
    /// <para>
    /// So it is one window and not one moment, and on a screen that changes by the second they can
    /// be a long way apart: the picture is retried inside <see cref="WindowPicture"/>'s draw
    /// budget, which is ten seconds, and this change widened what that budget absorbs. The tree
    /// carries its own instant in its header and the picture carries none, so what a pair is
    /// evidence about is the tree's moment, with the picture at or after it. That is the right way
    /// round for the walks these files come from, which read the tree and cite it — a clock on the
    /// picture running ahead of the tree beside it is the gap and not a fault on the screen.
    /// </para>
    /// </remarks>
    /// <exception cref="ScreenWouldNotBePhotographed">
    /// The window would not be photographed. The tree is on it.
    /// </exception>
    internal Screen See()
    {
        var scope = Front();
        var tree = UiTree.Render(scope);

        try
        {
            var picture = WindowPicture.Of(scope[0], [.. scope.Skip(1).Select(AppWindows.Handle)]);

            return new Screen(tree, picture.Png, picture.Size);
        }
        catch (ProbeFailed noPicture)
        {
            throw new ScreenWouldNotBePhotographed(tree, noPicture.Message);
        }
    }

    /// <summary>The tree alone, which is what a screen that has just changed is asked for.</summary>
    internal string Tree() => UiTree.Render(Front());

    /// <summary>
    /// Ends the application the way a crash does, with nothing asked and nothing let finish.
    /// </summary>
    /// <remarks>
    /// The only verb here that answers with nothing about a screen. Why it is a verb at all, and
    /// why the death is waited for, is <see cref="LaunchedApp.Kill"/>. Disposing this afterwards
    /// is still correct and still what the host does: it finds an application already gone.
    /// </remarks>
    internal void Kill() => _app.Kill();

    /// <summary>
    /// Pressing is <c>Invoke</c> and nothing else. A control that offers something else instead —
    /// a switch that toggles, a list that expands — is named along with what it does offer, so the
    /// screen that first needs one says which verb to add rather than this guessing on its behalf.
    /// Disabled is checked first because a dead button is the defect this tool exists to find, and
    /// finding it should read as a sentence and not as a stack trace.
    /// </summary>
    internal void Press(string target)
    {
        var element = Search.One(Front(), target);

        if (Reading.Flag(() => element.Current.IsEnabled) == false)
        {
            throw new ProbeFailed($"{ElementWords.Line(element)} is disabled: it cannot be pressed.");
        }

        if (!Search.Supports(element, InvokePattern.Pattern))
        {
            throw new ProbeFailed(
                $"{ElementWords.Line(element)} is not something that can be pressed. "
                + $"What it offers instead: {Offers(element)}.");
        }

        ((InvokePattern)element.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        Thread.Sleep(HedgeAfterAPress);
    }

    /// <summary>
    /// The value is set rather than typed key by key: what a probe wants in the field is what
    /// somebody meant to leave there, not the six intermediate strings on the way. Held to the
    /// same three questions <see cref="Press"/> asks — is it dead, does it take this at all, and
    /// if not what does it take — because a field that silently refused would be a screen this
    /// tool said nothing about.
    /// </summary>
    internal void Type(string target, string text)
    {
        var element = Search.One(Front(), target);

        if (Reading.Flag(() => element.Current.IsEnabled) == false)
        {
            throw new ProbeFailed($"{ElementWords.Line(element)} is disabled: nothing can be typed into it.");
        }

        if (!Search.Supports(element, ValuePattern.Pattern))
        {
            throw new ProbeFailed(
                $"{ElementWords.Line(element)} does not take a value. "
                + $"What it offers instead: {Offers(element)}.");
        }

        var field = (ValuePattern)element.GetCurrentPattern(ValuePattern.Pattern);
        if (Reading.Flag(() => field.Current.IsReadOnly) != false)
        {
            throw new ProbeFailed($"{ElementWords.Line(element)} is read only.");
        }

        field.SetValue(text);
        Thread.Sleep(HedgeAfterAPress);
    }

    /// <summary>
    /// Sends one key to a control, the way a keyboard does.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="Type"/> cannot do this and is not a near miss: <c>ValuePattern.SetValue</c> puts
    /// the right text in the field with no key ever going down, so a control that commits on Enter
    /// never hears one and a screen made of those closes on nothing. That is why <c>Clasificar</c>
    /// could not be driven and had to be reasoned about instead.
    /// </para>
    /// <para>
    /// Focus and then the queue, which is the only route a XAML screen reads keys off. A posted
    /// <c>WM_KEYDOWN</c> reaches the window procedure and no handler on the screen, and there is no
    /// automation pattern for a keystroke at all — so this is the one verb that goes around UI
    /// Automation, and it asks the same first question every other verb asks, because a key sent at
    /// a dead control is a step that did nothing and said so nowhere.
    /// </para>
    /// <para>
    /// It does not ask whether the control takes a value. What a key means is the control's — Enter
    /// on a button is a press and on a field is a commit — and a probe deciding for it would refuse
    /// the half of this screen the verb was added for.
    /// </para>
    /// <para>
    /// Focus is <em>checked</em> and not merely asked for, which is the whole of what makes this
    /// verb worth having. <c>SendInput</c> answers with how many events Windows put in the queue and
    /// never with where they went, and the queue belongs to whatever has the keyboard — so a
    /// <c>SetFocus</c> the foreground lock refused returns quietly, the count comes back right, and
    /// Enter lands in whatever a person was typing in while this says it committed a field. A step
    /// that ran, said <em>done</em> and proved nothing is the one failure this tool exists not to
    /// have, so the answer to <em>did it take</em> is read back off the element before a key is sent.
    /// </para>
    /// <para>
    /// Every verb now has the application in front, and this is the one that could not have worked
    /// without: a key goes to the focused window or it goes nowhere useful.
    /// </para>
    /// </remarks>
    internal void Key(string target, string key)
    {
        // The name before the element, so a typo costs nothing and reads the same from either
        // host: the command line refuses it before the application starts, and the server, which
        // has no script to check, refuses it here.
        var code = Instruction.KeyNamed(key);

        var element = Search.One(Front(), target, takingTheKeyboard: true);

        if (Reading.Flag(() => element.Current.IsEnabled) == false)
        {
            throw new ProbeFailed($"{ElementWords.Line(element)} is disabled: no key reaches it.");
        }

        try
        {
            element.SetFocus();
        }

        // Both ways it refuses: `InvalidOperationException` for an element that cannot take focus,
        // `ElementNotAvailableException` for one that has gone — which is ordinary on a screen that
        // redraws on every change, and would otherwise leave this verb as a stack trace where every
        // other one answers in a sentence.
        catch (Exception cannot)
            when (cannot is InvalidOperationException or ElementNotAvailableException)
        {
            throw new ProbeFailed(
                $"{ElementWords.Line(element)} would not take focus, so a key sent now would go to "
                + $"whatever has it instead: {cannot.Message}");
        }

        // Asked of the element rather than assumed of the call. `SetFocus` is best effort: Windows'
        // foreground lock refuses a process that is not already in front, and a provider that does
        // not implement focus returns without doing anything — neither throws, and both leave the
        // keyboard where it was.
        if (Reading.Flag(() => element.Current.HasKeyboardFocus) != true)
        {
            throw new ProbeFailed(
                $"{ElementWords.Line(element)} does not have the keyboard, so the \"{key}\" key "
                + "would go wherever the keyboard is. Bring the application in front and try again; "
                + "a control that never takes focus wants press or type instead.");
        }

        Native.Input[] both =
        [
            new()
            {
                Type = Native.InputKeyboard,
                Union = new Native.InputUnion { Keyboard = new Native.KeyboardInput { wVk = code } },
            },
            new()
            {
                Type = Native.InputKeyboard,
                Union = new Native.InputUnion
                {
                    Keyboard = new Native.KeyboardInput { wVk = code, dwFlags = Native.KeyUp },
                },
            },
        ];

        // Both events in one call, because SendInput guarantees nothing else is interleaved
        // between the events of one call and two calls have no such promise — a key that goes down
        // and stays down is a screen nothing afterwards is true of.
        if (Native.SendInput((uint)both.Length, both, Native.InputSize) != both.Length)
        {
            // What Windows said and not a guess at which of them it was: input blocked, a secure
            // desktop, a locked workstation and a window of higher integrity in front all look
            // identical from here, and naming one of the four is naming the wrong one three times
            // in four.
            throw new ProbeFailed(
                $"Windows would not take the \"{key}\" key for {ElementWords.Line(element)}, and "
                + $"said {Marshal.GetLastWin32Error()}. Something has the input queue: a UAC "
                + "prompt, a locked workstation, or an application running higher than this one.");
        }

        Thread.Sleep(HedgeAfterAPress);
    }

    /// <summary>
    /// A list has to be open before what is in it exists: the items of a closed combo box are not
    /// in the tree, so the container is expanded, the item is waited for, and the list is shut
    /// again — which leaves the screen as somebody choosing would have left it. The element the
    /// wait found is the one selected: looking it up a second time is a second chance for a
    /// light-dismissing popup to have shut in between.
    /// </summary>
    internal void Choose(string container, string item)
    {
        var list = Search.One(Front(), container);

        var opens = Search.Supports(list, ExpandCollapsePattern.Pattern)
            ? (ExpandCollapsePattern)list.GetCurrentPattern(ExpandCollapsePattern.Pattern)
            : null;
        opens?.Expand();

        var chosen = Patience.Until(ForAList, () =>
            Search.Among(Offered(list), item) is [var only] ? only : null);

        if (chosen is null)
        {
            // Read while the list is still open and only then shut it. The items of a closed combo
            // box are not in the tree at all, so building this message after the collapse — which
            // is what it did until it was run against a list that really did not have the item —
            // named the list and then listed nothing, on the one failure whose whole job is to say
            // what the caller should have asked for instead.
            var all = Offered(list);
            var offered = string.Join(
                Environment.NewLine + "  ",
                all.Take(EnoughOfAList).Select(ElementWords.Line));
            var rest = all.Count > EnoughOfAList
                ? $"{Environment.NewLine}  ... and {all.Count - EnoughOfAList} more"
                : string.Empty;

            opens?.Collapse();

            throw new ProbeFailed(
                $"\"{item}\" is not one thing in {ElementWords.Line(list)}. What is in it:"
                + Environment.NewLine + "  " + offered + rest);
        }

        Realise(chosen);
        // A pick that moves something on the screen is answered by the screen disabling the list
        // while it does, and UI Automation then refuses the call it was in the middle of: measured
        // on channel 0's picker mid-recording, where the pick moves the channel, the move happened
        // and Select still threw. The pick counts only when the list is disabled afterwards; a list
        // that is still enabled is a refusal that means what it says.
        TakenByTheScreen(list, () =>
            ((SelectionItemPattern)chosen.GetCurrentPattern(SelectionItemPattern.Pattern)).Select());

        if (opens is not null
            && Reading.Flag(() => opens.Current.ExpandCollapseState == ExpandCollapseState.Expanded) == true)
        {
            TakenByTheScreen(list, opens.Collapse);
        }

        Thread.Sleep(HedgeAfterAPress);
    }

    private static void TakenByTheScreen(AutomationElement list, Action act)
    {
        try
        {
            act();
        }
        catch (ElementNotEnabledException) when (Reading.Flag(() => list.Current.IsEnabled) == false)
        {
            // The list went dark because of the act itself; see the call in Choose.
        }
    }

    /// <summary>
    /// Everything the open list offers, drawn or not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A list long enough to need scrolling draws a window of itself and no more, and what is not
    /// drawn is not in the automation tree — so a walk finds whatever the list happens to be
    /// scrolled to and calls the rest absent. That is this tool's answer being decided by a scroll
    /// position, and it is why a screen was once asked to stop virtualising a picker so that this
    /// could read it: the wrong half paying for a hole in the half that is only looking.
    /// </para>
    /// <para>
    /// <see cref="ItemContainerPattern"/> is what a list implements to be asked about items it has
    /// not drawn, and what it hands back carries the name whether or not it is on screen. A list
    /// that does not implement it is walked exactly as before, which is what every short picker on
    /// every screen here goes on doing.
    /// </para>
    /// </remarks>
    private static List<AutomationElement> Offered(AutomationElement list)
    {
        if (!Search.Supports(list, ItemContainerPattern.Pattern))
        {
            return Search.Everything(list, SelectionItemPattern.Pattern);
        }

        var container = (ItemContainerPattern)list.GetCurrentPattern(ItemContainerPattern.Pattern);
        var offered = new List<AutomationElement>();

        // A ceiling and not a trust: this walks as far as the application says it goes, and a
        // provider answering its own last item with itself would otherwise never come back.
        for (AutomationElement? found = null; offered.Count < MostItems;)
        {
            var previous = found;
            found = Reading.Of(() => container.FindItemByProperty(previous, null, null));
            if (found is null)
            {
                break;
            }

            offered.Add(found);
        }

        return offered;
    }

    /// <summary>
    /// Draws the one item that was chosen, which is what makes it selectable — an item a list
    /// never drew supports nothing else. Before the pattern that acts on it, never after.
    /// </summary>
    private static void Realise(AutomationElement item)
    {
        if (Search.Supports(item, VirtualizedItemPattern.Pattern))
        {
            ((VirtualizedItemPattern)item.GetCurrentPattern(VirtualizedItemPattern.Pattern)).Realize();
        }
    }

    /// <summary>
    /// Across every window, because the thing being waited for is usually on one that did not
    /// exist when the press happened — and the window it turns up on becomes the screen the rest
    /// of the session is about, which is the name this answers with. On more than one it stops:
    /// naming the screen is this verb's whole job, and a verb that guessed would put everything
    /// after it on a window chosen by z-order.
    /// </summary>
    internal string Wait(string target)
    {
        string? trouble = null;

        var arrived = Patience.Until(ForAScreen, () =>
        {
            var on = new List<AutomationElement>();
            foreach (var window in _app.Windows.All())
            {
                try
                {
                    // The screen and its popups, so waiting for what a list opened is arriving at
                    // the screen it opened from and not at a window nobody can name.
                    if (Search.Matching(_app.Windows.Scope(window), target).Count > 0)
                    {
                        on.Add(window);
                    }
                }
                catch (ProbeFailed unreadable)
                {
                    // A window in flux now may be readable in a hundred milliseconds, which is
                    // what waiting is. If it never settles, the budget says so below.
                    trouble = unreadable.Message;
                }
            }

            return on.Count > 0 ? on : null;
        });

        if (arrived is null)
        {
            throw new ProbeFailed(
                $"\"{target}\" never appeared within {ForAScreen.TotalSeconds:0} seconds. "
                + $"Windows open: {WindowsOpen()}."
                + (trouble is null ? string.Empty : Environment.NewLine + trouble));
        }

        if (arrived.Count > 1)
        {
            throw new ProbeFailed(
                $"\"{target}\" is on {arrived.Count} of the application's windows — "
                + $"{AppWindows.Names(arrived)} — so it does not say which screen this is. "
                + "Wait for something only the screen you mean has on it.");
        }

        _app.Windows.TheScreenIs(arrived[0]);
        Foreground.Hold(_app.Windows, arrived[0]);

        return ElementWords.Name(arrived[0]);
    }

    /// <summary>
    /// Moves the pointer to the middle of the element, holds it there, and says which cursor it
    /// showed and when.
    /// </summary>
    /// <remarks>
    /// The answer is a sequence and not the last cursor read: <c>hand from 0.0 s; arrow at 0.85 s;
    /// hand at 1.00 s</c> is a control that flickers, which is the defect a single read at the end
    /// of two seconds calls fine. The tooltip a hover opens is a popup, so the tree the host writes
    /// next has its words.
    /// </remarks>
    internal string Hover(string target)
    {
        var element = Search.One(Front(), target);
        var (x, y) = Centre(element);

        Pointer.MoveTo(x, y);

        // Long enough for the window under the pointer to have handled the move: the cursor is set
        // by the window, and the first read after the move is otherwise the cursor from before it.
        Thread.Sleep(Settle);

        var seen = new List<(string Cursor, TimeSpan At)>();
        var clock = Stopwatch.StartNew();
        while (clock.Elapsed < HeldOver)
        {
            var now = Pointer.CursorAt().Name;
            if (seen.Count == 0 || seen[^1].Cursor != now)
            {
                seen.Add((now, clock.Elapsed));
            }

            Thread.Sleep(Sampled);
        }

        return $"hovered {ElementWords.Line(element)}: " + string.Join(
            "; ",
            seen.Select((one, at) => at == 0
                ? $"{one.Cursor} from 0.0 s"
                : $"{one.Cursor} at {Seconds(one.At)} s"));
    }

    /// <summary>
    /// Presses the left button at the middle of the element, moves by whole pixels in twelve steps
    /// and lets go; <c>0,0</c> is a click. Says where the window was and is, and what the element's
    /// range value was and is when it has one.
    /// </summary>
    /// <remarks>
    /// The button is let go in a <c>finally</c>: a button left down is a desktop nothing afterwards
    /// is true of, and this is the one verb that can leave it that way.
    /// </remarks>
    internal string Drag(string target, int dx, int dy)
    {
        var scope = Front();
        var element = Search.One(scope, target);
        var (x, y) = Centre(element);

        var windowBefore = RectOf(scope[0]);
        var valueBefore = RangeOf(element);

        Pointer.MoveTo(x, y);
        Thread.Sleep(Step);
        Pointer.Press();
        try
        {
            for (var step = 1; step <= Steps; step++)
            {
                Pointer.MoveTo(x + (dx * step / Steps), y + (dy * step / Steps));
                Thread.Sleep(Step);
            }
        }
        finally
        {
            Pointer.Release();
        }

        Thread.Sleep(HedgeAfterAPress);

        var said = $"dragged {ElementWords.Line(element)} by {dx},{dy}: window {windowBefore} then "
            + RectOf(scope[0]);

        var valueAfter = RangeOf(element);

        return valueBefore is null && valueAfter is null
            ? said
            : $"{said}; value {valueBefore ?? "none"} then {valueAfter ?? "none"}";
    }

    /// <summary>
    /// Selects the words with the mouse the way a person does: found with the element's text
    /// pattern, pressed at the left edge of the first line they are on and let go at the right edge
    /// of the last, then read back off the pattern's own selection.
    /// </summary>
    /// <remarks>
    /// Read back and refused when it is not what was asked for, because a drag that selected the
    /// wrong words or none is a step that ran and said done. The pattern's own
    /// <c>Select</c> would have been the shorter route and is not this: it does not go through the
    /// pointer, so it is not shown to raise what a real selection raises, and that event is what the
    /// <em>Corregir</em> pill listens to.
    /// </remarks>
    internal string Select(string target, string text)
    {
        var element = Search.One(Front(), target);

        if (!Search.Supports(element, TextPattern.Pattern))
        {
            throw new ProbeFailed(
                $"{ElementWords.Line(element)} offers no text to select. "
                + $"What it offers instead: {Offers(element)}.");
        }

        var pattern = (TextPattern)element.GetCurrentPattern(TextPattern.Pattern);
        var found = pattern.DocumentRange.FindText(text, backward: false, ignoreCase: false)
            ?? throw new ProbeFailed($"{ElementWords.Line(element)} has no \"{text}\" in it.");

        var lines = found.GetBoundingRectangles();
        if (lines.Length == 0)
        {
            throw new ProbeFailed(
                $"\"{text}\" is in {ElementWords.Line(element)} but not on the screen: it has no "
                + "rectangle to press and drag across.");
        }

        var first = lines[0];
        var last = lines[^1];
        var fromX = (int)first.Left + 1;
        var fromY = (int)(first.Top + (first.Height / 2));
        var toX = (int)last.Right - 1;
        var toY = (int)(last.Top + (last.Height / 2));

        Pointer.MoveTo(fromX, fromY);
        Thread.Sleep(Step);
        Pointer.Press();
        try
        {
            for (var step = 1; step <= Steps; step++)
            {
                Pointer.MoveTo(
                    fromX + ((toX - fromX) * step / Steps),
                    fromY + ((toY - fromY) * step / Steps));
                Thread.Sleep(Step);
            }
        }
        finally
        {
            Pointer.Release();
        }

        Thread.Sleep(HedgeAfterAPress);

        var selected = string.Concat(pattern.GetSelection().Select(range => range.GetText(-1)));

        return selected == text
            ? $"selected \"{selected}\" in {ElementWords.Line(element)}"
            : throw new ProbeFailed(
                $"Dragging across \"{text}\" in {ElementWords.Line(element)} selected "
                + $"\"{selected}\" instead.");
    }

    /// <summary>
    /// Sets the window's size in physical pixels, keeping its position, and says what it has now:
    /// a window will not go below the smallest its content allows, and the answer is the rectangle
    /// and not the one that was asked for.
    /// </summary>
    internal string Size(int width, int height)
    {
        var window = Front()[0];
        var handle = AppWindows.Handle(window);

        if (!Native.SetWindowPos(
            handle,
            IntPtr.Zero,
            0,
            0,
            width,
            height,
            Native.SwpNoMove | Native.SwpNoZOrder | Native.SwpNoActivate))
        {
            throw new ProbeFailed(
                $"Windows would not size \"{ElementWords.Name(window)}\" ({Marshal.GetLastWin32Error()}).");
        }

        Thread.Sleep(HedgeAfterAPress);

        return $"the window is now {RectOf(window)}";
    }

    private static (int X, int Y) Centre(AutomationElement element)
    {
        var box = Reading.Flag(() => element.Current.IsOffscreen) == true
            ? System.Windows.Rect.Empty
            : Reading.Of(() => (object)element.Current.BoundingRectangle) is System.Windows.Rect read
                ? read
                : System.Windows.Rect.Empty;

        return box.IsEmpty || box.Width <= 0 || box.Height <= 0
            ? throw new ProbeFailed(
                $"{ElementWords.Line(element)} has no place on the screen to put the pointer: it is "
                + "offscreen or has no size.")
            : ((int)(box.Left + (box.Width / 2)), (int)(box.Top + (box.Height / 2)));
    }

    private static string RectOf(AutomationElement window) =>
        Native.GetWindowRect(AppWindows.Handle(window), out var rect)
            ? $"{rect.Width}x{rect.Height} at {rect.Left},{rect.Top}"
            : "gone";

    private static string? RangeOf(AutomationElement element) =>
        Search.Supports(element, RangeValuePattern.Pattern)
            ? Reading.Of(() => ((RangeValuePattern)element.GetCurrentPattern(RangeValuePattern.Pattern))
                .Current.Value.ToString(CultureInfo.InvariantCulture))
            : null;

    private static string Seconds(TimeSpan at) => at.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture);

    public void Dispose() => _app.Dispose();

    private static string Offers(AutomationElement element)
    {
        var patterns = Reading.Of(() => element.GetSupportedPatterns()) ?? [];

        // `InvokePatternIdentifiers.Pattern` reads as `Invoke`. The point of this list is to tell
        // whoever hit it which verb the control wants, and the suffix is on every entry.
        return patterns.Length > 0
            ? string.Join(", ", patterns.Select(one =>
                one.ProgrammaticName.Replace("PatternIdentifiers.Pattern", string.Empty)))
            : "nothing";
    }

    private string WindowsOpen()
    {
        var windows = _app.Windows.All();

        return windows.Count > 0 ? AppWindows.Names(windows) : "none";
    }
}
