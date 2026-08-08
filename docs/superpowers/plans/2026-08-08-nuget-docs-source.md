# NuGet Documentation Source Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Serve `NuGet/docs.microsoft.com-nuget` from the MCP server's document tools, and rename those tools to reflect that they are no longer language-design-only.

**Architecture:** The server already searches and serves any `markdown: true` source in `sources.json` with no code change, so the work is three mismatches plus a catalog entry. Markdig gains the YAML front-matter extension (Learn front matter otherwise parses as a phantom setext heading that corrupts every outline and section path); `DocRanking` gains a fourth tier (release notes and archive otherwise outrank current guidance); and the three tools plus their feature namespace are renamed to drop "language".

**Tech Stack:** C# / .NET 10, MSTest, Markdig 1.3.2, ModelContextProtocol server SDK, git (sparse-checkout, blobless clone).

## Global Constraints

- **Pre-existing test failures — do not chase these.** Baseline on this branch: `DotNetKnowledge.Markdown.Tests` 20 passed / 0 failed. `DotNetKnowledge.Mcp.Tests` 100 passed / **3 failed**: `GitCommandRunnerTests.InheritedStandardInputReproducesTheHang`, `GitCommandRunnerTests.TimeoutNamesTheCommandThatExceededItsTier`, `GitCommandRunnerTests.TimeoutNamesTheTierThatExpired`. They fail before any change in this plan. A task is done when its own tests pass and these three are still the only failures.
- **Every project build requires 0 errors AND 0 warnings.** `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors` are inherited from the root `Directory.Build.props`. Never add `#pragma warning disable` to get past a warning.
- **Build/test commands:** `dotnet build DotNetKnowledge.slnx` and `dotnet test DotNetKnowledge.slnx`. Never the corpus solution — this plan does not touch `examples/`.
- **Never pipe a command through `tail` or `head`.** Redirect full output: `<command> &> .scratch/<what>-$(date +%Y%m%d-%H%M).log`, then search the log with `rg`. `.scratch/` is gitignored and confirmed.
- **stdout is the MCP protocol channel.** Never add a logging provider that writes to stdout.
- **No test fetches a real upstream repository.** Every fixture is a local `git init` tree, as the existing tests already do.
- **The wire format does not change.** Error codes (`section_not_found`, `path_not_found`, `source_not_synced`, `invalid_regex`, `invalid_cursor`, `invalid_request`, `git_timeout`, `source_invalid`), every JSON field name, and the cursor `kind` strings (`lang-search`, `lang-doc`, `lang-outline`) stay exactly as they are. The cursor kinds are opaque to callers; changing them would reject every previously issued cursor for no benefit.
- **American English**, LF line endings, UTF-8.
- **Documents state current truth only.** No "previously named X" footers. `docs/decisions.md` and `docs/gotchas.md` are the exceptions: append-only, newest-first, never edited.
- Work happens on branch `nuget-docs-source` in the worktree `.worktrees/nuget-docs-source`. Commit after each task. Do not push and do not open a PR.

## File Structure

**Created:**

| path | responsibility |
|---|---|
| `src/DotNetKnowledge.Markdown/MarkdownPipelines.cs` | The single Markdig pipeline configuration this library parses with |
| `docs/backlog/yaml-source-content-is-unsearchable.md` | Known gap: `.yml` content is invisible to search |
| `docs/backlog/cross-source-search-has-no-relevance-signal.md` | Known gap: ordering is tier-then-path, never query relevance |
| `docs/backlog/document-search-rescans-every-file.md` | Known gap: no index; every query line-scans every file |

**Renamed** (`src/DotNetKnowledge.Mcp/Features/LanguageDocs/` → `Features/Docs/`, tests likewise):

| from | to |
|---|---|
| `LanguageDocsTool.cs` | `DocsTool.cs` |
| `LanguageDocsQueryService.cs` | `DocsQueryService.cs` |
| `LanguageDocRanking.cs` | `DocRanking.cs` |
| `LanguageDocsModels.cs` | `DocsModels.cs` |
| `tests/…/Features/LanguageDocs/LanguageDocsToolTests.cs` | `tests/…/Features/Docs/DocsToolTests.cs` |
| `tests/…/Features/LanguageDocs/LanguageDocsQueryServiceTests.cs` | `tests/…/Features/Docs/DocsQueryServiceTests.cs` |
| `tests/…/Features/LanguageDocs/LanguageDocRankingTests.cs` | `tests/…/Features/Docs/DocRankingTests.cs` |

**Modified:** `MarkdownOutline.cs`, `MarkdownAtomicBlocks.cs`, `Program.cs`, `SourceCatalog.cs` (doc comment only), `sources.json`, `scripts/verify-no-vendored-content.cs`, `tests/…/Sources/SourceCatalogTests.cs`, `tests/…/Protocol/McpStdioTests.cs`, `tests/DotNetKnowledge.Markdown.Tests/MarkdownOutlineTests.cs`, `tests/DotNetKnowledge.Markdown.Tests/MarkdownAtomicBlocksTests.cs`, `CLAUDE.md`, `README.md`, `docs/design/mcp-tool-surface.md`, `docs/decisions.md`.

**Deliberately not modified:** `docs/superpowers/plans/2026-08-05-language-doc-tools.md` and `docs/superpowers/specs/2026-08-05-language-doc-tools-design.md` are historical records of completed work. `docs/decisions.md` is append-only — add an entry, never edit one.

---

### Task 1: One Markdig pipeline, with YAML front matter

Learn articles open with YAML front matter. Under the current pipeline the opening `---` parses as a thematic break, the metadata keys as a paragraph, and the closing `---` as that paragraph's **setext underline** — producing a level-2 heading whose text is the whole front-matter block. Every section path below it inherits that text as a prefix, which makes `get_doc_outline` unreadable and the `section` values it issues unusable.

This is measured, not assumed. A probe against Markdig 1.3.2 produced, for a document whose real headings are on lines 7 and 11:

```
WITHOUT UseYamlFrontMatter:  H2 startLine=2 text="title: Sample\nms.author: someone\nms.date: 02/12/2026"
                             H1 startLine=7   H2 startLine=11
WITH    UseYamlFrontMatter:  H1 startLine=7   H2 startLine=11
                             YamlFrontMatterBlock Line=0 (0-based), Lines.Count=3
```

The real headings report identical start lines either way, which is why line numbers, search hits, paging and previously issued cursors are unaffected.

`MarkdownOutline.Extract` and `MarkdownAtomicBlocks.Find` each build their own pipeline today. Two construction sites for one intended configuration is how they drift, and this change adds an extension both need.

