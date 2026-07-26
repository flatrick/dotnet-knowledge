# Visual Basic Language Reference Map

This page is a quick-find guide for the pinned `external/vblang/` submodule in this repo. Use it to get to the right upstream design artifact quickly. Treat the submodule as upstream reference material, not normal repo docs to edit casually.

## Quick Routes

| If you need to... | Start here | Notes |
|---|---|---|
| Understand what the repo is for | [`/external/vblang/README.md`/external/vblang/README.md) | High-level overview of the VB design repo. |
| Find the language spec | [`/external/vblang/spec/README.md`/external/vblang/spec/README.md) | This is a real local table of contents for the VB spec. |
| Find shipped features by language version | [`/external/vblang/Language-Version-History.md`/external/vblang/Language-Version-History.md) | Some entries link to C# proposal docs for shared features. |
| Find an active proposal | [`/external/vblang/proposals/README.md`/external/vblang/proposals/README.md) | Active proposals live directly under `proposals/*.md`. |
| Find inactive or rejected proposals | [`/external/vblang/proposals/inactive/README.md`/external/vblang/proposals/inactive/README.md), [`/external/vblang/proposals/rejected/README.md`/external/vblang/proposals/rejected/README.md) | VB has a smaller proposal surface than C#. |
| Find design meeting notes | [`/external/vblang/meetings/README.md`/external/vblang/meetings/README.md) | Notes are organized by year under `meetings/<year>/`. |

## What Exists In This Submodule

- `README.md`: top-level orientation and the embedded VB design process summary.
- `Language-Version-History.md`: quickest path from a released VB version to the feature note that describes it.
- `spec/`: the local markdown version of the VB spec, with a real table of contents in `spec/README.md`.
- `proposals/`: active, inactive, and rejected proposal docs.
- `meetings/`: archived design meeting notes by year.

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

Unlike the C# submodule, `external/vblang/spec/README.md` is a useful local starting point for the actual spec structure. If you need section-level navigation for syntax or semantics, begin there.

## Fast Search Patterns

```powershell
rg -n "overload resolution" external/vblang/spec external/vblang/proposals external/vblang/meetings
rg --files external/vblang/proposals
rg --files external/vblang/meetings | rg "2017|2018"
rg -n "language version|CallerArgumentExpression|Overload Resolution Priority" external/vblang/Language-Version-History.md
```
