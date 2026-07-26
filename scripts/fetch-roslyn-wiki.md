# fetch-roslyn-wiki.cs

Populates `external/roslyn-wiki/` with the `docs/wiki` subtree from
[dotnet/roslyn](https://github.com/dotnet/roslyn) at a pinned commit SHA.

No submodule, no persistent clone. A blobless sparse clone is created in a
temp directory, the target subtree is copied out, and the clone is deleted.

## Usage

```bash
dotnet scripts/fetch-roslyn-wiki.cs
```

Override the SHA without editing the script:

```bash
dotnet scripts/fetch-roslyn-wiki.cs -- --sha <new-sha>
# or via environment variable:
ROSLYN_SHA=<new-sha> dotnet scripts/fetch-roslyn-wiki.cs
```

## Updating the pin

1. Find the commit you want on [github.com/dotnet/roslyn/commits/main](https://github.com/dotnet/roslyn/commits/main).
2. Copy the full SHA.
3. Edit `PinnedSha` near the top of `scripts/fetch-roslyn-wiki.cs`.
4. Re-run the script.
5. Commit both the updated script and the refreshed `external/roslyn-wiki/` together
   (or just the script if the directory is gitignored).

## Committing vs gitignoring the output

Two valid workflows — pick one and note it in `git.md` if you commit:

| Approach | When to use |
|----------|-------------|
| **Commit `external/roslyn-wiki/`** | Matches how `docs/modelcontextprotocol/` is handled; content is always available without running the script; diffs show exactly what changed between pins. |
| **Gitignore `external/roslyn-wiki/`** | Keeps the repo lean; contributors run the script after clone; CI must run it too if it needs the content. |

The `.roslyn-commit` file inside `external/roslyn-wiki/` always records the
SHA that was fetched, regardless of which approach you choose.

## Why not a submodule?

The roslyn repo is very large. A submodule would clone the entire `.git`
history on every worktree initialization just to access a small wiki
subdirectory. The script approach fetches only the tree objects for
`docs/wiki` at the exact commit needed, then discards the clone.
