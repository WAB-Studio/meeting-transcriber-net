# winui@skills-dir — in-repo fork of Microsoft's WinUI plugin

A fork of the `winui` plugin from
[microsoft/win-dev-skills](https://github.com/microsoft/win-dev-skills), carried in-repo because two
of its instructions are wrong for this codebase and a plugin installed from a marketplace lives in
`~/.claude/plugins/cache/`, where an update silently reverts local fixes.

| | |
| --- | --- |
| Upstream | `microsoft/win-dev-skills`, tag `v0.5.0`, commit `f1028dd` (2026-07-21) |
| Upstream version | `marketplace.json` says 0.5.0, the plugin's own `plugin.json` says 0.3.0 — upstream is inconsistent, the tree is the same either way |
| Copied from | `~/.claude/plugins/marketplaces/win-dev-skills/plugins/winui/` |
| This fork | `0.5.0-mt.1` |

Only the WinUI layer (`src/MeetingTranscriber.App/`) is in scope. `Domain` and `Infrastructure`
build and test with the four commands in `CLAUDE.md`, which is what CI runs. The exception is
`winui-code-review`: its Security, Performance and Globalization checklists are plain C# and do
apply outside the UI.

## How it loads

This is a **skills-directory plugin**: any folder under `.claude/skills/` that contains a
`.claude-plugin/plugin.json` is loaded as a plugin named `<folder>@skills-dir` on the next session,
with no marketplace and no install step. It is discovered *in place* rather than copied into the
plugin cache, which is the whole point — editing a file here changes the live plugin.

```text
.claude/skills/winui/
├── .claude-plugin/plugin.json   ← presence of this file is what makes it a plugin
├── agents/winui-dev.agent.md    → agent  winui:winui-dev
├── skills/<name>/SKILL.md       → skills winui:<name>
└── README.md                    ← this file
```

Consequences worth knowing:

- Components are namespaced again: `winui:winui-dev-workflow`, not `winui-dev-workflow`. The
  `winui-dev` agent is back, which a plain `.claude/skills/` layout cannot carry.
- Because it is project scope, it loads only after the workspace **trust dialog** is accepted —
  the same gate that governs `.claude/settings.json`. Content coming from a repository is not
  trusted implicitly.
- **Launch Claude Code from the repository root.** Project-scope `@skills-dir` plugins load only
  from the `.claude/skills/` of the directory the session starts in; unlike plain skills they do not
  walk up to the repo root. From a subdirectory this plugin is simply absent. `/reload-plugins`
  after a `cd` also works.
- Edits to a `SKILL.md` apply immediately in the running session. Edits to `agents/` or
  `plugin.json` need `/reload-plugins` or a restart.
- To stop loading it: `claude plugin disable winui@skills-dir`, or delete the folder. There is no
  uninstall, because nothing was ever installed.
- Project scope also restricts components that execute code: MCP servers need per-server approval,
  LSP servers need workspace trust, and background monitors do not load at all. This plugin ships
  none of the three, so none of it bites.

Validated with `claude plugin validate .claude/skills/winui` → `✔ Validation passed`.

## Divergences from upstream

Keep this list current. Anything not listed here is byte-identical to `f1028dd`.

### 1. `winui-dev-workflow/SKILL.md` — Install Packages

Upstream says to run `dotnet add package <Name>` and to never pass `--version`, without mentioning
central package management at all. The advice turns out to be *correct* here — `dotnet add package`
is CPM-aware on SDK 10.0.302: it writes a versionless `<PackageReference>` to the `.csproj` and a
`<PackageVersion>` into `Directory.Packages.props`, alphabetically placed in the existing
`<ItemGroup>`. Verified empirically, not assumed.

What the section adds is the two caveats upstream has no reason to know about: `--no-restore` makes
the CLI write `Version="*"` instead of resolving a concrete version (every pin in this repo is
exact), and the CLI cannot write the comment that explains a deliberately held-back version — the
`Microsoft.Testing.Extensions.CodeCoverage` 18.0.x pin and the `SQLitePCLRaw.bundle_e_sqlite3` 3.x
pin against GHSA-2m69-gcr7-jv3q both depend on that comment surviving.

### 2. `winui-dev-workflow/BuildAndRun.ps1` — analyzer injection

To load `Microsoft.WindowsAppSDK.Analyzers`, the script writes a temporary `Directory.Build.props`
into the **project** folder, and it probes for a pre-existing one only in that same folder. This
repo's shared `Directory.Build.props` is at the root, so the probe misses it and the generated file
lands below it. MSBuild stops at the first `Directory.Build.props` walking up from the project, so
the root one is never imported and the App project loses `Nullable`, `LangVersion` and
`ImplicitUsings` for that build. `ImplicitUsings` dropping out surfaces as CS0246 on usings that a
plain `dotnet build` compiles fine — a phantom error, since the file is deleted in the `finally`.

The generated props now imports whatever `Directory.Build.props` sits above it:

```xml
<PropertyGroup>
  <ParentDirectoryBuildProps>$([MSBuild]::GetPathOfFileAbove('Directory.Build.props', '$(MSBuildThisFileDirectory)../'))</ParentDirectoryBuildProps>
</PropertyGroup>
<Import Project="$(ParentDirectoryBuildProps)" Condition="'$(ParentDirectoryBuildProps)' != ''" />
```

Verified against MSBuild with a two-level scratch layout: before the fix `InheritedFromRoot`,
`ImplicitUsings` and `Nullable` all evaluate empty; after it they evaluate to the root's values.
The `Condition` keeps the single-project case working — with no parent props the property is empty
and the import is skipped, which is upstream's own scenario.

## Re-syncing

Diff against upstream and re-apply the two changes by hand. Mirror `skills/` and `agents/` only —
`/MIR` over the plugin root would delete this README and `.claude-plugin/`, which upstream does not
have:

```powershell
$up = "$env:USERPROFILE\.claude\plugins\marketplaces\win-dev-skills\plugins\winui"
git -C "$env:USERPROFILE\.claude\plugins\marketplaces\win-dev-skills" pull
robocopy "$up\skills" .claude\skills\winui\skills /MIR /L    # /L = dry run, review first
robocopy "$up\agents" .claude\skills\winui\agents /MIR /L
```

Then bump the `-mt.N` suffix in `.claude-plugin/plugin.json` and re-run
`claude plugin validate .claude/skills/winui`.

There is nothing that enforces these fixes, so a re-sync that skips them regresses silently.
