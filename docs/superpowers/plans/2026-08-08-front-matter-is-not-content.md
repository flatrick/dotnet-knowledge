# Front Matter Is Not Content Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make YAML front matter invisible to `search_docs` and `get_doc`, so a Microsoft Learn article's metadata stops competing with its prose.

**Architecture:** One new function in `DotNetKnowledge.Markdown` answers "where does the body start", and both tools use it — `MarkdownLineSearch` skips lines before it, and `DocsQueryService.GetDocAsync` begins its whole-document range there. No parsing rule is written twice, and no other behavior changes.

**Tech Stack:** C# / .NET 10, MSTest, Markdig 1.3.2.

## Global Constraints

- **Every project build requires 0 errors AND 0 warnings.** `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors` are inherited from the root `Directory.Build.props`, analyzer rules included — note CA1861 fires on an inline `new[]{...}` array literal passed to an assertion, which must be extracted to a `static readonly` field. Never add a `#pragma warning disable`.
- **Build/test commands:** `dotnet build DotNetKnowledge.slnx` and `dotnet test DotNetKnowledge.slnx`.
- **Judging test results.** `tests/DotNetKnowledge.Mcp.Tests/Sources/GitCommandRunnerTests.cs` holds three long-standing timing-dependent failures unrelated to this work: `InheritedStandardInputReproducesTheHang` (fails every run), `TimeoutNamesTheCommandThatExceededItsTier` and `TimeoutNamesTheTierThatExpired` (intermittent, roughly 1 run in 3). Do NOT expect a fixed failure count and do not try to fix them. Judge a run by the TOTAL test count and by every failure being one of those three names.
- **Baseline before Task 1:** `DotNetKnowledge.Markdown.Tests` 25 passed / 0 failed. `DotNetKnowledge.Mcp.Tests` 110 total, 109 passed, 1 failed (`InheritedStandardInputReproducesTheHang`).
- **Never pipe a command through `tail` or `head`.** Redirect full output: `<command> &> .scratch/<what>-$(date +%Y%m%d-%H%M).log`, then read the log with the Read tool. `.scratch/` is gitignored.
- **The wire format does not change.** Error codes, JSON field names and the cursor `kind` strings (`lang-search`, `lang-doc`, `lang-outline`) stay exactly as they are.
- **No test fetches a real upstream repository.** Fixtures are local `git init` trees.
- **`docs/decisions.md` is append-only** — new entries go newest-first under the preamble; never edit or delete an existing entry.
- American English, LF line endings, UTF-8. Markdown prose wraps near 100 columns.
- Work happens on branch `frontmatter-not-content` in the worktree `.worktrees/frontmatter-not-content`. Commit after each task. Do not push and do not open a PR. Do NOT use bare `git stash` — the stash stack is shared across worktrees.

## File Structure

**Created:**

| path | responsibility |
|---|---|
| `src/DotNetKnowledge.Markdown/MarkdownFrontMatter.cs` | The single answer to "which line does this document's body start on" |
| `tests/DotNetKnowledge.Markdown.Tests/MarkdownFrontMatterTests.cs` | Its cases, including the two shapes Markdig treats differently |

**Modified:** `src/DotNetKnowledge.Markdown/MarkdownLineSearch.cs`, `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`, `src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs`, `tests/DotNetKnowledge.Markdown.Tests/MarkdownLineSearchTests.cs`, `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`, `docs/decisions.md`, `docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md`.

**Deliberately not modified:** `src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs` keeps its front-matter entry and its tests. Nothing in this server can page across front matter after this change, so that entry becomes unreachable through these tools — but `MarkdownAtomicBlocks` is a general-purpose function answering "which blocks must never be split", and narrowing its contract to match one caller's range choice would leave the next caller a silent hazard.

**Task order matters.** Task 2 (search) lands before Task 3 (fetch) on purpose. Search-skip alone leaves front matter unsearchable but still fetchable, which is harmless. Fetch-skip alone would leave search reporting hits at lines no call can return — the exact defect this plan exists to remove.

---

### Task 1: `MarkdownFrontMatter.BodyStartLine`

Microsoft Learn articles open with a YAML block. 408 of the 463 documents under `nuget-docs`' `docs/` tree have one; the three language sources have none. Both `search_docs` and `get_doc` need to agree on where such a document's content begins, so the rule lives in one function rather than being written twice.

