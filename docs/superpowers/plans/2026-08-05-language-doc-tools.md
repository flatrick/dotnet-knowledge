# Language-Doc Query Tools Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `search_language_docs`, `get_language_doc`, and `get_language_doc_outline` on the
dotnet-knowledge MCP server, per
[`docs/superpowers/specs/2026-08-05-language-doc-tools-design.md`](../specs/2026-08-05-language-doc-tools-design.md).

**Architecture:** A new dependency-free class library, `DotNetKnowledge.Markdown`, holds all
Markdig-based parsing: heading/outline extraction, fenced-code/table "atomic block" detection,
character-budget pagination that never splits one, and literal/regex line search. A new
`Features/LanguageDocs/` layer in `DotNetKnowledge.Mcp` composes that library with the existing
`SourceSynchronizer`/`SourceCatalog`/`SourceCache` for sync-checking, cursor encoding, and
provenance — mirroring `Features/ApiDocs/` exactly.

**Tech Stack:** .NET 10, Markdig 1.3.2, MSTest.Sdk 4.3.2 (matching every existing test project).

## Global Constraints

- Every new project inherits `TreatWarningsAsErrors=true` from the root `Directory.Build.props` —
  never override it. A build with any warning is a failing build.
- `search_language_docs`: `limit` 1–100, default 20.
- `get_language_doc`: `limit` is a **character budget**, 1000–50000, default 8000 — not an item
  count, unlike the other two tools.
- `get_language_doc_outline`: `limit` 1–500, default 100.
- Supported `source` values for all three tools: exactly `"csharplang"` and `"vblang"`. An
  unrecognized `source` is `invalid_request` (a generic `ArgumentException`), **not**
  `source_invalid` — verified against `ApiDocsTool`'s actual catch blocks; see the spec's Error
  Taxonomy section for why.
- Cursors reuse `ApiDocsQueryService`'s exact `PageCursor(Version, Kind, Scope, Offset, Revisions)`
  scheme: base64url-encoded JSON, rejected if `Kind`, `Scope`, or `Revisions` don't match the
  current request. New `Kind` values: `"lang-search"`, `"lang-doc"`, `"lang-outline"`.
- **Never derive a query-time "not found" exception from `InvalidOperationException`.** The reader
  callback passed to `SourceSynchronizer.ReadCurrentSourceAsync` runs *inside* a
  `try { } catch (InvalidOperationException) { throw new SourceNotSyncedException(...); }` block
  (this is how `ApiDocsQueryService` signals "not synced" too), so anything derived from
  `InvalidOperationException` thrown from within that callback is silently misreported as
  `source_not_synced`. This is not a hypothetical — it was caught live during planning verification
  (see Task 5). `LanguageDocPathNotFoundException`/`LanguageDocSectionNotFoundException` derive
  from plain `Exception`.
- Search-hit line text is truncated to 300 characters with a trailing `…` if longer.
- Every test project uses the same synthetic-local-git-repo fixture pattern as
  `ApiDocsQueryServiceTests`/`ApiDocsToolTests`: `git init` a temp repo, commit real content, write a
  matching `sources.json`, `SourceSynchronizer.SyncAsync` it for real. No mocking of git or the
  filesystem.
- Every array literal passed directly as a method argument (not as a field/property initializer)
  must be hoisted to a `private static readonly` field first — `dotnet build` treats analyzer rule
  CA1861 as an error under `TreatWarningsAsErrors`, and it fires on inline array arguments.

---

### Task 1: `DotNetKnowledge.Markdown` project, heading outline extraction

**Files:**
- Create: `src/DotNetKnowledge.Markdown/DotNetKnowledge.Markdown.csproj`
- Create: `src/DotNetKnowledge.Markdown/MarkdownHeading.cs`
- Create: `src/DotNetKnowledge.Markdown/MarkdownOutline.cs`
- Create: `tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj`
- Create: `tests/DotNetKnowledge.Markdown.Tests/MarkdownOutlineTests.cs`
- Modify: `DotNetKnowledge.slnx`

