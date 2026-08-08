# Front matter is metadata, not content

## Purpose

Stop `search_docs` matching inside YAML front matter and stop `get_doc` returning it.

Microsoft Learn articles open with a YAML block carrying `title`, `description`, `author`,
`ms.author`, `ms.date` and `ms.topic`. 408 of the 463 documents under `nuget-docs`' `docs/` tree have
one. None of it is documentation an agent asked for, and it is currently both searched and returned.

This supersedes the "frontmatter stays searchable" property in
[`2026-08-08-nuget-docs-source-design.md`](2026-08-08-nuget-docs-source-design.md), which argued that
suppressing it would manufacture a silent absence. Measurement after shipping shows the trade runs
the other way.

## Why this is a defect rather than a preference

**The noise dominates the signal.** Measured over `nuget-docs` at the pinned commit:

| query | total matching lines | from front-matter keys |
|---|---|---|
| `description` | 521 | 451 (87%) |
| `title` | 482 | 451 (94%) |
| `author` | 1105 | 868 (79%) |

408 files times six keys is roughly 2,400 metadata lines competing with real prose for a result page
capped at 20.

**A front-matter hit is one an agent cannot follow.** Those hits carry an empty `sectionPath`,
because no heading covers them. Once `get_doc` starts after the front matter — which is the half of
this that is not in dispute — they name a line no call will return. A search result pointing at
unfetchable content is worse than the absence that keeping it searchable was meant to avoid.

**Nothing else regresses.** csharplang, vblang and roslyn-wiki have zero files beginning with `---`,
so excluding front matter is inert on every source that existed before `nuget-docs`.

What is genuinely lost: a query for an `ms.author` handle or an `ms.date` stops returning anything.
That is metadata about a document, not an answer this server promises.

## 1. One place that knows where the body starts

Both tools must agree, so the rule lives in one function in `DotNetKnowledge.Markdown`:

```csharp
public static class MarkdownFrontMatter
{
    /// The 1-based line where the document's content begins: the first non-blank line after a
    /// leading YAML front-matter block, or 1 when the document has none.
    public static int BodyStartLine(string markdown);
}
```

It returns 11 for `docs/reference/nuspec.md` — front matter occupies lines 1 to 9 and line 10 is
blank — and 1 for a csharplang proposal.

**Skipping the blank lines is deliberate.** Starting at the line after the closing fence would open
every Learn fetch with a blank line. Line numbers stay honest regardless, because `startLine` is a
reported field rather than something the caller infers.

**Front matter with no body** returns `lines.Length + 1`, which makes the fetch range empty and
yields empty text instead of an error. No such file exists in `nuget-docs` today; the guard is one
comparison and the alternative is an exception on a document that is merely unusual.

## 2. `get_doc`

`DocsQueryService.GetDocAsync`'s whole-document branch changes `rangeStart = 1` to
`rangeStart = MarkdownFrontMatter.BodyStartLine(text)`.

Sectioned fetches are untouched: a section already begins at its heading, so front matter was never
inside one. Cursors need no change — a `lang-doc` cursor's offset is already a 1-based line number,
and the scope key is unchanged, so cursors issued before this change still decode and still point at
the same lines.

## 3. `search_docs`

`MarkdownLineSearch.Search` skips lines before `BodyStartLine`. Its two overloads funnel through one
body, so this is a single change point, and the rule then holds for every source and every caller.

The file prefilter in `DocsQueryService.ReadSearchSource` stays as it is. It is a superset check, so
a file whose only match is in front matter now costs one wasted Markdig parse and returns nothing —
correct, and not worth a second implementation of the same rule to optimize.

## 4. What deliberately does not change

`MarkdownAtomicBlocks` keeps its front-matter entry, and its tests stay.

Nothing in this server can page across front matter now, so that entry is unreachable through these
tools. It is kept because `MarkdownAtomicBlocks` is a general-purpose function answering "which
blocks must never be split", and front matter is one. Narrowing a library's contract to match one
caller's range choice is the wrong trade: the next caller inherits a silent hazard.

## 5. Testing

| suite | cases |
|---|---|
| `MarkdownFrontMatterTests` (new) | Learn document → 11; no front matter → 1; content on the line straight after the closing fence → that line; front matter with no body → past the end; adjacent `---` fences, which Markdig reads as two thematic breaks rather than front matter → 1 |
| `MarkdownLineSearchTests` | a front-matter key yields no hit; a body line still does, at its correct line number |
| `DocsQueryServiceTests` | whole-document `get_doc` on the Learn fixture begins at the H1 with `startLine` naming it and no `ms.author` in the text; `search_docs` over that fixture returns no front-matter hit |
| existing suites | counts and behavior unchanged for the three language sources |

No test fetches a real upstream repository; fixtures stay local `git init` trees.

## 6. Documents

`get_doc` and `search_docs` gain a clause in their tool descriptions: front matter is metadata, and
is neither searched nor returned.

A `docs/decisions.md` entry records the reversal with the figures above, naming the entry it
supersedes — the point of that file is that a superseded decision stays visible.

Two now-false lines in [`2026-08-08-nuget-docs-source-design.md`](2026-08-08-nuget-docs-source-design.md)
are corrected: the "Frontmatter stays searchable" property, and the sentence listing
`docs/domain/csharplang-map.md` and `docs/domain/vblang-map.md` as prose moving with the tool rename,
which the corpus-extraction commit deleted before that rename happened.
