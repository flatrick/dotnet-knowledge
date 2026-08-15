# Learn FAQ documents become searchable and readable

## Purpose

Close the silent absence recorded in [`docs/backlog/yaml-source-content-is-unsearchable.md`](../../backlog/yaml-source-content-is-unsearchable.md) for the content that is actually prose.

`DocsQueryService.ReadSearchSource` enumerates `*.md` and nothing else, and `ResolveFullPath` rejects any path not ending in `.md`.
A query that should match a NuGet FAQ answer returns an empty result set today, and an empty result set is indistinguishable from "no such content exists".
That is the failure mode the tool non-negotiables exist to prevent.

The fix is narrower than the backlog file assumes, because most `.yml` in the synced sources is not documentation at all.

## What the sources actually hold

Measured across every synced source at the pinned commits:

| source | `.md` | `.yml` | of those, prose |
|---|---|---|---|
| csharplang | 890 | 0 | — |
| vblang | 60 | 0 | — |
| roslyn-wiki | 70 | 9 | 0 |
| nuget-docs | 465 | 4 | 2 |

No source carries a `.yaml` file.

roslyn-wiki's nine are Azure Pipelines definitions — `azure-pipelines.yml`, `azure-pipelines-official.yml`, `azure-pipelines-pr-validation.yml` and five siblings, plus `es-metadata.yml`.
They are CI configuration for building Roslyn, not documentation about Roslyn, and putting them in front of an agent asking about C# would degrade search rather than improve it.

nuget-docs carries four: `docs/index.yml` (a Learn hub landing page — link lists), `docs/_breadcrumb/toc.yml` (navigation), and the two that matter:

- `docs/resources/NuGet-FAQ.yml` — 6 sections, 27 questions
- `docs/nuget-org/nuget-org-faq.yml` — 4 sections, 28 questions

55 question-and-answer pairs on package restore, `nuget.config`, package sources, and nuget.org account management — exactly the topics nuget-docs was added to serve.

### The discriminator is stated in the file

Microsoft Learn stamps its schema on line 1.
Nothing has to be guessed:

| file | line 1 begins | kind |
|---|---|---|
| `NuGet-FAQ.yml` | `### YamlMime:FAQ` | prose |
| `nuget-org-faq.yml` | `### YamlMime:FAQ` | prose |
| `index.yml` | `### YamlMime:Hub` | navigation |
| `docs/_breadcrumb/toc.yml` | a `- name:` sequence entry | navigation |
| `azure-pipelines*.yml` | a comment, or a `trigger:` key | CI configuration |
| `es-metadata.yml` | a `schemaVersion:` key | repository automation |

Scope is therefore `### YamlMime:FAQ` and only that.
A `.yml` without that exact marker stays invisible, as it is today.
The gate is content, not path or source, so a Learn FAQ added to any future source is picked up with no configuration change — and a pipeline definition never is.

## Rendering is the seam

A FAQ is rendered to markdown once, at the read, and every stage downstream runs on the rendered text unchanged.

```
NuGet-FAQ.yml ──► LearnYamlMime.Detect ──► FaqDocument.Parse ──► FaqMarkdown.Render ──► markdown
                                                                                          │
        MarkdownOutline.Extract ── MarkdownLineSearch.Search ── MarkdownAtomicBlocks.Find ─┘
                                   MarkdownPager.Page ── DocumentationText
```

This is what makes the feature small.
The two-level outline, `section` round-tripping, pagination, atomic-block protection, match budgeting, truncation reporting and the cursor scheme all work for a FAQ with no new code, because by the time any of them runs the document is markdown.

The rendering is deterministic from the document text, and the document text is fixed by the commit, so a cursor issued against a FAQ stays valid exactly as long as one issued against a markdown page.
`RevisionKey` binding is unchanged.

### The output shape

Given a FAQ whose `title` is *Widget frequently-asked questions*, with a section named *General* holding one question, the rendering is:

```markdown
# Widget frequently-asked questions

<the summary block, verbatim>

## General

### How do I install a widget?

<the answer block, verbatim>
```

`title` becomes the `#`, `summary` the prose beneath it, `sections[].name` the `##`, `questions[].question` the `###`, and `questions[].answer` the body.
The existing extractor then yields the section path `General > How do I install a widget?`, and `get_doc` with that `section` returns one question and its answer instead of the whole 24 KB file.