**Files:**
- Create: `src/DotNetKnowledge.Markdown/MarkdownFrontMatter.cs`
- Test: `tests/DotNetKnowledge.Markdown.Tests/MarkdownFrontMatterTests.cs`

**Interfaces:**
- Consumes: `MarkdownText.Normalize`, `MarkdownText.SplitLines` and `MarkdownPipelines.Default`, all `internal` in the same assembly.
- Produces: `public static class MarkdownFrontMatter` with `public static int BodyStartLine(string markdown)` in namespace `DotNetKnowledge.Markdown`. Tasks 2 and 3 call exactly this.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotNetKnowledge.Markdown.Tests/MarkdownFrontMatterTests.cs`:

```csharp
namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownFrontMatterTests
{
    // The shape every Microsoft Learn article has: front matter on lines 1-5, a blank line 6,
    // and the real heading on line 7.
    private const string LearnArticle =
        "---\n" +
        "title: Sample article\n" +
        "ms.author: someone\n" +
        "ms.date: 02/12/2026\n" +
        "---\n" +
        "\n" +
        "# Heading One\n" +
        "\n" +
        "Body text.\n";

    [TestMethod]
    public void BodyStartLineSkipsFrontMatterAndTheBlankLineAfterIt()
    {
        Assert.AreEqual(7, MarkdownFrontMatter.BodyStartLine(LearnArticle));
    }

    [TestMethod]
    public void BodyStartLineIsOneWhenThereIsNoFrontMatter()
    {
        const string document =
            "# Heading One\n" +
            "\n" +
            "Body text.\n";

        Assert.AreEqual(1, MarkdownFrontMatter.BodyStartLine(document));
    }

    [TestMethod]
    public void BodyStartLineTakesTheContentLineImmediatelyAfterTheClosingFence()
    {
        const string document =
            "---\n" +
            "title: Sample\n" +
            "---\n" +
            "# Heading One\n";

        Assert.AreEqual(4, MarkdownFrontMatter.BodyStartLine(document));
    }

    [TestMethod]
    public void BodyStartLinePointsPastTheEndWhenFrontMatterHasNoBody()
    {
        // No such file exists in nuget-docs today. Without this, a document that is only metadata
        // would make get_doc index outside the line array.
        const string document =
            "---\n" +
            "title: Sample\n" +
            "---\n";

        // Four lines after the trailing-newline split: "---", "title: Sample", "---", "".
        Assert.AreEqual(5, MarkdownFrontMatter.BodyStartLine(document));
    }

    [TestMethod]
    public void BodyStartLineIsOneForAdjacentFencesWhichAreNotFrontMatter()
    {
        // Measured against Markdig 1.3.2: "---" on adjacent lines is two thematic breaks, not an
        // empty front-matter block, so the document starts at line 1 like any other.
        const string document =
            "---\n" +
            "---\n" +
            "\n" +
            "# Heading One\n";

        Assert.AreEqual(1, MarkdownFrontMatter.BodyStartLine(document));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~MarkdownFrontMatterTests" &> .scratch/t1-red-$(date +%Y%m%d-%H%M).log
```

Expected: the build FAILS — `MarkdownFrontMatter` does not exist yet (`CS0103`/`CS0246`). That is the red state for this task.

- [ ] **Step 3: Write the implementation**

Create `src/DotNetKnowledge.Markdown/MarkdownFrontMatter.cs`:

```csharp
using Markdig.Extensions.Yaml;
using Markdig.Syntax;

namespace DotNetKnowledge.Markdown;

/// <summary>
/// Locates where a document's content begins, so front matter can be excluded from both search and
/// fetch by one rule rather than two. Microsoft Learn articles carry a YAML block of
/// <c>title</c>/<c>ms.author</c>/<c>ms.date</c> keys that is metadata about the document, not part
/// of it.
/// </summary>
public static class MarkdownFrontMatter
{
    /// <summary>
    /// The 1-based line where <paramref name="markdown"/>'s content begins: the first non-blank
    /// line after a leading YAML front-matter block, or 1 when the document has none. A document
    /// that is nothing but front matter returns one past its last line, which makes a fetch range
    /// empty rather than out of bounds.
    /// </summary>
    public static int BodyStartLine(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var normalized = MarkdownText.Normalize(markdown);
        var document = Markdig.Markdown.Parse(normalized, MarkdownPipelines.Default);
        var frontMatter = document.Descendants<YamlFrontMatterBlock>().FirstOrDefault();
        if (frontMatter is null)
            return 1;

        // Same arithmetic as MarkdownAtomicBlocks: Line is the 0-based opening "---", and the last
        // content line plus three is the first line past the closing one.
        var lastContentLine = frontMatter.Lines.Count > 0
            ? frontMatter.Lines.Lines[frontMatter.Lines.Count - 1].Line
            : frontMatter.Line;

        var lines = MarkdownText.SplitLines(normalized);
        var line = lastContentLine + 3;

        // Skip the blank line Learn authors leave after the fence; otherwise every fetched article
        // would open with one.
        while (line <= lines.Length && string.IsNullOrWhiteSpace(lines[line - 1]))
            line++;

        return line;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DotNetKnowledge.Markdown.Tests" &> .scratch/t1-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS, 30 total — the 25 baseline plus the 5 added. `Failed: 0`.

- [ ] **Step 5: Run the whole suite**

```bash
dotnet build DotNetKnowledge.slnx &> .scratch/t1-build-$(date +%Y%m%d-%H%M).log
dotnet test DotNetKnowledge.slnx &> .scratch/t1-full-$(date +%Y%m%d-%H%M).log
```

Expected: build 0 errors / 0 warnings. `Mcp.Tests` still 110 total with every failure drawn only from the `GitCommandRunnerTests` trio — this task adds a new type nothing calls yet, so nothing there can move.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Markdown/MarkdownFrontMatter.cs tests/DotNetKnowledge.Markdown.Tests/MarkdownFrontMatterTests.cs
git commit -m "Add MarkdownFrontMatter.BodyStartLine"
```

---

### Task 2: `search_docs` stops matching front matter

Measured over `nuget-docs` at the pinned commit, front-matter keys are 451 of 545 lines matching `description` (83%), 451 of 485 matching `title` (93%), and 451 of 1131 matching `author` (40%). Against a result page capped at 20, that metadata crowds out prose. The three language sources have zero files beginning with `---`, so this is inert on them.

**Files:**
- Modify: `src/DotNetKnowledge.Markdown/MarkdownLineSearch.cs` — the `Search` overload taking `Regex? compiledPattern`, which the other overload delegates to
- Test: `tests/DotNetKnowledge.Markdown.Tests/MarkdownLineSearchTests.cs`

**Interfaces:**
- Consumes: `MarkdownFrontMatter.BodyStartLine(string)` from Task 1.
- Produces: no signature change. `MarkdownLineSearch.Search` keeps both overloads and its `MarkdownLineHit(int Line, string Text, string SectionPath)` return shape; only which lines it considers changes.

- [ ] **Step 1: Write the failing test**

Add to `tests/DotNetKnowledge.Markdown.Tests/MarkdownLineSearchTests.cs`:

```csharp
    // 1: ---            5: ---
    // 2: title: ...     6:
    // 3: ms.author: ... 7: # Heading One
    // 4: ms.date: ...   8:
    //                   9: Body text about the title of a package.
    private const string LearnArticle =
        "---\n" +
        "title: Sample article\n" +
        "ms.author: someone\n" +
        "ms.date: 02/12/2026\n" +
        "---\n" +
        "\n" +
        "# Heading One\n" +
        "\n" +
        "Body text about the title of a package.\n";

    [TestMethod]
    public void SearchIgnoresFrontMatterAndStillMatchesTheBody()
    {
        var outline = MarkdownOutline.Extract(LearnArticle);

        // "title" appears on line 2 as a metadata key and on line 9 as prose. Only the prose is a
        // documentation hit; the key is metadata about the document, not part of it.
        var hits = MarkdownLineSearch.Search(LearnArticle, outline, "title", regex: false);

        Assert.HasCount(1, hits);
        Assert.AreEqual(9, hits[0].Line);
        Assert.AreEqual("Heading One", hits[0].SectionPath);
    }

    [TestMethod]
    public void SearchReturnsNothingForAFrontMatterOnlyTerm()
    {
        var outline = MarkdownOutline.Extract(LearnArticle);

        Assert.IsEmpty(MarkdownLineSearch.Search(LearnArticle, outline, "ms.author", regex: false));
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~MarkdownLineSearchTests" &> .scratch/t2-red-$(date +%Y%m%d-%H%M).log
```

Expected: BOTH fail. `SearchIgnoresFrontMatterAndStillMatchesTheBody` reports 2 hits instead of 1 (line 2 and line 9); `SearchReturnsNothingForAFrontMatterOnlyTerm` reports 1 hit instead of none.

- [ ] **Step 3: Skip the front matter**

In `src/DotNetKnowledge.Markdown/MarkdownLineSearch.cs`, inside the `Search` overload that takes `Regex? compiledPattern`, add the body-start lookup just after the existing `lines`/`hits` locals:

```csharp
        var lines = MarkdownText.SplitLines(MarkdownText.Normalize(markdown));
        var hits = new List<MarkdownLineHit>();

        // Front matter is metadata about the document, not part of it. Matching it would return
        // hits with no enclosing section, at lines get_doc does not return - a location the caller
        // cannot follow. This costs one extra parse for a file that reached this far, which the
        // caller's own prefilter has already narrowed to files that can match.
        var bodyStartLine = MarkdownFrontMatter.BodyStartLine(markdown);
```

and skip those lines inside the loop, before the match test:

```csharp
        for (var i = 0; i < lines.Length; i++)
        {
            var lineNumber = i + 1;
            if (lineNumber < bodyStartLine)
                continue;

            var matched = compiledPattern is not null
                ? compiledPattern.IsMatch(lines[i])
                : lines[i].Contains(pattern, StringComparison.Ordinal);
            if (!matched)
                continue;

            var section = outline.LastOrDefault(
                heading => heading.StartLine <= lineNumber && lineNumber < heading.EndLine);
            hits.Add(new MarkdownLineHit(lineNumber, lines[i], section?.Path ?? string.Empty));
        }
```

Note the existing `var lineNumber = i + 1;` moves to the top of the loop body; do not leave a second declaration further down.

- [ ] **Step 4: Run the tests to verify they pass**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DotNetKnowledge.Markdown.Tests" &> .scratch/t2-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS, 32 total (30 after Task 1, plus 2). The pre-existing `MarkdownLineSearchTests` cases must all still pass — their fixture has no front matter, so `bodyStartLine` is 1 and nothing is skipped.

- [ ] **Step 5: Run the whole suite**

```bash
dotnet build DotNetKnowledge.slnx &> .scratch/t2-build-$(date +%Y%m%d-%H%M).log
dotnet test DotNetKnowledge.slnx &> .scratch/t2-full-$(date +%Y%m%d-%H%M).log
```

Expected: build 0 errors / 0 warnings; `Mcp.Tests` 110 total with failures only from the `GitCommandRunnerTests` trio. The MCP-level search tests use fixtures without front matter, so none should move.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Markdown/MarkdownLineSearch.cs tests/DotNetKnowledge.Markdown.Tests/MarkdownLineSearchTests.cs
git commit -m "Exclude front matter from document search"
```

---

### Task 3: `get_doc` starts at the body

A whole-document fetch currently begins at line 1, so every Learn article opens with nine lines of `title:`/`ms.author:`/`ms.date:` before any prose. A sectioned fetch is already unaffected — a section begins at its heading, so front matter was never inside one.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs` — `GetDocAsync`, the `else` branch that sets `rangeStart = 1`, and the cursor range guard below it
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `MarkdownFrontMatter.BodyStartLine(string)` from Task 1; the existing private test helper `CreateServiceWithDocumentAsync(string root, string fileName, string content)`, which `git init`s a fixture repo containing `docs/<fileName>` under the source name `csharplang` and returns a `DocsQueryService`; and the existing `DeleteDirectory(string path)` helper.
- Produces: no signature change. `DocContentResult`'s shape is unchanged; `StartLine` now names the first body line for a whole-document fetch.

- [ ] **Step 1: Write the failing test**

The file already has a `LearnArticle` constant from earlier work, whose front matter is lines 1-6, blank line 7, and `# PackageReference in project files` on line 8. Add this test, following the file's existing `try`/`finally` temp-directory pattern:

```csharp
    [TestMethod]
    public async Task GetDocOmitsFrontMatterFromAWholeDocumentFetch()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(root, "article.md", LearnArticle);

            var content = await service.GetDocAsync(
                "docs/article.md", "csharplang", section: null, limit: 8000, cursor: null, CancellationToken.None);

            // The payload begins at the real heading, and StartLine names the line it came from -
            // so a returned line number still points at the same line in the file on disk.
            StringAssert.StartsWith(content.Text, "# PackageReference in project files");
            Assert.AreEqual(8, content.StartLine);
            StringAssert.DoesNotMatch(content.Text, new Regex("ms\\.author"));
            StringAssert.DoesNotMatch(content.Text, new Regex("ms\\.date"));
            Assert.IsFalse(content.IsPartial);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Both facts are verified against the file as it stands: the constant's front matter is lines 1-6 (`---`, four keys, `---`), line 7 is blank, and `# PackageReference in project files` is line 8; `using System.Text.RegularExpressions;` is already in the using block, so no new using is needed.

- [ ] **Step 2: Run the test to verify it fails**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~Features.Docs.DocsQueryServiceTests" &> .scratch/t3-red-$(date +%Y%m%d-%H%M).log
```

Expected: FAIL on `StringAssert.StartsWith` — the text begins `---` and `StartLine` is 1.

- [ ] **Step 3: Start the range at the body**

In `GetDocAsync`, change the `else` branch:

```csharp
        else
        {
            // Front matter is metadata about the document, not part of it, and search does not
            // return hits inside it either.
            rangeStart = MarkdownFrontMatter.BodyStartLine(text);
            rangeEndExclusive = lines.Length + 1;
        }
```

No new using is needed — the file already has `using DotNetKnowledge.Markdown;` on line 4.

- [ ] **Step 4: Make the range guard apply to cursors only**

Immediately below, the guard currently reads:

```csharp
        var startLine = cursor is null ? rangeStart : decodedStartLine;
        if (startLine < rangeStart || startLine >= rangeEndExclusive)
            throw new ArgumentException("cursor points outside the requested section.", nameof(cursor));
```

With a null cursor `startLine` is `rangeStart` by construction, so this can only ever reject a supplied cursor — except in one new case: a document that is nothing but front matter makes `rangeStart == rangeEndExclusive`, and the guard would then throw `invalid_cursor` at a caller who passed no cursor at all. Scope it to the case it is actually for:

```csharp
        var startLine = cursor is null ? rangeStart : decodedStartLine;
        // Only a supplied cursor can fall outside the range; with none, startLine is the range's own
        // start. A document that is entirely front matter has an empty range, and must page to empty
        // text rather than report a cursor error to a caller who sent no cursor.
        if (cursor is not null && (startLine < rangeStart || startLine >= rangeEndExclusive))
            throw new ArgumentException("cursor points outside the requested section.", nameof(cursor));
```

`MarkdownPager.Page` already handles `startLine == endLineExclusiveBound`: its loop does not run, `isPartial` is false, and the resulting slice is empty.

- [ ] **Step 5: Run the test to verify it passes**

```bash
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~Features.Docs.DocsQueryServiceTests" &> .scratch/t3-green-$(date +%Y%m%d-%H%M).log
```

Expected: PASS, including the pre-existing `GetOutlineOmitsYamlFrontMatterFromLearnArticles` and `GetDocAcceptsASectionPathIssuedForALearnArticle` — sectioned fetches must be untouched by this change.

- [ ] **Step 6: Run the whole suite**

```bash
dotnet build DotNetKnowledge.slnx &> .scratch/t3-build-$(date +%Y%m%d-%H%M).log
dotnet test DotNetKnowledge.slnx &> .scratch/t3-full-$(date +%Y%m%d-%H%M).log
```

Expected: build 0 errors / 0 warnings; `Mcp.Tests` 111 total (110 plus this one), with failures only from the `GitCommandRunnerTests` trio.

- [ ] **Step 7: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs
git commit -m "Start a whole-document fetch at the body, not the front matter"
```

---

### Task 4: Say so in the tool descriptions and the records

An agent chooses a tool from its description, and this change removes something the previous description implied was there. `docs/decisions.md` records the reversal because the original design argued the opposite in writing.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs` — the `search_docs` and `get_doc` `[Description]` blocks
- Modify: `docs/decisions.md` — prepend one entry
- Modify: `docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md` — two now-false lines

**Interfaces:**
- Consumes: the behavior from Tasks 2 and 3.
- Produces: nothing code depends on.

- [ ] **Step 1: Amend the `search_docs` description**

Replace the final sentence `"is the source's own. Fetch the document for the full text.")]` with:

```csharp
        "is the source's own. Fetch the document for the full text. YAML front matter, which " +
        "Microsoft Learn articles carry, is metadata about a document rather than part of it and " +
        "is not searched.")]
```

- [ ] **Step 2: Amend the `get_doc` description**

Replace the final sentence `"and an include token names a real path this tool can fetch.")]` with:

```csharp
        "and an include token names a real path this tool can fetch. A whole-document fetch begins " +
        "at the document's first content line: YAML front matter is metadata and is not returned, " +
        "and startLine names the line the text actually came from.")]
