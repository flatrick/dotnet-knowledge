# Caller-input normalization for Docs tools — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `get_doc`, `get_doc_outline` and `search_docs` a one-shot fallback that retries a caller's `section`, `path`, or `query` against an HTML-entity/typography-decoded form when the literal value would otherwise fail, and reports the substitution transparently.

**Architecture:** A single shared normalizer (`CallerInputNormalization.TryNormalize`) is invoked only from inside each call site's existing failure path — never before the literal attempt. A retry that succeeds returns the canonical resolved value (not the caller's spelling) plus a `NormalizationNote` on the result.

**Tech Stack:** C# / .NET, `DotNetKnowledge.Mcp` (MCP stdio server), MSTest.

Spec: [`docs/superpowers/specs/2026-08-09-caller-input-normalization-design.md`](../specs/2026-08-09-caller-input-normalization-design.md).

## Global Constraints

- Every included call site tries the literal input first, exactly as today; normalization is attempted only after that would fail. A request that already matches literally is unaffected.
- `search_docs` in `regex: true` mode is never normalized.
- A result produced via normalization reports the canonical resolved value (`Path`/`Section`), not the caller's original spelling, and carries a non-null `NormalizationNote`.
- If normalization doesn't change the input, or the retry also fails, the error is byte-for-byte what it is today.
- No new global input filter. `CallerInputNormalization` is only ever called from inside a failure branch a caller would otherwise have hit.
- This plan covers `DocsQueryService`/`DocsTool` only. `ApiDocsQueryService`/`ApiDocsTool` (`lookup_api`, `find_api_references`, `search_api`, `search_api_text`) are a separate plan reusing the same `CallerInputNormalization` primitive.

---

### Task 1: Shared normalizer primitive

**Files:**
- Create: `src/DotNetKnowledge.Mcp/Text/CallerInputNormalization.cs`
- Test: `tests/DotNetKnowledge.Mcp.Tests/Text/CallerInputNormalizationTests.cs`

**Interfaces:**
- Produces: `CallerInputNormalization.TryNormalize(string input, out string normalized) : bool` — returns `true` and sets `normalized` only when it differs from `input`; returns `false` and sets `normalized` to `input` unchanged otherwise. Consumed by Tasks 2–4.

- [ ] **Step 1: Write the failing tests**

Create `tests/DotNetKnowledge.Mcp.Tests/Text/CallerInputNormalizationTests.cs`:

```csharp
using DotNetKnowledge.Mcp.Text;

namespace DotNetKnowledge.Mcp.Tests.Text;

[TestClass]
public sealed class CallerInputNormalizationTests
{
    [TestMethod]
    [DataRow("Filter with x &gt; y", "Filter with x > y")]
    [DataRow("Span&lt;char&gt; support", "Span<char> support")]
    [DataRow("Tom &amp; Jerry", "Tom & Jerry")]
    [DataRow("&quot;quoted&quot;", "\"quoted\"")]
    [DataRow("It&#39;s", "It's")]
    public void TryNormalizeDecodesHtmlEntities(string input, string expected)
    {
        var changed = CallerInputNormalization.TryNormalize(input, out var normalized);

        Assert.IsTrue(changed);
        Assert.AreEqual(expected, normalized);
    }

    [TestMethod]
    [DataRow("‘quoted’", "'quoted'")]
    [DataRow("“quoted”", "\"quoted\"")]
    public void TryNormalizeFoldsCurlyQuotesToStraightQuotes(string input, string expected)
    {
        var changed = CallerInputNormalization.TryNormalize(input, out var normalized);

        Assert.IsTrue(changed);
        Assert.AreEqual(expected, normalized);
    }

    [TestMethod]
    public void TryNormalizeFoldsNonBreakingSpaceToRegularSpace()
    {
        var changed = CallerInputNormalization.TryNormalize("a b", out var normalized);

        Assert.IsTrue(changed);
        Assert.AreEqual("a b", normalized);
    }

    [TestMethod]
    [DataRow("Feature A > Motivation")]
    [DataRow("plain text with no artifacts")]
    [DataRow("")]
    public void TryNormalizeReturnsFalseWhenInputIsAlreadyClean(string input)
    {
        var changed = CallerInputNormalization.TryNormalize(input, out var normalized);

        Assert.IsFalse(changed);
        Assert.AreEqual(input, normalized);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~CallerInputNormalizationTests"`
Expected: build error — `CallerInputNormalization` does not exist.

