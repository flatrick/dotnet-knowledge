# C# Language Reference Map

This page is a quick-find guide for the pinned `external/csharplang/` submodule in this repo. Use it to get to the right upstream design artifact quickly. Treat the submodule as upstream reference material, not normal repo docs to edit casually.

## Quick Routes

| If you need to... | Start here | Notes |
|---|---|---|
| Understand what the repo is for | [`/external/csharplang/README.md`/external/csharplang/README.md) | High-level overview of proposals, meetings, and process. |
| Find the current design process | [`/external/csharplang/Design-Process.md`/external/csharplang/Design-Process.md) | Best entry point for proposal lifecycle and status terms. |
| Find shipped features by C# version | [`/external/csharplang/Language-Version-History.md`/external/csharplang/Language-Version-History.md) | Links from language versions to shipped feature docs. |
| Find an active proposal | [`/external/csharplang/proposals/README.md`/external/csharplang/proposals/README.md) | Active proposals live directly under `proposals/*.md`. |
| Find versioned, shipped proposal docs | [`/external/csharplang/proposals/`/external/csharplang/proposals/) | Shipped proposals are grouped into folders like `csharp-13.0/`. |
| Find inactive or rejected proposals | [`/external/csharplang/proposals/inactive/README.md`/external/csharplang/proposals/inactive/README.md), [`/external/csharplang/proposals/rejected/README.md`/external/csharplang/proposals/rejected/README.md) | Use these when a feature idea is no longer in the active working set. |
| Find design meeting notes | [`/external/csharplang/meetings/README.md`/external/csharplang/meetings/README.md) | Notes are organized by year under `meetings/<year>/`. |
| Find the language spec | [`/external/csharplang/spec/README.md`/external/csharplang/spec/README.md) | Important caveat: this file mostly redirects to `dotnet/csharpstandard`. |

## What Exists In This Submodule

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

Unlike the VB submodule, `external/csharplang/spec/README.md` is not the authoritative local spec text. It is mainly a pointer into the separate `dotnet/csharpstandard` repo, with a local note about older text. Use it as a handoff page, not as proof that the local `spec/` folder is the current source of truth.

## Fast Search Patterns

```powershell
rg -n "overload resolution priority" external/csharplang/proposals external/csharplang/meetings
rg --files external/csharplang/proposals | rg "csharp-13.0|collection-expressions"
rg -n "champion|working set|needs implementation" external/csharplang/Design-Process.md external/csharplang/README.md
rg --files external/csharplang/meetings | rg "2024|2025"
```
