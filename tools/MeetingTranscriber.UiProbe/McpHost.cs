using System.ComponentModel;
using System.Windows.Automation;

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace MeetingTranscriber.UiProbe;

/// <summary>
/// One host over <see cref="Session"/>: one application, held open, driven a turn at a time.
/// </summary>
/// <remarks>
/// <para>
/// The other is <see cref="CommandLine"/>, and <see cref="Program"/> says why both. The difference
/// this one makes is where the deciding happens: a script has to be written whole before the
/// application exists, so an agent that wants to press what it just read has to start again and
/// replay everything up to there — which is not slowness, it is guessing, because between the two
/// runs the screen it read is gone. Here the application stays where it was and every answer is
/// what the screen became, so the next step is chosen from the last one.
/// </para>
/// <para>
/// Every verb answers with a tree for that reason: a press whose answer is an exit code has said
/// nothing an agent can act on. <c>see</c> is the only one that also carries the picture, because
/// a picture is tens of thousands of tokens and the tree is what the questions about a screen are
/// actually made of.
/// </para>
/// <para>
/// The whole of every tool runs on <see cref="UiThread"/>, which is what makes the field below
/// safe without a lock: it is the only thread that reads or writes it.
/// </para>
/// </remarks>
internal sealed class McpHost : IDisposable
{
    private const string About = """
        Drives this repository's WinUI application: starts it, reads the UI Automation tree of the
        window it opens, presses what is on it, and answers with what the screen became.

        Call `start` first — nothing else works until an application is open — and `close` when you
        are done with it, because it stays open between calls.

        Every answer is the tree of the screen: one line per element, indented by depth, spelled
        `Type #x:Name "the words on it"`, with `value=`, `disabled` and `offscreen` where they
        apply. Name an element back by its `x:Name` or by the words on it. `see` also returns a
        picture of the window, which is far more expensive than the tree every other verb already
        gives you — reach for it to check a layout, not to read a string.

        A press does not wait for what it caused: `wait` for something on the screen you expect
        before you look at it. `wait` is also what says which window you are on when more than one
        is open.

        `type` leaves the text in a field with no key going down, so a control that commits on
        Enter is committed with `key` and never by typing into it.

        Every verb is refused once the application is older than the code on disk, because what
        Windows starts is the build it last registered rather than the one you last made. To pick
        up a change: `close`, build, `start` — in that order, because a running application holds
        its own assemblies open and the build fails on them.

        Every verb is also refused once the running copy of this tool is older than the sources it
        was built from: end the session, publish it, open a new one.

        It drives a corpus of its own, so Record may be pressed. `start` moves the pointer to the
        user's corpus aside and `close` puts it back, and the line `start` prints says which corpus
        the application it just opened is on — that line, and not this paragraph, is what is true of
        the application you are driving.
        """;

    /// <summary>Lent by <see cref="Program"/>, which owns it, and not disposed here.</summary>
    private readonly UiThread _ui;

    /// <summary>Touched only on <see cref="_ui"/>, by every tool below and by nothing else.</summary>
    private Session? _open;

    /// <summary>
    /// The pointer this host is holding, or nothing when no application has been started. Touched
    /// only on <see cref="_ui"/>, like <see cref="_open"/>.
    /// </summary>
    /// <remarks>
    /// Made on the first <c>start</c> and not when this host is constructed. <c>.mcp.json</c>
    /// starts this server at every Claude Code session in this checkout, most of which never drive
    /// anything — and a pointer put aside for one of those would leave somebody's own corpus behind
    /// a probe folder for hours, for a verb nobody called.
    /// </remarks>
    private ProbeCorpus? _corpus;

    private McpHost(UiThread ui) => _ui = ui;