- [ ] **Step 3: Implement the normalizer**

Create `src/DotNetKnowledge.Mcp/Text/CallerInputNormalization.cs`:

```csharp
using System.Net;
using System.Text.RegularExpressions;

namespace DotNetKnowledge.Mcp.Text;

/// <summary>
/// Fixes reversible encoding artifacts in caller-supplied text — HTML entities and common
/// typographic substitutions — before a second match attempt, never the first.
/// </summary>
/// <remarks>
/// See docs/superpowers/specs/2026-08-09-caller-input-normalization-design.md. This type has no
/// opinion about when it is safe to call; every call site decides that for itself by only invoking
/// it from inside a failure path the literal input has already taken.
/// </remarks>
public static partial class CallerInputNormalization
{
    [GeneratedRegex("[‘’]")]
    private static partial Regex SingleCurlyQuotePattern { get; }

    [GeneratedRegex("[“”]")]
    private static partial Regex DoubleCurlyQuotePattern { get; }

    /// <summary>
    /// Decodes HTML entities, folds curly quotes to straight ones, and folds a non-breaking space
    /// to a regular one. Returns whether <paramref name="normalized"/> actually differs from
    /// <paramref name="input"/>, so a caller only pays for a second match attempt when this could
    /// plausibly change the outcome.
    /// </summary>
    public static bool TryNormalize(string input, out string normalized)
    {
        ArgumentNullException.ThrowIfNull(input);

        var decoded = WebUtility.HtmlDecode(input);
        var straightened = SingleCurlyQuotePattern.Replace(decoded, "'");
        straightened = DoubleCurlyQuotePattern.Replace(straightened, "\"");
        normalized = straightened.Replace(' ', ' ');

        return !string.Equals(normalized, input, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~CallerInputNormalizationTests"`
Expected: PASS, 9 tests (5 + 2 + 1 + 3 data rows... count is whatever `[DataRow]` expands to; all green).

- [ ] **Step 5: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Text/CallerInputNormalization.cs tests/DotNetKnowledge.Mcp.Tests/Text/CallerInputNormalizationTests.cs
git commit -m "Add CallerInputNormalization, the shared fallback-retry text normalizer"
```

---

### Task 2: `get_doc`'s `section` fallback

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs:122-189` (`GetDocAsync`)
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs`

**Interfaces:**
- Consumes: `CallerInputNormalization.TryNormalize(string, out string) : bool` (Task 1).
- Produces: `DocNormalizationNote(string Message)` record; `DocContentResult.NormalizationNote` field. Consumed by Tasks 3–4, which add the same field to `DocOutlineResult` and `DocSearchResult`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs` (inside the `DocsQueryServiceTests` class, near `GetDocAsyncReturnsAWholeSectionAndRejectsAnUnknownSection`):

```csharp
    [TestMethod]
    public async Task GetDocAsyncAcceptsAnHtmlEncodedSectionSeparatorAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var result = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "Feature A &gt; Motivation", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.AreEqual("Feature A > Motivation", result.Section);
            StringAssert.Contains(result.Text, "Some motivating prose about feature A.");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "Feature A &gt; Motivation");
            StringAssert.Contains(result.NormalizationNote!.Message, "Feature A > Motivation");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncDoesNotReportNormalizationWhenTheLiteralSectionAlreadyMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var result = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "Feature A > Motivation", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncStillThrowsWhenNormalizationDoesNotProduceAMatch()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var exception = await Assert.ThrowsExactlyAsync<DocSectionNotFoundException>(() => service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", "No Such Section &gt; At All", limit: 8000, cursor: null,
                CancellationToken.None));
            StringAssert.Contains(exception.Message, "get_doc_outline");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs` (inside `DocsToolTests`, needs `System.Text.Json` which is already imported):

```csharp
    [TestMethod]
    public async Task GetDocReturnsNormalizationNoteAsCamelCaseJsonWhenTheFallbackFires()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.GetDoc(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None,
                section: "Feature A &gt; Motivation");

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual(
                "Feature A > Motivation", document.RootElement.GetProperty("section").GetString());
            StringAssert.Contains(
                document.RootElement.GetProperty("normalizationNote").GetProperty("message").GetString(),
                "Feature A > Motivation");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

`DocsToolTests.cs` already declares a private `static async Task<DocsQueryService> CreateServiceAsync(string root)` helper (used by its other tests) that writes its `ProposalA` fixture (`"# Feature A\n\n## Motivation\n\nSome motivating prose.\n"`) to `docs/proposal-a.md` and syncs it — reuse it as-is, no new helper needed.

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests|FullyQualifiedName~DocsToolTests"`
Expected: compile errors — `DocContentResult` has no `NormalizationNote` member.

