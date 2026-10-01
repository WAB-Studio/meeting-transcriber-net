using System.Text.Encodings.Web;
using System.Text.Json;

using MeetingTranscriber.Domain.Time;
using MeetingTranscriber.Infrastructure.Storage;

namespace MeetingTranscriber.Mcp;

/// <summary>What one request to a tool was, and what came back: one line of the record.</summary>
/// <param name="At">When the request was answered.</param>
/// <param name="Corpus">The folder the corpus opened from, or the one that was looked for when none opened.</param>
/// <param name="Tool">The tool's name.</param>
/// <param name="Asked">Each argument the tool takes, as the agent sent it or as the default filled it in.</param>
/// <param name="Answered">False when the answer was a refusal or the tool failed.</param>
/// <param name="Rows">How many rows the answer held.</param>
/// <param name="Bytes">The UTF-8 length of the text the agent was handed.</param>
/// <param name="More">Whether the answer said there was more.</param>
/// <param name="Refusal">Why nothing came back, or the type of the defect that stopped it.</param>
internal sealed record RecordedRequest(
    UtcTimestamp At,
    string? Corpus,
    string Tool,
    IReadOnlyDictionary<string, object?> Asked,
    bool Answered,
    int Rows,
    int Bytes,
    bool More,
    string? Refusal);

/// <summary>
/// Where an agent's reads are written down, so that what it read can be reconstructed afterwards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Outside the corpus, and that is the reason this is its own file.</b> ISC-98 promises this
/// server never writes a corpus, and <c>ReadOnlyTests</c> holds it by sweeping the source for
/// anything that writes a file. A record kept in the corpus folder would make that evidence false,
/// so it lives beside <c>ui-language</c> under the user's local application data, where
/// <c>LanguageChoice</c> keeps its file, and nothing here may name how a path inside a corpus is
/// built. A packaged process's writes there are redirected into the package's container, which an
/// uninstall removes: accepted, because this is a record of what was read and not something paid
/// for or typed.
/// </para>
/// <para>
/// One JSON object a line, appended. The file is opened for append and shared for reading only, so
/// a second server that holds it is waited on for up to a second and then refused, rather than two
/// writers interleaving half a line each.
/// </para>
/// </remarks>
internal static class RequestRecord
{
    internal const string FileName = "agent-requests.jsonl";

    private static readonly TimeSpan LongestToWaitForTheFile = TimeSpan.FromSeconds(1);

    private static readonly JsonSerializerOptions Line = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DictionaryKeyPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>This user's record: <c>%LOCALAPPDATA%\MeetingTranscriber\agent-requests.jsonl</c>.</summary>
    internal static FileInfo OfThisUser() => new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        CorpusLocation.ApplicationFolderName,
        FileName));

    /// <summary>
    /// Writes one line, or throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> saying why it could not.
    /// </summary>
    internal static void Append(FileInfo record, RecordedRequest request)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(request);

        record.Directory?.Create();

        // The instant is written as the text the corpus writes it as, and the rest under the
        // names the plan gave the line.
        var bytes = new System.Text.UTF8Encoding(false).GetBytes(
            JsonSerializer.Serialize(
                new
                {
                    at = request.At.ToString(),
                    corpus = request.Corpus,
                    tool = request.Tool,
                    asked = request.Asked,
                    answered = request.Answered,
                    rows = request.Rows,
                    bytes = request.Bytes,
                    more = request.More,
                    refusal = request.Refusal,
                },
                Line) + "\n");

        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            try
            {
                using var stream = new FileStream(
                    record.FullName, FileMode.Append, FileAccess.Write, FileShare.Read);

                stream.Write(bytes);
                return;
            }
            catch (IOException) when (
                waited.Elapsed < LongestToWaitForTheFile && !Directory.Exists(record.FullName))
            {
                // Another server holds the file for the length of one line, which is the case this
                // waits for. Any other IOException is waited on for the same second and then says
                // what it is; a record that is a folder is refused at once.
                Thread.Sleep(20);
            }
        }
    }
}
