# NuGet documentation as a synchronized source

## Purpose

Serve NuGet's package-management documentation from the same version-pinned, local surface that already serves the language-design and API docs.

An agent working on a .NET project hits `PackageReference` semantics, restore failures, `nuget.config` precedence, pack targets and central package management constantly, and today this server answers none of it.
The fallback is a web search, which is exactly the failure this server exists to remove: an answer whose version is unknown.

The content is `NuGet/docs.microsoft.com-nuget` — 465 searchable markdown files, 2.2 MB of prose, 17 MB checked out.
Everything the server needs to search and serve it already exists; the work is a catalog entry plus three mismatches between that machinery and the shape of Microsoft Learn content.

## What already works, unchanged

`SourceCatalog` validation, `SourceSynchronizer`, `SourceCache`, `list_sources` and `sync_source` accept the new source from configuration alone.
The `markdown: true` flag already routes a source into the document tools' default fan-out.
The provenance envelope, the no-silent-truncation rules, the cursor scheme and the never-download-from-a-query-tool rule all carry over untouched.

No code change is required to make the source *reachable*.
The changes below exist because reaching it is not the same as answering well from it.

## 1. Catalog entry

```json
"nuget-docs": {
  "repository": "NuGet/docs.microsoft.com-nuget",
  "url": "https://github.com/NuGet/docs.microsoft.com-nuget.git",
  "pin": "2b52d770c577cf48b902dc176bdd3941a811d9d2",
  "head": "main",
  "sparse": ["docs"],
  "purpose": "NuGet package management: PackageReference, restore, packing, nuget.config, CLI and MSBuild reference, nuget.org.",
  "markdown": true
}
```

`sparse: ["docs"]` yields 465 markdown files: 463 under `docs/`, plus the repository's `README.md` and `CONTRIBUTING.md`, because cone-mode sparse-checkout always includes the root directory's own files.
It also fetches 14 MB of images alongside the 2.2 MB of markdown.
Cone mode can only include directories, and `media/` folders are nested inside each topic directory, so excluding them would require switching `SourceSynchronizer` to non-cone patterns — git's documented slow path, applied to a 790 MB source, to save 14 MB.
The existing sources already carry the same kind of ballast (roslyn-wiki checks out 11 MB for 0.7 MB of markdown), so this is the established policy rather than a concession made for NuGet.

### Licensing

The repository carries both a `LICENSE` and a `LICENSE-CODE` file, the conventional split for Microsoft documentation repositories; this design makes no claim about what either says.
Nothing about the licensing invariant changes: the content is fetched into the per-user cache outside the working tree, never vendored, submoduled, or pasted into a document, so no license claim is made or needed.

`scripts/verify-no-vendored-content.cs` gains a sixth shape rule so a pasted Learn article is caught the way a pasted csharplang proposal already is.
Learn articles are recognizable by their frontmatter — an opening `---` fence containing both `ms.author:` and `ms.date:`.
The rule joins both the tracked-tree scan and the `--history` pattern list.

## 2. The document tools become source-agnostic

The three markdown tools are named for language-design documents while the service beneath them is source-agnostic.
Adding a fourth source that is not about language design makes the name false, and a tool an agent cannot discover from its name is a capability that does not exist.

| was | becomes |
|---|---|
| `search_language_docs` | `search_docs` |
| `get_language_doc` | `get_doc` |
| `get_language_doc_outline` | `get_doc_outline` |