- [ ] **Step 3: Add `DocNormalizationNote` and the `NormalizationNote` field**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`, add after `DocLineHit` and before `DocSearchResult`:

```csharp
public sealed record DocNormalizationNote(string Message);
```

Change `DocContentResult` to:

```csharp
public sealed record DocContentResult(
    string Path,
    SourceProvenance Source,
    string? Section,
    string Text,
    int StartLine,
    int EndLine,
    bool IsPartial,
    string? NextPageToken,
    DocNormalizationNote? NormalizationNote = null);
```

- [ ] **Step 4: Add the fallback to `GetDocAsync`'s section matching**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`, replace the section-matching block inside `GetDocAsync` (currently lines 139–147) so the method reads:

```csharp
    public async Task<DocContentResult> GetDocAsync(
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
        string? resolvedSection = section;
        DocNormalizationNote? normalizationNote = null;
        if (section is not null)
        {
            var headings = MarkdownOutline.Extract(text);
            var heading = headings.FirstOrDefault(
                candidate => string.Equals(candidate.Path, section, StringComparison.Ordinal));
            if (heading is null && CallerInputNormalization.TryNormalize(section, out var normalizedSection))
            {
                heading = headings.FirstOrDefault(
                    candidate => string.Equals(candidate.Path, normalizedSection, StringComparison.Ordinal));
                if (heading is not null)
                {
                    normalizationNote = new DocNormalizationNote(
                        $"No section matched '{section}' exactly; resolved to '{heading.Path}' after " +
                        "decoding HTML entities and typographic characters in the section path.");
                }
            }

            if (heading is null)
                throw new DocSectionNotFoundException(section, path, source);

            resolvedSection = heading.Path;
            rangeStart = heading.StartLine;
            rangeEndExclusive = heading.EndLine;
        }
        else
        {
            // Front matter is metadata about the document, not part of it, and search does not
            // return hits inside it either.
            rangeStart = MarkdownFrontMatter.BodyStartLine(text);
            rangeEndExclusive = lines.Length + 1;
        }

        // MarkdownOutline.Extract (above, for a sectioned fetch) and MarkdownAtomicBlocks.Find
        // (here) each run their own full Markdig parse of the same document. Sharing one parse
        // across both would mean exposing Markdig's MarkdownDocument in this library's public
        // surface, which cuts against its "input is markdown text, output is plain data" design
        // (docs/decisions.md); left as a follow-up rather than done in this fix wave.
        var atomicBlocks = MarkdownAtomicBlocks.Find(text);
        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, path, resolvedSection ?? string.Empty);
        var decodedStartLine = DecodeCursor(cursor, "lang-doc", scope, revisions);
        // DecodeCursor's own "no cursor" sentinel is 0, an item-count offset that only makes sense
        // for "lang-outline"/"lang-search" cursors; for "lang-doc", Offset is a 1-based line number
        // instead, and 0 is never a valid line. So a null cursor takes the range's own start line
        // here rather than trusting the decoded 0 sentinel.
        var startLine = cursor is null ? rangeStart : decodedStartLine;
        // Only a supplied cursor can fall outside the range; with none, startLine is the range's own
        // start. A document that is entirely front matter has an empty range, and must page to empty
        // text rather than report a cursor error to a caller who sent no cursor.
        if (cursor is not null && (startLine < rangeStart || startLine >= rangeEndExclusive))
            throw new ArgumentException("cursor points outside the requested section.", nameof(cursor));

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine, rangeEndExclusive, limit);
        var pageText = string.Join('\n', lines[(startLine - 1)..(endLineExclusive - 1)]);

        return new DocContentResult(
            path,
            provenance,
            resolvedSection,
            pageText,
            startLine,
            endLineExclusive - 1,
            isPartial,
            isPartial ? EncodeCursor("lang-doc", scope, endLineExclusive, revisions) : null,
            normalizationNote);
    }
```