**Files:**
- Create: `src/DotNetKnowledge.Markdown/MarkdownPipelines.cs`
- Modify: `src/DotNetKnowledge.Markdown/MarkdownOutline.cs:17-19`
- Modify: `src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs:17-19`, and add a block loop after the table loop at line 38
- Test: `tests/DotNetKnowledge.Markdown.Tests/MarkdownOutlineTests.cs`
- Test: `tests/DotNetKnowledge.Markdown.Tests/MarkdownAtomicBlocksTests.cs`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: `internal static class MarkdownPipelines` with `public static MarkdownPipeline Default { get; }` in namespace `DotNetKnowledge.Markdown`. Behavioral contract later tasks rely on: `MarkdownOutline.Extract` returns no `MarkdownHeading` for a YAML front-matter block, and `MarkdownAtomicBlocks.Find` returns a `MarkdownBlockRange` covering it.

`MarkdownBlockRange.EndLine` is **exclusive** — the first line *after* the block. Confirmed by the existing fenced-code arithmetic (`lastContentLine + 3`: 0-based last content → 1-based is +1, closing fence is +2, exclusive end is +3) and by `MarkdownPager.Page`, which extends a page with `stopLine = block.EndLine`.

- [ ] **Step 1: Write the failing outline test**

Add to `tests/DotNetKnowledge.Markdown.Tests/MarkdownOutlineTests.cs`, after the `SampleDocument` constant:

```csharp
    // A Microsoft Learn article's opening block. Without the YAML front-matter extension the
    // closing "---" is a setext underline for the metadata paragraph above it, so the whole block
    // parses as a level-2 heading and prefixes every section path in the document.
    private const string FrontMatterDocument =
        "---\n" +
        "title: Sample\n" +
        "ms.author: someone\n" +
        "ms.date: 02/12/2026\n" +
        "---\n" +
        "\n" +
        "# Heading One\n" +
        "\n" +
        "Body text.\n" +
        "\n" +
        "## Heading Two\n" +
        "\n" +
        "More body.\n";

    // A genuine setext heading is one line away from the front-matter form above. A fix that
    // silences front matter by also silencing this is a regression, so both are asserted.
    private const string SetextDocument =
        "Real Setext Heading\n" +
        "---\n" +
        "\n" +
        "Body.\n";
```

And these test methods:

```csharp
    [TestMethod]
    public void ExtractIgnoresYamlFrontMatter()
    {
        var headings = MarkdownOutline.Extract(FrontMatterDocument);

        Assert.HasCount(2, headings);
        Assert.AreEqual(1, headings[0].Level);
        Assert.AreEqual("Heading One", headings[0].Text);
        Assert.AreEqual("Heading One", headings[0].Path);
        Assert.AreEqual(7, headings[0].StartLine);
        Assert.AreEqual("Heading One > Heading Two", headings[1].Path);
        Assert.AreEqual(11, headings[1].StartLine);
    }

    [TestMethod]
    public void ExtractStillRecognizesSetextHeadings()
    {
        var headings = MarkdownOutline.Extract(SetextDocument);

        Assert.HasCount(1, headings);
        Assert.AreEqual(2, headings[0].Level);
        Assert.AreEqual("Real Setext Heading", headings[0].Text);
        Assert.AreEqual(1, headings[0].StartLine);
    }
```

- [ ] **Step 2: Run the tests to verify the first fails and the second passes**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~MarkdownOutlineTests" &> .scratch/t1-red-$(date +%Y%m%d-%H%M).log
```

Expected: `ExtractIgnoresYamlFrontMatter` FAILS — `Assert.HasCount` reports 3 headings, not 2, because the front matter produced a phantom level-2 heading. `ExtractStillRecognizesSetextHeadings` PASSES already; it is a guard, and it must keep passing after Step 4.

- [ ] **Step 3: Create the shared pipeline**

Create `src/DotNetKnowledge.Markdown/MarkdownPipelines.cs`:

```csharp
using Markdig;

namespace DotNetKnowledge.Markdown;

/// <summary>
/// The one Markdig configuration this library parses with. Every parse site resolves it from here:
/// <see cref="MarkdownOutline"/> and <see cref="MarkdownAtomicBlocks"/> must agree on what the
/// document is, and two builders configured separately is how they stop agreeing.
/// </summary>
/// <remarks>
/// <c>UseYamlFrontMatter</c> is not cosmetic. Without it a Microsoft Learn article's closing
/// <c>---</c> is a setext underline for the metadata paragraph above it, so the front matter
/// becomes a level-2 heading whose text is the whole block — and every section path beneath it
/// inherits that text. Classifying the block does not move any line number: heading positions come
/// from character spans over the normalized text, which are identical either way.
/// </remarks>
internal static class MarkdownPipelines
{
    public static MarkdownPipeline Default { get; } =
        new MarkdownPipelineBuilder()
            .UsePipeTables()
            .UseYamlFrontMatter()
            .Build();
}
```

- [ ] **Step 4: Point both parse sites at it**

In `src/DotNetKnowledge.Markdown/MarkdownOutline.cs`, replace:

```csharp
        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var document = Markdig.Markdown.Parse(normalized, pipeline);
```

with:

```csharp
        var document = Markdig.Markdown.Parse(normalized, MarkdownPipelines.Default);
```

In `src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs`, make the same replacement, and add `using Markdig.Extensions.Yaml;` to the using block. Then add this loop after the `Table` loop and before `return blocks.OrderBy(...)`:

```csharp
        // Front matter is a single semantic unit like a fence or a table: a page boundary inside it
        // splits a key from its value. Same arithmetic as the fenced case — Line is the 0-based
        // opening "---", the last content line plus three is the exclusive end past the closing one.
        // An empty block ("---\n---") has no content lines, so it falls back to its own start.
        foreach (var frontMatter in document.Descendants<YamlFrontMatterBlock>())
        {
            var lastContentLine = frontMatter.Lines.Count > 0
                ? frontMatter.Lines.Lines[frontMatter.Lines.Count - 1].Line
                : frontMatter.Line;
            blocks.Add(new MarkdownBlockRange(frontMatter.Line + 1, lastContentLine + 3));
        }