The rename reaches the C#: `Features/LanguageDocs/` becomes `Features/Docs/`, with `DocsTool`, `DocsQueryService`, `DocRanking`, `DocLineHit`, `DocSearchResult`, `DocContentResult`, `DocOutlineResult`, `DocOutlineEntry`, `DocSectionNotFoundException` and `DocPathNotFoundException`.
Tests move to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/`.
Renaming only the three `[McpServerTool(Name = ...)]` strings would leave the code asserting these tools are about language design while they serve packaging docs — the same mismatch, one layer down.

**The wire format does not move.** Error codes (`section_not_found`, `path_not_found`, `source_not_synced`, `invalid_regex`, `invalid_cursor`, `invalid_request`, `git_timeout`, `source_invalid`), JSON field names, and the cursor `kind` strings (`lang-search`, `lang-doc`, `lang-outline`) all stay as they are.
The cursor kinds are opaque to callers and changing them would reject every cursor issued before the rename for no benefit.

No deprecated aliases.
The server is user-global and personal, MCP clients discover tools at connect time, and nothing outside this repository depends on the old names.
Six tool definitions describing three capabilities is the context tax this server exists to avoid, and a deprecated alias with no forcing function is permanent.

Prose that moves with the rename: `CLAUDE.md`, `README.md`, `docs/design/mcp-tool-surface.md`.

Prose that does **not** move: `docs/decisions.md` and `docs/superpowers/{plans,specs}/2026-08-05-language-doc-tools*`.
Those are append-only history and exempt from convention 2; the rename gets a new decisions entry instead of an edit to an old one.

## 3. Markdown pipeline

`MarkdownOutline.Extract` and `MarkdownAtomicBlocks.Find` each construct their own `new MarkdownPipelineBuilder().UsePipeTables().Build()`.
Two construction sites for one intended configuration is how they drift, and this change adds a second extension to both.
They collapse into one internal factory in `DotNetKnowledge.Markdown`:

```csharp
internal static MarkdownPipeline Default { get; } =
    new MarkdownPipelineBuilder().UsePipeTables().UseYamlFrontMatter().Build();
```

### Why the frontmatter extension is required, not cosmetic

451 of the 463 documents under `docs/` open with YAML frontmatter.
Without `UseYamlFrontMatter()`, the opening `---` parses as a thematic break, the metadata keys parse as a paragraph, and the closing `---` parses as that paragraph's **setext underline** — a level-2 heading whose text is the entire frontmatter block.

That single phantom heading corrupts the whole document:

```
BEFORE                                  AFTER
H2  "title: NuGet PackageReference      H1  "PackageReference in project files"
     in project files description:      H2  "PackageReference in project files
     ... author: nkolev92 ..."                > Project type support"