This changes three things beyond the fallback itself: `scope` is now keyed on `resolvedSection` rather than the raw `section` argument (so a page-2 request lands on the same scope whether the caller resends the original or the corrected spelling), the returned `Section` is now the canonical `heading.Path` rather than an echo of the request, and the new `normalizationNote` parameter is threaded to the result. `ReadDocumentAsync` is untouched by this task — it still returns a 2-tuple; Task 3 changes its shape.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests|FullyQualifiedName~DocsToolTests"`
Expected: PASS, including every pre-existing test in both files (in particular `GetDocAsyncReturnsAWholeSectionAndRejectsAnUnknownSection` and `GetDocAcceptsASectionPathIssuedForALearnArticle`, which assert `Section` equals the literal request — that's unchanged for a request that already matches literally, since `resolvedSection` only ever differs from `section` when normalization fired).

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs
git commit -m "Retry get_doc's section match once against a decoded form on a miss"
```

---

### Task 3: `get_doc` / `get_doc_outline`'s `path` fallback

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs:29-59,122-217` (`GetOutlineAsync`, `GetDocAsync`, `ReadDocumentAsync`)
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `CallerInputNormalization.TryNormalize` (Task 1), `DocNormalizationNote` (Task 2).
- Produces: `DocOutlineResult.NormalizationNote` field; `ReadDocumentAsync` now returns `(string Text, SourceProvenance Provenance, string ResolvedPath, DocNormalizationNote? Note)` instead of a 2-tuple — internal to `DocsQueryService`, not consumed elsewhere.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`:

```csharp
    [TestMethod]
    public async Task GetDocAsyncAcceptsAnHtmlEncodedPathAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "a&b.md", "# A and B\n\nProse about A and B.\n");

            var result = await service.GetDocAsync(
                "docs/a&amp;b.md", "csharplang", section: null, limit: 8000, cursor: null, CancellationToken.None);

            Assert.AreEqual("docs/a&b.md", result.Path);
            StringAssert.Contains(result.Text, "Prose about A and B.");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "docs/a&amp;b.md");
            StringAssert.Contains(result.NormalizationNote!.Message, "docs/a&b.md");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncAcceptsAnHtmlEncodedPathAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "a&b.md", "# A and B\n\n## Detail\n\nMore prose.\n");

            var result = await service.GetOutlineAsync(
                "docs/a&amp;b.md", "csharplang", limit: 100, cursor: null, CancellationToken.None);

            Assert.AreEqual("docs/a&b.md", result.Path);
            Assert.IsNotNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncStillThrowsPathNotFoundWhenNormalizationDoesNotResolveIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            await Assert.ThrowsExactlyAsync<DocPathNotFoundException>(() => service.GetDocAsync(
                "docs/does&amp;not-exist.md", "csharplang", section: null, limit: 8000, cursor: null,
                CancellationToken.None));
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncCombinesPathAndSectionNormalizationNotesWhenBothFire()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "a&b.md", "# A and B\n\n## Detail\n\nMore prose.\n");

            var result = await service.GetDocAsync(
                "docs/a&amp;b.md", "csharplang", "A and B &gt; Detail", limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.AreEqual("docs/a&b.md", result.Path);
            Assert.AreEqual("A and B > Detail", result.Section);
            StringAssert.Contains(result.Text, "More prose.");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "docs/a&amp;b.md");
            StringAssert.Contains(result.NormalizationNote!.Message, "A and B &gt; Detail");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests"`
Expected: FAIL — `DocPathNotFoundException` thrown for the encoded-path cases (no retry exists yet), and `DocOutlineResult` has no `NormalizationNote` member (compile error) until Step 3 lands the field.

- [ ] **Step 3: Add `NormalizationNote` to `DocOutlineResult`**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`, change `DocOutlineResult` to:

```csharp
public sealed record DocOutlineResult(
    string Path,
    SourceProvenance Source,
    IReadOnlyList<DocOutlineEntry> Entries,
    bool IsPartial,
    string? NextPageToken,
    DocNormalizationNote? NormalizationNote = null);
```

- [ ] **Step 4: Split `ReadDocumentAsync` into a retrying wrapper and its inner attempt**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`, replace `ReadDocumentAsync` (currently lines 191–208) with:

