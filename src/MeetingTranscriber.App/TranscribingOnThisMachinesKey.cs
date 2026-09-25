using MeetingTranscriber.Infrastructure.Storage;
using MeetingTranscriber.Processing.Deepgram;
using MeetingTranscriber.Processing.Intake;

namespace MeetingTranscriber.App;

/// <summary>
/// The one thing in the application that spends: what <see cref="JobRunner"/>'s pump sends with,
/// on this machine's own Deepgram key.
/// </summary>
/// <remarks>
/// Here and nowhere a suite can reference, the same reason <c>DeepgramCommands</c> is the command
/// line's own site for the same call: <c>DeepgramKeyTests.Nothing_but_the_key_itself_reads_a_Deepgram_key</c>
/// fails on a fourth file naming <see cref="DeepgramKey"/>, so a test that wanted to drive this
/// class for real would be the second caller that guard exists to catch. What a suite gets instead
/// is <see cref="MeetingTranscriber.Processing.Intake.SendingToTheProvider"/> itself — every fact
/// about what a call comes to is proved against a lambda or a fake client, and this class adds
/// nothing to that beyond where the key comes from and how long a call may take.
/// </remarks>
internal static class TranscribingOnThisMachinesKey
{
    /// <summary>
    /// How long one call may take, upload and all — <see cref="DeepgramTranscription.LongEnoughForAWholeMeeting"/>,
    /// the one declaration this and <c>DeepgramCommands</c> both use: the default 100 seconds kills
    /// every real call, and a whole meeting is what this sends.
    /// </summary>
    private static readonly HttpClient Client = new() { Timeout = DeepgramTranscription.LongEnoughForAWholeMeeting };

    /// <summary>
    /// What the application's runner sends with. The key is read fresh at every call and held in no
    /// field, so a key kept or forgotten between two sends is never stale by the time the next one
    /// asks for it.
    /// </summary>
    public static SendingToTheProvider Sending() => async (audio, asked, response, stopping) =>
    {
        var key = DeepgramKey.OfThisInstall().Read();

        return await new DeepgramTranscription(Client)
            .SendAsync(audio, asked, key, response, stopping)
            .ConfigureAwait(false);
    };
}
