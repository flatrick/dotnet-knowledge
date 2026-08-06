# Visual Basic Language Reference Map

This page is a quick-find guide to `vblang`, one of the upstream sources in
[`sources.json`](../../sources.json). It is not tracked here: `sync_source` fetches it into a
per-user cache, and the query tools read it from there. Nothing in this repository ever holds a copy.

**Sync before you query.** No query tool downloads — an unsynced source fails with an imperative
remedy naming the call to make. `sync_source(name: "vblang")` once per machine is enough; every
answer afterwards carries the pin it came from.

## Quick Routes

Every path below is relative to the source root, and goes to `get_language_doc(path, source:
"vblang")`. For anything longer than a page, call `get_language_doc_outline` first and then ask for a
single `section` — that is the cheap way in.

| If you need to... | `path` | Notes |
|---|---|---|
| Understand what the repo is for | `README.md` | High-level overview of the VB design repo. |
| Find the language spec | `spec/README.md` | This is a real table of contents for the VB spec. |
| Find shipped features by language version | `Language-Version-History.md` | Some entries link to C# proposal docs for shared features. Only covers VB 15.0 onward — see the caveat below. |
| Find an active proposal | `proposals/README.md` | Active proposals live directly under `proposals/*.md`. |
| Find inactive or rejected proposals | `proposals/inactive/README.md`, `proposals/rejected/README.md` | VB has a smaller proposal surface than C#. |
| Find design meeting notes | `meetings/README.md` | Notes are organized by year under `meetings/<year>/`. |

## What Exists In This Source

The `sparse` array in `sources.json` limits the checkout to `proposals`, `spec`, `meetings`, and
`Language-Version-History.md` — the paths the tools read. `README.md` is at the root and comes with
it.

- `README.md`: top-level orientation and the embedded VB design process summary.
- `Language-Version-History.md`: quickest path from a released VB version to the feature note that describes it.
- `spec/`: the markdown version of the VB spec, with a real table of contents in `spec/README.md`.
- `proposals/`: active, inactive, and rejected proposal docs.
- `meetings/`: archived design meeting notes by year.

## Version-History Caveat

`Language-Version-History.md` is **not** a complete version history. It documents deltas from VB 15.0
(Visual Studio 2017) onward plus a few later point releases, and has nothing for VB 1.0 through
VB 11 — generics, LINQ, XML literals, auto-properties, statement lambdas, async/await, string
interpolation and `NameOf` are all shipped features with no entry. `spec/` is topic-organized rather
than version-gated, so it does not fill that gap either. For pre-VB14 questions, start from
`https://learn.microsoft.com/dotnet/visual-basic/whats-new/`; this is the same gap and the same
fallback that
[`docs/design/language-feature-showcase-design.md`](../design/language-feature-showcase-design.md)
records for the corpus.

## Proposal Layout

- `proposals/*.md`: active proposals.
- `proposals/inactive/`: ideas kept on record but not currently prioritized.
- `proposals/rejected/`: ideas the team does not intend to pursue.
- `proposals/proposal-template.md`: template for proposal-shaped docs.

Unlike C#, this repo does not currently use a large set of shipped-version proposal folders. Start with `Language-Version-History.md` when the question is about released behavior, not `proposals/`.

## Meeting Notes Layout

- `meetings/README.md`: explains what the notes are and how they evolve.
- `meetings/<year>/README.md`: year-level index for a specific design cycle.
- `meetings/<year>/*.md`: individual notes, usually named by date.

The meeting history is smaller than the C# repo, so year-level browsing is often enough before switching to direct text search.

## Spec Note

Unlike the C# source, `spec/README.md` is a useful starting point for the actual spec structure. If you need section-level navigation for syntax or semantics, begin there.

## Fast Search Patterns

`search_language_docs` returns `path:line` hits with the matched line and a section heading path — no
bodies. Feed a hit's `path` and `section` straight back to `get_language_doc`.

```text
search_language_docs(query: "overload resolution", source: "vblang")
search_language_docs(query: "CallerArgumentExpression|Overload Resolution Priority", source: "vblang", regex: true)
```

Structured search will not cover every need — this corpus itself was built by grepping the raw
proposal trees. `list_sources` returns `cacheDir` so that stays possible:

```powershell
rg -n "overload resolution" <cacheDir>/vblang/spec <cacheDir>/vblang/proposals <cacheDir>/vblang/meetings
rg --files <cacheDir>/vblang/meetings | rg "2017|2018"
```