**`metadata:` is dropped.** `author`, `ms.author`, `ms.date`, `ms.topic` and `ms.update-cycle` are metadata about the document, not the document.
That is the call [`2026-08-08-front-matter-is-not-content-design.md`](2026-08-08-front-matter-is-not-content-design.md) already made for markdown front matter, and a FAQ behaves the same way for the same reason.

**Answer bodies are rendered verbatim.** Learn authoring syntax — relative links, `> [!NOTE]` alerts, fenced code, lists — is passed through untouched, as it is for markdown pages.
One useful consequence: `NuGet-FAQ.yml`'s summary links to `../nuget-org/nuget-org-faq.yml`, and once `.yml` is reachable through `get_doc` an agent can actually follow it.

**A question is flattened to one line.** `question: |` is a block scalar and may span lines; a multi-line `###` would not be a heading at all and would break the outline. Interior whitespace collapses to single spaces.

Neither FAQ contains an ATX heading inside an answer body today, so no answer injects a spurious outline entry.
If one ever does, it behaves exactly as an unexpected heading in a markdown page behaves — inherited behavior, not a new failure mode.

## Line numbers are rendered-space, and the payload says so

A rendered document's line numbers do not index the file on disk.
Rather than assert a location that cannot be checked, every payload for a rendered document declares what it is.

`renderedFrom` — absent for markdown, `"YamlMime:FAQ"` for a rendered FAQ — is added to `DocLineHit`, `DocContentResult` and `DocOutlineResult` in `Features/Docs/DocsModels.cs`.
It lands on `DocLineHit` rather than on `DocSearchResult` because an unfiltered search fans across both kinds and the two must be distinguishable hit by hit.

The three tool descriptions in `Features/Docs/DocsTool.cs` gain one clause: when `renderedFrom` is set, `path` names a real file but the line numbers index the server's rendering of it, not its bytes.

Reporting the true `.yml` source line alongside was considered and rejected as scope: it requires carrying YamlDotNet node marks through the renderer into every hit, and the declaring field already tells an agent not to trust the number against the raw file.

## Components

A new `src/DotNetKnowledge.Yaml/` project, with `tests/DotNetKnowledge.Yaml.Tests/`, both added to `DotNetKnowledge.slnx`.
It references YamlDotNet and owns three things:

| type | contract |
|---|---|
| `LearnYamlMime.Detect(string text)` | the schema name on line 1, or `null` |
| `FaqDocument.Parse(string text)` | plain data: `Title`, `Summary`, `Sections[]` of `Name` and `Questions[]` of `Question`/`Answer` |
| `FaqMarkdown.Render(FaqDocument)` | a markdown string |

Its contract is the mirror of `DotNetKnowledge.Markdown`'s — YAML text in, markdown text out — which is why it is a sibling project rather than a folder inside it.
`DotNetKnowledge.Markdown`'s stated design is "input is markdown text, output is plain data" (`docs/decisions.md`), and a YAML reader inverts both halves of that.
YamlDotNet stays behind this boundary and is referenced by no other project.

YamlDotNet rather than a hand-rolled reader: the schema is shallow, but block scalars, quoting styles, escapes and indentation rules are exactly the corners a hand-rolled reader gets wrong, and getting one wrong silently corrupts answer prose — the same class of failure this change exists to remove.

## Changes to `DocsQueryService`

Three touch points, all in `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`.
A single private helper — read the file, and if its extension is `.yml` or `.yaml`, detect, parse and render — is the only place either library meets the other.

**`ResolveFullPath`** — the `.md` extension guard accepts `.md`, `.yml` and `.yaml`.
A `.yml` that fails `Detect` throws `DocPathNotFoundException`, the same exception an unknown path throws today, because a pipeline definition is not a document this server serves.
`.yaml` is accepted despite no source carrying one: the content gate is what decides, so the extension list costs nothing and avoids a false absence if Learn ever ships one.

**`ReadSearchSource`** — enumerates `*.md`, `*.yml` and `*.yaml`.
Rendering happens **before** the cheap per-line prefilter, so the prefilter tests the same text `MarkdownLineSearch` will match against.
Testing the raw YAML instead would skip a file whose only match is a word the rendering produces, reintroducing the silent absence one layer down.
`relativePath` remains the real `.yml` path, so a hit round-trips into `get_doc`.

