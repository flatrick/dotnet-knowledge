# C# Language Reference Map

This page is a quick-find guide to `csharplang`, one of the upstream sources in
[`sources.json`](../../sources.json). It is not tracked here: `sync_source` fetches it into a
per-user cache, and the query tools read it from there. Nothing in this repository ever holds a copy.

**Sync before you query.** No query tool downloads — an unsynced source fails with an imperative
remedy naming the call to make. `sync_source(name: "csharplang")` once per machine is enough; every
answer afterwards carries the pin it came from.

## Quick Routes

Every path below is relative to the source root, and goes to `get_language_doc(path, source:
"csharplang")`. For anything longer than a page, call `get_language_doc_outline` first and then ask
for a single `section` — that is the cheap way in.

| If you need to... | `path` | Notes |
|---|---|---|
| Understand what the repo is for | `README.md` | High-level overview of proposals, meetings, and process. |
| Find the current design process | `Design-Process.md` | Best entry point for proposal lifecycle and status terms. |
| Find shipped features by C# version | `Language-Version-History.md` | Links from language versions to shipped feature docs. |
| Find an active proposal | `proposals/README.md` | Active proposals live directly under `proposals/*.md`. |
| Find versioned, shipped proposal docs | `proposals/csharp-<version>/…` | Shipped proposals are grouped into folders like `csharp-13.0/`. |
| Find inactive or rejected proposals | `proposals/inactive/README.md`, `proposals/rejected/README.md` | Use these when a feature idea is no longer in the active working set. |
| Find design meeting notes | `meetings/README.md` | Notes are organized by year under `meetings/<year>/`. |
| Find the language spec | `spec/README.md` | Important caveat: this file mostly redirects to `dotnet/csharpstandard`. |

## What Exists In This Source

The `sparse` array in `sources.json` limits the checkout to `proposals`, `spec`, `meetings`, and
`Language-Version-History.md` — the paths the tools read. `README.md` and `Design-Process.md` are at
the root and come with it.

- `README.md`: top-level orientation for the repo.
- `Design-Process.md`: the most useful process doc for understanding proposal stages, championing, milestones, and status.
- `Language-Version-History.md`: quickest path from a shipped language version to the proposal or feature note that describes it.
- `proposals/`: the main feature-design surface.
- `meetings/`: the main historical decision record.
- `spec/`: legacy local spec index plus links out to the current C# standard repo.

## Proposal Layout

- `proposals/*.md`: active proposals under current discussion or implementation.
- `proposals/csharp-<version>/`: proposal docs for features that shipped in that language version.
- `proposals/inactive/`: ideas kept on record but not currently prioritized.
- `proposals/rejected/`: ideas the language team does not intend to pursue.
- `proposals/proposal-template.md`: template for reading or drafting proposal-shaped docs.

## Meeting Notes Layout

- `meetings/README.md`: explains what the notes are and how they evolve.
- `meetings/<year>/README.md`: year-level index for a specific design cycle.
- `meetings/<year>/*.md`: individual notes, usually named by date.

If you know the year but not the exact topic, start at the year README. If you know the feature name, search both `proposals/` and `meetings/` because decisions often move between the two.

## Spec Caveat

Unlike the VB source, `spec/README.md` is not the authoritative local spec text. It is mainly a pointer into the separate `dotnet/csharpstandard` repo, with a local note about older text. Use it as a handoff page, not as proof that the local `spec/` folder is the current source of truth.

## Fast Search Patterns

`search_language_docs` returns `path:line` hits with the matched line and a section heading path — no
bodies. Feed a hit's `path` and `section` straight back to `get_language_doc`.

```text
search_language_docs(query: "overload resolution priority", source: "csharplang")
search_language_docs(query: "champion|working set|needs implementation", source: "csharplang", regex: true)
search_language_docs(query: "collection expressions", source: "csharplang")
```

Structured search will not cover every need — this corpus itself was built by grepping the raw
proposal trees. `list_sources` returns `cacheDir` so that stays possible:

```powershell
rg -n "overload resolution priority" <cacheDir>/csharplang/proposals <cacheDir>/csharplang/meetings
rg --files <cacheDir>/csharplang/meetings | rg "2024|2025"
```
