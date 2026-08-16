# Agent setup — dotnet-knowledge

This file is the entry point for LLM agents and contributors. Read it before doing anything else.
For server work, read [docs/design/mcp-tool-surface.md](docs/design/mcp-tool-surface.md); for known
deferred work, read [docs/backlog/README.md](docs/backlog/README.md).

## What this repository is

An MCP stdio server giving an agent trustworthy, local, version-pinned answers about C#, VB.NET and
Roslyn — API shape, and language-design proposals and specification text — fetched from upstream
Microsoft repositories at commits this repository pins.

The bundled language-feature example corpus that used to live under `examples/` has moved to
[flatrick/dotnet-code-examples](https://github.com/flatrick/dotnet-code-examples). It has no
dependency on this server and is not fetched or served by it today; see
[`docs/design/mcp-tool-surface.md`](docs/design/mcp-tool-surface.md) for whether bundled-example
queries against it are planned.

**The caller is always an agent.** Design every response for context economy and machine parsing,
not for human browsing — structured payloads, no ASCII tables, no decorative formatting.

## Directory map

| Path | Purpose |
|------|---------|
| `docs/design/mcp-tool-surface.md` | The server's tool surface, provenance envelope, and sync model. |
| `docs/design/ci.md` | What CI runs and when, why every version is pinned, and the runbook to activate the merge gate. |
| `docs/backlog/` | Known issues and deferred decisions, one file each. Read before assuming a rough edge is undiscovered. |
| `sources.json` | The upstream sources, their pinned commits, and their sparse-checkout paths. |
| `scripts/*.cs` | Dev tooling, as single-file C# programs — `dotnet scripts/foo.cs -- <args>`. |
| `scripts/probes/` | Throwaway diagnostic instruments answering questions about the *host*, not about this repository's code. [`scripts/probes/README.md`](scripts/probes/README.md) states what each one can and cannot settle. |
| `version.json` | Nerdbank.GitVersioning config. The package version carries the commit it was built from, which is how the install script tells a current tool from a stale one. |

## Prerequisites

**Run `dotnet scripts/install-mcp-tool.cs -- install` once per machine, before the first session
that uses the `dotnet-knowledge` tools.** `.mcp.json` and `.codex/config.toml` start the server with
the `dotnet-knowledge` command from `~/.dotnet/tools`, which this install creates. Use
`-- reinstall` when a server is already running: it stops every process launched from the installed
shim first, which Windows requires because a running server locks the shim executable.

The install is machine-global, not per-checkout: a worktree needs no MCP setup of its own, and
installing from one worktree changes the server for every concurrent session. The script self-locates
from its own file path, so a worktree's copy always installs *that worktree's* code regardless of the
shell's working directory.

Re-run the same command after changing server code, then **restart the MCP connection** — a running
client keeps serving the previously installed binary until it reconnects. With no arguments the
script reports the installed version and whether it was built from this checkout's HEAD.

## Conventions inherited deliberately

These come from `flatrick/dotnet-mcp`, where this content originated. They are kept because they
earned their place, not out of habit.

1. **Tooling is single-file C#, never a shell script.** No `.sh`, `.ps1`, `.bat`, or `.py`. Every
   tool must run on native Windows with only the .NET SDK, and needs no project of its own. Pass
   arguments after `--`, because `dotnet <file>.cs` silently claims some flags for itself.
2. **No silent truncation.** A capped result set must say so — `isPartial`, `nextPageToken`, or
   similar. An agent that receives a quietly-truncated search concludes the symbol does not exist.
3. **State current truth only.** Documents do not narrate their own history; `git log` is the
   changelog. No "previously said X" footers, no dated verification stamps.
   [`docs/decisions.md`](docs/decisions.md) and [`docs/gotchas.md`](docs/gotchas.md) are the two
   deliberate exceptions: they are append-only, dated, and never edited, because a superseded entry
   is what stops a settled question being reopened. Do not tidy them.
4. **American English** for identifiers, comments, and prose, except where an external standard
   specifies otherwise (the Model Context Protocol's `notifications/cancelled` stays as spelled).
5. **Never commit upstream content.** The sources in `sources.json` are fetched into a per-user
   cache at runtime — never vendored, never submoduled, never pasted into a doc. Everything tracked
   here is authored here, which is what makes the MIT grant in `LICENSE` true. Do not point the
   fetch cache at the working tree and then stage the result, and do not re-add the `dotnet-mcp`
   submodules out of habit. [`THIRD-PARTY-NOTICES.md`](THIRD-PARTY-NOTICES.md) states the policy;
   `dotnet scripts/verify-no-vendored-content.cs` enforces it and exits 1 on a finding.

## Continuous integration

**No CI runs. Actions is disabled for this repository, and verification is whatever you run
locally.** Workflow configuration is still maintained — `.github/workflows/*.yml` is current and a
new job should be added when new tests are — but nothing executes it. Minutes cost money on a
private repository, and while this is a personal project that cost buys nothing a local run does
not already provide.

Two consequences follow, and both are easy to get wrong:

- **A commit's presence on `main` says nothing about whether it was verified.** Not "the gate is
  advisory" — there is no run to read. If you need to know whether something passes, run it.
- **Adding a test does not make it run anywhere.** A suite nobody invokes locally is dead weight
  dressed as coverage, so a new suite needs an entry in [`CLAUDE.md`](CLAUDE.md)'s command list, not
  only a workflow step.

[`docs/design/ci.md`](docs/design/ci.md) has the workflow's design and the runbook for turning
Actions on and activating a merge gate, for whenever that becomes worth paying for.
