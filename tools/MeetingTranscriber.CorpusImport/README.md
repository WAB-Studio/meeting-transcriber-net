# Legacy corpus import

Reads a corpus written by the Python system into a .NET one. Run by hand, once per machine that
has an old corpus:

```powershell
dotnet run --project tools/MeetingTranscriber.CorpusImport -- <corpus-directory> --database <corpus.db>
```

`--copy <directory>` copies the sources into the new corpus instead of registering them where they
already are. `--language <code>` is the language recorded for a meeting whose rendered transcript
does not say — the paid response does not carry it, it was a request parameter.

The corpus it reads is only ever read: nothing in it is created, rewritten, moved or deleted. It is
repeatable, so running it twice imports nothing the second time.

## This is meant to be deleted

The Python system is closed, has two users, and everything here is dead the day the last old corpus
has been imported. It lives outside `src/` so that day is a deletion and not a refactor:

```text
tools/MeetingTranscriber.CorpusImport/
tests/MeetingTranscriber.CorpusImport.Tests/
```

Delete both folders, drop their two lines from `MeetingTranscriber.slnx`, and drop `YamlDotNet`
from `Directory.Packages.props` — this is the only thing that reads YAML. Nothing under `src/`
references either project, so nothing else moves.

Two things it introduced do **not** come out with it, because they are the application's and not
the import's: the `companies` table, and the `Turns` rule the projection is built on.