```

Leave the `get_doc_outline` description alone — it already states that front matter is not a heading and does not appear.

- [ ] **Step 3: Prepend the decisions entry**

Add to `docs/decisions.md`, directly under the preamble and above the current newest entry, with a `---` separator between it and the entry below:

```markdown
### 2026-08-08 · Front matter is metadata, excluded from both search and fetch

`MarkdownFrontMatter.BodyStartLine` is the one rule; `MarkdownLineSearch` skips lines before it and
`GetDocAsync` starts a whole-document range there. Supersedes the "frontmatter stays searchable"
property of
[`docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md`](superpowers/specs/2026-08-08-nuget-docs-source-design.md).
Rejected: keeping it searchable, whose stated reason was that suppressing it would manufacture a
silent absence. Measured over `nuget-docs` at the pin, front-matter keys are 451 of 545 lines
matching `description`, 451 of 485 matching `title` and 451 of 1131 matching `author` — against a
page capped at 20 — and once `get_doc` starts after the front matter those hits carry an empty
section path and name lines no call returns, which is a worse failure than the absence.
Also rejected: excluding it from `get_doc` only, which leaves search and fetch disagreeing about
what exists; and returning the parsed keys as a structured field, which no client wants.
Spec: [`docs/superpowers/specs/2026-08-08-front-matter-is-not-content-design.md`](superpowers/specs/2026-08-08-front-matter-is-not-content-design.md).
```

- [ ] **Step 4: Correct the two stale lines in the nuget-docs spec**

In `docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md`:

(a) The bullet beginning `- **Frontmatter stays searchable.**` asserts behavior that no longer holds. Replace that bullet with:

```markdown
- **Front matter is not content.** It is metadata about the document, so neither `search_docs` nor
  `get_doc` returns it. See
  [`2026-08-08-front-matter-is-not-content-design.md`](2026-08-08-front-matter-is-not-content-design.md).
