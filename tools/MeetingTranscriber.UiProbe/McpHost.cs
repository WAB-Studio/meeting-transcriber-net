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

        Every verb is refused once the application is older than the code on disk, because what
        Windows starts is the build it last registered rather than the one you last made. To pick
        up a change: `close`, build, `start` — in that order, because a running application holds
        its own assemblies open and the build fails on them.
        """;

    /// <summary>Lent by <see cref="Program"/>, which owns it, and not disposed here.</summary>
    private readonly UiThread _ui;

    /// <summary>Touched only on <see cref="_ui"/>, by every tool below and by nothing else.</summary>
    private Session? _open;

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
        if (!_ui.RunWithin(UiThread.ToStop, () =>
            {
                _open?.Dispose();
                _open = null;
            }))
        {
            Console.Error.WriteLine(
                $"The application would not close within {UiThread.ToStop.TotalSeconds:0} seconds. "
                + "Ending anyway, which is what takes it with us.");
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
                // The new one before the old one is let go, and that ordering is the whole point:
                // the commonest reason this is called is to pick up a change, the commonest reason
                // it fails is that the change was not built, and closing first would charge an
                // agent the screen it had walked to for asking.
                var opened = Session.Open();

                _open?.Dispose();
                _open = opened;

                return Text($"{opened.StartedAs}{Break}{opened.Tree()}");
            })),

        Tool(
            "see",
            "The tree of the screen and a picture of the window. Changes nothing. The picture is "
            + "large — every other verb already answers with the tree, so use this one when the "
            + "question is about how the screen looks rather than what is on it.",
            () => Answer(() =>
            {
                var screen = Live().See();

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

        Tool(
            "close",
            "Closes the application. Nothing but `start` works afterwards, and a build of the "
            + "application needs this first.",
            () => Answer(() =>
            {
                if (_open is null)
                {
                    return Text("Nothing was open.");
                }

                _open.Dispose();
                _open = null;

                return Text("Closed.");
            })),
    ];

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
                _open = null;
                session.Dispose();
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
