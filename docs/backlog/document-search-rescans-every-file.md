# Every document search rescans every file in every source

`search_docs` has no index. Each call reads every `*.md` file in each searched source from
disk and scans it line by line. There is no cache between calls, so two identical queries do
identical work.

## Why it matters

An unfiltered query now reads 1489 files totalling 10.2 MB, up from 1024 files and 8.0 MB
before `nuget-docs` was added — 45% more files, 28% more bytes. Every source added from here
lands on the same per-query cost, and the tool that pays it is the one an agent calls first,
before it knows enough to pass `source`.

## Evidence

- `ReadSearchSource` calls `File.ReadAllText` per file on every invocation.
- Per-source markdown, measured at the pinned commits: csharplang 893 files / 6.3 MB, vblang
  60 / 1.0 MB, roslyn-wiki 71 / 0.7 MB, nuget-docs 465 / 2.2 MB.
- The existing prefilter helps only the parse, not the read: a file that cannot match still
  gets read in full, and only the Markdig parse is skipped.

## Suggested fix

Measure before building anything — a full scan of 10 MB may be well inside acceptable, and an
index that must be invalidated on every `sync_source` is real complexity. If it does need
fixing, the cheap version is a per-source line index built at sync time and stored beside the
checkout, invalidated by the same commit hash the provenance envelope already carries.
