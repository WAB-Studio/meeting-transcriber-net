using System.Diagnostics;
using System.Text;
using System.Text.Json;

// The body of the fake Claude Code CLI the Processing suite runs ClaudeCodeSummaries against. It is
// a compiled program, not a script, so a runner without a usable PowerShell cannot stop it from
// starting: the generated claude.cmd only has to reach dotnet.exe by absolute path.
//
// Usage: <dll> <fake folder> [the arguments Claude Code was called with]. The folder holds
// `queue/` (behaviours, one JSON file per run, consumed in order), `calls/` (one JSON file per call,
// written once the call has been read), `version.txt`, `version-refused` and `trace.log`.
var noBom = new UTF8Encoding(false);
var json = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
var root = args[0];
var asked = args[1..];
var clock = Stopwatch.StartNew();

Trace($"fake started, arguments={asked.Length}");
var code = Run();
Trace($"fake finished with {code} after {clock.ElapsedMilliseconds} ms");
return code;

int Run()
{
    var isVersion = asked.Length > 0 && asked[0] == "--version";

    // Only a real run is given a redirected stream to read; a --version ask inherits whatever the
    // parent had, and reading it would block on it.
    var prompt = string.Empty;
    if (!isVersion)
    {
        using var stdin = new MemoryStream();
        Console.OpenStandardInput().CopyTo(stdin);
        prompt = noBom.GetString(stdin.ToArray());
    }

    var environment = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
    {
        environment[(string)entry.Key] = (string?)entry.Value ?? string.Empty;
    }

    var callsFolder = Path.Combine(root, "calls");
    var call = new
    {
        arguments = asked,
        environment,
        workspace = Environment.CurrentDirectory,
        files = Directory.GetFiles(Environment.CurrentDirectory).Select(Path.GetFileName).ToArray(),
        prompt,
    };

    // Written under another name and renamed, so a test reading `calls/` while this runs never
    // finds half a file.
    var number = Directory.GetFiles(callsFolder, "*.json").Length;
    var pending = Path.Combine(callsFolder, $"{number:D6}.tmp");
    File.WriteAllText(pending, JsonSerializer.Serialize(call, json), noBom);
    File.Move(pending, Path.Combine(callsFolder, $"{number:D6}.json"));

    if (isVersion)
    {
        if (File.Exists(Path.Combine(root, "version-refused")))
        {
            Write(Console.OpenStandardError(), "fake claude: refuses to say its version");
            return 1;
        }

        Write(Console.OpenStandardOutput(), File.ReadAllText(Path.Combine(root, "version.txt")));
        return 0;
    }

    var next = Directory.GetFiles(Path.Combine(root, "queue"), "*.json").OrderBy(name => name, StringComparer.Ordinal).FirstOrDefault();
    if (next is null)
    {
        Write(Console.OpenStandardError(), "fake claude: no behaviour was queued for this call");
        return 1;
    }

    using var behaviour = JsonDocument.Parse(File.ReadAllText(next));
    File.Delete(next);
    var body = behaviour.RootElement;
    switch (body.GetProperty("kind").GetString())
    {
        case "answer":
            Write(Console.OpenStandardOutput(), body.GetProperty("stdout").GetString() ?? string.Empty);
            return 0;
        case "exit":
            Write(Console.OpenStandardError(), body.GetProperty("standardError").GetString() ?? string.Empty);
            return body.GetProperty("code").GetInt32();
        default:
            Thread.Sleep(TimeSpan.FromSeconds(300));
            return 1;
    }
}

void Write(Stream stream, string text)
{
    var bytes = noBom.GetBytes(text);
    stream.Write(bytes, 0, bytes.Length);
    stream.Flush();
}

void Trace(string line)
{
    File.AppendAllText(Path.Combine(root, "trace.log"), $"{DateTime.UtcNow:HH:mm:ss.fff} {line}\r\n", noBom);
}