H1  "PackageReference in project files"
H2  "title: ... > Project type support"
```

Because a section path is built from the heading stack, every path in every affected document carries the frontmatter blob as a prefix.
`get_doc_outline` becomes unreadable and the `section` values it issues — which callers round-trip verbatim into `get_doc` — become unusable.

Four properties of the fix:

- **Line numbers do not move.** `MarkdownOutline` derives `StartLine` from character spans over the normalized text, and `totalLines` from `MarkdownText.SplitLines`. Neither depends on how the leading block is classified. Search hits, `get_doc` paging, and previously issued cursors are unaffected.
- **Zero regression for existing sources.** 0 of 893 csharplang files, 0 of 60 vblang files and 0 of 71 roslyn-wiki files begin with `---`. The extension is inert on every source in the catalog today.
- **Front matter is not content.** It is metadata about the document, so neither `search_docs` nor `get_doc` returns it. See [`2026-08-08-front-matter-is-not-content-design.md`](2026-08-08-front-matter-is-not-content-design.md).
- **The frontmatter block joins the atomic set** in `MarkdownAtomicBlocks`, so a page boundary cannot land mid-key — the same reasoning that already protects fenced blocks and tables.

### Learn authoring syntax is returned verbatim

`[!INCLUDE [x](../includes/x.md)]` (10 occurrences), `> [!NOTE]` alerts and `:::image` blocks are returned exactly as authored.
`get_doc`'s description gains a clause saying so, and noting that an include token names a real path the agent can fetch with a second call.

Splicing include targets inline was rejected: it would break the guarantee that a search hit's `path:line` points at the text actually on disk, and that guarantee is shared with every other source.

## 4. Ranking

`DocRanking.DocumentTypeRank` weights a hit by its repo-relative path.
Today it has three tiers: `proposals/` and `spec/` rank 0, `meetings/` ranks 2, everything else ranks 1.
Every NuGet path would fall into the middle tier, leaving intra-source ordering to the path-ordinal tiebreak — which sorts `docs/archive/` first and `docs/reference/` far down.

The noise is measured, not assumed. Of the lines matching common queries:

| query | matching lines | in `release-notes/` or `archive/` |
|---|---|---|
| `restore` | 1170 | 479 (41%) |
| `PackageReference` | 425 | 76 (18%) |
| `package source mapping` | 87 | 24 (28%) |
| `central package management` | 62 | 15 (24%) |

Against the default limit of 20, that is up to eight of the first twenty hits spent on release notes for versions long shipped — and the failure is invisible, because an agent holding twenty historical hits has no signal that better documents sit below the cut.

Four tiers replace three:

| tier | paths | meaning |
|---|---|---|
| 0 | `proposals/`, `spec/` | defines a language feature |
| 1 | `docs/api/`, `docs/concepts/`, `docs/consume-packages/`, `docs/create-packages/`, `docs/guides/`, `docs/hosting-packages/`, `docs/nuget-org/`, `docs/policies/`, `docs/quickstart/`, `docs/reference/`, `docs/visual-studio-extensibility/` | current NuGet guidance |
| 2 | everything else — roslyn-wiki, `docs/includes/`, `docs/resources/`, loose top-level files | |
| 3 | `meetings/`, `docs/release-notes/`, `docs/archive/` | discusses a thing in passing, or describes a version long shipped |

Two properties this is built to have.

**Existing sources keep their relative order exactly.** Language proposals still outrank roslyn-wiki, which still outranks LDM meeting notes. The change inserts a tier and splits "historical" out of the middle; it does not reorder anything that exists today.

**NuGet guidance ranks below language proposals, and it costs nothing.** Equal tiers fall through to the path-ordinal tiebreak, and `docs/…` sorts before `proposals/…` — so a flat tier would have made an unfiltered `search_docs("records")` lead with NuGet hits, a regression for the tools' existing use. Placing NuGet at tier 1 prevents that, and measurement says the reverse direction is free: `PackageReference`, `package restore`, `nuspec` and `central package management` each return **0 hits** across csharplang, vblang and roslyn-wiki combined. A NuGet query reaches NuGet documents because nothing in the language sources competes for it.

The NuGet patterns carry their `docs/` prefix, so `docs/reference/` cannot collide with a future source.
roslyn-wiki is the only other source rooted at `docs/`, and it holds `docs/wiki/` alone.

## 5. Testing

No test fetches the real repository; fixtures are local trees, as they are today.

| suite | change |
|---|---|
| `MarkdownOutlineTests` | frontmatter produces no heading, and a genuine setext heading still does — the two forms differ by one line, and a fix that takes both is a regression |
| `MarkdownAtomicBlocksTests` | a frontmatter block is atomic; both callers resolve the same pipeline instance |
| `DocRankingTests` | the four tiers, plus an order-preservation case asserting the existing sources rank relative to each other exactly as before |
| `DocsQueryServiceTests`, `DocsToolTests` | renamed; the fixture source gains a document with frontmatter so outline extraction and `section` round-tripping are covered end to end |
| `SourceCatalogTests` | `BundledCatalogCarriesRepositoryIdentityForProvenance` gains `NuGet/docs.microsoft.com-nuget` |
| `McpStdioTests` | asserts `search_docs`, `get_doc` and `get_doc_outline` appear in `tools/list` |

The `McpStdioTests` entry closes a real gap rather than following the rename.
That test asserts only `list_sources`, `sync_source`, `lookup_api` and `search_api` — the document tools have never been checked at the protocol boundary, so without this the rename ships with no protocol-level coverage at all.

## 6. Known gaps

Three consequences this design does not solve.
Each gets a file in `docs/backlog/` rather than a silent omission.

1. **YAML content is unsearchable.** `docs/resources/NuGet-FAQ.yml` and `docs/nuget-org/nuget-org-faq.yml` hold real answers in Learn's structured-FAQ schema, and roslyn-wiki has 9 more `.yml` files. Search reads `*.md` only, so these return nothing — a silent absence of exactly the kind the tool non-negotiables warn about. Teaching the tools to read YAML would touch the path guard, the outline extractor and the pager, all shared with every source, and a structured FAQ has no heading tree to outline.
2. **Unfiltered search has no cross-source relevance signal.** Ordering is tier, then heading-or-not, then path. Nothing measures how well a document answers the query. A fourth source makes this more visible than three did.
3. **Search cost grows.** Every unfiltered query line-scans every markdown file in every markdown source: 1024 files and 8.0 MB today, 1489 files and 10.2 MB after — 45% more files, 28% more bytes. There is no index.

## 7. Standing-record obligations

- `docs/decisions.md` — one entry for the tool rename (rejecting deprecated aliases and a NuGet-specific tool family), one for the ranking tiers (rejecting a flat tier and a backlog deferral).
- `README.md` status summary and `docs/design/mcp-tool-surface.md` tool surface updated for the rename and the new source.
- `CLAUDE.md`'s implemented-tools list updated.