```csharp
    private async Task<(string Text, SourceProvenance Provenance, string ResolvedPath, DocNormalizationNote? Note)>
        ReadDocumentAsync(string source, string path, CancellationToken cancellationToken)
    {
        try
        {
            return await ReadDocumentAttemptAsync(source, path, cancellationToken).ConfigureAwait(false);
        }
        catch (DocPathNotFoundException) when (CallerInputNormalization.TryNormalize(path, out var normalizedPath))
        {
            var (text, provenance, resolvedPath, _) =
                await ReadDocumentAttemptAsync(source, normalizedPath, cancellationToken).ConfigureAwait(false);
            var note = new DocNormalizationNote(
                $"'{path}' was not found; resolved to '{resolvedPath}' after decoding HTML entities and " +
                "typographic characters in the path.");
            return (text, provenance, resolvedPath, note);
        }
    }

    private async Task<(string Text, SourceProvenance Provenance, string ResolvedPath, DocNormalizationNote? Note)>
        ReadDocumentAttemptAsync(string source, string path, CancellationToken cancellationToken)
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

        return (read.Text, read.Provenance, path, null);
    }
```

The `when` filter means a `path` that normalization doesn't change (`TryNormalize` returns `false`) never attempts a retry — the original `DocPathNotFoundException` propagates immediately, byte-for-byte as today. If the retry itself also throws `DocPathNotFoundException`, that propagates uncaught out of `ReadDocumentAsync` too, since it isn't wrapped in another `try`.

- [ ] **Step 5: Update `GetOutlineAsync` for the new `ReadDocumentAsync` shape**

In the same file, replace `GetOutlineAsync` (currently lines 29–59) with:

```csharp
    public async Task<DocOutlineResult> GetOutlineAsync(
        string path,
        string source,
        int limit,
        string? cursor,
        CancellationToken cancellationToken)
    {
        ValidateSource(source);
        if (limit is < 1 or > 500)
            throw new ArgumentOutOfRangeException(nameof(limit), "limit must be between 1 and 500.");

        var (text, provenance, resolvedPath, note) =
            await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var headings = MarkdownOutline.Extract(text);

        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, resolvedPath);
        var offset = DecodeCursor(cursor, "lang-outline", scope, revisions);
        if (offset > headings.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = headings.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < headings.Count;

        return new DocOutlineResult(
            resolvedPath,
            provenance,
            page.Select(heading => new DocOutlineEntry(heading.Level, heading.Text, heading.Path)).ToArray(),
            isPartial,
            isPartial ? EncodeCursor("lang-outline", scope, nextOffset, revisions) : null,
            note);
    }
```

- [ ] **Step 6: Update `GetDocAsync` for the new `ReadDocumentAsync` shape and combine notes**

Replace `GetDocAsync` (the version Task 2 just landed) with:

```csharp
    public async Task<DocContentResult> GetDocAsync(
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

        var (text, provenance, resolvedPath, pathNote) =
            await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
        var lines = text.ReplaceLineEndings("\n").Split('\n');

        int rangeStart;
        int rangeEndExclusive;
        string? resolvedSection = section;
        DocNormalizationNote? sectionNote = null;
        if (section is not null)
        {
            var headings = MarkdownOutline.Extract(text);
            var heading = headings.FirstOrDefault(
                candidate => string.Equals(candidate.Path, section, StringComparison.Ordinal));
            if (heading is null && CallerInputNormalization.TryNormalize(section, out var normalizedSection))
            {
                heading = headings.FirstOrDefault(
                    candidate => string.Equals(candidate.Path, normalizedSection, StringComparison.Ordinal));
                if (heading is not null)
                {
                    sectionNote = new DocNormalizationNote(
                        $"No section matched '{section}' exactly; resolved to '{heading.Path}' after " +
                        "decoding HTML entities and typographic characters in the section path.");
                }
            }

            if (heading is null)
                throw new DocSectionNotFoundException(section, resolvedPath, source);

            resolvedSection = heading.Path;
            rangeStart = heading.StartLine;
            rangeEndExclusive = heading.EndLine;
        }
        else
        {
            // Front matter is metadata about the document, not part of it, and search does not
            // return hits inside it either.
            rangeStart = MarkdownFrontMatter.BodyStartLine(text);
            rangeEndExclusive = lines.Length + 1;
        }

        // MarkdownOutline.Extract (above, for a sectioned fetch) and MarkdownAtomicBlocks.Find
        // (here) each run their own full Markdig parse of the same document. Sharing one parse
        // across both would mean exposing Markdig's MarkdownDocument in this library's public
        // surface, which cuts against its "input is markdown text, output is plain data" design
        // (docs/decisions.md); left as a follow-up rather than done in this fix wave.
        var atomicBlocks = MarkdownAtomicBlocks.Find(text);
        var revisions = new[] { RevisionKey(provenance) };
        var scope = EncodeScope(source, resolvedPath, resolvedSection ?? string.Empty);
        var decodedStartLine = DecodeCursor(cursor, "lang-doc", scope, revisions);
        // DecodeCursor's own "no cursor" sentinel is 0, an item-count offset that only makes sense
        // for "lang-outline"/"lang-search" cursors; for "lang-doc", Offset is a 1-based line number
        // instead, and 0 is never a valid line. So a null cursor takes the range's own start line
        // here rather than trusting the decoded 0 sentinel.
        var startLine = cursor is null ? rangeStart : decodedStartLine;
        // Only a supplied cursor can fall outside the range; with none, startLine is the range's own
        // start. A document that is entirely front matter has an empty range, and must page to empty
        // text rather than report a cursor error to a caller who sent no cursor.
        if (cursor is not null && (startLine < rangeStart || startLine >= rangeEndExclusive))
            throw new ArgumentException("cursor points outside the requested section.", nameof(cursor));

        var (endLineExclusive, isPartial) = MarkdownPager.Page(
            lines, atomicBlocks, startLine, rangeEndExclusive, limit);
        var pageText = string.Join('\n', lines[(startLine - 1)..(endLineExclusive - 1)]);

        return new DocContentResult(
            resolvedPath,
            provenance,
            resolvedSection,
            pageText,
            startLine,
            endLineExclusive - 1,
            isPartial,
            isPartial ? EncodeCursor("lang-doc", scope, endLineExclusive, revisions) : null,
            CombineNotes(pathNote, sectionNote));
    }

    private static DocNormalizationNote? CombineNotes(DocNormalizationNote? first, DocNormalizationNote? second)
    {
        if (first is null)
            return second;
        if (second is null)
            return first;

        return new DocNormalizationNote(first.Message + " " + second.Message);
    }
```