```

- [ ] **Step 5: Write the atomic-block test**

Add to `tests/DotNetKnowledge.Markdown.Tests/MarkdownAtomicBlocksTests.cs`:

```csharp
    [TestMethod]
    public void FindTreatsYamlFrontMatterAsAtomic()
    {
        const string document =
            "---\n" +
            "title: Sample\n" +
            "ms.author: someone\n" +
            "ms.date: 02/12/2026\n" +
            "---\n" +
            "\n" +
            "# Heading One\n" +
            "\n" +
            "Body text.\n";

        var blocks = MarkdownAtomicBlocks.Find(document);

        // Opening "---" is line 1, content is lines 2-4, closing "---" is line 5, and EndLine is
        // exclusive — so the range is [1, 6).
        Assert.HasCount(1, blocks);
        Assert.AreEqual(1, blocks[0].StartLine);
        Assert.AreEqual(6, blocks[0].EndLine);
    }

    [TestMethod]
    public void FindTreatsMinimalYamlFrontMatterAsAtomic()
    {
        // The smallest input Markdig 1.3.2 actually parses as front matter: one content line,
        // even a blank one. This pins the arithmetic at its lower boundary.
        const string document =
            "---\n" +
            "\n" +
            "---\n" +
            "\n" +
            "# Heading One\n";

        var blocks = MarkdownAtomicBlocks.Find(document);

        Assert.HasCount(1, blocks);
        Assert.AreEqual(1, blocks[0].StartLine);
        Assert.AreEqual(4, blocks[0].EndLine);
    }

    [TestMethod]
    public void FindDoesNotTreatAdjacentFencesAsFrontMatter()
    {
        // Measured against Markdig 1.3.2: "---" on adjacent lines is two thematic breaks, not an
        // empty front-matter block. This is the boundary the zero-lines guard in Find defends, and
        // it is why that guard is never reached today.
        const string document =
            "---\n" +
            "---\n" +
            "\n" +
            "# Heading One\n";

        var blocks = MarkdownAtomicBlocks.Find(document);

        Assert.IsEmpty(blocks);
    }