    internal static async Task<int> RunAsync(UiThread ui)
    {
        // Stdout is the protocol and nothing else may reach it. Redirected rather than merely
        // avoided: the core prints, the runtime prints, and one stray line makes an agent's
        // session fail as a parse error a long way from whatever wrote it.
        Console.SetOut(Console.Error);

        using var host = new McpHost(ui);

        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "ui-probe", Title = "UI probe", Version = "1.0.0" },
            ServerInstructions = About,
            ToolCollection = host.Tools(),
        };

        await using var server = McpServer.Create(new StdioServerTransport(options), options);
        await server.RunAsync();

        return 0;
    }

    public void Dispose()
    {
        // On the windows thread, because that is where it was opened and where the elements it
        // holds are valid — and bounded, because this runs while the process is shutting down and
        // an unbounded wait on a wedged thread is how the application ends up outliving everything
        // that was supposed to close it. Past the budget the leash is what is left, and the leash
        // can only fire once this process is gone.
        if (!_ui.RunWithin(UiThread.ToStop, LetGo))
        {
            Console.Error.WriteLine(
                $"The application would not close within {UiThread.ToStop.TotalSeconds:0} seconds. "
                + "Ending anyway, which is what takes it with us.");

            // File work and not window work, so it does not need the thread that would not answer.
            // An application this process could not close is one the leash is about to take, and a
            // pointer left aside would outlive both.
            _corpus?.Dispose();
            _corpus = null;
        }
    }

    private McpServerPrimitiveCollection<McpServerTool> Tools() =>
    [
        Tool(
            "start",
            "Starts the application and answers with the tree of the screen it opens. Replaces one "
            + "already open, so it is also how you pick up a rebuild — after `close` and a build.",
            () => Answer(() =>
            {
                // Everything that refuses a start without an application, before the pointer that
                // says where somebody's meetings are is touched at all. A checkout with nothing
                // registered and a published copy owed a publish are the two commonest answers
                // this verb gives, and neither should have moved a file to say so.
                var bearings = Bearings.Taken();

                var corpus = _corpus ??= ProbeCorpus.PointedAtItsOwn();

                // Every start and not only the first. The application reads the pointer once, when
                // it launches, so the only moment it has to be right is this one — and between two
                // starts a walk that drove the move-corpus screen, another probe process closing,
                // or the user can each have left it saying something else.
                corpus.StillPointedAtItsOwn();

                Session opened;
                try
                {
                    // The new one before the old one is let go, and that ordering is the whole
                    // point: the commonest reason this is called is to pick up a change, the
                    // commonest reason it fails is that the change was not built, and closing
                    // first would charge an agent the screen it had walked to for asking.
                    opened = Session.Open(bearings);
                }
                catch
                {
                    // The pointer goes back only when this leaves nothing open. A start that
                    // failed over an application already running is still a probe session, and
                    // that session's window is on the probe's corpus.
                    if (_open is null)
                    {
                        LetGo();
                    }

                    throw;
                }

                _open?.Dispose();
                _open = opened;

                return Text(
                    $"{opened.StartedAs}{Environment.NewLine}{corpus.Arrangement}"
                    + $"{Break}{opened.Tree()}");
            })),

        Tool(
            "see",
            "The tree of the screen and a picture of the window. Changes nothing. The picture is "
            + "large — every other verb already answers with the tree, so use this one when the "
            + "question is about how the screen looks rather than what is on it.",
            () => Answer(() =>
            {
                Screen screen;
                try
                {
                    screen = Live().See();
                }

                // Still an error, and still says so — a `see` that answered with half of what it
                // promises and no mark on it would be read as a screen with nothing on it. What it
                // stops being is a turn that comes back with a sentence about a photograph and
                // nothing about the screen, which is the half the call was for.
                catch (ScreenWouldNotBePhotographed noPicture)
                {
                    return Failure($"{noPicture.Message}{Break}{noPicture.Tree}");
                }

                return new CallToolResult
                {
                    Content =
                    [
                        new TextContentBlock { Text = $"{screen.Tree}{Break}picture: {screen.Size}" },
                        ImageContentBlock.FromBytes(screen.Picture, "image/png"),
                    ],
                };
            })),

        Tool(
            "press",
            "Invokes a control, and answers with the tree of the screen it became. Fails naming "
            + "what the control offers instead when it is not something that can be pressed.",
            ([Description("The x:Name of the control, or the words on it.")] string element) =>
                Turn(session =>
                {
                    session.Press(element);

                    return $"pressed {element}";
                })),

        Tool(
            "type",
            "Sets a field's value, and answers with the tree of the screen it became. Fails when "
            + "the field is disabled, read only, or takes no value.",
            (
                [Description("The x:Name of the field, or the words on it.")] string element,
                [Description("What to leave in it.")] string text) =>
                Turn(session =>
                {
                    session.Type(element, text);

                    return $"typed \"{text}\" into {element}";
                })),

        Tool(
            "key",
            "Sends one key to a control and answers with the tree of the screen it became. `type` "
            + "raises no key event, so this is what commits a field that commits on Enter, closes "
            + "a dialogue on Escape, or moves focus on Tab. Fails naming the three when the key is "
            + "anything else, and when the control is disabled.",
            (
                [Description("The x:Name of the control, or the words on it.")] string element,
                [Description("One of: enter, escape, tab.")] string key) =>
                Turn(session =>
                {
                    session.Key(element, key);

                    return $"sent {key} to {element}";
                })),

        Tool(
            "choose",
            "Opens a list, picks the named item out of it, shuts it again, and answers with the "
            + "tree of the screen it became. Fails listing what is in the list when the item is "
            + "not one thing in it.",
            (
                [Description("The x:Name of the list, or the words on it.")] string list,
                [Description("The words on the item to pick.")] string item) =>
                Turn(session =>
                {
                    session.Choose(list, item);

                    return $"chose \"{item}\" in {list}";
                })),

        Tool(
            "wait",
            "Blocks until something is on one of the application's windows, makes that window the "
            + "screen from then on, and answers with its tree. Use it after any press whose "
            + "effect you are about to read.",
            ([Description("The x:Name of something on the screen you expect, or the words on it.")] string element) =>
                Turn(session => $"on \"{session.Wait(element)}\"")),

        // There is no `kill` here, and the omission is a decision. `Session` has one, and reaching
        // what a start finds after a crash is what it is for — but every walk that needs it is a
        // script, because a crash is the end of a session and what comes after it is a second run
        // reading what the first left. Nothing has yet wanted it a turn at a time, and the one
        // destructive verb in this tool is not the one to offer a caller that does not exist.
        Tool(
            "close",
            "Closes the application. Nothing but `start` works afterwards, and a build of the "
            + "application needs this first.",
            () => Answer(() =>
            {
                // The pointer as well as the application, and the pointer even when there is no
                // application: `_corpus` outliving `_open` is what a `start` that opened one and
                // then threw leaves, and that is the state in which somebody's own application is
                // opening on the probe's corpus.
                if (_open is null && _corpus is null)
                {
                    return Text("Nothing was open.");
                }

                LetGo();

                return Text("Closed.");
            })),
    ];

    /// <summary>
    /// Lets go of the application and of the pointer, in that order, and forgets both.
    /// </summary>
    /// <remarks>
    /// One method because the two always end together and the pairing was spelled out in four
    /// places: <c>close</c>, the crash branch of <see cref="Live"/>, a failed <c>start</c> and
    /// <see cref="Dispose"/>. The fifth place somebody drops a session and forgets the pointer is
    /// somebody's own application opening on the probe's corpus, silently, until the next probe
    /// run.
    /// <para>
    /// The application first and never the pointer: it holds the corpus open and read the pointer
    /// at its launch, and a pointer put back while it is still writing is the next launch's answer
    /// arriving under this one.
    /// </para>
    /// </remarks>
    private void LetGo()
    {
        _open?.Dispose();
        _open = null;

        _corpus?.Dispose();
        _corpus = null;
    }

    private static McpServerTool Tool(string name, string does, Delegate what) =>
        McpServerTool.Create(what, new McpServerToolCreateOptions { Name = name, Description = does });

    private static string Break => Environment.NewLine + Environment.NewLine;

    private static CallToolResult Text(string said) =>
        new() { Content = [new TextContentBlock { Text = said }] };

    /// <summary>
    /// Every verb but <c>start</c> and <c>close</c>: refuse a session that cannot be trusted, do
    /// the thing, and answer with what the screen became rather than with whether it worked.
    /// </summary>
    private Task<CallToolResult> Turn(Func<Session, string> act) => Answer(() =>
    {
        var session = Live();
        var did = act(session);

        return Text($"{did}{Break}{Became(session)}");
    });

    /// <summary>
    /// The tree, or the reason there is not one. Not a failure: a press that opened a second
    /// window worked, and what an agent needs back is that there are now two of them and which
    /// verb says so. Reporting it as an error would say the press did not happen — which is also
    /// why the element going away mid-read is caught here and not left to the general handler,
    /// since that is the very race a press causes.
    /// </summary>
    private static string Became(Session session)
    {
        try
        {
            return session.Tree();
        }
        catch (Exception cannot) when (cannot is ProbeFailed or ElementNotAvailableException)
        {
            return cannot.Message;
        }
    }

    /// <summary>
    /// The open session, once it has been asked whether it is still worth answering from. A
    /// session whose application has gone is dropped here rather than left to fail the same way on
    /// every call after it: <c>start</c> is the way out and an agent should be told so once.
    /// </summary>
    private Session Live()
    {
        var session = _open
            ?? throw new ProbeFailed("No application is open. Call start first.");

        try
        {
            // Every turn and not only at launch: a session outlives a build, and a window of the
            // wrong build is the one wrong answer nothing about the answer would reveal.
            session.MustStillBeUsable();
        }
        catch
        {
            // An application that has gone will never come back, so the session is let go here
            // rather than left to say the same thing on every call after this one. A stale build
            // is not the same refusal: that window is still open and still has to be closed.
            if (session.HasGone)
            {
                // A crash is an ending this tool can see, so the pointer goes back at the first
                // verb after it rather than waiting for a `close` nobody is going to call.
                LetGo();
            }

            throw;
        }

        return session;
    }

    /// <summary>
    /// Runs the whole of a tool on the windows thread, and turns anything it throws into an answer
    /// rather than into a dead server. That is the difference this host lives on: a command line
    /// that stops has cost a re-run, and a server that stops has cost the open application and
    /// every screen the agent had walked to.
    /// </summary>
    private Task<CallToolResult> Answer(Func<CallToolResult> work) => _ui.RunAsync(() =>
    {
        try
        {
            return work();
        }
        catch (ProbeFailed failure)
        {
            return Failure(failure.Message);
        }
        catch (Exception broke) when (broke is not (OutOfMemoryException or StackOverflowException))
        {
            return Failure($"The probe broke rather than finding anything: {broke}");
        }
    });

    private static CallToolResult Failure(string why) =>
        new() { Content = [new TextContentBlock { Text = why }], IsError = true };
}