`CombineNotes` covers the rare case where both `path` and `section` need normalizing in the same call: both messages are reported, concatenated, rather than one silently winning over the other.

- [ ] **Step 7: Run the tests to verify they pass**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests"`
Expected: PASS, including every pre-existing test — in particular the ones asserting `Path` echoes the literal request (`GetOutlineAsyncRejectsAPathThatEscapesTheSourceRoot` etc.), which still holds for any request that already matches literally, since `resolvedPath` only differs from `path` when normalization fired.

- [ ] **Step 8: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs
git commit -m "Retry get_doc/get_doc_outline's path resolution once against a decoded form on a miss"
```

---

### Task 4: `search_docs`'s `query` fallback

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs:61-120` (`SearchAsync`)
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `CallerInputNormalization.TryNormalize` (Task 1), `DocNormalizationNote` (Task 2).
- Produces: `DocSearchResult.NormalizationNote` field; `CollectHitsAsync(string query, Regex? compiledPattern, string[] sourceNames, CancellationToken) : Task<(List<DocLineHit> Hits, List<SourceProvenance> SearchedSources)>` — internal to `DocsQueryService`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`:

```csharp
    [TestMethod]
    public async Task SearchAsyncAcceptsAnHtmlEncodedQueryAndReportsNormalization()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "proposal-f.md", "# Feature F\n\nA constraint written as T > U compares the two.\n");

            var result = await service.SearchAsync(
                "T &gt; U", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.HasCount(1, result.Hits);
            StringAssert.Contains(result.Hits[0].Text, "T > U");
            Assert.IsNotNull(result.NormalizationNote);
            StringAssert.Contains(result.NormalizationNote!.Message, "T &gt; U");
            StringAssert.Contains(result.NormalizationNote!.Message, "T > U");
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncDoesNotNormalizeARegexQuery()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        try
        {
            var service = await CreateServiceWithDocumentAsync(
                root, "proposal-f.md", "# Feature F\n\nA constraint written as T > U compares the two.\n");

            var result = await service.SearchAsync(
                "T &gt; U", regex: true, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.IsEmpty(result.Hits);
            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncDoesNotReportNormalizationWhenTheLiteralQueryAlreadyMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var result = await service.SearchAsync(
                "FeatureA", regex: false, source: null, limit: 20, cursor: null, CancellationToken.None);

            Assert.IsNull(result.NormalizationNote);
        }
        finally
        {
            if (Directory.Exists(root))
                DeleteDirectory(root);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests"`
Expected: FAIL — `DocSearchResult` has no `NormalizationNote` member (compile error) until Step 3, and `SearchAsyncAcceptsAnHtmlEncodedQueryAndReportsNormalization` finds 0 hits without the retry.

- [ ] **Step 3: Add `NormalizationNote` to `DocSearchResult`**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`, change `DocSearchResult` to:

```csharp
public sealed record DocSearchResult(
    IReadOnlyList<DocLineHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<SourceProvenance> SearchedSources,
    DocNormalizationNote? NormalizationNote = null);
```

- [ ] **Step 4: Extract `CollectHitsAsync` and add the retry to `SearchAsync`**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`, replace `SearchAsync` (currently lines 61–120) with:

```csharp
    public async Task<DocSearchResult> SearchAsync(
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

        // Validate once, up front, and keep the built Regex: an invalid pattern must fail the same
        // way regardless of how many markdown files a source happens to hold, and every source's
        // scan reuses this one instance instead of rebuilding it per file.
        var compiledPattern = regex ? new Regex(query, RegexOptions.NonBacktracking) : null;

        var sourceNames = ResolveSourceNames(source);
        var (hits, searchedSources) = await CollectHitsAsync(query, compiledPattern, sourceNames, cancellationToken)
            .ConfigureAwait(false);

        var effectiveQuery = query;
        DocNormalizationNote? note = null;
        if (hits.Count == 0 && !regex && CallerInputNormalization.TryNormalize(query, out var normalizedQuery))
        {
            var (normalizedHits, normalizedSearchedSources) = await CollectHitsAsync(
                normalizedQuery, compiledPattern: null, sourceNames, cancellationToken).ConfigureAwait(false);
            if (normalizedHits.Count > 0)
            {
                hits = normalizedHits;
                searchedSources = normalizedSearchedSources;
                effectiveQuery = normalizedQuery;
                note = new DocNormalizationNote(
                    $"No literal match for '{query}'; results reflect the HTML-entity/typography-" +
                    $"normalized form '{normalizedQuery}'.");
            }
        }

        var ordered = DocRanking.Order(hits, effectiveQuery);

        var revisions = searchedSources.Select(RevisionKey).ToArray();
        var scope = EncodeScope(effectiveQuery, regex, source ?? string.Empty);
        var offset = DecodeCursor(cursor, "lang-search", scope, revisions);
        if (offset > ordered.Count)
            throw new ArgumentException("cursor points beyond the available result set.", nameof(cursor));

        var page = ordered.Skip(offset).Take(limit).ToArray();
        var nextOffset = offset + page.Length;
        var isPartial = nextOffset < ordered.Count;

        return new DocSearchResult(
            page,
            isPartial,
            isPartial ? EncodeCursor("lang-search", scope, nextOffset, revisions) : null,
            searchedSources,
            note);
    }

    private async Task<(List<DocLineHit> Hits, List<SourceProvenance> SearchedSources)> CollectHitsAsync(
        string query, Regex? compiledPattern, string[] sourceNames, CancellationToken cancellationToken)
    {
        var hits = new List<DocLineHit>();
        var searchedSources = new List<SourceProvenance>();

        foreach (var sourceName in sourceNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SourceSearchRead read;
            try
            {
                read = await _synchronizer.ReadCurrentSourceAsync(
                    sourceName,
                    (definition, state, directory) =>
                        ReadSearchSource(directory, definition, state, query, compiledPattern, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException exception)
            {
                throw new SourceNotSyncedException(sourceName, exception);
            }

            searchedSources.Add(read.Provenance);
            hits.AddRange(read.Hits);
        }

        return (hits, searchedSources);
    }
```

`compiledPattern` is only non-null on the literal attempt (`regex: true` never reaches the retry branch at all, since the `!regex` guard short-circuits it); the retry call always passes `compiledPattern: null`, matching the literal-substring path `ReadSearchSource` already has.

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests"`
Expected: PASS — every pre-existing `SearchAsync` test (literal, regex, multi-source, cursor, non-markdown rejection) plus the three new ones.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs
git commit -m "Retry search_docs's literal query once against a decoded form on zero hits"
```

---

### Task 5: Tool descriptions and standing-record updates

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs:20-30,77-90,137-142`
- Modify: `docs/design/mcp-tool-surface.md:110-140`
- Modify: `docs/decisions.md`

**Interfaces:** none — description strings and documentation only, no code contract changes.

- [ ] **Step 1: Update `search_docs`'s description**

In `src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs`, the `SearchDocs` method's `[Description(...)]` currently ends:

```csharp
        "Microsoft Learn articles carry, is metadata about a document rather than part of it and " +
        "is not searched.")]
```

Change to:

```csharp
        "Microsoft Learn articles carry, is metadata about a document rather than part of it and " +
        "is not searched. A literal, non-regex query that matches nothing is retried once against " +
        "an HTML-entity/typography-decoded form; a hit set produced this way carries " +
        "normalizationNote naming the form actually matched.")]
```

- [ ] **Step 2: Update `get_doc`'s description**

In the same file, the `GetDoc` method's `[Description(...)]` currently ends:

```csharp
        "at the document's first content line: YAML front matter is metadata and is not returned, " +
        "and startLine names the line the text actually came from.")]
```

Change to:

```csharp
        "at the document's first content line: YAML front matter is metadata and is not returned, " +
        "and startLine names the line the text actually came from. If \"path\" or \"section\" " +
        "doesn't match exactly, one retry is attempted against an HTML-entity/typography-decoded " +
        "form of the same value; a response produced this way carries normalizationNote and " +
        "reports the resolved path/section, never the request's own spelling.")]
```

- [ ] **Step 3: Update `get_doc_outline`'s description**

In the same file, the `GetDocOutline` method's `[Description(...)]` currently reads:

```csharp
    [Description(
        "Return a synchronized documentation file's heading tree, no bodies: " +
        "each entry's level, text, and section path - the path get_doc's section " +
        "parameter accepts verbatim. YAML front matter, which Microsoft Learn articles carry, is " +
        "not a heading and does not appear. Paginated like the other tools.")]
```

Change to:

```csharp
    [Description(
        "Return a synchronized documentation file's heading tree, no bodies: " +
        "each entry's level, text, and section path - the path get_doc's section " +
        "parameter accepts verbatim. YAML front matter, which Microsoft Learn articles carry, is " +
        "not a heading and does not appear. Paginated like the other tools. If \"path\" doesn't " +
        "match exactly, one retry is attempted against an HTML-entity/typography-decoded form; a " +
        "response produced this way carries normalizationNote and reports the resolved path.")]
```

- [ ] **Step 4: Update `docs/design/mcp-tool-surface.md`**

In the `── documentation ──` block, after the `search_docs` entry's `→ limit: 1-100, default 20` line, add:

```
    → a literal query matching nothing retries once against an HTML-entity/
      typography-decoded form; a hit set from that retry carries
      normalizationNote (not attempted when regex: true)
```

After the `get_doc` entry's `limit is a character budget...` block (the last `→` line for that entry), add:

```
    → a "path" or "section" that doesn't match exactly retries once against
      an HTML-entity/typography-decoded form; a response from that retry
      carries normalizationNote and reports the resolved value, not the
      request's own spelling
```

After the `get_doc_outline` entry's existing `→` lines, add:

```
    → a "path" that doesn't match exactly gets the same one-shot decoded
      retry, carrying normalizationNote
```

- [ ] **Step 5: Add the `docs/decisions.md` entry**

Insert immediately after the `---` separator near the top of `docs/decisions.md` (before the existing `### 2026-08-08 · Front matter is metadata...` entry, since the file is newest-first):

```markdown
### 2026-08-09 · A caller-input encoding miss gets one normalized retry, not a global filter

`get_doc`'s `section` and `path`, and `search_docs`'s non-regex `query`, retry once against an
HTML-entity/typography-decoded form of the caller's input, and only after the literal value has
already failed to match — never before. A response produced this way reports the resolved value,
not the caller's spelling, and carries `normalizationNote` naming the substitution. Rejected:
normalizing every string parameter unconditionally at the tool boundary, which would make a
legitimately-authored `&gt;` in real heading text unreachable and would leave nothing to compare
once the raw form is gone, so nothing to report. See
[`docs/superpowers/specs/2026-08-09-caller-input-normalization-design.md`](superpowers/specs/2026-08-09-caller-input-normalization-design.md).
```

- [ ] **Step 6: Build to confirm the description-string edits compile**

Run: `dotnet build DotNetKnowledge.slnx`
Expected: builds clean — these are string literal changes only, so this step is a compile sanity check, not a behavior test.

- [ ] **Step 7: Commit**

```bash
git add src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs docs/design/mcp-tool-surface.md docs/decisions.md
git commit -m "Document the caller-input normalization fallback on the Docs tools' surface"
```