```

- [ ] **Step 6: Run the full Markdown suite**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DotNetKnowledge.Markdown.Tests" &> .scratch/t1-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS, 24 total (the 20 baseline plus the 4 added). Read the last 20 lines of the log and confirm `Failed: 0`. If `ExtractStillRecognizesSetextHeadings` broke, the fix took both forms and is wrong.

- [ ] **Step 7: Run the whole suite to confirm nothing else moved**

```bash
dotnet test DotNetKnowledge.slnx &> .scratch/t1-full-$(date +%Y%m%d-%H%M).log
```

Expected: `DotNetKnowledge.Mcp.Tests` still 100 passed / 3 failed, and the 3 are the `GitCommandRunnerTests` trio from Global Constraints. No source in the catalog today uses front matter (0 of 893 csharplang files, 0 of 60 vblang, 0 of 71 roslyn-wiki), so nothing in the MCP suite should change.

- [ ] **Step 8: Commit**

```bash
git add src/DotNetKnowledge.Markdown/ tests/DotNetKnowledge.Markdown.Tests/
git commit -m "Parse YAML front matter through one shared Markdig pipeline"
```

---

### Task 2: Rename the document feature to be source-agnostic

The three tools are named for language-design documents while the service beneath them is source-agnostic. A tool an agent cannot discover from its name is a capability that does not exist, so the rename reaches the C# rather than stopping at the `[McpServerTool(Name = ...)]` strings.

This task is one commit because a partial rename does not compile. It is a pure rename plus three description edits: no behavior changes, and every existing test must pass unchanged apart from the two that assert on tool names inside error messages.

**Files:**
- Rename: the four `src/DotNetKnowledge.Mcp/Features/LanguageDocs/*.cs` files and the three test files, per the File Structure table
- Modify: `src/DotNetKnowledge.Mcp/Program.cs:2` and `:20`
- Modify: `src/DotNetKnowledge.Mcp/Sources/SourceCatalog.cs:15-16` (doc comment)
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs:137`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs:368`

**Interfaces:**
- Consumes: `MarkdownPipelines.Default` behavior from Task 1 (indirectly, through `MarkdownOutline`).
- Produces: namespace `DotNetKnowledge.Mcp.Features.Docs` containing `DocsTool`, `DocsQueryService` (constructor `DocsQueryService(SourceCatalog, SourceCache, SourceSynchronizer)`; methods `SearchAsync`, `GetDocAsync`, `GetOutlineAsync` with unchanged signatures), `DocRanking.Order(IEnumerable<DocLineHit>, string)`, and records `DocLineHit(string Path, int Line, string Text, bool IsTruncated, string SectionPath, SourceProvenance Source)`, `DocSearchResult`, `DocContentResult`, `DocOutlineEntry`, `DocOutlineResult`, plus `DocPathNotFoundException` and `DocSectionNotFoundException`. Tool names `search_docs`, `get_doc`, `get_doc_outline`. Tasks 3, 4 and 7 depend on these exact names.

- [ ] **Step 1: Move the files**

```bash
git mv src/DotNetKnowledge.Mcp/Features/LanguageDocs src/DotNetKnowledge.Mcp/Features/Docs
git mv src/DotNetKnowledge.Mcp/Features/Docs/LanguageDocsTool.cs src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs
git mv src/DotNetKnowledge.Mcp/Features/Docs/LanguageDocsQueryService.cs src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs
git mv src/DotNetKnowledge.Mcp/Features/Docs/LanguageDocRanking.cs src/DotNetKnowledge.Mcp/Features/Docs/DocRanking.cs
git mv src/DotNetKnowledge.Mcp/Features/Docs/LanguageDocsModels.cs src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs
git mv tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs tests/DotNetKnowledge.Mcp.Tests/Features/Docs
git mv tests/DotNetKnowledge.Mcp.Tests/Features/Docs/LanguageDocsToolTests.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs
git mv tests/DotNetKnowledge.Mcp.Tests/Features/Docs/LanguageDocsQueryServiceTests.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs
git mv tests/DotNetKnowledge.Mcp.Tests/Features/Docs/LanguageDocRankingTests.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocRankingTests.cs
```

- [ ] **Step 2: Rename the symbols**

Apply this mapping across `src/DotNetKnowledge.Mcp/` and `tests/DotNetKnowledge.Mcp.Tests/`. Order matters — apply the longest identifiers first so `LanguageDocsTool` is not partly rewritten by the `LanguageDoc` rule.

| from | to |
|---|---|
| `Features.LanguageDocs` | `Features.Docs` |
| `GetLanguageDocOutline` | `GetDocOutline` |
| `SearchLanguageDocs` | `SearchDocs` |
| `GetLanguageDoc` | `GetDoc` |
| `LanguageDocsQueryServiceTests` | `DocsQueryServiceTests` |
| `LanguageDocsQueryService` | `DocsQueryService` |
| `LanguageDocSectionNotFoundException` | `DocSectionNotFoundException` |
| `LanguageDocPathNotFoundException` | `DocPathNotFoundException` |
| `LanguageDocOutlineEntry` | `DocOutlineEntry` |
| `LanguageDocOutlineResult` | `DocOutlineResult` |
| `LanguageDocContentResult` | `DocContentResult` |
| `LanguageDocSearchResult` | `DocSearchResult` |
| `LanguageDocRankingTests` | `DocRankingTests` |
| `LanguageDocRanking` | `DocRanking` |
| `LanguageDocLineHit` | `DocLineHit` |
| `LanguageDocsToolTests` | `DocsToolTests` |
| `LanguageDocsTool` | `DocsTool` |

Then in `src/DotNetKnowledge.Mcp/Program.cs` change the using on line 2 to `using DotNetKnowledge.Mcp.Features.Docs;` and the registration on line 20 to `builder.Services.AddSingleton<DocsQueryService>();`.

Verify nothing was missed:

```bash
rg --hidden -n "LanguageDoc" src/ tests/ --glob '!**/obj/**'
```

Expected: no output.

- [ ] **Step 3: Rename the tools and update their descriptions**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs`, replace the three attribute/description blocks.

`search_language_docs` becomes:

```csharp
    [McpServerTool(Name = "search_docs", ReadOnly = true, Idempotent = true)]
    [Description(
        "Search synchronized documentation sources - C# and VB.NET language design (proposals, " +
        "spec, LDM meeting notes), Roslyn contributor docs, and NuGet package management - by " +
        "literal substring or, with regex: true, a .NET regex evaluated with the non-backtracking " +
        "engine. Returns path:line hits with the matched line and a server-issued section heading " +
        "path, never file bodies; call get_doc for content. A long matched line is capped at 300 " +
        "characters with isTruncated saying so; the text carries no marker, so any ellipsis in it " +
        "is the source's own. Fetch the document for the full text.")]
```

`get_language_doc` becomes:

```csharp
    [McpServerTool(Name = "get_doc", ReadOnly = true, Idempotent = true)]
    [Description(
        "Fetch a synchronized documentation file by its repo-relative path. " +
        "Pass section as a heading path exactly as returned by search_docs or " +
        "get_doc_outline to fetch just that section; omit it for the whole document. " +
        "Heading paths are normalized - inline markdown such as backticks is stripped, and levels " +
        "are joined with \" > \" - so build them from those tools rather than from raw markdown, " +
        "where \"## `Span<char>` support\" reads as \"Span<char> support\". " +
        "Pages by an approximate character budget (limit) that never splits a fenced code block " +
        "or a table. Text is returned exactly as authored: Microsoft Learn syntax such as " +
        "[!INCLUDE [x](../includes/x.md)], > [!NOTE] alerts and :::image blocks is not resolved, " +
        "and an include token names a real path this tool can fetch.")]
```

`get_language_doc_outline` becomes:

```csharp
    [McpServerTool(Name = "get_doc_outline", ReadOnly = true, Idempotent = true)]
    [Description(
        "Return a synchronized documentation file's heading tree, no bodies: " +
        "each entry's level, text, and section path - the path get_doc's section " +
        "parameter accepts verbatim. YAML front matter, which Microsoft Learn articles carry, is " +
        "not a heading and does not appear. Paginated like the other tools.")]
```

The C# method names were already covered by the mapping in Step 2 (`SearchDocs`, `GetDoc`, `GetDocOutline`), so after this step no identifier or string in the file says "language".

- [ ] **Step 4: Update the two user-facing error messages**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`, the exception messages name tools an agent is told to call, so they must name the new ones:

```csharp
public sealed class DocPathNotFoundException : Exception
{
    public DocPathNotFoundException(string path, string sourceName)
        : base($"'{path}' was not found in '{sourceName}'. Call search_docs, or list_sources for cacheDir.")
```

```csharp
public sealed class DocSectionNotFoundException : Exception
{
    public DocSectionNotFoundException(string section, string path, string sourceName)
        : base($"Section '{section}' was not found in '{path}' ({sourceName}). " +
               "Call get_doc_outline to see valid section paths for this document.")
```

In `src/DotNetKnowledge.Mcp/Sources/SourceCatalog.cs` line 15, update the doc comment on the `Markdown` parameter:

```csharp
/// Whether <c>search_docs</c>/<c>get_doc</c>/<c>get_doc_outline</c> can
```

- [ ] **Step 5: Update the two tests that assert on those messages**

In `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs:137` and `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs:368`, change the expected substring `"get_language_doc_outline"` to `"get_doc_outline"`.

- [ ] **Step 6: Build and run the whole suite**

```bash
dotnet build DotNetKnowledge.slnx &> .scratch/t2-build-$(date +%Y%m%d-%H%M).log
dotnet test DotNetKnowledge.slnx &> .scratch/t2-test-$(date +%Y%m%d-%H%M).log
```

Expected: build 0 errors, 0 warnings. Tests: `Markdown.Tests` 24 passed; `Mcp.Tests` 100 passed / 3 failed with only the `GitCommandRunnerTests` trio. A rename that changes any other count has changed behavior and is wrong.

- [ ] **Step 7: Confirm no stale name survives anywhere in code**

```bash
rg --hidden -n "search_language_docs|get_language_doc|LanguageDoc" src/ tests/ --glob '!**/obj/**'
```

Expected: no output.

- [ ] **Step 8: Commit**

```bash
git add src/ tests/
git commit -m "Rename the document tools and feature namespace to drop 'language'"
```

---

### Task 3: Prove the front-matter fix end to end through the query service

Task 1 fixed the parse and asserted it against `MarkdownOutline` directly. That is the unit level. What an agent actually calls is `get_doc_outline` and `get_doc(section:)`, which read a file out of a synced source, extract the outline, and match a caller-supplied section path against it. A front-matter document has never been through that path, and the round trip — outline issues a section path, `get_doc` accepts it verbatim — is the contract the fix exists to protect.

**Files:**
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `DocsQueryService`, `DocOutlineResult`, `DocContentResult` from Task 2; the existing private helper `CreateServiceWithDocumentAsync(string root, string fileName, string content)`, which `git init`s a fixture repo containing `docs/<fileName>`, writes a one-source catalog with `markdown: true`, syncs it, and returns a service.
- Produces: nothing later tasks consume.

- [ ] **Step 1: Write the failing tests**

Add the fixture constant near the other document constants in `DocsQueryServiceTests`:

```csharp
    // A Microsoft Learn article. Every NuGet document opens this way, and 451 of the 463 under
    // docs/ do. Front matter must not become a heading, and the section path the outline issues
    // must be the one get_doc accepts back.
    private const string LearnArticle =
        "---\n" +
        "title: Sample article\n" +
        "ms.author: someone\n" +
        "ms.date: 02/12/2026\n" +
        "ms.topic: concept-article\n" +
        "---\n" +
        "\n" +
        "# PackageReference in project files\n" +
        "\n" +
        "Intro prose about package references.\n" +
        "\n" +
        "## Project type support\n" +
        "\n" +
        "Prose about which project types support it.\n";
```

And these test methods, following the existing `try`/`finally` temp-directory pattern used elsewhere in the file:

```csharp
    [TestMethod]
    public async Task GetOutlineOmitsYamlFrontMatterFromLearnArticles()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "article.md", LearnArticle);

            var outline = await service.GetOutlineAsync(
                "docs/article.md", "csharplang", limit: 100, cursor: null, CancellationToken.None);

            Assert.HasCount(2, outline.Entries);
            Assert.AreEqual("PackageReference in project files", outline.Entries[0].Path);
            Assert.AreEqual(1, outline.Entries[0].Level);
            Assert.AreEqual(
                "PackageReference in project files > Project type support",
                outline.Entries[1].Path);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAcceptsASectionPathIssuedForALearnArticle()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "article.md", LearnArticle);

            var outline = await service.GetOutlineAsync(
                "docs/article.md", "csharplang", limit: 100, cursor: null, CancellationToken.None);
            var section = outline.Entries[1].Path;

            // The round trip is the contract: whatever the outline issued, get_doc takes verbatim.
            var content = await service.GetDocAsync(
                "docs/article.md", "csharplang", section, limit: 8000, cursor: null, CancellationToken.None);

            StringAssert.Contains(content.Text, "## Project type support");
            StringAssert.Contains(content.Text, "Prose about which project types support it.");
            StringAssert.DoesNotMatch(content.Text, new Regex("ms\\.author"));
            Assert.IsFalse(content.IsPartial);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Add `using System.Text.RegularExpressions;` to the file's using block if it is not already there.

- [ ] **Step 2: Verify they fail against the pre-Task-1 parse**

Task 1 already landed the fix, so these tests would pass immediately and prove nothing. Temporarily restore the two parse sites to their pre-Task-1 state instead. Do **not** use `git stash` — the stash stack is shared with the main checkout and every other worktree, and another session may pop your entry.

Find Task 1's commit and check out its parent's version of the two files:

```bash
TASK1=$(git log --format='%H %s' | rg -m1 'Parse YAML front matter through one shared Markdig pipeline' | cut -d' ' -f1)
git checkout "$TASK1^" -- src/DotNetKnowledge.Markdown/MarkdownOutline.cs src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests" &> .scratch/t3-red-$(date +%Y%m%d-%H%M).log
```

Expected: FAIL on `GetOutlineOmitsYamlFrontMatterFromLearnArticles` — `Assert.HasCount` reports 3 entries, not 2, because the front matter parsed as a heading. `GetDocAcceptsASectionPathIssuedForALearnArticle` also fails, on the `ms.author` assertion, because the section it round-trips is now prefixed by the front-matter blob.

Restore immediately:

```bash
git checkout HEAD -- src/DotNetKnowledge.Markdown/MarkdownOutline.cs src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs
git status --short
```

Expected: `git status --short` shows only the modified test file. If it shows either markdown source file, the restore did not take — fix that before continuing.

- [ ] **Step 3: Run them green**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests" &> .scratch/t3-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS, with the existing tests in the class still passing.

- [ ] **Step 4: Commit**

```bash
git add tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs
git commit -m "Cover a Learn article's outline and section round trip end to end"
```

---

### Task 4: Assert the document tools at the protocol boundary

`McpStdioTests` asserts only `list_sources`, `sync_source`, `lookup_api` and `search_api` in `tools/list`. The document tools have never been checked over stdio, so after Task 2 the rename has no protocol-level coverage at all. This closes a real gap rather than following the rename.

**Files:**
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs:54-61`

**Interfaces:**
- Consumes: tool names `search_docs`, `get_doc`, `get_doc_outline` from Task 2.
- Produces: nothing later tasks consume.

- [ ] **Step 1: Write the assertions**

After the existing `CollectionAssert.Contains(names, "search_api");` line, add:

```csharp
            CollectionAssert.Contains(names, "search_docs");
            CollectionAssert.Contains(names, "get_doc");
            CollectionAssert.Contains(names, "get_doc_outline");
            AssertOptional(tools, "search_docs", "regex");
            AssertOptional(tools, "search_docs", "source");
            AssertOptional(tools, "get_doc", "section");
            AssertOptional(tools, "get_doc_outline", "cursor");
```

- [ ] **Step 2: Verify the assertions actually bite**

Temporarily change `"search_docs"` in the first new line to `"search_docs_typo"`, then run:

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~McpStdioTests" &> .scratch/t4-red-$(date +%Y%m%d-%H%M).log
```

Expected: FAIL on the `CollectionAssert.Contains` for `search_docs_typo`. This proves the test reaches a live `tools/list` rather than passing vacuously. Revert the typo before continuing.

- [ ] **Step 3: Run it green**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~McpStdioTests" &> .scratch/t4-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs
git commit -m "Assert the document tools in the stdio tools/list test"
```

---

### Task 5: Four ranking tiers

`DocRanking.DocumentTypeRank` has three tiers: `proposals/` and `spec/` rank 0, `meetings/` ranks 2, everything else ranks 1. Every NuGet path would land in the middle tier, leaving intra-source ordering to the path-ordinal tiebreak — which sorts `docs/archive/` first and `docs/reference/` far down.

Measured noise in the NuGet tree: of 1170 lines matching `restore`, 479 (41%) are in `release-notes/` or `archive/`; for `PackageReference` it is 76 of 425, for `package source mapping` 24 of 87, for `central package management` 15 of 62. Against the default limit of 20 that is up to eight of the first twenty hits spent on versions long shipped, and the failure is invisible — an agent holding twenty historical hits has no signal that better documents sit below the cut.

NuGet guidance ranks *below* language proposals deliberately. Equal tiers fall through to the path-ordinal tiebreak and `docs/…` sorts before `proposals/…`, so a flat tier would make an unfiltered `search_docs("records")` lead with NuGet hits — a regression for the tools' existing use. The reverse direction is free: `PackageReference`, `package restore`, `nuspec` and `central package management` each return 0 hits across csharplang, vblang and roslyn-wiki combined, so a NuGet query reaches NuGet documents because nothing competes for it.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocRanking.cs:35-45`
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocRankingTests.cs`

**Interfaces:**
- Consumes: `DocRanking.Order`, `DocLineHit` and the `Hit`/`OrderedPaths` test helpers from Task 2.
- Produces: no new public surface. `DocumentTypeRank` stays private; only its returned ordering changes.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocRankingTests.cs`:

```csharp
    [TestMethod]
    public void CurrentNuGetGuidanceOutranksReleaseNotesAndArchive()
    {
        var ordered = OrderedPaths(
            "restore",
            Hit("docs/archive/NuGet-2.x-release-notes.md", 88, "restore behavior changed"),
            Hit("docs/release-notes/NuGet-6.0.md", 14, "restore behavior changed"),
            Hit("docs/consume-packages/Package-Restore.md", 30, "restore behavior changed"));

        Assert.AreEqual("docs/consume-packages/Package-Restore.md", ordered[0]);
    }

    [TestMethod]
    public void LanguageProposalsOutrankNuGetGuidance()
    {
        // Equal tiers fall through to the path tiebreak, where "docs/" sorts before "proposals/".
        // Tiering NuGet below proposals is what keeps an unfiltered language query answering with
        // language documents.
        var ordered = OrderedPaths(
            "records",
            Hit("docs/reference/nuspec.md", 10, "records are listed here"),
            Hit("proposals/csharp-9.0/records.md", 3, "records are declared like this"));

        Assert.AreEqual("proposals/csharp-9.0/records.md", ordered[0]);
    }

    [TestMethod]
    public void ExistingSourcesKeepTheirRelativeOrder()
    {
        // The change inserts a tier and splits "historical" out of the middle. It must not reorder
        // anything that already existed: proposal, then wiki, then meeting notes.
        var ordered = OrderedPaths(
            "records",
            Hit("meetings/2020/LDM-2020-01-08.md", 5, "records discussion"),
            Hit("docs/wiki/Roslyn-Overview.md", 12, "records overview"),
            Hit("proposals/csharp-9.0/records.md", 3, "records proposal"));

        CollectionAssert.AreEqual(
            new[]
            {
                "proposals/csharp-9.0/records.md",
                "docs/wiki/Roslyn-Overview.md",
                "meetings/2020/LDM-2020-01-08.md",
            },
            ordered);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocRankingTests" &> .scratch/t5-red-$(date +%Y%m%d-%H%M).log
```

Expected: `CurrentNuGetGuidanceOutranksReleaseNotesAndArchive` FAILS with `docs/archive/NuGet-2.x-release-notes.md` in first place — all three paths tie at rank 1, so the alphabetical path tiebreak decides. `LanguageProposalsOutrankNuGetGuidance` PASSES already (`proposals/` is rank 0 today). `ExistingSourcesKeepTheirRelativeOrder` PASSES already — it is the regression guard, and it must still pass after Step 3.

- [ ] **Step 3: Replace `DocumentTypeRank`**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocRanking.cs`, replace the `DocumentTypeRank` method and its comment with:

```csharp
    // Current NuGet guidance. The "docs/" prefix is load-bearing: it keeps these from colliding
    // with a future source's own "reference/" or "concepts/" tree. roslyn-wiki is the only other
    // source rooted at "docs/", and it holds "docs/wiki/" alone.
    private static readonly string[] NuGetGuidancePaths =
    [
        "docs/api/",
        "docs/concepts/",
        "docs/consume-packages/",
        "docs/create-packages/",
        "docs/guides/",
        "docs/hosting-packages/",
        "docs/nuget-org/",
        "docs/policies/",
        "docs/quickstart/",
        "docs/reference/",
        "docs/visual-studio-extensibility/",
    ];

    // Documents about the past. A meeting note discusses a feature in passing; a release note
    // describes a version long shipped. Of 1170 NuGet lines matching "restore", 479 are here.
    private static readonly string[] HistoricalPaths =
    [
        "meetings/",
        "docs/release-notes/",
        "docs/archive/",
    ];

    // A proposal or the specification defines a feature, so it leads. NuGet guidance sits below it
    // rather than beside it: equal ranks fall through to the path tiebreak, where "docs/" sorts
    // ahead of "proposals/", and a language query must not be answered by a packaging document.
    // The slash-terminated segments keep "proposal-a.md" (a filename) from reading as the
    // proposals tree.
    private static int DocumentTypeRank(string path)
    {
        if (path.Contains("proposals/", StringComparison.OrdinalIgnoreCase)
            || path.Contains("spec/", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (HistoricalPaths.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            return 3;
        if (NuGetGuidancePaths.Any(segment => path.Contains(segment, StringComparison.OrdinalIgnoreCase)))
            return 1;
        return 2;
    }
```

Update the class-level `<summary>` so it states current truth — it currently says "Orders … by how authoritative the containing document is, then by whether the match landed on a heading", which stays accurate; add a sentence naming the four tiers.

- [ ] **Step 4: Run the ranking tests**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocRankingTests" &> .scratch/t5-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS, including the pre-existing `ProposalOutranksMeetingNotes` and `HeadingMatchOutranksProseMatchInSameDocument`.

- [ ] **Step 5: Run the whole suite**

```bash
dotnet test DotNetKnowledge.slnx &> .scratch/t5-full-$(date +%Y%m%d-%H%M).log
```

Expected: only the 3 baseline `GitCommandRunnerTests` failures.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/Docs/DocRanking.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocRankingTests.cs
git commit -m "Rank current NuGet guidance above release notes and archive"
```

---

### Task 6: Add the source to the catalog

`SourceCatalog` validation, `SourceSynchronizer`, `SourceCache`, `list_sources` and `sync_source` all accept a new source from configuration alone. This task is the entry plus the test that pins its identity.

`sparse: ["docs"]` yields 465 markdown files: 463 under `docs/`, plus the repository's own `README.md` and `CONTRIBUTING.md`, because cone-mode sparse-checkout always includes the root directory's files. It also brings 14 MB of images alongside 2.2 MB of markdown. Cone mode can only include directories and `media/` folders are nested inside each topic directory, so excluding them would mean switching `SourceSynchronizer` to non-cone patterns — git's documented slow path, applied to a 790 MB source, to save 14 MB. The existing sources already carry the same ballast (roslyn-wiki checks out 11 MB for 0.7 MB of markdown), so this is the established policy.

**Files:**
- Modify: `sources.json`
- Test: `tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCatalogTests.cs:103-113`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: source name `nuget-docs`, repository identity `NuGet/docs.microsoft.com-nuget`. Task 7's documentation refers to both.

- [ ] **Step 1: Write the failing catalog test**

In `tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCatalogTests.cs`, add to `BundledCatalogCarriesRepositoryIdentityForProvenance`, after the `roslyn-wiki` line:

```csharp
        Assert.AreEqual("NuGet/docs.microsoft.com-nuget", catalog.Sources["nuget-docs"].Repository);
        Assert.IsTrue(catalog.Sources["nuget-docs"].Markdown);
```

- [ ] **Step 2: Run it to verify it fails**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~SourceCatalogTests" &> .scratch/t6-red-$(date +%Y%m%d-%H%M).log
```

Expected: FAIL with `KeyNotFoundException` — `nuget-docs` is not in the catalog.

- [ ] **Step 3: Add the entry**

In `sources.json`, add after the `roslyn-wiki` entry (mind the trailing comma on the entry above it):

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

- [ ] **Step 4: Run it green**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~SourceCatalogTests" &> .scratch/t6-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS. `sources.json` is copied next to the server assembly by the build, so a stale copy is the likely cause if it still fails — rebuild first.

- [ ] **Step 5: Verify the pin and sparse path against the real repository**

A manual check, not a test — no test may fetch a real repository. This runs the exact command sequence `SourceSynchronizer` uses (blobless clone, `--sparse`, `sparse-checkout set`, checkout at the pin), so a failure here is a failure of the catalog entry rather than of the server.

```bash
git clone --filter=blob:none --no-checkout --sparse https://github.com/NuGet/docs.microsoft.com-nuget.git .scratch/nuget-verify &> .scratch/t6-clone-$(date +%Y%m%d-%H%M).log
git -C .scratch/nuget-verify sparse-checkout set docs
git -C .scratch/nuget-verify checkout 2b52d770c577cf48b902dc176bdd3941a811d9d2
find .scratch/nuget-verify -name '*.md' -not -path '*/.git/*' | wc -l
```

Expected: `465` — 463 under `docs/`, plus the repository's root `README.md` and `CONTRIBUTING.md`, which cone-mode sparse-checkout always includes. A different count means the pin moved or the sparse path is wrong; investigate before proceeding rather than adjusting the expected number.

Driving the built server over stdio needs a redirected-process driver, not a shell pipe — a shell `>` redirect swallows the server's stdout entirely and reads as a server fault. `scripts/probes/probe-mcp-host.cs` is the instrument if a live `sync_source` call is wanted; it is not required for this task.

- [ ] **Step 6: Commit**

```bash
git add sources.json tests/DotNetKnowledge.Mcp.Tests/Sources/SourceCatalogTests.cs
git commit -m "Add the NuGet documentation source to the catalog"
```

---

### Task 7: Teach the licensing guard the Learn article shape

The repository is MIT and every tracked file is authored here. Content can be pasted without its header, so `verify-no-vendored-content.cs` checks the distinctive *shape* of each upstream source. A sixth source needs a sixth shape rule, or a pasted NuGet article is the one kind of upstream content the guard does not recognize.

Learn articles are identified by their front-matter keys — `ms.author:`, `ms.date:` or `ms.topic:` at the start of a line. Verified against all 173 non-source tracked files: **zero matches**, so the rule adds no false positive today. Source files are exempt from shape rules already (`.cs`, `.csx`, `.vb`), which is what keeps the front-matter fixture added in Task 1 from tripping it.

**Files:**
- Modify: `scripts/verify-no-vendored-content.cs:203-215` (`shapePatterns`) and `:282-291` (`historyPatterns`)

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces: rule name `learn-article`, used in both scans.

- [ ] **Step 1: Add the tracked-tree shape rule**

In `shapePatterns`, after the `ldm-notes` entry:

```csharp
    ("learn-article",
        new Regex(@"^ms\.(author|date|topic):\s*\S", RegexOptions.Multiline),
        "Microsoft Learn article front matter, the file shape used by nuget-docs."),
```

- [ ] **Step 2: Add the matching history rule**

The history scan is line-based, so it takes the same expression without the `Multiline` option. In `historyPatterns`, after the `ldm-notes` entry:

```csharp
            ("learn-article", @"^ms\.(author|date|topic): ?\S", true),
```

`IsShape: true` is required — it is what excludes `.cs`, `.csx` and `.vb` from the history scan too. Without it the two scans disagree, and a tree that passes reports a finding the moment someone adds `--history`.

- [ ] **Step 3: Run the guard against the clean tree**

```bash
dotnet scripts/verify-no-vendored-content.cs &> .scratch/t7-clean-$(date +%Y%m%d-%H%M).log
echo "exit=$?"
```

Expected: exit 0, no findings.

- [ ] **Step 4: Prove the rule actually fires**

The scan covers tracked paths only, so the probe file must be staged:

```bash
printf -- '---\ntitle: Probe\nms.author: someone\nms.date: 02/12/2026\n---\n\n# Probe\n' > docs/probe-learn-article.md
git add docs/probe-learn-article.md
dotnet scripts/verify-no-vendored-content.cs &> .scratch/t7-probe-$(date +%Y%m%d-%H%M).log
echo "exit=$?"
```

Expected: exit 1, with a `learn-article` finding naming `docs/probe-learn-article.md`. Confirm by searching the log:

```bash
rg -n "learn-article" .scratch/t7-probe-*.log
```

Then remove the probe:

```bash
git rm -f --cached docs/probe-learn-article.md
rm docs/probe-learn-article.md
```

- [ ] **Step 5: Re-run clean and commit**

```bash
dotnet scripts/verify-no-vendored-content.cs &> .scratch/t7-final-$(date +%Y%m%d-%H%M).log
echo "exit=$?"
git status --short
git add scripts/verify-no-vendored-content.cs
git commit -m "Recognize Microsoft Learn article front matter in the licensing guard"
```

Expected: exit 0, and `git status --short` shows no leftover probe file.

---

### Task 8: Documentation, decisions and backlog

The rename and the new source change what several standing documents say. `docs/decisions.md` records what was chosen over what, so reopening the question costs nothing; the backlog records gaps this design does not close, because a silent absence is the failure mode these tools are built to avoid.

**Files** (line numbers are against the tree after `main`'s corpus-extraction commit was merged into this branch):
- Modify: `CLAUDE.md:71`, `:99-100`
- Modify: `README.md:70`
- Modify: `docs/design/mcp-tool-surface.md:110`, `:118`, `:123`, `:136`, `:166`, `:241`
- Modify: `docs/decisions.md` (prepend two entries, below the preamble and above the newest existing entry)
- Create: `docs/backlog/yaml-source-content-is-unsearchable.md`
- Create: `docs/backlog/cross-source-search-has-no-relevance-signal.md`
- Create: `docs/backlog/document-search-rescans-every-file.md`
- Modify: `docs/backlog/README.md` (three new table rows)

**Interfaces:**
- Consumes: tool names from Task 2, the tier model from Task 4, the source name from Task 5.
- Produces: nothing code depends on.

- [ ] **Step 1: Rename the tools throughout the prose**

Apply `search_language_docs` → `search_docs`, `get_language_doc_outline` → `get_doc_outline`, `get_language_doc` → `get_doc` (longest first) across `CLAUDE.md`, `README.md` and `docs/design/mcp-tool-surface.md`.

`docs/domain/csharplang-map.md` and `docs/domain/vblang-map.md` were deleted by `main`'s corpus-extraction commit and are no longer in scope.

Do **not** touch `docs/superpowers/plans/2026-08-05-language-doc-tools.md`, `docs/superpowers/specs/2026-08-05-language-doc-tools-design.md`, or any existing entry in `docs/decisions.md`. Those are historical records of completed work; convention 2 exempts them, and rewriting them destroys the record that the question was already asked.

Verify:

```bash
rg --hidden -n "search_language_docs|get_language_doc" CLAUDE.md README.md AGENTS.md docs/design/
```

Expected: no output.

- [ ] **Step 2: Update the tool inventories and the source list**

The "Implemented:" tool lists in `CLAUDE.md` and `README.md` keep the same nine tools; only the three document ones change name.

`docs/design/mcp-tool-surface.md` needs three further edits beyond the renames:
- Retitle the `── language design docs ──` band to `── documentation ──`.
- Line 118 enumerates the searchable sources as "csharplang, vblang, and roslyn-wiki today". It must now read "csharplang, nuget-docs, roslyn-wiki and vblang today" — that list is the one an agent reads to learn what the tool covers.
- Anywhere the document states how many sources `sources.json` declares, it now declares six.

- [ ] **Step 3: Append the decisions entries**

Add to `docs/decisions.md`, newest-first directly under the preamble, four lines each:

```markdown
### 2026-08-08 · The document tools drop "language" from their names

`search_language_docs`/`get_language_doc`/`get_language_doc_outline` become
`search_docs`/`get_doc`/`get_doc_outline`, and `Features/LanguageDocs/` becomes `Features/Docs/`
with its types renamed to match; error codes, JSON field names and cursor `kind` strings are
unchanged, so no previously issued cursor is rejected.
Rejected: keeping the names and broadening only the descriptions, which leaves a tool an agent
cannot discover from its name — the capability then exists only for a caller who reads the
description. Also rejected: a parallel `search_nuget_docs` family, which adds three tool definitions
to every agent's context for no new capability and makes each future markdown source repeat the
pattern; and deprecated aliases, which put six definitions in context to describe three capabilities
with no forcing function to ever remove them.

### 2026-08-08 · NuGet guidance ranks below language proposals and above release notes

`DocRanking.DocumentTypeRank` goes to four tiers: proposals and spec 0, current NuGet guidance 1,
everything else 2, meeting notes and NuGet release-notes/archive 3.
Rejected: a flat tier for NuGet. Equal ranks fall through to the path tiebreak, where `docs/` sorts
ahead of `proposals/`, so an unfiltered language query would have been answered by packaging
documents. Rejected: deferring the ordering to the backlog — of 1170 NuGet lines matching
`restore`, 479 are release notes or archive, so against the default limit of 20 the source would
have shipped measurably worse than it needed to be, and an agent holding twenty historical hits has
no signal that better documents sit below the cut. The reverse direction costs nothing:
`PackageReference`, `package restore`, `nuspec` and `central package management` each return 0 hits
across csharplang, vblang and roslyn-wiki combined. Spec:
[`docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md`](superpowers/specs/2026-08-08-nuget-docs-source-design.md).
```

- [ ] **Step 4: Write the three backlog files**

Each states a current condition, why it matters, the evidence, and a suggested fix — not a history of how it was found. Write these three files.

`docs/backlog/yaml-source-content-is-unsearchable.md`:

```markdown
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
```

`docs/backlog/cross-source-search-has-no-relevance-signal.md`:

```markdown
# Unfiltered document search has no relevance signal

`DocRanking.Order` sorts by document tier, then by whether the match landed on a heading,
then by path ordinal, line, and repo. Nothing in that chain measures how well a document
answers the query. `Order` even takes the query as a parameter and does not read it.

## Why it matters

A `search_docs` call without `source` fans out across four sources. Within a tier, which
source leads is decided by how its paths sort alphabetically — `docs/` before `proposals/`
before `spec/` — which is unrelated to what the caller asked.

The tiering added with `nuget-docs` works around the worst case rather than solving it: NuGet
guidance is deliberately ranked below language proposals precisely because the tiebreak below
that point is arbitrary. That is a workaround holding a real gap closed.

## Evidence

- `DocRanking.Order`'s `query` parameter is documented as "accepted for symmetry with the
  other rankers and to leave room for query-dependent weighting; today the ordering is driven
  by the hit's path and text alone."
- `ApiTextRanking` and `ApiSearchRanking` do use their query. The document ranker is the
  outlier.

## Suggested fix

Score a hit against the query before falling through to path ordering: whole-query match over
partial, match in a heading over match in prose (already partly there), match in the section
path over match in body text. Keep the tiers — they encode document authority, which is a
different quantity from relevance and should not be collapsed into it.
```

`docs/backlog/document-search-rescans-every-file.md`:

```markdown
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
```

Then add three rows to the table in `docs/backlog/README.md`, matching the existing two-row format — `| [Title](file.md) | Area | Why it is deferred |`, with area `server` for all three.

- [ ] **Step 5: Verify the docs are consistent and the guard still passes**

```bash
dotnet scripts/verify-no-vendored-content.cs &> .scratch/t8-guard-$(date +%Y%m%d-%H%M).log
echo "exit=$?"
rg --hidden -n "search_language_docs|get_language_doc" CLAUDE.md README.md AGENTS.md docs/design/
```

Expected: guard exit 0; no output from `rg`.

- [ ] **Step 6: Run the full suite one last time**

```bash
dotnet build DotNetKnowledge.slnx &> .scratch/t8-build-$(date +%Y%m%d-%H%M).log
dotnet test DotNetKnowledge.slnx &> .scratch/t8-test-$(date +%Y%m%d-%H%M).log
```

Expected: build 0 errors / 0 warnings. `Markdown.Tests` 24 passed (20 baseline + 4 from Task 1). `Mcp.Tests` 105 passed / 3 failed — 100 baseline, plus 2 from Task 3 and 3 from Task 5; Tasks 4 and 6 add assertions inside existing test methods, so they raise no count. The 3 failures are the `GitCommandRunnerTests` trio from Global Constraints. Read the last 20 lines of each log and paste them into the completion report.

- [ ] **Step 7: Commit**

```bash
git add CLAUDE.md README.md docs/
git commit -m "Document the NuGet source, the tool rename and the ranking tiers"
```

---

## Completion criteria

- `dotnet build DotNetKnowledge.slnx` — 0 errors, 0 warnings.
- `dotnet test DotNetKnowledge.slnx` — the only failures are the three baseline `GitCommandRunnerTests`.
- `dotnet scripts/verify-no-vendored-content.cs` — exit 0.
- `rg --hidden -n "LanguageDoc|search_language_docs|get_language_doc" src/ tests/ CLAUDE.md README.md AGENTS.md docs/design/ --glob '!**/obj/**'` — no output.
- `Markdown.Tests` 24 passed; `Mcp.Tests` 105 passed.
- Eight commits on `nuget-docs-source`, one per task.
