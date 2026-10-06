namespace MeetingTranscriber.Recording.Tests;

/// <summary>
/// The settings screen's line of writes (O-20261005-09): in turn, the last of two quick choices
/// written, and a failure that is its own caller's and nobody else's.
/// </summary>
public class WritesInTurnTests
{
    [Fact]
    public async Task A_write_waits_for_the_one_asked_before_it()
    {
        var writes = new WritesInTurn();
        var first = new TaskCompletionSource();
        var order = new List<string>();

        var one = writes.Run(async () =>
        {
            order.Add("first starts");
            await first.Task;
            order.Add("first ends");
        });
        var two = writes.Run(() =>
        {
            order.Add("second");
            return Task.CompletedTask;
        });

        await Task.Delay(50, TestContext.Current.CancellationToken);
        order.ShouldBe(["first starts"]);

        first.SetResult();
        await Task.WhenAll(one, two);

        order.ShouldBe(["first starts", "first ends", "second"]);
    }

    [Fact]
    public async Task The_last_of_two_quick_choices_is_the_one_written()
    {
        var writes = new WritesInTurn();
        var choice = new LastChoice();
        var gate = new TaskCompletionSource();
        var written = new List<string>();

        async Task Choose(string answer)
        {
            var ask = choice.Ask();
            await writes.Run(async () =>
            {
                await gate.Task;
                if (!choice.IsStill(ask))
                {
                    return;
                }

                written.Add(answer);
            });
        }

        var first = Choose("transcribe");
        var second = Choose("nothing");
        gate.SetResult();
        await Task.WhenAll(first, second);

        written.ShouldBe(["nothing"]);
    }

    [Fact]
    public async Task A_write_that_throws_is_its_callers_and_leaves_the_next_one_running()
    {
        var writes = new WritesInTurn();
        var shown = new List<string>();

        var failing = writes.Run(async () =>
        {
            await Task.Yield();
            throw new IOException("the disk is full");
        });
        var next = writes.Run(() =>
        {
            shown.Add("ran");
            return Task.CompletedTask;
        });

        await Should.ThrowAsync<IOException>(failing);
        await next;

        shown.ShouldBe(["ran"]);
    }

    [Fact]
    public void A_choice_is_the_newest_only_until_another_is_made()
    {
        var choice = new LastChoice();

        var first = choice.Ask();
        choice.IsStill(first).ShouldBeTrue();

        var second = choice.Ask();

        choice.IsStill(first).ShouldBeFalse();
        choice.IsStill(second).ShouldBeTrue();
    }
}