**Interfaces:**
- Produces: `MarkdownHeading(int Level, string Text, string Path, int StartLine, int EndLine)` —
  `StartLine`/`EndLine` are 1-based, `EndLine` **exclusive** (the next same-or-higher heading's
  `StartLine`, or one past the document's last line).
- Produces: `MarkdownOutline.Extract(string markdown) -> IReadOnlyList<MarkdownHeading>`.

- [ ] **Step 1: Create the project**

`src/DotNetKnowledge.Markdown/DotNetKnowledge.Markdown.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>DotNetKnowledge.Markdown</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Markdig" Version="1.3.2" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the test project**

`tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj`:

```xml
<Project Sdk="MSTest.Sdk/4.3.2">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>DotNetKnowledge.Markdown.Tests</RootNamespace>
    <UseVSTest>true</UseVSTest>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DotNetKnowledge.Markdown\DotNetKnowledge.Markdown.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Write the failing test**

`tests/DotNetKnowledge.Markdown.Tests/MarkdownOutlineTests.cs`:

```csharp
using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownOutlineTests
{
    private const string SampleDocument =
        "# Title\n" +
        "\n" +
        "## A\n" +
        "\n" +
        "Some prose in A.\n" +
        "\n" +
        "### B\n" +
        "\n" +
        "Nested under B.\n" +
        "\n" +
        "## C\n" +
        "\n" +
        "## A\n" +
        "\n" +
        "Repeated heading text.\n";

    [TestMethod]
    public void ExtractBuildsAncestorPathsAndLineRanges()
    {
        var headings = MarkdownOutline.Extract(SampleDocument);

        Assert.HasCount(5, headings);
        Assert.AreEqual("Title", headings[0].Path);
        Assert.AreEqual(1, headings[0].Level);
        Assert.AreEqual(1, headings[0].StartLine);
        Assert.AreEqual("Title > A", headings[1].Path);
        Assert.AreEqual("Title > A > B", headings[2].Path);
        Assert.AreEqual("Title > C", headings[3].Path);
    }

    [TestMethod]
    public void ExtractSuffixesOnlyTheCollidingPath()
    {
        var headings = MarkdownOutline.Extract(SampleDocument);

        // "## A" occurs twice as a direct child of "Title": the first keeps its plain path,
        // the second collides and gets a suffix. Every other path is untouched.
        Assert.AreEqual("Title > A", headings[1].Path);
        Assert.AreEqual("Title > A (2)", headings[4].Path);
    }

    [TestMethod]
    public void ExtractComputesExclusiveEndLinesFromTheNextSameOrHigherHeading()
    {
        var headings = MarkdownOutline.Extract(SampleDocument);

        var sectionA = headings.Single(h => h.Path == "Title > A");
        var sectionB = headings.Single(h => h.Path == "Title > A > B");
        var sectionC = headings.Single(h => h.Path == "Title > C");
        var sectionALast = headings.Single(h => h.Path == "Title > A (2)");

        // "## A" ends where the next same-or-higher heading, "## C", begins: "### B" nests inside
        // A (a lower level does not close it), so A's own range extends past B to C.
        Assert.AreEqual(sectionC.StartLine, sectionA.EndLine);
        // "### B" ends where "## C" begins too - the next heading at any level closes a childless one.
        Assert.AreEqual(sectionC.StartLine, sectionB.EndLine);
        // The last heading in the document ends one past the last line.
        var totalLines = SampleDocument.Split('\n').Length;
        Assert.AreEqual(totalLines + 1, sectionALast.EndLine);
    }

    [TestMethod]
    public void ExtractIgnoresAHeadingMarkerInsideAFencedCodeBlock()
    {
        const string document = "# Title\n\n```\n# not a heading\n```\n";

        var headings = MarkdownOutline.Extract(document);

        Assert.HasCount(1, headings);
        Assert.AreEqual("Title", headings[0].Path);
    }

    [TestMethod]
    public void ExtractHandlesASetextHeading()
    {
        const string document = "Setext Heading\n--------------\n\nBody text.\n";

        var headings = MarkdownOutline.Extract(document);

        Assert.HasCount(1, headings);
        Assert.AreEqual(2, headings[0].Level);
        Assert.AreEqual("Setext Heading", headings[0].Text);
        Assert.AreEqual(1, headings[0].StartLine);
    }

    [TestMethod]
    public void ExtractStripsInlineFormattingFromHeadingText()
    {
        const string document = "## Sub `Heading` with *emphasis*\n";

        var headings = MarkdownOutline.Extract(document);

        Assert.AreEqual("Sub Heading with emphasis", headings[0].Text);
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj`
Expected: build FAILS — `MarkdownHeading`/`MarkdownOutline` don't exist yet.

- [ ] **Step 5: Implement `MarkdownHeading`**

`src/DotNetKnowledge.Markdown/MarkdownHeading.cs`:

```csharp
namespace DotNetKnowledge.Markdown;

public sealed record MarkdownHeading(int Level, string Text, string Path, int StartLine, int EndLine);
```

- [ ] **Step 6: Implement `MarkdownOutline`**

`src/DotNetKnowledge.Markdown/MarkdownOutline.cs`:

```csharp
using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace DotNetKnowledge.Markdown;

public static class MarkdownOutline
{
    public static IReadOnlyList<MarkdownHeading> Extract(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        var totalLines = markdown.ReplaceLineEndings("\n").Split('\n').Length;

        // HeadingBlock.Line is not usable directly: for a setext heading ("Title\n-----") it
        // points at the underline row, not the heading text itself, because the block parser only
        // confirms the heading once it sees the underline. The character span's start offset maps
        // to the correct line for both ATX and setext forms.
        var raw = document.Descendants<HeadingBlock>()
            .Select(heading => (Level: heading.Level, Text: RenderPlainText(heading.Inline), StartLine: LineNumberAt(markdown, heading.Span.Start)))
            .ToArray();

        var headings = new List<MarkdownHeading>(raw.Length);
        var ancestorStack = new List<(int Level, string Text)>();
        var pathOccurrences = new Dictionary<string, int>(StringComparer.Ordinal);

        for (var i = 0; i < raw.Length; i++)
        {
            var (level, text, startLine) = raw[i];

            while (ancestorStack.Count > 0 && ancestorStack[^1].Level >= level)
                ancestorStack.RemoveAt(ancestorStack.Count - 1);

            var basePath = string.Join(" > ", ancestorStack.Select(ancestor => ancestor.Text).Append(text));
            var occurrence = pathOccurrences.TryGetValue(basePath, out var count) ? count + 1 : 1;
            pathOccurrences[basePath] = occurrence;
            var path = occurrence == 1 ? basePath : $"{basePath} ({occurrence})";

            ancestorStack.Add((level, text));

            var endLine = totalLines + 1;
            for (var j = i + 1; j < raw.Length; j++)
            {
                if (raw[j].Level <= level)
                {
                    endLine = raw[j].StartLine;
                    break;
                }
            }

            headings.Add(new MarkdownHeading(level, text, path, startLine, endLine));
        }

        return headings;
    }

    private static int LineNumberAt(string markdown, int charOffset)
    {
        var line = 1;
        for (var i = 0; i < charOffset; i++)
        {
            if (markdown[i] == '\n')
                line++;
        }

        return line;
    }

    private static string RenderPlainText(ContainerInline? inline)
    {
        if (inline is null)
            return string.Empty;

        var builder = new System.Text.StringBuilder();

        void Walk(Inline? node)
        {
            while (node is not null)
            {
                switch (node)
                {
                    case LiteralInline literal:
                        builder.Append(literal.Content.ToString());
                        break;
                    case CodeInline code:
                        builder.Append(code.Content);
                        break;
                    case ContainerInline container:
                        Walk(container.FirstChild);
                        break;
                }

                node = node.NextSibling;
            }
        }

        Walk(inline);
        return builder.ToString();
    }
}
```

- [ ] **Step 7: Run the test to verify it passes**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj`
Expected: PASS, all 6 tests. Verified during planning: 6/6 pass, `dotnet build ... -warnaserror`
reports 0 warnings.

- [ ] **Step 8: Add both new projects to the solution**

`DotNetKnowledge.slnx` lists the server-side projects. Add:

```xml
  <Project Path="src/DotNetKnowledge.Markdown/DotNetKnowledge.Markdown.csproj" />
  <Project Path="tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj" />
```

Run: `dotnet sln DotNetKnowledge.slnx list` — expected: both new projects listed.

- [ ] **Step 9: Commit**

```bash
git add src/DotNetKnowledge.Markdown tests/DotNetKnowledge.Markdown.Tests DotNetKnowledge.slnx
git commit -m "Add DotNetKnowledge.Markdown with heading outline extraction"
```

---

### Task 2: Atomic-block detection (fenced code, tables)

**Files:**
- Create: `src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs`
- Create: `tests/DotNetKnowledge.Markdown.Tests/MarkdownAtomicBlocksTests.cs`

**Interfaces:**
- Consumes: nothing from Task 1 (parses independently).
- Produces: `MarkdownAtomicBlocks.Find(string markdown) -> IReadOnlyList<(int StartLine, int EndLine)>`
  — 1-based, `EndLine` **exclusive**, same convention as `MarkdownHeading`.

- [ ] **Step 1: Write the failing test**

`tests/DotNetKnowledge.Markdown.Tests/MarkdownAtomicBlocksTests.cs`:

```csharp
using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownAtomicBlocksTests
{
    // Line numbers (1-based):
    //  1: # Title            9: ```csharp         16: | X | Y |
    //  2:                   10: class Foo         17: |---|---|
    //  3: ## A               11: {                18: | 1 | 2 |
    //  4:                   12:     void Bar() { } 19:
    //  5: ### B              13: }                 20: Tail line.
    //  6:                   14: ```
    //  7: ## C               15:
    //  8:
    private const string Document =
        "# Title\n\n## A\n\n### B\n\n## C\n\n" +
        "```csharp\nclass Foo\n{\n    void Bar() { }\n}\n```\n\n" +
        "| X | Y |\n|---|---|\n| 1 | 2 |\n\nTail line.\n";

    [TestMethod]
    public void FindReturnsTheFencedCodeBlockAsAnExclusiveEndRange()
    {
        var blocks = MarkdownAtomicBlocks.Find(Document);

        var fenced = blocks.Single(b => b.StartLine == 9);
        Assert.AreEqual(15, fenced.EndLine);
    }

    [TestMethod]
    public void FindReturnsTheTableAsAnExclusiveEndRange()
    {
        var blocks = MarkdownAtomicBlocks.Find(Document);

        var table = blocks.Single(b => b.StartLine == 16);
        Assert.AreEqual(19, table.EndLine);
    }

    [TestMethod]
    public void FindReturnsBlocksOrderedByStartLine()
    {
        var blocks = MarkdownAtomicBlocks.Find(Document);

        CollectionAssert.AreEqual(
            blocks.OrderBy(b => b.StartLine).ToArray(),
            blocks.ToArray());
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj --filter FullyQualifiedName~MarkdownAtomicBlocksTests`
Expected: build FAILS — `MarkdownAtomicBlocks` doesn't exist yet.

- [ ] **Step 3: Implement `MarkdownAtomicBlocks`**

`src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs`:

```csharp
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;

namespace DotNetKnowledge.Markdown;

public static class MarkdownAtomicBlocks
{
    public static IReadOnlyList<(int StartLine, int EndLine)> Find(string markdown)
    {
        ArgumentNullException.ThrowIfNull(markdown);

        var pipeline = new MarkdownPipelineBuilder().UsePipeTables().Build();
        var document = Markdig.Markdown.Parse(markdown, pipeline);
        var blocks = new List<(int StartLine, int EndLine)>();

        foreach (var fenced in document.Descendants<FencedCodeBlock>())
        {
            var lastContentLine = fenced.Lines.Count > 0
                ? fenced.Lines.Lines[fenced.Lines.Count - 1].Line
                : fenced.Line;
            blocks.Add((fenced.Line + 1, lastContentLine + 3));
        }

        foreach (var table in document.Descendants<Table>())
        {
            var rows = table.OfType<TableRow>().ToArray();
            if (rows.Length == 0)
                continue;
            blocks.Add((table.Line + 1, rows[^1].Line + 2));
        }

        return blocks.OrderBy(block => block.StartLine).ToArray();
    }
}
```

Note on the arithmetic: `FencedCodeBlock.Line` (0-based) is the opening fence line;
`fenced.Lines.Lines[^1].Line` (0-based) is the last *content* line inside the fence (the closing
` ``` ` itself is not part of `Lines`). Converting to 1-based and making the end exclusive:
`lastContentLine + 3` = `(lastContentLine + 1)` [1-based content line] `+ 1` [the closing fence
line] `+ 1` [make it exclusive]. `Table.Line` (0-based) is the header row; a `TableRow` in Markdig
already excludes the separator row, so the last `TableRow`'s `.Line` is the table's true last
source line — `+ 2` converts to 1-based and makes it exclusive.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj --filter FullyQualifiedName~MarkdownAtomicBlocksTests`
Expected: PASS, all 3 tests. Verified during planning.

- [ ] **Step 5: Commit**

```bash
git add src/DotNetKnowledge.Markdown/MarkdownAtomicBlocks.cs tests/DotNetKnowledge.Markdown.Tests/MarkdownAtomicBlocksTests.cs
git commit -m "Add fenced-code and table atomic-block detection"
```

---

### Task 3: Character-budget pager

**Files:**
- Create: `src/DotNetKnowledge.Markdown/MarkdownPager.cs`
- Create: `tests/DotNetKnowledge.Markdown.Tests/MarkdownPagerTests.cs`

**Interfaces:**
- Consumes: the `(int StartLine, int EndLine)` shape `MarkdownAtomicBlocks.Find` produces (Task 2),
  but takes it as a plain parameter — no direct call to `MarkdownAtomicBlocks` itself, so it's
  independently testable with hand-built block lists.
- Produces:
  `MarkdownPager.Page(IReadOnlyList<string> lines, IReadOnlyList<(int StartLine, int EndLine)> atomicBlocks, int startLine, int endLineExclusiveBound, int charBudget) -> (int EndLineExclusive, bool IsPartial)`.
  `lines` is 0-indexed (`lines[0]` is source line 1); `startLine`/`endLineExclusiveBound` are the
  1-based line-number convention used everywhere else in this library.

- [ ] **Step 1: Write the failing test**

`tests/DotNetKnowledge.Markdown.Tests/MarkdownPagerTests.cs`:

```csharp
using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownPagerTests
{
    [TestMethod]
    public void PageStopsAtABudgetOnAnOrdinaryLineBoundary()
    {
        var lines = new[] { "1234567890", "abcde", "fghij", "klmno" };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks: [], startLine: 1, endLineExclusiveBound: 5, charBudget: 17);

        // line 1 costs 11 (10 chars + newline); line 2 would add 6 more (17, at budget) so it's
        // included; line 3 would push to 23, over budget, so the page stops before it.
        Assert.AreEqual(3, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageAlwaysIncludesAtLeastOneLineEvenOverBudget()
    {
        var lines = new[] { "a very long line that alone exceeds the budget", "next" };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks: [], startLine: 1, endLineExclusiveBound: 3, charBudget: 5);

        Assert.AreEqual(2, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageNeverStopsInsideAFencedCodeBlockOrTable()
    {
        // Lines 1-2 are prose; 3-5 are one fenced block; 6 is prose. A budget that would
        // naturally cut inside the block must instead extend through line 5.
        var lines = new[] { "before", "before2", "```", "code line", "```", "after" };
        var atomicBlocks = new[] { (StartLine: 3, EndLine: 6) };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine: 1, endLineExclusiveBound: 7, charBudget: 20);

        Assert.AreEqual(6, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageNeverExtendsPastTheBoundEvenWhenABlockWouldCrossIt()
    {
        var lines = new[] { "```", "code", "```" };
        // A block that (pathologically) extends past the requested bound must not pull the page
        // past that bound; the bound wins.
        var atomicBlocks = new[] { (StartLine: 1, EndLine: 4) };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine: 1, endLineExclusiveBound: 3, charBudget: 1);

        Assert.AreEqual(2, endLineExclusive);
        Assert.IsTrue(isPartial);
    }

    [TestMethod]
    public void PageReturnsNotPartialWhenTheWholeRangeFits()
    {
        var lines = new[] { "a", "b", "c" };

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks: [], startLine: 1, endLineExclusiveBound: 4, charBudget: 1000);

        Assert.AreEqual(4, endLineExclusive);
        Assert.IsFalse(isPartial);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj --filter FullyQualifiedName~MarkdownPagerTests`
Expected: build FAILS — `MarkdownPager` doesn't exist yet.

- [ ] **Step 3: Implement `MarkdownPager`**

`src/DotNetKnowledge.Markdown/MarkdownPager.cs`:

```csharp
namespace DotNetKnowledge.Markdown;

public static class MarkdownPager
{
    public static (int EndLineExclusive, bool IsPartial) Page(
        IReadOnlyList<string> lines,
        IReadOnlyList<(int StartLine, int EndLine)> atomicBlocks,
        int startLine,
        int endLineExclusiveBound,
        int charBudget)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(atomicBlocks);

        var stopLine = startLine;
        var chars = 0;

        while (stopLine < endLineExclusiveBound)
        {
            var lineLength = lines[stopLine - 1].Length + 1;
            if (chars > 0 && chars + lineLength > charBudget)
                break;

            chars += lineLength;
            stopLine++;
        }

        // Never end in the middle of a fenced code block or a table: extend past any atomic
        // block that started before stopLine but has not yet ended, unless doing so would cross
        // the requested bound (a malformed document's unclosed fence must not pull the page past
        // where the caller asked it to stop).
        bool extended;
        do
        {
            extended = false;
            foreach (var block in atomicBlocks)
            {
                if (block.StartLine < stopLine && block.EndLine > stopLine && block.EndLine <= endLineExclusiveBound)
                {
                    stopLine = block.EndLine;
                    extended = true;
                }
            }
        } while (extended);

        return (stopLine, stopLine < endLineExclusiveBound);
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj --filter FullyQualifiedName~MarkdownPagerTests`
Expected: PASS, all 5 tests. Verified during planning.

- [ ] **Step 5: Commit**

```bash
git add src/DotNetKnowledge.Markdown/MarkdownPager.cs tests/DotNetKnowledge.Markdown.Tests/MarkdownPagerTests.cs
git commit -m "Add character-budget pager that respects atomic blocks"
```

---

### Task 4: Literal/regex line search

**Files:**
- Create: `src/DotNetKnowledge.Markdown/MarkdownLineSearch.cs`
- Create: `tests/DotNetKnowledge.Markdown.Tests/MarkdownLineSearchTests.cs`

**Interfaces:**
- Consumes: `MarkdownHeading` (Task 1) — a caller passes an already-extracted outline in, this
  method does not call `MarkdownOutline.Extract` itself.
- Produces: `MarkdownLineHit(int Line, string Text, string SectionPath)` and
  `MarkdownLineSearch.Search(string markdown, IReadOnlyList<MarkdownHeading> outline, string pattern, bool regex) -> IReadOnlyList<MarkdownLineHit>`.
  Regex mode uses `RegexOptions.NonBacktracking`; an unsupported construct throws
  `NotSupportedException`, invalid syntax throws `RegexParseException` (itself an
  `ArgumentException`) — both straight from the framework, uncaught here.

- [ ] **Step 1: Write the failing test**

`tests/DotNetKnowledge.Markdown.Tests/MarkdownLineSearchTests.cs`:

```csharp
using System.Text.RegularExpressions;
using DotNetKnowledge.Markdown;

namespace DotNetKnowledge.Markdown.Tests;

[TestClass]
public sealed class MarkdownLineSearchTests
{
    // 1: # Title      4: Some prose in A.
    // 2:               5:
    // 3: ## A          6: class Foo in a code line
    private const string Document = "# Title\n\n## A\n\nSome prose in A.\n\nclass Foo in a code line\n";

    [TestMethod]
    public void SearchLiteralMatchesCaseSensitiveSubstringsAndAttributesTheEnclosingSection()
    {
        var outline = MarkdownOutline.Extract(Document);

        var hits = MarkdownLineSearch.Search(Document, outline, "prose", regex: false);

        Assert.HasCount(1, hits);
        Assert.AreEqual(5, hits[0].Line);
        Assert.AreEqual("Title > A", hits[0].SectionPath);

        Assert.IsEmpty(MarkdownLineSearch.Search(Document, outline, "PROSE", regex: false));
    }

    [TestMethod]
    public void SearchRegexMatchesAndAttributesTheEnclosingSection()
    {
        var outline = MarkdownOutline.Extract(Document);

        var hits = MarkdownLineSearch.Search(Document, outline, "class \\w+", regex: true);

        Assert.HasCount(1, hits);
        Assert.AreEqual(7, hits[0].Line);
        Assert.AreEqual("Title > A", hits[0].SectionPath);
    }

    [TestMethod]
    public void SearchRegexThrowsBeforeMatchingWhenThePatternUsesABackreference()
    {
        var outline = MarkdownOutline.Extract(Document);

        Assert.ThrowsExactly<NotSupportedException>(() =>
            MarkdownLineSearch.Search(Document, outline, @"(\w+)\s+\1", regex: true));
    }

    [TestMethod]
    public void SearchRegexThrowsAParseExceptionForInvalidSyntax()
    {
        var outline = MarkdownOutline.Extract(Document);

        Assert.ThrowsExactly<RegexParseException>(() =>
            MarkdownLineSearch.Search(Document, outline, "[unterminated(", regex: true));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj --filter FullyQualifiedName~MarkdownLineSearchTests`
Expected: build FAILS — `MarkdownLineSearch`/`MarkdownLineHit` don't exist yet.

- [ ] **Step 3: Implement `MarkdownLineSearch`**

`src/DotNetKnowledge.Markdown/MarkdownLineSearch.cs`:

```csharp
using System.Text.RegularExpressions;

namespace DotNetKnowledge.Markdown;

public sealed record MarkdownLineHit(int Line, string Text, string SectionPath);

public static class MarkdownLineSearch
{
    public static IReadOnlyList<MarkdownLineHit> Search(
        string markdown,
        IReadOnlyList<MarkdownHeading> outline,
        string pattern,
        bool regex)
    {
        ArgumentNullException.ThrowIfNull(markdown);
        ArgumentNullException.ThrowIfNull(outline);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        var compiled = regex ? new Regex(pattern, RegexOptions.NonBacktracking) : null;
        var lines = markdown.ReplaceLineEndings("\n").Split('\n');
        var hits = new List<MarkdownLineHit>();

        for (var i = 0; i < lines.Length; i++)
        {
            var matched = compiled is not null
                ? compiled.IsMatch(lines[i])
                : lines[i].Contains(pattern, StringComparison.Ordinal);
            if (!matched)
                continue;

            var lineNumber = i + 1;
            var section = outline.LastOrDefault(
                heading => heading.StartLine <= lineNumber && lineNumber < heading.EndLine);
            hits.Add(new MarkdownLineHit(lineNumber, lines[i], section?.Path ?? string.Empty));
        }

        return hits;
    }
}
```

`outline.LastOrDefault(...)` relies on `MarkdownOutline.Extract`'s document-order output: since a
child heading's line range nests inside its parent's and always appears later in that flat list,
the *last* heading whose range contains a line is the most specific (deepest) enclosing section.

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj`
Expected: PASS, all 18 tests in the project (6 + 3 + 5 + 4 across all four files). Verified during
planning: 18/18 pass, `dotnet build src/DotNetKnowledge.Markdown/DotNetKnowledge.Markdown.csproj -warnaserror` reports 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/DotNetKnowledge.Markdown/MarkdownLineSearch.cs tests/DotNetKnowledge.Markdown.Tests/MarkdownLineSearchTests.cs
git commit -m "Add literal and non-backtracking-regex line search"
```

---

### Task 5: `LanguageDocsQueryService`/`LanguageDocsTool` foundation + `get_language_doc_outline`

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsModels.cs`
- Create: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsQueryService.cs`
- Create: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsTool.cs`
- Modify: `src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj`
- Modify: `src/DotNetKnowledge.Mcp/Program.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsQueryServiceTests.cs`
- Create: `tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsToolTests.cs`

**Interfaces:**
- Consumes: `MarkdownOutline.Extract` (Task 1); `DotNetKnowledge.Mcp.Features.ApiDocs.SourceProvenance`
  and `SourceNotSyncedException` (already exist, reused directly rather than duplicated);
  `DotNetKnowledge.Mcp.Sources.{SourceCatalog, SourceCache, SourceSynchronizer, SourceDefinition, SourceSyncState}`
  (already exist).
- Produces (this task): `LanguageDocOutlineEntry(int Level, string Text, string Path)`,
  `LanguageDocOutlineResult(string Path, SourceProvenance Source, IReadOnlyList<LanguageDocOutlineEntry> Entries, bool IsPartial, string? NextPageToken)`,
  `LanguageDocPathNotFoundException(string path, string sourceName)` (derives from `Exception`, not
  `InvalidOperationException` — see Global Constraints),
  `LanguageDocsQueryService.GetOutlineAsync(string path, string source, int limit, string? cursor, CancellationToken) -> Task<LanguageDocOutlineResult>`,
  the `get_language_doc_outline` MCP tool. Tasks 6 and 7 add to these same three files rather than
  creating new ones.

- [ ] **Step 1: Write the failing tests**

`tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsQueryServiceTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Features.LanguageDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.LanguageDocs;

[TestClass]
public sealed class LanguageDocsQueryServiceTests
{
    private const string ProposalA =
        "# Feature A\n" +
        "\n" +
        "## Motivation\n" +
        "\n" +
        "Some motivating prose about feature A.\n" +
        "\n" +
        "## Detailed design\n" +
        "\n" +
        "```csharp\n" +
        "class Foo { }\n" +
        "```\n" +
        "\n" +
        "## Alternatives\n" +
        "\n" +
        "### Alternative 1\n" +
        "\n" +
        "Alternative text.\n";

    private const string ProposalB =
        "# Feature B\n" +
        "\n" +
        "## Summary\n" +
        "\n" +
        "Summary text mentioning FeatureA for cross-file search.\n";

    [TestMethod]
    public async Task GetOutlineAsyncReturnsHeadingsAndPaginates()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var first = await service.GetOutlineAsync(
                "docs/proposal-a.md", "csharplang", limit: 2, cursor: null, CancellationToken.None);

            Assert.HasCount(2, first.Entries);
            Assert.AreEqual("Feature A", first.Entries[0].Path);
            Assert.AreEqual("Feature A > Motivation", first.Entries[1].Path);
            Assert.IsTrue(first.IsPartial);
            Assert.IsNotNull(first.NextPageToken);
            Assert.AreEqual("test/csharplang", first.Source.Repo);

            var second = await service.GetOutlineAsync(
                "docs/proposal-a.md", "csharplang", limit: 2, first.NextPageToken, CancellationToken.None);
            Assert.AreEqual("Feature A > Detailed design", second.Entries[0].Path);

            await Assert.ThrowsExactlyAsync<ArgumentException>(() => service.GetOutlineAsync(
                "docs/proposal-b.md", "csharplang", limit: 2, first.NextPageToken, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncRejectsAPathThatEscapesTheSourceRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            await Assert.ThrowsExactlyAsync<LanguageDocPathNotFoundException>(() => service.GetOutlineAsync(
                "../../etc/passwd", "csharplang", limit: 20, cursor: null, CancellationToken.None));
            await Assert.ThrowsExactlyAsync<LanguageDocPathNotFoundException>(() => service.GetOutlineAsync(
                "docs/does-not-exist.md", "csharplang", limit: 20, cursor: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncThrowsSourceNotSyncedWhenNeverSynced()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(root);
            var catalogPath = Path.Combine(root, "sources.json");
            await WriteCatalogAsync(catalogPath, Path.Combine(root, "origin"), new string('a', 40));
            var catalog = new SourceCatalog(catalogPath);
            var cache = new SourceCache(Path.Combine(root, "cache"));
            var synchronizer = new SourceSynchronizer(catalog, cache);
            var service = new LanguageDocsQueryService(catalog, cache, synchronizer);

            var exception = await Assert.ThrowsExactlyAsync<SourceNotSyncedException>(() => service.GetOutlineAsync(
                "docs/proposal-a.md", "csharplang", limit: 20, cursor: null, CancellationToken.None));
            Assert.AreEqual("csharplang", exception.SourceName);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<LanguageDocsQueryService> CreateServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var docsDirectory = Path.Combine(repository, "docs");
        Directory.CreateDirectory(docsDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-a.md"), ProposalA);
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-b.md"), ProposalB);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
        return new LanguageDocsQueryService(catalog, cache, synchronizer);
    }

    private static async Task WriteCatalogAsync(string path, string repository, string pin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["csharplang"] = new
                {
                    repository = "test/csharplang",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test language docs.",
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task<string> RunGitAsync(string? workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.AreEqual(0, process.ExitCode, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(path, recursive: true);
    }
}
```

`tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsToolTests.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.LanguageDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.LanguageDocs;

[TestClass]
public sealed class LanguageDocsToolTests
{
    private const string ProposalA =
        "# Feature A\n\n## Motivation\n\nSome motivating prose.\n";

    [TestMethod]
    public async Task GetLanguageDocOutlineNamesTheRequiredSyncWhenSourceIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var catalog = new SourceCatalog();
            var cache = new SourceCache(root);
            var synchronizer = new SourceSynchronizer(catalog, cache);
            var service = new LanguageDocsQueryService(catalog, cache, synchronizer);

            var json = await LanguageDocsTool.GetLanguageDocOutline(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("source_not_synced", document.RootElement.GetProperty("error").GetString());
            Assert.AreEqual("csharplang", document.RootElement.GetProperty("source").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "sync_source");
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [TestMethod]
    public async Task GetLanguageDocOutlineReturnsPathNotFoundForAMissingFile()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.GetLanguageDocOutline(
                "docs/missing.md", "csharplang", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("path_not_found", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetLanguageDocOutlineReturnsInvalidRequestForAnUnrecognizedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.GetLanguageDocOutline(
                "docs/proposal-a.md", "not-a-real-source", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_request", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    private static async Task<LanguageDocsQueryService> CreateServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var docsDirectory = Path.Combine(repository, "docs");
        Directory.CreateDirectory(docsDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-a.md"), ProposalA);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
        return new LanguageDocsQueryService(catalog, cache, synchronizer);
    }

    private static async Task WriteCatalogAsync(string path, string repository, string pin)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = new Dictionary<string, object>
            {
                ["csharplang"] = new
                {
                    repository = "test/csharplang",
                    url = repository,
                    pin,
                    head = "main",
                    sparse = new[] { "docs" },
                    purpose = "Test language docs.",
                },
            },
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static async Task<string> RunGitAsync(string? workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.AreEqual(0, process.ExitCode, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    private static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(path, recursive: true);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~LanguageDocs`
Expected: build FAILS — none of `LanguageDocsQueryService`, `LanguageDocsTool`, or the models exist
yet, and `DotNetKnowledge.Mcp` doesn't yet reference `DotNetKnowledge.Markdown`.

- [ ] **Step 3: Reference `DotNetKnowledge.Markdown` from the server project**

Add to `src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj`, in its own `ItemGroup` after the
existing `PackageReference`s:

```xml
  <ItemGroup>
    <ProjectReference Include="..\DotNetKnowledge.Markdown\DotNetKnowledge.Markdown.csproj" />
  </ItemGroup>
```

- [ ] **Step 4: Implement the models**

`src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsModels.cs`:

```csharp
using DotNetKnowledge.Mcp.Features.ApiDocs;

namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

public sealed record LanguageDocOutlineEntry(int Level, string Text, string Path);

public sealed record LanguageDocOutlineResult(
    string Path,
    SourceProvenance Source,
    IReadOnlyList<LanguageDocOutlineEntry> Entries,
    bool IsPartial,
    string? NextPageToken);

public sealed class LanguageDocPathNotFoundException : Exception
{
    public LanguageDocPathNotFoundException(string path, string sourceName)
        : base($"'{path}' was not found in '{sourceName}'. Call search_language_docs, or list_sources for cacheDir.")
    {
        Path = path;
        SourceName = sourceName;
    }

    public string Path { get; }
    public string SourceName { get; }
}
```

- [ ] **Step 5: Implement `LanguageDocsQueryService`**

`src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsQueryService.cs`:

```csharp
using System.Text;
using System.Text.Json;
using DotNetKnowledge.Markdown;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

public sealed class LanguageDocsQueryService
{
    private static readonly string[] SupportedSources = ["csharplang", "vblang"];

    private readonly SourceCatalog _catalog;
    private readonly SourceSynchronizer _synchronizer;

    public LanguageDocsQueryService(SourceCatalog catalog, SourceCache cache, SourceSynchronizer synchronizer)
    {
        _catalog = catalog;
        ArgumentNullException.ThrowIfNull(cache);
        _synchronizer = synchronizer;
    }

    public async Task<LanguageDocOutlineResult> GetOutlineAsync(
        string path,
        string source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidateSource(source);
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 500.");

        var (text, provenance) = await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var headings = MarkdownOutline.Extract(text);

        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, path);
        var offset = DecodeCursor(cursor, "lang-outline", scope, revisions);
        if (offset > headings.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = headings.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < headings.Count;

        return new LanguageDocOutlineResult(
            path,
            provenance,
            page.Select(heading => new LanguageDocOutlineEntry(heading.Level, heading.Text, heading.Path)).ToArray(),
            isPartial,
            isPartial ? EncodeCursor("lang-outline", scope, nextOffset, revisions) : null);
    }

    private async Task<(string Text, SourceProvenance Provenance)> ReadDocumentAsync(
        string source, string path, CancellationToken cancellationToken)
    {
        DocumentRead read;
        try
        {
            read = await _synchronizer.ReadCurrentSourceAsync(
                source,
                (definition, state, directory) => ReadDocument(directory, source, path, definition, state),
                cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception)
        {
            throw new SourceNotSyncedException(source, exception);
        }

        return (read.Text, read.Provenance);
    }

    private sealed record DocumentRead(string Text, SourceProvenance Provenance);

    private static DocumentRead ReadDocument(
        string directory, string source, string path, SourceDefinition definition, SourceSyncState state)
    {
        var fullPath = ResolveFullPath(directory, source, path);
        return new DocumentRead(File.ReadAllText(fullPath), ToProvenance(definition, state));
    }

    private static string ResolveFullPath(string directory, string source, string path)
    {
        var fullRoot = Path.GetFullPath(directory);
        var candidate = Path.GetFullPath(Path.Combine(fullRoot, path));
        var rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        if (string.IsNullOrWhiteSpace(path)
            || Path.IsPathRooted(path)
            || !candidate.StartsWith(rootPrefix, comparison)
            || !File.Exists(candidate))
        {
            throw new LanguageDocPathNotFoundException(path, source);
        }

        return candidate;
    }

    private void ValidateSource(string source)
    {
        if (!SupportedSources.Contains(source, StringComparer.Ordinal) || !_catalog.Sources.ContainsKey(source))
            throw new ArgumentException("source must be \"csharplang\" or \"vblang\".", nameof(source));
    }

    private static SourceProvenance ToProvenance(SourceDefinition definition, SourceSyncState state) =>
        new(definition.Repository, state.Ref, state.Commit, state.FetchedAt);

    private static string RevisionKey(SourceProvenance provenance) =>
        provenance.Repo + "@" + provenance.Ref + "@" + provenance.Commit;

    private static string EncodeScope(params object[] values) => JsonSerializer.Serialize(values);

    private static string EncodeCursor(string kind, string scope, int offset, IReadOnlyList<string> revisions)
    {
        var json = JsonSerializer.Serialize(new PageCursor(Version: 1, Kind: kind, Scope: scope, Offset: offset, Revisions: revisions));
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static int DecodeCursor(string? cursor, string kind, string scope, IReadOnlyList<string> revisions)
    {
        if (cursor is null)
            return 0;

        try
        {
            var base64 = cursor.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight(base64.Length + ((4 - (base64.Length % 4)) % 4), '=');
            var decoded = JsonSerializer.Deserialize<PageCursor>(
                Encoding.UTF8.GetString(Convert.FromBase64String(base64)));

            if (decoded is null
                || decoded.Version != 1
                || decoded.Offset < 0
                || !string.Equals(decoded.Kind, kind, StringComparison.Ordinal)
                || !string.Equals(decoded.Scope, scope, StringComparison.Ordinal)
                || decoded.Revisions is null
                || !decoded.Revisions.SequenceEqual(revisions, StringComparer.Ordinal))
            {
                throw new ArgumentException("cursor does not match this request.", nameof(cursor));
            }

            return decoded.Offset;
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            throw new ArgumentException("cursor is invalid.", nameof(cursor), exception);
        }
    }

    private sealed record PageCursor(int Version, string Kind, string Scope, int Offset, IReadOnlyList<string> Revisions);
}
```

- [ ] **Step 6: Implement `LanguageDocsTool`**

`src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsTool.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using ModelContextProtocol.Server;

namespace DotNetKnowledge.Mcp.Features.LanguageDocs;

[McpServerToolType]
public sealed class LanguageDocsTool
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [McpServerTool(Name = "get_language_doc_outline", ReadOnly = true, Idempotent = true)]
    [Description(
        "Return a synchronized C# or VB.NET language-design document's heading tree, no bodies: " +
        "each entry's level, text, and section path - the path get_language_doc's section " +
        "parameter accepts verbatim. Paginated like the other tools.")]
    public static async Task<string> GetLanguageDocOutline(
        string path,
        string source,
        LanguageDocsQueryService service,
        CancellationToken cancellationToken,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.GetOutlineAsync(
                path,
                source,
                limit ?? 100,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (LanguageDocPathNotFoundException exception)
        {
            return SerializeError("path_not_found", exception.Message);
        }
        catch (SourceNotSyncedException exception)
        {
            return SerializeSourceNotSynced(exception);
        }
        catch (ArgumentException exception)
        {
            return SerializeArgumentException(exception);
        }
        catch (TimeoutException exception)
        {
            return SerializeError("git_timeout", exception.Message);
        }
    }

    private static string SerializeSourceNotSynced(SourceNotSyncedException exception) =>
        JsonSerializer.Serialize(
            new { error = "source_not_synced", message = exception.Message, source = exception.SourceName },
            WriteOptions);

    private static string SerializeArgumentException(ArgumentException exception) =>
        SerializeError(
            string.Equals(exception.ParamName, "cursor", StringComparison.Ordinal) ? "invalid_cursor" : "invalid_request",
            exception.Message);

    private static string SerializeError(string error, string message) =>
        JsonSerializer.Serialize(new { error, message }, WriteOptions);
}
```

- [ ] **Step 7: Register the service in `Program.cs`**

Add the `using` beside the existing `Features.ApiDocs` one:

```csharp
using DotNetKnowledge.Mcp.Features.LanguageDocs;
```

Add the registration beside the existing `ApiDocsQueryService` one:

```csharp
builder.Services.AddSingleton<LanguageDocsQueryService>();
```

- [ ] **Step 8: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~LanguageDocs`
Expected: PASS, all 6 tests (3 in each file). Verified during planning: 6/6 pass, `dotnet build
src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj -warnaserror` reports 0 warnings.

- [ ] **Step 9: Commit**

```bash
git add src/DotNetKnowledge.Mcp tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs
git commit -m "Add get_language_doc_outline"
```

---

### Task 6: `search_language_docs`

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsQueryService.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsTool.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsQueryServiceTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsToolTests.cs`

**Interfaces:**
- Consumes: `MarkdownLineSearch.Search`, `MarkdownOutline.Extract` (Tasks 4, 1).
- Produces:
  `LanguageDocLineHit(string Path, int Line, string Text, string SectionPath, SourceProvenance Source)`,
  `LanguageDocSearchResult(IReadOnlyList<LanguageDocLineHit> Hits, bool IsPartial, string? NextPageToken, IReadOnlyList<SourceProvenance> SearchedSources)`,
  `LanguageDocsQueryService.SearchAsync(string query, bool regex, string? source, int limit, string? cursor, CancellationToken) -> Task<LanguageDocSearchResult>`,
  the `search_language_docs` MCP tool.

- [ ] **Step 1: Write the failing tests**

Add to `LanguageDocsQueryServiceTests.cs`, just above the closing brace of
`CreateServiceAsync`'s preceding method (i.e., add this new field near the other `private static
readonly` fields at the top of the class, and this new test method anywhere among the other
`[TestMethod]`s):

```csharp
    private static readonly string[] ExpectedRegexHitPaths = ["docs/proposal-a.md", "docs/proposal-b.md"];
```

```csharp
    [TestMethod]
    public async Task SearchAsyncMatchesLiteralAndRegexAcrossFilesAndOrdersDeterministically()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var literal = await service.SearchAsync(
                "FeatureA", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);
            Assert.HasCount(1, literal.Hits);
            Assert.AreEqual("docs/proposal-b.md", literal.Hits[0].Path);
            Assert.AreEqual("Feature B > Summary", literal.Hits[0].SectionPath);

            var regex = await service.SearchAsync(
                "Feature [AB]", regex: true, source: null, limit: 20, cursor: null, CancellationToken.None);
            Assert.HasCount(2, regex.Hits);
            CollectionAssert.AreEqual(
                ExpectedRegexHitPaths,
                regex.Hits.Select(hit => hit.Path).ToArray());

            await Assert.ThrowsExactlyAsync<NotSupportedException>(() => service.SearchAsync(
                @"(\w+)\s+\1", regex: true, source: null, limit: 20, cursor: null, CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Add to `LanguageDocsToolTests.cs`, among the other `[TestMethod]`s:

```csharp
    [TestMethod]
    public async Task SearchLanguageDocsReturnsInvalidRegexForAnUnsupportedConstruct()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.SearchLanguageDocs(
                @"(\w+)\s+\1", service, CancellationToken.None, regex: true);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_regex", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchLanguageDocsReturnsInvalidRequestForAnUnrecognizedSource()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.SearchLanguageDocs(
                "prose", service, CancellationToken.None, source: "not-a-real-source");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("invalid_request", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Also change `LanguageDocsQueryServiceTests`'s `CreateServiceAsync` fixture: it must write **two**
files so cross-file search has something to distinguish. Replace its body with:

```csharp
    private static async Task<LanguageDocsQueryService> CreateServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var docsDirectory = Path.Combine(repository, "docs");
        Directory.CreateDirectory(docsDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-a.md"), ProposalA);
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-b.md"), ProposalB);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
        return new LanguageDocsQueryService(catalog, cache, synchronizer);
    }
```

(Task 5's version of this fixture already writes both `ProposalA` and `ProposalB` — this step is a
no-op if Task 5 was completed as written above; it's spelled out here so this task's test file is
self-contained to verify against.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~LanguageDocs`
Expected: build FAILS — `SearchAsync`/`SearchLanguageDocs` don't exist yet.

- [ ] **Step 3: Add the search models**

In `LanguageDocsModels.cs`, add above `LanguageDocOutlineEntry`:

```csharp
public sealed record LanguageDocLineHit(
    string Path,
    int Line,
    string Text,
    string SectionPath,
    SourceProvenance Source);

public sealed record LanguageDocSearchResult(
    IReadOnlyList<LanguageDocLineHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<SourceProvenance> SearchedSources);
```

- [ ] **Step 4: Add `SearchAsync` and its helpers to `LanguageDocsQueryService`**

Add the `using System.Text.RegularExpressions;` to the top of the file, alongside the existing
`using`s.

Add `SearchAsync`, just above the existing `ReadDocumentAsync` method:

```csharp
    public async Task<LanguageDocSearchResult> SearchAsync(
        string query,
        bool regex,
        string? source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 100.");

        // Validate once, up front: an invalid pattern must fail the same way regardless of how
        // many markdown files a source happens to hold, rather than only surfacing on whichever
        // file MarkdownLineSearch happens to reach first.
        if (regex)
            _ = new Regex(query, RegexOptions.NonBacktracking);

        var sourceNames = ResolveSourceNames(source);
        var hits = new List<LanguageDocLineHit>();
        var searchedSources = new List<SourceProvenance>();

        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceSearchRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) => ReadSearchSource(directory, definition, state, query, regex),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            hits.AddRange(read.Hits);
        }

        var ordered = hits
            .OrderBy(hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(hit => hit.Line)
            .ThenBy(hit => hit.Source.Repo, StringComparer.Ordinal)
            .ToArray();

        var revisions = searchedSources.Select(RevisionKey).ToArray();
        var scope = EncodeScope(query, regex, source ?? string.Empty);
        var offset = DecodeCursor(cursor, "lang-search", scope, revisions);
        if (offset > ordered.Length)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Length;

        return new LanguageDocSearchResult(
            page,
            isPartial,
            isPartial ? EncodeCursor("lang-search", scope, nextOffset, revisions) : null,
            searchedSources);
    }
```

Add `ReadSearchSource`, just above the existing `ResolveFullPath` method:

```csharp
    private sealed record SourceSearchRead(SourceProvenance Provenance, IReadOnlyList<LanguageDocLineHit> Hits);

    private static SourceSearchRead ReadSearchSource(
        string directory, SourceDefinition definition, SourceSyncState state, string query, bool regex)
    {
        var provenance = ToProvenance(definition, state);
        var fullRoot = Path.GetFullPath(directory);
        var hits = new List<LanguageDocLineHit>();

        foreach (var file in Directory.EnumerateFiles(fullRoot, "*.md", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            var outline = MarkdownOutline.Extract(text);
            var relativePath = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');

            foreach (var hit in MarkdownLineSearch.Search(text, outline, query, regex))
            {
                var truncated = hit.Text.Length > 300 ? hit.Text[..300] + "…" : hit.Text;
                hits.Add(new LanguageDocLineHit(relativePath, hit.Line, truncated, hit.SectionPath, provenance));
            }
        }

        return new SourceSearchRead(provenance, hits);
    }
```

Add `ResolveSourceNames`, just above the existing `ValidateSource` method:

```csharp
    private string[] ResolveSourceNames(string? source)
    {
        if (source is not null)
        {
            ValidateSource(source);
            return [source];
        }

        return SupportedSources
            .Where(_catalog.Sources.ContainsKey)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }
```

- [ ] **Step 5: Add `SearchLanguageDocs` to `LanguageDocsTool`**

Add the `using System.Text.RegularExpressions;` to the top of the file.

Add the tool method, just above the existing `get_language_doc_outline` one:

```csharp
    [McpServerTool(Name = "search_language_docs", ReadOnly = true, Idempotent = true)]
    [Description(
        "Search synchronized C# and VB.NET language-design documents (proposals, spec, LDM " +
        "meeting notes) by literal substring or, with regex: true, a .NET regex evaluated with " +
        "the non-backtracking engine. Returns path:line hits with the matched line and a " +
        "server-issued section heading path, never file bodies; call get_language_doc for content.")]
    public static async Task<string> SearchLanguageDocs(
        string query,
        LanguageDocsQueryService service,
        CancellationToken cancellationToken,
        bool? regex = null,
        string? source = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.SearchAsync(
                query,
                regex ?? false,
                source,
                limit ?? 20,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (RegexParseException exception)
        {
            return SerializeError("invalid_regex", exception.Message);
        }
        catch (NotSupportedException exception)
        {
            return SerializeError("invalid_regex", exception.Message);
        }
        catch (SourceNotSyncedException exception)
        {
            return SerializeSourceNotSynced(exception);
        }
        catch (ArgumentException exception)
        {
            return SerializeArgumentException(exception);
        }
        catch (TimeoutException exception)
        {
            return SerializeError("git_timeout", exception.Message);
        }
    }
```

`RegexParseException` is itself an `ArgumentException`, so its `catch` must come before the
generic `ArgumentException` one — it already does, above.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~LanguageDocs`
Expected: PASS, all 9 tests (4 service + 5 tool). Verified during planning: 9/9 pass, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/LanguageDocs tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs
git commit -m "Add search_language_docs"
```

---

### Task 7: `get_language_doc`

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsQueryService.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/LanguageDocs/LanguageDocsTool.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsQueryServiceTests.cs`
- Modify: `tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs/LanguageDocsToolTests.cs`

**Interfaces:**
- Consumes: `MarkdownAtomicBlocks.Find`, `MarkdownPager.Page`, `MarkdownOutline.Extract` (Tasks 2, 3, 1).
- Produces:
  `LanguageDocContentResult(string Path, SourceProvenance Source, string? Section, string Text, int StartLine, int EndLine, bool IsPartial, string? NextPageToken)`,
  `LanguageDocSectionNotFoundException(string section, string path, string sourceName)` (derives
  from `Exception`, per Global Constraints),
  `LanguageDocsQueryService.GetDocAsync(string path, string source, string? section, int limit, string? cursor, CancellationToken) -> Task<LanguageDocContentResult>`,
  the `get_language_doc` MCP tool.

- [ ] **Step 1: Write the failing tests**

Add to `LanguageDocsQueryServiceTests.cs`, among the other `[TestMethod]`s:

```csharp
    [TestMethod]
    public async Task GetDocAsyncReturnsAWholeSectionAndRejectsAnUnknownSection()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var section = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "Feature A > Motivation", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.AreEqual("Feature A > Motivation", section.Section);
            StringAssert.Contains(section.Text, "Some motivating prose about feature A.");
            Assert.IsFalse(section.Text.Contains("Detailed design"));
            Assert.IsFalse(section.IsPartial);

            var exception = await Assert.ThrowsExactlyAsync<LanguageDocSectionNotFoundException>(() => service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "No Such Section", limit: 8000, cursor: null,
                CancellationToken.None));
            StringAssert.Contains(exception.Message, "get_language_doc_outline");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncPagesByCharacterBudgetWithoutSplittingAFencedCodeBlock()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            // "## Detailed design" section is: heading, blank, ```csharp, class Foo { }, ```, blank.
            // A budget that would naively cut inside the fence must extend past it instead.
            var page = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "Feature A > Detailed design", limit: 1000, cursor: null,
                CancellationToken.None);

            StringAssert.Contains(page.Text, "```csharp");
            StringAssert.Contains(page.Text, "class Foo { }");
            StringAssert.Contains(page.Text, "```");
            Assert.IsFalse(page.IsPartial);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Add to `LanguageDocsToolTests.cs`, among the other `[TestMethod]`s:

```csharp
    [TestMethod]
    public async Task GetLanguageDocReturnsSectionNotFoundNamingTheOutlineTool()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await LanguageDocsTool.GetLanguageDoc(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None, section: "No Such Section");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("section_not_found", document.RootElement.GetProperty("error").GetString());
            StringAssert.Contains(document.RootElement.GetProperty("message").GetString(), "get_language_doc_outline");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~LanguageDocs`
Expected: build FAILS — `GetDocAsync`/`GetLanguageDoc`/`LanguageDocContentResult`/
`LanguageDocSectionNotFoundException` don't exist yet.

- [ ] **Step 3: Add the content model and exception**

In `LanguageDocsModels.cs`, add above `LanguageDocOutlineEntry`:

```csharp
public sealed record LanguageDocContentResult(
    string Path,
    SourceProvenance Source,
    string? Section,
    string Text,
    int StartLine,
    int EndLine,
    bool IsPartial,
    string? NextPageToken);
```

Add at the end of the file, after `LanguageDocPathNotFoundException`:

```csharp
public sealed class LanguageDocSectionNotFoundException : Exception
{
    public LanguageDocSectionNotFoundException(string section, string path, string sourceName)
        : base($"Section '{section}' was not found in '{path}' ({sourceName}). " +
               "Call get_language_doc_outline to see valid section paths for this document.")
    {
        Section = section;
        Path = path;
        SourceName = sourceName;
    }

    public string Section { get; }
    public string Path { get; }
    public string SourceName { get; }
}
```

- [ ] **Step 4: Add `GetDocAsync` to `LanguageDocsQueryService`**

Add it just above the existing `ReadDocumentAsync` method (after `SearchAsync` if Task 6 already
landed):

```csharp
    public async Task<LanguageDocContentResult> GetDocAsync(
        string path,
        string source,
        string? section,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidateSource(source);
        if (limit is < 1000 or > 50000)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1000 and 50000.");

        var (text, provenance) = await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        int rangeStart;
        int rangeEndExclusive;
        if (section is not null)
        {
            var heading = MarkdownOutline.Extract(text)
                .FirstOrDefault(candidate => string.Equals(candidate.Path, section, StringComparison.Ordinal));
            if (heading is null)
                throw new LanguageDocSectionNotFoundException(section, path, source);
            rangeStart = heading.StartLine;
            rangeEndExclusive = heading.EndLine;
        }
        else
        {
            rangeStart = 1;
            rangeEndExclusive = lines.Length + 1;
        }

        var atomicBlocks = MarkdownAtomicBlocks.Find(text);
        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, path, section ?? string.Empty);
        var decodedStartLine = DecodeCursor(cursor, "lang-doc", scope, revisions);
        var startLine = cursor is null ? rangeStart : decodedStartLine;
        if (startLine < rangeStart || startLine >= rangeEndExclusive)
            throw new ArgumentException("cursor points outside the requested section.", nameof(cursor));

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine, rangeEndExclusive, limit);
        var pageText = string.Join('\n', lines[(startLine - 1)..(endLineExclusive - 1)]);

        return new LanguageDocContentResult(
            path,
            provenance,
            section,
            pageText,
            startLine,
            endLineExclusive - 1,
            isPartial,
            isPartial ? EncodeCursor("lang-doc", scope, endLineExclusive, revisions) : null);
    }
```

The `cursor is null ? rangeStart : decodedStartLine` line matters: `DecodeCursor` returns `0` for a
`null` cursor (its sentinel for "start of sequence"), which is never a valid 1-based line number.
The very first page of any doc/section fetch starts at `rangeStart` (1 for a whole document, or the
section heading's own line), not at line 0.

- [ ] **Step 5: Add `GetLanguageDoc` to `LanguageDocsTool`**

Add it just above the existing `get_language_doc_outline` one:

```csharp
    [McpServerTool(Name = "get_language_doc", ReadOnly = true, Idempotent = true)]
    [Description(
        "Fetch a synchronized C# or VB.NET language-design document by its repo-relative path. " +
        "Pass section as a heading path exactly as returned by search_language_docs or " +
        "get_language_doc_outline to fetch just that section; omit it for the whole document. " +
        "Pages by an approximate character budget (limit) that never splits a fenced code block " +
        "or a table.")]
    public static async Task<string> GetLanguageDoc(
        string path,
        string source,
        LanguageDocsQueryService service,
        CancellationToken cancellationToken,
        string? section = null,
        int? limit = null,
        string? cursor = null)
    {
        try
        {
            var result = await service.GetDocAsync(
                path,
                source,
                section,
                limit ?? 8000,
                cursor,
                cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Serialize(result, WriteOptions);
        }
        catch (LanguageDocSectionNotFoundException exception)
        {
            return SerializeError("section_not_found", exception.Message);
        }
        catch (LanguageDocPathNotFoundException exception)
        {
            return SerializeError("path_not_found", exception.Message);
        }
        catch (SourceNotSyncedException exception)
        {
            return SerializeSourceNotSynced(exception);
        }
        catch (ArgumentException exception)
        {
            return SerializeArgumentException(exception);
        }
        catch (TimeoutException exception)
        {
            return SerializeError("git_timeout", exception.Message);
        }
    }
```

`LanguageDocSectionNotFoundException`'s catch must come before `LanguageDocPathNotFoundException`'s
only because both are unrelated sibling types here (order between them doesn't actually matter,
since neither derives from the other) — keep them adjacent for readability, matching the order
they can occur in (`GetDocAsync` resolves the document, i.e. path, before it resolves the section).

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj --filter FullyQualifiedName~LanguageDocs`
Expected: PASS, all 12 tests (6 service + 6 tool). Verified during planning: 12/12 pass, 0 warnings.

- [ ] **Step 7: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/LanguageDocs tests/DotNetKnowledge.Mcp.Tests/Features/LanguageDocs
git commit -m "Add get_language_doc"
```

---

### Task 8: Full-solution verification

**Files:** none (verification only).

**Interfaces:** none.

- [ ] **Step 1: Build the whole solution with warnings as errors**

Run: `dotnet build DotNetKnowledge.slnx`
Expected: the server-side projects build (`DotNetKnowledge.Markdown`, `DotNetKnowledge.Mcp`, both
`Mcp.Tests` fixture projects, `Markdown.Tests`), 0 warnings, 0 errors. Verified during planning.

The corpus is a separate concern with its own solution and its own .NET host: build it with
`dotnet build Corpus.slnx`, which also covers the example-corpus `host` project that `Corpus.Tests`
references. This plan predates that split and originally expected `Corpus.Tests` and `host` in this
same build.

- [ ] **Step 2: Run every test project**

Run: `dotnet test tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj`
Expected: PASS, 18/18.

Run: `dotnet test tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj`
Expected: PASS, 48/48 (36 pre-existing + 12 new). Verified during planning — this includes the
pre-existing `McpStdioTests`, confirming the new `LanguageDocsQueryService` DI registration doesn't
break server startup.

- [ ] **Step 3: Smoke-test all three tools over stdio**

Follow the redirected-process driver pattern `tests/DotNetKnowledge.Mcp.Tests/Protocol/McpStdioTests.cs`
already establishes (a shell pipe swallows the server's stdout and looks like a server fault,
per `AGENTS.md`) to confirm `search_language_docs`, `get_language_doc`, and
`get_language_doc_outline` all appear in `tools/list` and answer `source_not_synced` for an
unsynced source when called through a real MCP client connection, not just through
`DotNetKnowledge.Mcp.Tests`' direct method calls.

- [ ] **Step 4: No commit** — this task only verifies work already committed in Tasks 1–7.

---

### Task 9: Documentation updates

**Files:**
- Modify: `docs/design/mcp-tool-surface.md`
- Modify: `README.md`
- Modify: `docs/decisions.md`

**Interfaces:** none.

- [ ] **Step 1: Add numeric limits to `docs/design/mcp-tool-surface.md`**

In the `search_language_docs` block, add a line after the existing `source: restricts` line:

```
    → limit: 1-100, default 20
```

In the `get_language_doc` block, add a line after `no size cap and no refusal...`:

```
    → limit is a character budget, not an item count: 1000-50000,
      default 8000, snapped to a line boundary and never splitting
      a fenced code block or a table
```

Change the `get_language_doc_outline` signature line from:

```
get_language_doc_outline(path, source)
```

to:

```
get_language_doc_outline(path, source, limit?, cursor?)
    → limit: 1-500, default 100
```

- [ ] **Step 2: Update `README.md`'s status section**

Find the paragraph:

> Language design-document queries and bundled-example queries remain future work; their intended
> surface is recorded in [`docs/design/mcp-tool-surface.md`](docs/design/mcp-tool-surface.md).

Replace with:

> Language design-document queries — `search_language_docs`, `get_language_doc`, and
> `get_language_doc_outline` — are implemented and work under an MCP client. Bundled-example
> queries remain future work; their intended surface is recorded in
> [`docs/design/mcp-tool-surface.md`](docs/design/mcp-tool-surface.md).

Also update the earlier sentence in the same section:

> The server's source and API-doc tools are implemented and work under an MCP client:
> `list_sources`, `sync_source`, `search_api` and `lookup_api` all answer correctly over stdio,
> including a first sync of a large upstream repository.

to add the three new tool names to that list.

- [ ] **Step 3: Add three entries to `docs/decisions.md`**

Append (newest-first, so these go immediately after the file's opening preamble, before the
existing `2026-08-05` entries — check the date against the actual day this task lands and adjust
if it differs):

```markdown
### 2026-08-05 · Markdown parsing lives in its own library, not inside the server

`DotNetKnowledge.Markdown` holds heading extraction, atomic-block detection, character-budget
paging, and line search, with no dependency on the MCP server, `SourceCache`, or JSON. Rejected:
building this directly in `Features/LanguageDocs/`, which would make it unreusable and untestable
without the server's other dependencies.

### 2026-08-05 · `get_language_doc` pages by a character budget, not a line count

A budget bounds response size predictably regardless of how prose-heavy or grammar-production-heavy
a section is; it snaps to the nearest line boundary and never splits a fenced code block or a
table. Rejected: line-count paging, whose response size varies enormously between a one-sentence
paragraph and a wide grammar production.

### 2026-08-05 · A section-path collision gets a suffix only when it actually collides

Two headings with the exact same full ancestor-chain text (a rare but real case, e.g. a repeated
template section) get `Path` and `Path (2)`; every non-colliding path is untouched. Rejected:
unconditionally numbering every heading by sibling position, which makes the overwhelming majority
of paths — the ones that never collide — more verbose for no reason.
```

- [ ] **Step 4: Commit**

```bash
git add docs/design/mcp-tool-surface.md README.md docs/decisions.md
git commit -m "docs: update the language-doc tools' status and design surface"
```
