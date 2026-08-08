# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Orientation

Read [`AGENTS.md`](AGENTS.md) first; it carries the server's conventions and the reasoning behind
them. This file covers commands and the cross-file architecture. For current state,
[`README.md`](README.md) has the status summary and [`docs/backlog/`](docs/backlog/README.md) holds
one file per known issue.

Before working on something that looks already-settled or surprisingly awkward, read
[`docs/decisions.md`](docs/decisions.md) and [`docs/gotchas.md`](docs/gotchas.md). They are
append-only and newest-first: decisions record what was rejected and why, gotchas record facts that
cost real time and are not inferable from the code.

This repository is an MCP stdio server — `src/DotNetKnowledge.Mcp/` — giving an agent version-pinned,
local answers about C#, VB.NET and Roslyn: API shape, and language-design proposals and
specification text, fetched from the upstream Microsoft repos pinned in `sources.json`.

The bundled language-feature example corpus that used to live under `examples/` has moved to
[flatrick/dotnet-code-examples](https://github.com/flatrick/dotnet-code-examples); it is not part of
this repository and has no dependency on it.

**The MCP server's caller is always an agent.** Design responses for context economy and machine
parsing — structured payloads, no ASCII tables, no decorative formatting.

## Commands

```bash
# MCP server
dotnet build src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj
dotnet run --project src/DotNetKnowledge.Mcp        # stdio; expects a client on stdin
dotnet build DotNetKnowledge.slnx                   # src/ and its tests
dotnet test DotNetKnowledge.slnx                    # ditto

# The installed server — what .mcp.json and .codex/config.toml launch
dotnet scripts/install-mcp-tool.cs                  # which build would launch next
dotnet scripts/install-mcp-tool.cs -- install       # pack + install/update the user-global tool
dotnet scripts/install-mcp-tool.cs -- uninstall     # remove it

# Dev tooling — single-file C# programs; arguments go after `--`
dotnet scripts/verify-no-vendored-content.cs                     # licensing guard; exit 1 on a finding
dotnet scripts/verify-no-vendored-content.cs -- --history        # same checks against every commit
```

`dotnet <file>.cs` silently claims some flags for itself, which is why every script takes its
arguments after `--`.

Smoke-testing the server over stdio needs a redirected-process driver, not a shell pipe — a Git Bash
`>` redirect swallows the server's stdout entirely and looks like a server fault.

## The MCP server

`Program.cs` is a generic-host stdio server: `AddMcpServer().WithStdioServerTransport()
.WithToolsFromAssembly()`, with `SourceCatalog` (reads `sources.json`) and `SourceCache` (resolves
the on-disk cache) as singletons. Tools live in `Features/<Area>/<Name>Tool.cs` and are discovered by
assembly scan.

**stdout is the protocol channel.** Every log goes to stderr; anything else written to stdout
corrupts the session and surfaces as an opaque client-side parse error. Logging is configured for
this in `Program.cs` — do not add a console provider that writes to stdout.

Cache location defaults to `%LOCALAPPDATA%\dotnet-knowledge\sources` — the per-user *data*
directory elsewhere: `$XDG_DATA_HOME`/`~/.local/share` on Linux, `~/Library/Application Support` on
macOS — overridable with `DOTNET_KNOWLEDGE_CACHE`. It sits outside any repository so one download
serves every clone and worktree on the machine, and it is deliberately not the XDG *cache*
directory: a synced pin must survive cache cleaners (`docs/decisions.md`).

Implemented: `list_sources`, `sync_source`, `search_api`, `lookup_api`, `search_api_text`,
`find_api_references`, `search_language_docs`, `get_language_doc` and `get_language_doc_outline`.
Bundled-example queries are future work. The intended surface for all of them is in `docs/design/mcp-tool-surface.md`; known
defects are one file each in `docs/backlog/`.

`Text/DocumentationText.cs` is the one seam where documentation text is cleaned, and it has two
stages whose placement is load-bearing: **normalization at the read**, before anything matches, and
**budgeting at the payload**, after matching and paging. Putting either on the wrong side breaks a
guarantee — normalizing on the way out would make the text a search matched differ from the text it
returns, and budgeting before a match would drop every hit past the cap and report an absence.
`docs/design/mcp-tool-surface.md` has the reasoning.

### Non-negotiables for every tool

These are correctness obligations, not preferences:

- **Every payload carries the provenance envelope** — `repo`, `ref` (`pinned` / `head:<branch>` /
  `bundled`), `commit`, `fetchedAt`. Never absent, never inferred. The reason to prefer this server
  over a web search is that its answers are tied to a known revision.
- **No query tool ever triggers a download.** It checks, and fails fast with an imperative remedy
  naming the tool to call. A partially-synced source answers with plausible absences that look like
  real "not found" results — the dangerous failure, because nothing about it looks like an error.
- **No silent truncation.** Every capped result set carries `isPartial` or a cursor, and every
  capped *string* carries `isTruncated`. An agent that receives a quietly-truncated search concludes
  the symbol does not exist. Report truncation in a field, never by marking the text: an ellipsis
  cannot be told from one the source itself wrote.
- **`list_sources` keeps returning `cacheDir`.** Structured lookup will not cover everything, and an
  agent has no other way to find them.
- **Search tools return names and locations, never bodies.** `search_api` returns fully-qualified
  names; `search_language_docs` returns `path:line` hits. The agent then spends context on a single
  `lookup_api` or `get_language_doc`. `search_api_text` carries the matched prose because the match
  *is* the location — there is no line number inside an XML element to point at — but it is capped
  at 300 characters and names the owning symbol, so the follow-up call is still the way to read the
  entry.
- **Every subprocess this server starts redirects standard input.** Git blocks during process
  startup when it inherits a piped stdin handle that the parent is concurrently reading — not from
  a piped stdin alone, which is harmless and measured at 34 ms. An MCP stdio server always has that
  read pending, because the read *is* the JSON-RPC transport. `GitCommandRunner` redirects standard
  input; anything else that starts a process must too. `docs/gotchas.md` records the evidence.

## Licensing — never commit upstream content

The repository is MIT (`LICENSE`) and **every tracked file is authored here**. The sources in
`sources.json` are fetched at runtime into a per-user cache outside the working tree; they are never
vendored, submoduled, or pasted into a doc. That is what keeps the MIT grant truthful, and it is the
one invariant here with consequences outside the repository.

The realistic ways it breaks are mundane — pointing the fetch cache at the working tree and
committing it, re-adding the `dotnet-mcp` submodules out of habit, pasting spec text into a doc for
convenience. Run `dotnet scripts/verify-no-vendored-content.cs` before committing anything that adds
files in bulk; it checks tracked paths, third-party copyright headers, and the document shapes of
the upstream sources, and exits 1 on a finding.

Never silence a finding by loosening a rule. `THIRD-PARTY-NOTICES.md` has the policy.

The default scan covers the tracked tree; `--history` covers every blob in every commit. Deleting a
file does not remove it from the repository, so a finding from `--history` means rewriting history,
not deleting a file — which is why it is much cheaper to not commit the content in the first place.

## Continuous integration — configured, not running

**Actions is disabled for this repository. No workflow executes, and local runs are the only
verification there is.** Keep the workflow configuration current, and add a job when you add a
suite, so turning Actions on is a settings change rather than a project — but never treat a
workflow step as the thing that runs a test. Minutes cost money on a private repository, and while
this is a personal project that cost buys nothing a local run does not.

So: a commit on `main` carries no evidence that anything passed, because there is no run to read;
and a new suite needs an entry in the command list above, not only a workflow step, or nothing will
ever invoke it. [`docs/design/ci.md`](docs/design/ci.md) has the workflow's design and the runbook
for turning Actions on.

## Conventions

1. **Tooling is single-file C#, never a shell script.** No `.sh`, `.ps1`, `.bat`, or `.py` — every
   tool must run on native Windows with only the .NET SDK. `scripts/Directory.Build.props` resets
   the production analyzer settings for these.
2. **State current truth only.** Documents do not narrate their own history; `git log` is the
   changelog. No "previously said X" footers, no dated verification stamps.
   [`docs/decisions.md`](docs/decisions.md) and [`docs/gotchas.md`](docs/gotchas.md) are the two
   deliberate exceptions: they are append-only, dated, and never edited, because a superseded entry
   is what stops a settled question being reopened. Do not tidy them.
3. **American English** for identifiers, comments, and prose, except where an external standard
   specifies otherwise (MCP's `notifications/cancelled` stays as spelled).
4. **LF line endings, UTF-8**, enforced by `.gitattributes`.
5. Nothing is shared back to `flatrick/dotnet-mcp`, where this content originated. The extraction
   was one-way and final — never set up synchronization or copy files back. The same is true in the
   other direction for `flatrick/dotnet-code-examples`, extracted from this repository: nothing is
   shared back there either.
6. **`.scratch/` at the repository root is the scratch folder**, gitignored and never committed. Any
   throwaway working file belongs there — build and test logs, diagnostic scripts, probe programs,
   intermediate data — not logs alone, and in preference to any session or temporary directory
   outside the repository. Anything that must survive or reach another machine is not scratch:
   tracked diagnostic tooling goes in [`scripts/probes/`](scripts/probes/README.md).