**`ReadDocument`** — renders on the way in, for `get_doc` and `get_doc_outline` alike.

`ResolveSourceNames` and `ValidateSource` are untouched: FAQ detection is content-based, so no `sources.json` field and no per-source opt-in is involved.
`nuget-docs` is already `markdown: true`.

### Ranking

`docs/resources/` sits in tier 2 under the tiers set by [`2026-08-08-nuget-docs-source-design.md`](2026-08-08-nuget-docs-source-design.md), and `docs/nuget-org/` in tier 1.
Neither moves.
An FAQ answer is guidance, not a version-specific note, so nothing about the existing tiers misplaces it, and re-tiering for two files would be a change made without evidence.

## Error handling

A `.yml` carrying the FAQ marker that then fails to parse must not be silently skipped — skipping it is the disease, one layer further in.

`FaqDocument.Parse` catches YamlDotNet's exceptions at the library boundary and throws a typed `FaqParseException` naming what failed.
The two callers differ, because their blast radius differs:

- **`get_doc` / `get_doc_outline`** — the exception surfaces. The caller named one document and gets told that document could not be read, which is strictly better than a plausible-looking absence.
- **`search_docs`** — one bad file must not fail a fan-out across four sources. `DocSearchResult` gains `skippedDocuments`, a list of path and reason, populated when a FAQ-marked file cannot be parsed. This mirrors `skippedDeclarations` on the API payloads, which exists for exactly this situation and for exactly this reason.

`skippedDocuments` is absent when empty, so an ordinary search pays nothing for it.

Shapes that are valid, not errors: a FAQ with no `sections`, an empty `summary`, a section with no questions, and a question with an empty answer.
Each renders to what it says — a heading with nothing beneath it — because the file genuinely says that.

## Testing

No test reads the real cache; fixtures are local trees, as they are today.

| suite | coverage |
|---|---|
| `LearnYamlMimeTests` | marker present, absent, a different `YamlMime`, leading BOM, leading blank lines, a file whose first line merely resembles the marker |
| `FaqDocumentTests` | nominal parse; missing `sections`; a malformed document throws `FaqParseException`; `metadata` is not carried |
| `FaqMarkdownTests` | heading levels and section paths; a multi-line question flattens to one line; answer body preserved verbatim including fences and alerts; empty section and empty answer |
| `DocsQueryServiceTests` | search finds a hit inside an answer and reports `renderedFrom`; a non-FAQ `.yml` in the same fixture is never searched; `get_doc_outline` returns the two-level tree; `get_doc` with a section returns one question and answer; `get_doc` without a section returns the whole rendering; a malformed FAQ appears in `skippedDocuments` rather than vanishing; a `.md` hit still has no `renderedFrom` |
| `DocsToolTests` | `renderedFrom` and `skippedDocuments` appear in the serialized payloads |

Verification beyond the suites: `dotnet build` and `dotnet test` on `DotNetKnowledge.slnx`, then a live smoke over the real cache confirming a known answer — a `nuget.config` question from `NuGet-FAQ.yml` — is findable through `search_docs` and readable through `get_doc` at its section path.

## Standing-record obligations

- Delete `docs/backlog/yaml-source-content-is-unsearchable.md` and its row in `docs/backlog/README.md`; `git log` is the record.
- `docs/decisions.md` — one entry: scope gated on `YamlMime:FAQ` rather than all `.yml` (rejecting both the backlog file's own suggestion and the "no outline for a FAQ" fallback), and rendered-space line numbers declared through `renderedFrom` rather than mapped back to source marks.
- `docs/design/mcp-tool-surface.md` — the document tools now serve rendered FAQ documents; `renderedFrom` and `skippedDocuments` join the payload descriptions.
- `README.md` status summary and `CLAUDE.md`'s architecture notes mention `DotNetKnowledge.Yaml` alongside `DotNetKnowledge.Markdown`.
- `scripts/verify-no-vendored-content.cs` — the `learn-article` shape rule is `^ms\.(author|date|topic):\s*\S` under `RegexOptions.Multiline`, so the key must sit at column 0. A FAQ nests `ms.author:`, `ms.date:` and `ms.topic:` two spaces under `metadata:`, so a pasted FAQ evades the rule today. Allowing leading whitespace in that one pattern closes it, and costs nothing: no legitimate tracked file carries an indented `ms.date:` either.