```

(b) The sentence listing prose that moves with the tool rename names `docs/domain/csharplang-map.md` and `docs/domain/vblang-map.md`, which the corpus-extraction commit deleted before that rename shipped. Remove those two paths from the list, leaving `CLAUDE.md`, `README.md` and `docs/design/mcp-tool-surface.md`.

Change nothing else in that file, and do not touch any existing entry in `docs/decisions.md`.

- [ ] **Step 5: Verify**

```bash
dotnet build DotNetKnowledge.slnx &> .scratch/t4-build-$(date +%Y%m%d-%H%M).log
dotnet test DotNetKnowledge.slnx &> .scratch/t4-test-$(date +%Y%m%d-%H%M).log
dotnet scripts/verify-no-vendored-content.cs &> .scratch/t4-guard-$(date +%Y%m%d-%H%M).log
echo "guard exit=$?"
rg --hidden -n "docs/domain|Frontmatter stays searchable" docs/superpowers/specs/2026-08-08-nuget-docs-source-design.md
```

Expected: build 0 errors / 0 warnings; `Mcp.Tests` 111 total with failures only from the `GitCommandRunnerTests` trio; guard exit 0; the final `rg` returns nothing.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs docs/
git commit -m "Record front matter as metadata in the tool descriptions and decisions"
```

---

## Completion criteria

- `dotnet build DotNetKnowledge.slnx` — 0 errors, 0 warnings.
- `dotnet test DotNetKnowledge.slnx` — `Markdown.Tests` 32 passed / 0 failed; `Mcp.Tests` 111 total, every failure drawn only from the `GitCommandRunnerTests` trio.
- `dotnet scripts/verify-no-vendored-content.cs` — exit 0.
- Four commits on `frontmatter-not-content`, one per task.
- Manual confirmation after the server is reinstalled: `search_docs(query: "description", source: "nuget-docs")` returns no `sectionPath: ""` hits, and `get_doc(path: "docs/reference/nuspec.md", source: "nuget-docs")` begins at `# .nuspec reference` with `startLine` 11.
