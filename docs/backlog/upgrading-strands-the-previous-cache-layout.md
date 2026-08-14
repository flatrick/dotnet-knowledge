# Upgrading to the generation layout strands the previous cache

Sources used to live at `<cacheRoot>/<source>`. They now live at
`<cacheRoot>/.generations/<source>/<generation>/{repository,supplements}`, and the sync-state schema
version was bumped so every previously synchronized source reads as unsynchronized until it is
fetched again. Nothing removes the old directories: `PruneGenerationDirectories` only collects
retired generations inside `.generations/<source>/`, and the pre-generation tree is not one.

## Why it matters

The old copy is never read again and never reclaimed, so a cache that was working before the upgrade
roughly doubles on disk and stays that way. It is a one-time cost per machine, but it lands on every
existing user without being mentioned anywhere, and the cache deliberately sits outside any
repository so nothing else will clean it up either.

## Evidence

Measured on a real cache immediately after re-synchronizing all six sources:

| Stranded directory | Size |
|---|---|
| `dotnet-api-docs` | 771.1 MB |
| `roslyn-wiki` | 318.8 MB |
| `roslyn-api-docs` | 301.3 MB |
| `nuget-docs` | 32.1 MB |
| `csharplang` | 27.8 MB |
| `vblang` | 1.5 MB |
| **Total stranded** | **1452.6 MB** |
| New `.generations` tree | 1422.3 MB |

`list_sources` also reports `cacheDir` as the legacy `<cacheRoot>/<source>` while a source is
unsynchronized and as `<generation>/repository` afterwards, so before the re-sync it points at
exactly the stale tree that will be stranded.

## Suggested fix

Delete the old per-source directory when a source publishes its first generation: at that moment the
new tree is complete and the old one is provably unreferenced. Guard the delete the way
`TryDeleteRetiredDirectory` already does, so a locked file leaves the space in use rather than
failing a successful sync.

Removing a directory the user did not ask about deserves care — an alternative is to report the
reclaimable paths from `list_sources` and let the operator delete them, which is slower to take
effect but never surprises anyone.
