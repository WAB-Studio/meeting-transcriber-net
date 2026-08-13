# Legacy corpus import

Reads a corpus written by the Python system into a .NET one. Run by hand, once per machine that
has an old corpus:

```powershell
dotnet run --project tools/MeetingTranscriber.CorpusImport -- <python-corpus> --corpus <directory>
```

`--corpus` is the folder of the corpus to write into, database and files together, and it is made
if it is not there. It may not be the corpus being read, or anywhere inside it. The sources are
copied in: a stored path is read against the corpus holding the row, so a row left pointing back at
the Python corpus would name a file that is not where the corpus says it is. `--language <code>` is
the language recorded for a meeting whose rendered transcript does not say — the paid response does
not carry it, it was a request parameter.

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
the import's: the classification tree — `nodes`, `meeting_nodes` and `templates` — and the `Turns`
rule the projection is built on.
