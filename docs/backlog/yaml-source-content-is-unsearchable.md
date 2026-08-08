# YAML content in a synchronized source is unsearchable

`DocsQueryService.ReadSearchSource` enumerates `*.md` and nothing else, and
`ResolveFullPath` rejects any path that does not end in `.md`. Every other file in a
synced source is invisible to `search_docs` and unreachable through `get_doc`.

## Why it matters

Some of that content is prose an agent would want. `nuget-docs` carries
`docs/resources/NuGet-FAQ.yml` and `docs/nuget-org/nuget-org-faq.yml` in Microsoft Learn's
structured-FAQ schema — question-and-answer pairs on exactly the topics the source was added
for. `roslyn-wiki` carries nine more `.yml` files.

The failure mode is the dangerous one: a query that should match returns an empty result
set, which is indistinguishable from "no such content exists". Nothing in the payload says a
whole class of file was never read.

## Evidence

- `ReadSearchSource` calls `Directory.EnumerateFiles(fullRoot, "*.md", SearchOption.AllDirectories)`.
- `ResolveFullPath` throws `DocPathNotFoundException` unless the candidate
  `EndsWith(".md", StringComparison.OrdinalIgnoreCase)`.
- `NuGet-FAQ.yml` is a `sections:` / `questions:` / `question:` / `answer:` tree, not markdown.

## Suggested fix

Not obvious, which is why this is deferred rather than done. Three parts of the pipeline
assume markdown, and all three are shared with every source: the `.md` path guard,
`MarkdownOutline.Extract` (a structured FAQ has no heading tree, so `get_doc_outline` has
nothing to return for one), and `MarkdownPager` (its atomic blocks are fences and tables).

The cheapest honest option may be narrower than full support: teach `search_docs` to read
`.yml` and report hits, while `get_doc_outline` reports that the document has no outline
rather than returning an empty one — an empty outline is another silent absence.
