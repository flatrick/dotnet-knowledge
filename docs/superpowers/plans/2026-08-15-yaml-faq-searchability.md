# Learn FAQ Searchability Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make Microsoft Learn structured-FAQ documents (`### YamlMime:FAQ`) searchable through `search_docs` and readable through `get_doc` and `get_doc_outline`, closing a silent absence where a query that should match returns an empty result set.

**Architecture:** A new `DotNetKnowledge.Yaml` library detects the Learn schema marker, parses the FAQ, and renders it to markdown. `DocsQueryService` calls that renderer at the one point where a document is read; every stage downstream — outline extraction, line search, atomic blocks, paging, budgeting — runs on the rendered markdown with no change. Payloads declare the rendering through a `renderedFrom` field, because a rendered document's line numbers do not index the file on disk.

**Tech Stack:** .NET 10, C#, YamlDotNet 18.1.0, Markdig (existing), MSTest.Sdk 4.3.2, ModelContextProtocol 2.0.0.

**Spec:** [`docs/superpowers/specs/2026-08-15-yaml-faq-searchability-design.md`](../specs/2026-08-15-yaml-faq-searchability-design.md)

## Global Constraints

- **A warning fails the build.** The repo root `Directory.Build.props` sets `TreatWarningsAsErrors` and `MSBuildTreatWarningsAsErrors` to `true`, with `AnalysisLevel` `latest-recommended`. Do not suppress a warning to get a build through.
- **Target framework is `net10.0`**, `ImplicitUsings` and `Nullable` both `enable`, on every new project.
- **YamlDotNet version is exactly `18.1.0`**, referenced only by `src/DotNetKnowledge.Yaml`. No other project may reference it.
- **LF line endings, UTF-8**, enforced by `.gitattributes`. **American English** in identifiers, comments and prose.
- **stdout is the MCP protocol channel.** Never add a console logging provider, never `Console.WriteLine` from server code.
- **No silent truncation and no silent absence.** Every capped result set carries `isPartial` or a cursor; every capped string carries `isTruncated`; anything the server declined to read is named in the payload.
- **Every payload keeps its provenance envelope** — `repo`, `ref`, `commit`, `fetchedAt`. Nothing in this plan touches it; do not let a refactor drop it.
- **No test reads the real source cache.** Fixtures are local git repositories created in a temp directory, as `DocsQueryServiceTests.CreateServiceAsync` already does.
- **Scratch files go in `.scratch/`** at the worktree root. Redirect full command output to a log file with PowerShell `*>`; never pipe a build or test through `tail`, `head`, or `Select-Object -First`.
- **Tooling is single-file C#**, never a shell script, and is always invoked as `dotnet run --file <script>.cs` or `dotnet <script>.cs -- <args>`.
- Work happens in the worktree `.claude/worktrees/yaml-faq-searchability` on branch `worktree-yaml-faq-searchability`. Do not `cd` to the main checkout.

---

### Task 1: `DotNetKnowledge.Yaml` project and schema detection

Creates the library, its test project, the solution entries, and the one function that decides whether a `.yml` file is a document this server serves. Everything else in the plan depends on this existing.

**Files:**
- Create: `src/DotNetKnowledge.Yaml/DotNetKnowledge.Yaml.csproj`
- Create: `src/DotNetKnowledge.Yaml/LearnYamlMime.cs`
- Create: `tests/DotNetKnowledge.Yaml.Tests/DotNetKnowledge.Yaml.Tests.csproj`
- Create: `tests/DotNetKnowledge.Yaml.Tests/LearnYamlMimeTests.cs`
- Modify: `DotNetKnowledge.slnx`

**Interfaces:**
- Consumes: nothing.
- Produces: `DotNetKnowledge.Yaml.LearnYamlMime.Detect(string text) → string?` returning the schema name (`"FAQ"`, `"Hub"`) or `null`; and the constant `LearnYamlMime.Faq` whose value is `"FAQ"`.

- [ ] **Step 1: Create the library project file**

`src/DotNetKnowledge.Yaml/DotNetKnowledge.Yaml.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>DotNetKnowledge.Yaml</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="YamlDotNet" Version="18.1.0" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create the test project file**

`tests/DotNetKnowledge.Yaml.Tests/DotNetKnowledge.Yaml.Tests.csproj` — mirrors `tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj` exactly, including `UseVSTest`:

```xml
<Project Sdk="MSTest.Sdk/4.3.2">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <RootNamespace>DotNetKnowledge.Yaml.Tests</RootNamespace>
    <UseVSTest>true</UseVSTest>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DotNetKnowledge.Yaml\DotNetKnowledge.Yaml.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add both projects to the solution**

`DotNetKnowledge.slnx` — add two `<Project>` lines so the file reads:

```xml
<Solution>
  <Project Path="src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj" />
  <Project Path="src/DotNetKnowledge.Markdown/DotNetKnowledge.Markdown.csproj" />
  <Project Path="src/DotNetKnowledge.Yaml/DotNetKnowledge.Yaml.csproj" />
  <Project Path="tests/DotNetKnowledge.Mcp.Tests/DotNetKnowledge.Mcp.Tests.csproj" />
  <Project Path="tests/DotNetKnowledge.Mcp.Tests.ApiFixture/DotNetKnowledge.Mcp.Tests.ApiFixture.csproj" />
  <Project Path="tests/DotNetKnowledge.Mcp.Tests.GitRunnerHost/DotNetKnowledge.Mcp.Tests.GitRunnerHost.csproj" />
  <Project Path="tests/DotNetKnowledge.Markdown.Tests/DotNetKnowledge.Markdown.Tests.csproj" />
  <Project Path="tests/DotNetKnowledge.Yaml.Tests/DotNetKnowledge.Yaml.Tests.csproj" />
</Solution>
```

- [ ] **Step 4: Write the failing tests**

`tests/DotNetKnowledge.Yaml.Tests/LearnYamlMimeTests.cs`:

```csharp
using DotNetKnowledge.Yaml;

namespace DotNetKnowledge.Yaml.Tests;

[TestClass]
public sealed class LearnYamlMimeTests
{
    [TestMethod]
    public void DetectReadsTheFaqMarker()
    {
        Assert.AreEqual("FAQ", LearnYamlMime.Detect("### YamlMime:FAQ\nmetadata:\n  title: x\n"));
    }

    [TestMethod]
    public void DetectReadsAnyOtherSchemaName()
    {
        // Hub is a Learn landing page of link lists. Detect reports what it found; deciding which
        // schemas are servable belongs to the caller, not here.
        Assert.AreEqual("Hub", LearnYamlMime.Detect("### YamlMime:Hub\ntitle: NuGet\n"));
    }

    [TestMethod]
    public void DetectSkipsAByteOrderMark()
    {
        Assert.AreEqual("FAQ", LearnYamlMime.Detect("﻿### YamlMime:FAQ\n"));
    }

    [TestMethod]
    public void DetectSkipsLeadingBlankLines()
    {
        Assert.AreEqual("FAQ", LearnYamlMime.Detect("\n   \n### YamlMime:FAQ\n"));
    }

    [TestMethod]
    public void DetectReturnsNullForAPipelineDefinition()
    {
        // The nine .yml files in roslyn-wiki are Azure Pipelines definitions. None carries a marker.
        Assert.IsNull(LearnYamlMime.Detect("# Branches that trigger a build\ntrigger:\n  - main\n"));
    }

    [TestMethod]
    public void DetectReturnsNullWhenTheFirstLineOnlyResemblesTheMarker()
    {
        Assert.IsNull(LearnYamlMime.Detect("#### YamlMime:FAQ\n"));
        Assert.IsNull(LearnYamlMime.Detect("## YamlMime:FAQ\n"));
        Assert.IsNull(LearnYamlMime.Detect("text before ### YamlMime:FAQ\n"));
    }

    [TestMethod]
    public void DetectOnlyReadsTheFirstNonBlankLine()
    {
        // A marker further down is not a marker; it would let a pipeline file smuggle itself in.
        Assert.IsNull(LearnYamlMime.Detect("trigger:\n### YamlMime:FAQ\n"));
    }

    [TestMethod]
    public void DetectReturnsNullForEmptyInput()
    {
        Assert.IsNull(LearnYamlMime.Detect(string.Empty));
        Assert.IsNull(LearnYamlMime.Detect("\n\n"));
    }
}
```

- [ ] **Step 5: Run the tests to verify they fail**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~LearnYamlMimeTests" *> ".scratch/test-mime-$ts.log"
```

Expected: FAIL — the build cannot resolve `LearnYamlMime`. Read the log file; do not re-run to see output.

- [ ] **Step 6: Write the implementation**

`src/DotNetKnowledge.Yaml/LearnYamlMime.cs`:

```csharp
namespace DotNetKnowledge.Yaml;

/// <summary>
/// Microsoft Learn stamps a schema marker on a YAML document's first line. It is the only reliable
/// way to tell a documentation file from a build pipeline definition that happens to share the
/// extension: of the .yml files in the synchronized sources, nine are Azure Pipelines definitions
/// and two are prose.
/// </summary>
public static class LearnYamlMime
{
    /// <summary>The one schema this server renders and serves.</summary>
    public const string Faq = "FAQ";

    private const string Prefix = "### YamlMime:";

    /// <summary>
    /// The schema name on the document's first non-blank line, or null when there is no marker.
    /// Only that line is examined: a marker further down would let any file claim a schema.
    /// </summary>
    public static string? Detect(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        var remaining = text.AsSpan().TrimStart('﻿');
        while (!remaining.IsEmpty)
        {
            var breakIndex = remaining.IndexOf('\n');
            var line = (breakIndex < 0 ? remaining : remaining[..breakIndex]).Trim();
            if (!line.IsEmpty)
            {
                return line.StartsWith(Prefix, StringComparison.Ordinal)
                    ? line[Prefix.Length..].Trim().ToString()
                    : null;
            }

            if (breakIndex < 0)
                break;

            remaining = remaining[(breakIndex + 1)..];
        }

        return null;
    }
}
```

- [ ] **Step 7: Run the tests to verify they pass**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~LearnYamlMimeTests" *> ".scratch/test-mime-$ts.log"
```

Expected: PASS, 8 tests. Read the last lines of the log file and confirm the count.

- [ ] **Step 8: Commit**

```bash
git add src/DotNetKnowledge.Yaml tests/DotNetKnowledge.Yaml.Tests DotNetKnowledge.slnx
git commit -m "Add DotNetKnowledge.Yaml and detect the Learn schema marker"
```

---

### Task 2: Parse a FAQ document

Turns FAQ YAML into plain data, and makes an unrecognized document a loud failure rather than an empty one.

**Files:**
- Create: `src/DotNetKnowledge.Yaml/FaqDocument.cs`
- Create: `src/DotNetKnowledge.Yaml/FaqParseException.cs`
- Create: `tests/DotNetKnowledge.Yaml.Tests/FaqDocumentTests.cs`

**Interfaces:**
- Consumes: `LearnYamlMime` from Task 1 (not called here; the caller gates on it).
- Produces:
  - `sealed record FaqQuestion(string Question, string Answer)`
  - `sealed record FaqSection(string Name, IReadOnlyList<FaqQuestion> Questions)`
  - `sealed record FaqDocument(string? Title, string? Summary, IReadOnlyList<FaqSection> Sections)`
  - `static FaqDocument FaqDocument.Parse(string text)`
  - `sealed class FaqParseException : Exception`

- [ ] **Step 1: Write the failing tests**

`tests/DotNetKnowledge.Yaml.Tests/FaqDocumentTests.cs`:

```csharp
using DotNetKnowledge.Yaml;

namespace DotNetKnowledge.Yaml.Tests;

[TestClass]
public sealed class FaqDocumentTests
{
    // The shape Microsoft Learn's structured-FAQ schema uses: a metadata block that is not content,
    // an optional summary, then sections of question-and-answer pairs as block scalars.
    private const string NominalFaq =
        "### YamlMime:FAQ\n" +
        "metadata:\n" +
        "  title: Widget FAQ\n" +
        "  ms.author: someone\n" +
        "  ms.date: 01/08/2026\n" +
        "title: Widget frequently-asked questions\n" +
        "summary: |\n" +
        "  Intro prose about widgets.\n" +
        "sections:\n" +
        "  - name: General\n" +
        "    questions:\n" +
        "      - question: |\n" +
        "          How do I install a widget?\n" +
        "        answer: |\n" +
        "          Run the installer.\n" +
        "      - question: |\n" +
        "          Where do widgets live?\n" +
        "        answer: |\n" +
        "          In the widget directory.\n" +
        "  - name: Troubleshooting\n" +
        "    questions:\n" +
        "      - question: |\n" +
        "          Why did my widget fail?\n" +
        "        answer: |\n" +
        "          Check the log.\n";

    [TestMethod]
    public void ParseReadsTitleSummarySectionsAndQuestions()
    {
        var document = FaqDocument.Parse(NominalFaq);

        Assert.AreEqual("Widget frequently-asked questions", document.Title);
        StringAssert.Contains(document.Summary, "Intro prose about widgets.");
        Assert.AreEqual(2, document.Sections.Count);
        Assert.AreEqual("General", document.Sections[0].Name);
        Assert.AreEqual(2, document.Sections[0].Questions.Count);
        Assert.AreEqual("Troubleshooting", document.Sections[1].Name);
        Assert.AreEqual(1, document.Sections[1].Questions.Count);
        StringAssert.Contains(document.Sections[0].Questions[0].Question, "How do I install a widget?");
        StringAssert.Contains(document.Sections[0].Questions[0].Answer, "Run the installer.");
    }

    [TestMethod]
    public void ParseDoesNotCarryMetadata()
    {
        // metadata is about the document, not part of it - the same call front matter gets.
        var document = FaqDocument.Parse(NominalFaq);

        Assert.AreEqual("Widget frequently-asked questions", document.Title);
        Assert.IsFalse(document.Sections.Any(section =>
            section.Questions.Any(question => question.Answer.Contains("ms.author", StringComparison.Ordinal))));
    }

    [TestMethod]
    public void ParseAcceptsAnAbsentSummaryAndTitle()
    {
        // nuget-org-faq.yml carries no summary. That is a real document, not a broken one.
        var document = FaqDocument.Parse(
            "### YamlMime:FAQ\n" +
            "sections:\n" +
            "  - name: Only\n" +
            "    questions:\n" +
            "      - question: |\n" +
            "          Q?\n" +
            "        answer: |\n" +
            "          A.\n");

        Assert.IsNull(document.Title);
        Assert.IsNull(document.Summary);
        Assert.AreEqual(1, document.Sections.Count);
    }

    [TestMethod]
    public void ParseAcceptsAnEmptyAnswer()
    {
        var document = FaqDocument.Parse(
            "### YamlMime:FAQ\n" +
            "sections:\n" +
            "  - name: Only\n" +
            "    questions:\n" +
            "      - question: |\n" +
            "          Q?\n");

        Assert.AreEqual(string.Empty, document.Sections[0].Questions[0].Answer);
    }

    [TestMethod]
    public void ParseRejectsADocumentWithNoSections()
    {
        // YamlDotNet's IgnoreUnmatchedProperties turns a wholly unrecognized document into all-null
        // properties and reports success. Reporting that as "a FAQ with no content" is exactly the
        // silent absence this feature exists to remove.
        var exception = Assert.ThrowsExactly<FaqParseException>(() =>
            FaqDocument.Parse("### YamlMime:FAQ\nunrelated: value\n"));

        StringAssert.Contains(exception.Message, "no sections");
    }

    [TestMethod]
    public void ParseRejectsASectionWithNoQuestions()
    {
        var exception = Assert.ThrowsExactly<FaqParseException>(() =>
            FaqDocument.Parse(
                "### YamlMime:FAQ\n" +
                "sections:\n" +
                "  - name: Empty\n"));

        StringAssert.Contains(exception.Message, "Empty");
    }

    [TestMethod]
    public void ParseRejectsMalformedYaml()
    {
        var exception = Assert.ThrowsExactly<FaqParseException>(() =>
            FaqDocument.Parse(
                "### YamlMime:FAQ\n" +
                "sections:\n" +
                "  - name: Broken\n" +
                "   questions: [\n"));

        Assert.IsNotNull(exception.Message);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~FaqDocumentTests" *> ".scratch/test-faqdoc-$ts.log"
```

Expected: FAIL — `FaqDocument` and `FaqParseException` do not exist.

- [ ] **Step 3: Write the exception type**

`src/DotNetKnowledge.Yaml/FaqParseException.cs`:

```csharp
namespace DotNetKnowledge.Yaml;

/// <summary>
/// A document declared itself a Learn FAQ and then could not be read as one. Distinct from "this
/// file is not a FAQ", which is not an error: a caller that cannot tell those apart reports a
/// broken document as an absent one.
/// </summary>
public sealed class FaqParseException : Exception
{
    public FaqParseException(string message)
        : base(message)
    {
    }

    public FaqParseException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
```

- [ ] **Step 4: Write the parser**

`src/DotNetKnowledge.Yaml/FaqDocument.cs`:

```csharp
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace DotNetKnowledge.Yaml;

public sealed record FaqQuestion(string Question, string Answer);

public sealed record FaqSection(string Name, IReadOnlyList<FaqQuestion> Questions);

/// <summary>
/// A Microsoft Learn structured-FAQ document as plain data. Input is YAML text; output carries no
/// YamlDotNet type, so the dependency stops at this assembly's boundary.
/// </summary>
public sealed record FaqDocument(string? Title, string? Summary, IReadOnlyList<FaqSection> Sections)
{
    private static readonly IDeserializer Deserializer = new DeserializerBuilder()
        .WithNamingConvention(CamelCaseNamingConvention.Instance)
        .IgnoreUnmatchedProperties()
        .Build();

    public static FaqDocument Parse(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        FaqYaml? raw;
        try
        {
            raw = Deserializer.Deserialize<FaqYaml>(text);
        }
        catch (YamlException exception)
        {
            throw new FaqParseException(
                $"The document declares YamlMime:{LearnYamlMime.Faq} but is not valid YAML: {exception.Message}",
                exception);
        }

        // IgnoreUnmatchedProperties is what lets the metadata block pass without a model for every
        // Learn key. Its cost is that a wholly unrecognized document deserializes to all-null
        // properties and reports success, so the absence of sections has to be rejected here.
        // Learn's schema requires them, which is what makes this unambiguous.
        if (raw?.Sections is not { Count: > 0 })
        {
            throw new FaqParseException(
                $"The document declares YamlMime:{LearnYamlMime.Faq} but has no sections.");
        }

        var sections = new List<FaqSection>(raw.Sections.Count);
        foreach (var section in raw.Sections)
        {
            var name = section.Name ?? string.Empty;
            if (section.Questions is not { Count: > 0 })
                throw new FaqParseException($"FAQ section '{name}' has no questions.");

            sections.Add(new FaqSection(
                name,
                section.Questions
                    .Select(question => new FaqQuestion(question.Question ?? string.Empty, question.Answer ?? string.Empty))
                    .ToArray()));
        }

        return new FaqDocument(raw.Title, raw.Summary, sections);
    }

    // The wire shape, private so no caller depends on the deserializer's mutable model.
    private sealed class FaqYaml
    {
        public string? Title { get; set; }

        public string? Summary { get; set; }

        public List<SectionYaml>? Sections { get; set; }
    }

    private sealed class SectionYaml
    {
        public string? Name { get; set; }

        public List<QuestionYaml>? Questions { get; set; }
    }

    private sealed class QuestionYaml
    {
        public string? Question { get; set; }

        public string? Answer { get; set; }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~FaqDocumentTests" *> ".scratch/test-faqdoc-$ts.log"
```

Expected: PASS, 7 tests. If `ParseRejectsMalformedYaml` fails because YamlDotNet raised something other than `YamlException`, widen the catch to the actual type shown in the log — do not catch bare `Exception`.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Yaml tests/DotNetKnowledge.Yaml.Tests
git commit -m "Parse Learn FAQ documents, and reject an unrecognized one loudly"
```

---

### Task 3: Render a FAQ to markdown

The seam. After this, a FAQ is markdown and every existing stage handles it unmodified.

**Files:**
- Create: `src/DotNetKnowledge.Yaml/FaqMarkdown.cs`
- Create: `tests/DotNetKnowledge.Yaml.Tests/FaqMarkdownTests.cs`

**Interfaces:**
- Consumes: `FaqDocument`, `FaqSection`, `FaqQuestion` from Task 2.
- Produces: `static string FaqMarkdown.Render(FaqDocument document)`.

- [ ] **Step 1: Write the failing tests**

`tests/DotNetKnowledge.Yaml.Tests/FaqMarkdownTests.cs`:

```csharp
using DotNetKnowledge.Yaml;

namespace DotNetKnowledge.Yaml.Tests;

[TestClass]
public sealed class FaqMarkdownTests
{
    private static FaqDocument Document(string? title, string? summary, params FaqSection[] sections) =>
        new(title, summary, sections);

    [TestMethod]
    public void RenderMakesSectionsLevelOneAndQuestionsLevelTwo()
    {
        var markdown = FaqMarkdown.Render(Document(
            "Widget frequently-asked questions",
            null,
            new FaqSection("General", [new FaqQuestion("How do I install a widget?", "Run the installer.")])));

        StringAssert.Contains(markdown, "# General");
        StringAssert.Contains(markdown, "## How do I install a widget?");
        StringAssert.Contains(markdown, "Run the installer.");
    }

    [TestMethod]
    public void RenderOmitsTheTitle()
    {
        // As an H1 the title becomes the ancestor of every heading, prefixing every section path
        // with the document's own name and making a two-level outline three levels deep. The
        // payload already carries document identity in `path`.
        var markdown = FaqMarkdown.Render(Document(
            "Widget frequently-asked questions",
            null,
            new FaqSection("General", [new FaqQuestion("Q?", "A.")])));

        Assert.IsFalse(markdown.Contains("Widget frequently-asked questions", StringComparison.Ordinal));
        Assert.IsFalse(markdown.StartsWith("# Widget", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RenderPutsTheSummaryBeforeTheFirstSection()
    {
        var markdown = FaqMarkdown.Render(Document(
            null,
            "Intro prose about widgets.",
            new FaqSection("General", [new FaqQuestion("Q?", "A.")])));

        Assert.IsTrue(
            markdown.IndexOf("Intro prose", StringComparison.Ordinal)
            < markdown.IndexOf("# General", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RenderFlattensAMultiLineQuestionToOneLine()
    {
        // question: | is a block scalar and may span lines. A multi-line "## " is not a heading at
        // all, and the outline would lose the entry entirely.
        var markdown = FaqMarkdown.Render(Document(
            null,
            null,
            new FaqSection("General", [new FaqQuestion("How do I\ninstall   a widget?", "A.")])));

        StringAssert.Contains(markdown, "## How do I install a widget?");
    }

    [TestMethod]
    public void RenderPreservesAnswerBodiesVerbatim()
    {
        const string answer =
            "Use the CLI:\n" +
            "\n" +
            "```bash\n" +
            "widget install\n" +
            "```\n" +
            "\n" +
            "> [!NOTE]\n" +
            "> See [the guide](../guides/widgets.md).";

        var markdown = FaqMarkdown.Render(Document(
            null, null, new FaqSection("General", [new FaqQuestion("Q?", answer)])));

        StringAssert.Contains(markdown, "```bash\nwidget install\n```");
        StringAssert.Contains(markdown, "> [!NOTE]");
        StringAssert.Contains(markdown, "[the guide](../guides/widgets.md)");
    }

    [TestMethod]
    public void RenderEmitsAHeadingForAQuestionWithNoAnswer()
    {
        var markdown = FaqMarkdown.Render(Document(
            null, null, new FaqSection("General", [new FaqQuestion("Q?", string.Empty)])));

        StringAssert.Contains(markdown, "## Q?");
    }

    [TestMethod]
    public void RenderSeparatesEveryBlockWithABlankLine()
    {
        // Markdig needs the blank line: "# General" immediately followed by prose still parses, but
        // two headings run together do not, and the outline would silently lose one.
        var markdown = FaqMarkdown.Render(Document(
            null,
            null,
            new FaqSection("General", [new FaqQuestion("Q1?", "A1."), new FaqQuestion("Q2?", "A2.")])));

        StringAssert.Contains(markdown, "# General\n\n## Q1?\n\nA1.\n\n## Q2?\n\nA2.\n");
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~FaqMarkdownTests" *> ".scratch/test-faqmd-$ts.log"
```

Expected: FAIL — `FaqMarkdown` does not exist.

- [ ] **Step 3: Write the renderer**

`src/DotNetKnowledge.Yaml/FaqMarkdown.cs`:

```csharp
using System.Text;
using System.Text.RegularExpressions;

namespace DotNetKnowledge.Yaml;

/// <summary>
/// Renders a FAQ to markdown. This is the seam: everything downstream - outline extraction, line
/// search, atomic blocks, paging, budgeting - runs on what this returns, unchanged, because by
/// then the document is markdown like any other.
/// </summary>
public static partial class FaqMarkdown
{
    public static string Render(FaqDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var builder = new StringBuilder();

        // Title is deliberately not rendered. As an H1 it would be the ancestor of every heading in
        // the file, prefixing all of the document's section paths with its own name.
        if (!string.IsNullOrWhiteSpace(document.Summary))
            builder.Append(document.Summary.TrimEnd()).Append("\n\n");

        foreach (var section in document.Sections)
        {
            builder.Append("# ").Append(Flatten(section.Name)).Append("\n\n");

            foreach (var question in section.Questions)
            {
                builder.Append("## ").Append(Flatten(question.Question)).Append("\n\n");
                if (!string.IsNullOrWhiteSpace(question.Answer))
                    builder.Append(question.Answer.TrimEnd()).Append("\n\n");
            }
        }

        return builder.ToString();
    }

    // A heading has to be one line. A block scalar need not be.
    private static string Flatten(string value) => WhitespaceRun().Replace(value, " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRun();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~FaqMarkdownTests" *> ".scratch/test-faqmd-$ts.log"
```

Expected: PASS, 7 tests.

- [ ] **Step 5: Commit**

```bash
git add src/DotNetKnowledge.Yaml tests/DotNetKnowledge.Yaml.Tests
git commit -m "Render a FAQ to markdown, sections as H1 and questions as H2"
```

---

### Task 4: Payload fields for rendered documents and skipped ones

Model-only change. It compiles and leaves every existing test passing before any behavior depends on it.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs`
- Modify: `src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj`

**Interfaces:**
- Consumes: nothing from earlier tasks.
- Produces:
  - `DocLineHit(string Path, int Line, string Text, bool IsTruncated, string SectionPath, GitProvenance Source, string? RenderedFrom = null)`
  - `DocSkippedDocument(string Path, string Reason)`
  - `DocSearchResult(..., DocNormalizationNote? NormalizationNote = null, IReadOnlyList<DocSkippedDocument>? SkippedDocuments = null)`
  - `DocContentResult(..., DocNormalizationNote? NormalizationNote = null, string? RenderedFrom = null)`
  - `DocOutlineResult(..., DocNormalizationNote? NormalizationNote = null, string? RenderedFrom = null)`

- [ ] **Step 1: Add the project reference**

`src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj` — add alongside the existing `DotNetKnowledge.Markdown` reference (find the `<ItemGroup>` holding `ProjectReference` entries):

```xml
<ProjectReference Include="..\DotNetKnowledge.Yaml\DotNetKnowledge.Yaml.csproj" />
```

- [ ] **Step 2: Add the new fields**

`src/DotNetKnowledge.Mcp/Features/Docs/DocsModels.cs` — every new field is an optional trailing parameter, so existing construction sites keep compiling. Replace the five declarations at the top of the file with:

```csharp
public sealed record DocLineHit(
    string Path,
    int Line,
    string Text,
    bool IsTruncated,
    string SectionPath,
    GitProvenance Source,
    // Set when this hit came from a document the server rendered rather than read verbatim. The
    // line number then indexes the rendering, not the bytes on disk. It belongs per hit, not per
    // result, because an unfiltered search fans across rendered and verbatim documents at once.
    string? RenderedFrom = null);

public sealed record DocNormalizationNote(string Message);

/// <summary>
/// A document the server declined to read, and why. A dropped file is indistinguishable from one
/// with no matches, which is the failure the no-silent-absence rule exists to prevent; this is the
/// document-side counterpart of skippedDeclarations on the API payloads.
/// </summary>
public sealed record DocSkippedDocument(string Path, string Reason);

public sealed record DocSearchResult(
    IReadOnlyList<DocLineHit> Hits,
    bool IsPartial,
    string? NextPageToken,
    IReadOnlyList<GitProvenance> SearchedSources,
    DocNormalizationNote? NormalizationNote = null,
    IReadOnlyList<DocSkippedDocument>? SkippedDocuments = null);

public sealed record DocContentResult(
    string Path,
    GitProvenance Source,
    string? Section,
    string Text,
    int StartLine,
    int EndLine,
    bool IsPartial,
    string? NextPageToken,
    DocNormalizationNote? NormalizationNote = null,
    string? RenderedFrom = null);

public sealed record DocOutlineEntry(int Level, string Text, string Path);

public sealed record DocOutlineResult(
    string Path,
    GitProvenance Source,
    IReadOnlyList<DocOutlineEntry> Entries,
    bool IsPartial,
    string? NextPageToken,
    DocNormalizationNote? NormalizationNote = null,
    string? RenderedFrom = null);
```

Leave `DocPathNotFoundException` and `DocSectionNotFoundException` in the file untouched.

- [ ] **Step 2b: Confirm the serializer omits the new fields when unset**

Read `src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs` and confirm `WriteOptions` still sets `DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull`. It does today; that is what keeps `renderedFrom` and `skippedDocuments` absent from an ordinary markdown payload rather than appearing as `null`. Change nothing — this is a read-only check.

- [ ] **Step 3: Build and run the full suite to verify nothing regressed**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx *> ".scratch/test-models-$ts.log"
```

Expected: PASS, with the same test count as before this task plus the 22 from Tasks 1–3. No existing test changes behavior — the new parameters all default.

- [ ] **Step 4: Commit**

```bash
git add src/DotNetKnowledge.Mcp
git commit -m "Add renderedFrom and skippedDocuments to the document payloads"
```

---

### Task 5: Read a FAQ through get_doc and get_doc_outline

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `LearnYamlMime.Detect`, `LearnYamlMime.Faq`, `FaqDocument.Parse`, `FaqMarkdown.Render`, `FaqParseException` (Tasks 1–3); `DocContentResult.RenderedFrom`, `DocOutlineResult.RenderedFrom` (Task 4).
- Produces: `DocsQueryService.RenderIfServable(string fullPath, string text) → RenderedDocument?` and `DocsQueryService.DocumentExtensions`, both private, consumed by Task 6 within the same file.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`. First add these fixture constants beside the existing `LearnArticle` constant:

```csharp
    // A Microsoft Learn structured FAQ. nuget-docs carries two of these, holding 55 question and
    // answer pairs that search cannot see today.
    private const string LearnFaq =
        "### YamlMime:FAQ\n" +
        "metadata:\n" +
        "  title: Widget FAQ\n" +
        "  ms.author: someone\n" +
        "title: Widget frequently-asked questions\n" +
        "summary: |\n" +
        "  Intro prose about widgets.\n" +
        "sections:\n" +
        "  - name: General\n" +
        "    questions:\n" +
        "      - question: |\n" +
        "          How do I install a widget?\n" +
        "        answer: |\n" +
        "          Run the widgetinstaller command.\n" +
        "      - question: |\n" +
        "          Where do widgets live?\n" +
        "        answer: |\n" +
        "          In the widget directory.\n" +
        "  - name: Troubleshooting\n" +
        "    questions:\n" +
        "      - question: |\n" +
        "          Why did my widget fail?\n" +
        "        answer: |\n" +
        "          Check the widgetlog file.\n";

    // An Azure Pipelines definition. roslyn-wiki carries nine. It must stay invisible.
    private const string PipelineYaml =
        "# Branches that trigger a build on commit\n" +
        "trigger:\n" +
        "  - main\n" +
        "steps:\n" +
        "  - script: build widgetinstaller\n";

    // A file that claims the schema and then cannot be read as one.
    private const string BrokenFaq =
        "### YamlMime:FAQ\n" +
        "unrelated: value\n";
```

Then add these test methods:

```csharp
    [TestMethod]
    public async Task GetDocAsyncRendersAFaqAndDeclaresIt()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.GetDocAsync(
                "docs/widget-faq.yml", "csharplang", section: null, limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.AreEqual("YamlMime:FAQ", result.RenderedFrom);
            StringAssert.Contains(result.Text, "# General");
            StringAssert.Contains(result.Text, "## How do I install a widget?");
            StringAssert.Contains(result.Text, "Run the widgetinstaller command.");
            // Title and metadata are document identity, not content.
            Assert.IsFalse(result.Text.Contains("ms.author", StringComparison.Ordinal));
            Assert.IsFalse(result.Text.Contains("Widget frequently-asked questions", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetOutlineAsyncReturnsATwoLevelTreeForAFaq()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.GetOutlineAsync(
                "docs/widget-faq.yml", "csharplang", limit: 100, cursor: null, CancellationToken.None);

            Assert.AreEqual("YamlMime:FAQ", result.RenderedFrom);
            Assert.AreEqual(5, result.Entries.Count);
            Assert.AreEqual(2, result.Entries.Count(entry => entry.Level == 1));
            Assert.AreEqual(3, result.Entries.Count(entry => entry.Level == 2));
            Assert.AreEqual("General", result.Entries[0].Path);
            Assert.AreEqual("General > How do I install a widget?", result.Entries[1].Path);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncReturnsASingleQuestionBySectionPath()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.GetDocAsync(
                "docs/widget-faq.yml", "csharplang",
                section: "General > How do I install a widget?",
                limit: 8000, cursor: null, CancellationToken.None);

            Assert.AreEqual("General > How do I install a widget?", result.Section);
            StringAssert.Contains(result.Text, "Run the widgetinstaller command.");
            Assert.IsFalse(result.Text.Contains("Check the widgetlog file.", StringComparison.Ordinal));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncRejectsAYamlFileThatIsNotAFaq()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            await Assert.ThrowsExactlyAsync<DocPathNotFoundException>(async () =>
                await service.GetDocAsync(
                    "docs/azure-pipelines.yml", "csharplang", section: null, limit: 8000,
                    cursor: null, CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncSurfacesAFaqThatCannotBeParsed()
    {
        // The caller named one document. Telling them it could not be read is strictly better than
        // a plausible-looking absence.
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            await Assert.ThrowsExactlyAsync<FaqParseException>(async () =>
                await service.GetDocAsync(
                    "docs/broken-faq.yml", "csharplang", section: null, limit: 8000,
                    cursor: null, CancellationToken.None));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocAsyncLeavesMarkdownUnrendered()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.GetDocAsync(
                "docs/proposal-a.md", "csharplang", section: null, limit: 8000, cursor: null,
                CancellationToken.None);

            Assert.IsNull(result.RenderedFrom);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
```

Add the fixture builder beside the existing `CreateServiceAsync`. It mirrors it exactly, writing three extra files:

```csharp
    private static async Task<DocsQueryService> CreateServiceWithYamlAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var docsDirectory = Path.Combine(repository, "docs");
        Directory.CreateDirectory(docsDirectory);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "proposal-a.md"), ProposalA);
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "widget-faq.yml"), LearnFaq);
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "azure-pipelines.yml"), PipelineYaml);
        await File.WriteAllTextAsync(Path.Combine(docsDirectory, "broken-faq.yml"), BrokenFaq);
        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        var pin = (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("csharplang", requestedRef: null, CancellationToken.None);
        return new DocsQueryService(catalog, cache, synchronizer);
    }
```

Add `using DotNetKnowledge.Yaml;` to the file's using block. If the existing tests use a different temp-directory cleanup helper than `DeleteDirectory(root)`, use whichever one the file already defines — read the existing `finally` blocks and match them.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests" *> ".scratch/test-getdoc-$ts.log"
```

Expected: FAIL — `ResolveFullPath` rejects a `.yml` path, so every new test throws `DocPathNotFoundException`.

- [ ] **Step 3: Add the rendering helper and widen the path guard**

`src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs` — add `using DotNetKnowledge.Yaml;` to the using block, then add these members beside the existing private helpers:

```csharp
    /// <summary>
    /// Extensions a document may have. .yaml is accepted although no source carries one today: the
    /// content marker is what decides, so listing it costs nothing and avoids a false absence.
    /// </summary>
    private static readonly string[] DocumentExtensions = [".md", ".yml", ".yaml"];

    private sealed record RenderedDocument(string Text, string? RenderedFrom);

    private static bool HasDocumentExtension(string path) =>
        DocumentExtensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static bool IsYamlPath(string path) =>
        path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// The one place a non-markdown document becomes markdown. Returns null when the file is YAML
    /// this server does not serve - a pipeline definition or a navigation file - which is not an
    /// error. Throws <see cref="FaqParseException"/> when a file claims the FAQ schema and then
    /// cannot be read as one, which is.
    /// </summary>
    private static RenderedDocument? RenderIfServable(string fullPath, string text)
    {
        if (!IsYamlPath(fullPath))
            return new RenderedDocument(text, null);

        if (!string.Equals(LearnYamlMime.Detect(text), LearnYamlMime.Faq, StringComparison.Ordinal))
            return null;

        return new RenderedDocument(
            FaqMarkdown.Render(FaqDocument.Parse(text)),
            $"YamlMime:{LearnYamlMime.Faq}");
    }
```

Then in `ResolveFullPath`, replace the extension condition:

```csharp
            || !candidate.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
```

with:

```csharp
            || !HasDocumentExtension(candidate)
```

- [ ] **Step 4: Render on the way in**

Still in `DocsQueryService.cs`, change `DocumentRead` and `ReadDocument`:

```csharp
    private sealed record DocumentRead(string Text, string? RenderedFrom, GitProvenance Provenance);

    private static DocumentRead ReadDocument(
        string directory, string source, string path, SourceDefinition definition, SourceSyncState state)
    {
        var fullPath = ResolveFullPath(directory, source, path);
        var rendered = RenderIfServable(fullPath, File.ReadAllText(fullPath));

        // YAML this server does not serve is not a document, and the caller must not be able to
        // tell it apart from a path that does not exist.
        if (rendered is null)
            throw new DocPathNotFoundException(path, source);

        return new DocumentRead(rendered.Text, rendered.RenderedFrom, ToProvenance(definition, state));
    }
```

Widen the two read tuples so `RenderedFrom` reaches the callers. In `ReadDocumentAttemptAsync`, change the return type to
`Task<(string Text, GitProvenance Provenance, string ResolvedPath, DocNormalizationNote? Note, string? RenderedFrom)>`
and its final line to `return (read.Text, read.Provenance, path, null, read.RenderedFrom);`.

Make the same signature change to `ReadDocumentAsync`, and update its two return sites: the success path returns the tuple unchanged, and the normalization-retry path destructures with the extra element:

```csharp
                var (text, provenance, resolvedPath, _, renderedFrom) =
                    await ReadDocumentAttemptAsync(source, normalizedPath, cancellationToken).ConfigureAwait(false);
                var note = new DocNormalizationNote(
                    $"'{path}' was not found; resolved to '{resolvedPath}' after decoding HTML entities and " +
                    "typographic characters in the path.");
                return (text, provenance, resolvedPath, note, renderedFrom);
```

- [ ] **Step 5: Pass it into the two payloads**

In `GetOutlineAsync`, change the destructuring line to:

```csharp
        var (text, provenance, resolvedPath, note, renderedFrom) =
            await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
```

and add `renderedFrom` as the final argument of the returned `DocOutlineResult`, after `note`.

In `GetDocAsync`, change the destructuring line to:

```csharp
        var (text, provenance, resolvedPath, pathNote, renderedFrom) =
            await ReadDocumentAsync(source, path, cancellationToken).ConfigureAwait(false);
```

and add `renderedFrom` as the final argument of the returned `DocContentResult`, after `CombineNotes(pathNote, sectionNote)`.

- [ ] **Step 6: Run the tests to verify they pass**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests" *> ".scratch/test-getdoc-$ts.log"
```

Expected: PASS, every existing `DocsQueryServiceTests` test plus the six new ones.

- [ ] **Step 7: Commit**

```bash
git add src/DotNetKnowledge.Mcp tests/DotNetKnowledge.Mcp.Tests
git commit -m "Serve rendered Learn FAQ documents through get_doc and get_doc_outline"
```

---

### Task 6: Search a FAQ, and report a FAQ that could not be read

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs`
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsQueryServiceTests.cs`

**Interfaces:**
- Consumes: `RenderIfServable`, `HasDocumentExtension`, `DocumentExtensions` (Task 5, same file); `DocSkippedDocument`, `DocLineHit.RenderedFrom`, `DocSearchResult.SkippedDocuments` (Task 4).
- Produces: nothing later tasks call directly.

- [ ] **Step 1: Write the failing tests**

Add to `DocsQueryServiceTests.cs`:

```csharp
    [TestMethod]
    public async Task SearchAsyncFindsAHitInsideAFaqAnswer()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.SearchAsync(
                "widgetinstaller", regex: false, source: "csharplang", limit: 20, cursor: null,
                CancellationToken.None);

            var hit = result.Hits.Single(candidate => candidate.Path.EndsWith(".yml", StringComparison.Ordinal));
            Assert.AreEqual("docs/widget-faq.yml", hit.Path);
            Assert.AreEqual("YamlMime:FAQ", hit.RenderedFrom);
            // The hit must be locatable: a section path is what get_doc accepts back.
            Assert.AreEqual("General > How do I install a widget?", hit.SectionPath);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncNeverReadsAYamlFileThatIsNotAFaq()
    {
        // "widgetinstaller" appears in the pipeline fixture's build step too. A CI definition is
        // not documentation and must not surface.
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.SearchAsync(
                "widgetinstaller", regex: false, source: "csharplang", limit: 20, cursor: null,
                CancellationToken.None);

            Assert.IsFalse(result.Hits.Any(hit => hit.Path.Contains("azure-pipelines", StringComparison.Ordinal)));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncReportsAFaqItCouldNotParse()
    {
        // One unreadable file must not fail a fan-out, and must not vanish either.
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.SearchAsync(
                "widgetinstaller", regex: false, source: "csharplang", limit: 20, cursor: null,
                CancellationToken.None);

            Assert.IsNotNull(result.SkippedDocuments);
            var skipped = result.SkippedDocuments.Single();
            Assert.AreEqual("docs/broken-faq.yml", skipped.Path);
            StringAssert.Contains(skipped.Reason, "no sections");
            // The good FAQ still answered.
            Assert.IsTrue(result.Hits.Any(hit => hit.Path == "docs/widget-faq.yml"));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchAsyncLeavesMarkdownHitsUnmarked()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var result = await service.SearchAsync(
                "motivating", regex: false, source: "csharplang", limit: 20, cursor: null,
                CancellationToken.None);

            var hit = result.Hits.Single(candidate => candidate.Path == "docs/proposal-a.md");
            Assert.IsNull(hit.RenderedFrom);
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~SearchAsync" *> ".scratch/test-search-$ts.log"
```

Expected: FAIL — `ReadSearchSource` enumerates `*.md` only, so the FAQ produces no hit and `SkippedDocuments` is null.

- [ ] **Step 3: Enumerate every document extension**

`src/DotNetKnowledge.Mcp/Features/Docs/DocsQueryService.cs` — add beside the other private helpers:

```csharp
    /// <summary>
    /// Enumerated per extension rather than with a single wildcard, so a source's non-document
    /// ballast - nuget-docs checks out 14 MB of images - is never walked. The extension is
    /// re-checked because a Windows search pattern also matches 8.3 short names.
    /// </summary>
    private static IEnumerable<string> EnumerateDocumentFiles(string fullRoot) =>
        DocumentExtensions
            .SelectMany(extension => Directory.EnumerateFiles(fullRoot, $"*{extension}", SearchOption.AllDirectories))
            .Where(HasDocumentExtension);
```

- [ ] **Step 4: Render before the prefilter, and collect what was skipped**

Replace the body of `ReadSearchSource` with this. The `SourceSearchRead` record gains a third member:

```csharp
    private sealed record SourceSearchRead(
        GitProvenance Provenance,
        IReadOnlyList<DocLineHit> Hits,
        IReadOnlyList<DocSkippedDocument> Skipped);

    private static SourceSearchRead ReadSearchSource(
        string directory,
        SourceDefinition definition,
        SourceSyncState state,
        string query,
        Regex? compiledPattern,
        CancellationToken cancellationToken)
    {
        var provenance = ToProvenance(definition, state);
        var fullRoot = Path.GetFullPath(directory);
        var hits = new List<DocLineHit>();
        var skipped = new List<DocSkippedDocument>();

        foreach (var file in EnumerateDocumentFiles(fullRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relativePath = Path.GetRelativePath(fullRoot, file).Replace('\\', '/');

            RenderedDocument? document;
            try
            {
                // Rendering happens before the prefilter so the prefilter tests the same text the
                // matcher will. Testing raw YAML would skip a file whose only match is a word the
                // rendering produces - the same silent absence, one layer down.
                document = RenderIfServable(file, File.ReadAllText(file));
            }
            catch (FaqParseException exception)
            {
                // A dropped file is indistinguishable from one with no matches. One unreadable
                // document must not fail a fan-out across every source, so it is named instead.
                skipped.Add(new DocSkippedDocument(relativePath, exception.Message));
                continue;
            }

            // YAML this server does not serve. Not an absence worth reporting: it was never a
            // document, and naming every pipeline definition would bury the ones that matter.
            if (document is null)
                continue;

            var text = document.Text;

            // Skip the full Markdig parse entirely for a file that cannot match: a source can hold
            // hundreds of documents, and most queries match none of them. This must check
            // per-line, the same way MarkdownLineSearch.Search below actually matches: an anchored
            // pattern like "^## " behaves differently against a single line than against the whole
            // file text (^ without RegexOptions.Multiline only matches offset 0 of whatever string
            // it's given), so a whole-file check would wrongly skip files whose only match isn't on
            // line 1.
            var lines = text.ReplaceLineEndings("\n").Split('\n');
            var mightMatch = compiledPattern is not null
                ? lines.Any(compiledPattern.IsMatch)
                : lines.Any(line => line.Contains(query, StringComparison.Ordinal));
            if (!mightMatch)
                continue;

            var outline = MarkdownOutline.Extract(text);

            foreach (var hit in MarkdownLineSearch.Search(text, outline, query, compiledPattern))
            {
                var (matchedText, isTruncated) = DocumentationText.Budget(hit.Text, MatchTextBudget);
                hits.Add(new DocLineHit(
                    relativePath, hit.Line, matchedText, isTruncated, hit.SectionPath, provenance,
                    document.RenderedFrom));
            }
        }

        return new SourceSearchRead(provenance, hits, skipped);
    }
```

- [ ] **Step 5: Carry the skipped list up to the payload**

Change `CollectHitsAsync` to return and accumulate a third list. Its signature becomes:

```csharp
    private async Task<(List<DocLineHit> Hits, List<GitProvenance> SearchedSources, List<DocSkippedDocument> Skipped)>
        CollectHitsAsync(
            string query, Regex? compiledPattern, string[] sourceNames, CancellationToken cancellationToken)
```

Declare `var skipped = new List<DocSkippedDocument>();` beside the other two locals, add `skipped.AddRange(read.Skipped);` beside the existing `hits.AddRange(read.Hits);`, and return `(hits, searchedSources, skipped)`.

In `SearchAsync`, update both call sites to destructure three values:

```csharp
        var (hits, searchedSources, skipped) = await CollectHitsAsync(
            query, compiledPattern, sourceNames, cancellationToken).ConfigureAwait(false);
```

and inside the normalization-retry block:

```csharp
            var (normalizedHits, normalizedSearchedSources, normalizedSkipped) = await CollectHitsAsync(
                normalizedQuery, compiledPattern: null, sourceNames, cancellationToken).ConfigureAwait(false);
            if (normalizedHits.Count > 0)
            {
                hits = normalizedHits;
                searchedSources = normalizedSearchedSources;
                skipped = normalizedSkipped;
                effectiveQuery = normalizedQuery;
```

Finally, add the last argument to the returned `DocSearchResult`, after `note`:

```csharp
            skipped.Count > 0 ? skipped : null);
```

- [ ] **Step 6: Run the tests to verify they pass**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsQueryServiceTests" *> ".scratch/test-search-$ts.log"
```

Expected: PASS, including the four new tests and every pre-existing one.

- [ ] **Step 7: Commit**

```bash
git add src/DotNetKnowledge.Mcp tests/DotNetKnowledge.Mcp.Tests
git commit -m "Search rendered FAQ documents, and name a FAQ that would not parse"
```

---

### Task 7: Tool descriptions and the unreadable-document error code

An agent that cannot tell a rendered line number from a real one will follow a hit into `cacheDir` and land on the wrong line. The description is the only place that gets said.

**Files:**
- Modify: `src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs`
- Test: `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs`

**Interfaces:**
- Consumes: `FaqParseException` (Task 2); the payload fields from Task 4.
- Produces: the JSON error code `document_unreadable`.

- [ ] **Step 1: Write the failing tests**

Add to `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsToolTests.cs`. Read the file first and match how its existing tests build a `DocsQueryService` and deserialize a payload — reuse that helper rather than inventing one.

```csharp
    [TestMethod]
    public async Task GetDocSerializesRenderedFrom()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var json = await DocsTool.GetDoc(
                "docs/widget-faq.yml", "csharplang", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("YamlMime:FAQ", document.RootElement.GetProperty("renderedFrom").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocOmitsRenderedFromForMarkdown()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var json = await DocsTool.GetDoc(
                "docs/proposal-a.md", "csharplang", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.IsFalse(document.RootElement.TryGetProperty("renderedFrom", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchDocsSerializesSkippedDocuments()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var json = await DocsTool.SearchDocs(
                "widgetinstaller", service, CancellationToken.None, source: "csharplang");

            using var document = JsonDocument.Parse(json);
            var skipped = document.RootElement.GetProperty("skippedDocuments");
            Assert.AreEqual(1, skipped.GetArrayLength());
            Assert.AreEqual("docs/broken-faq.yml", skipped[0].GetProperty("path").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task SearchDocsOmitsSkippedDocumentsWhenNothingWasSkipped()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceAsync(root);

            var json = await DocsTool.SearchDocs(
                "motivating", service, CancellationToken.None, source: "csharplang");

            using var document = JsonDocument.Parse(json);
            Assert.IsFalse(document.RootElement.TryGetProperty("skippedDocuments", out _));
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    [TestMethod]
    public async Task GetDocReportsAnUnreadableFaqAsAnError()
    {
        var root = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-tests-{Guid.NewGuid():N}");
        try
        {
            var service = await CreateServiceWithYamlAsync(root);

            var json = await DocsTool.GetDoc(
                "docs/broken-faq.yml", "csharplang", service, CancellationToken.None);

            using var document = JsonDocument.Parse(json);
            Assert.AreEqual("document_unreadable", document.RootElement.GetProperty("error").GetString());
        }
        finally
        {
            DeleteDirectory(root);
        }
    }
```

`DocsToolTests` needs the same fixture builder and constants Task 5 added to `DocsQueryServiceTests`. Rather than duplicating them, move `LearnFaq`, `PipelineYaml`, `BrokenFaq` and `CreateServiceWithYamlAsync` into a new `internal static class DocsTestFixtures` in `tests/DotNetKnowledge.Mcp.Tests/Features/Docs/DocsTestFixtures.cs`, and have both test classes call it. Update Task 5's tests to the moved names in the same commit.

- [ ] **Step 2: Run the tests to verify they fail**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsToolTests" *> ".scratch/test-tool-$ts.log"
```

Expected: FAIL — `document_unreadable` is not produced; `GetDoc` currently lets `FaqParseException` escape.

- [ ] **Step 3: Catch the parse failure in both read tools**

`src/DotNetKnowledge.Mcp/Features/Docs/DocsTool.cs` — add `using DotNetKnowledge.Yaml;` to the using block, then add this catch clause to **both** `GetDoc` and `GetDocOutline`, immediately after the existing `catch (DocPathNotFoundException ...)` clause:

```csharp
        catch (FaqParseException exception)
        {
            return SerializeError("document_unreadable", exception.Message);
        }
```

Do not add it to `SearchDocs`: a search reports an unreadable document through `skippedDocuments` instead, so one bad file cannot fail a fan-out.

- [ ] **Step 4: Extend the three tool descriptions**

In the `[Description(...)]` for `search_docs`, append this sentence to the existing string:

```csharp
        "A hit carrying renderedFrom came from a document this server rendered rather than read " +
        "verbatim - today a Microsoft Learn structured FAQ (renderedFrom \"YamlMime:FAQ\"). Its " +
        "path names a real file, but the line number indexes the rendering, not the file's bytes. " +
        "A document that declared a schema this server renders and then could not be read is " +
        "named in skippedDocuments with the reason, rather than silently contributing no hits."
```

In the `[Description(...)]` for `get_doc`, append:

```csharp
        "A response carrying renderedFrom is the server's rendering of a structured document - " +
        "today a Microsoft Learn FAQ, whose sections and questions become headings - so startLine " +
        "and endLine index that rendering rather than the file on disk. The FAQ's own title and " +
        "metadata are identity rather than content and are not returned."
```

In the `[Description(...)]` for `get_doc_outline`, append:

```csharp
        "A Microsoft Learn structured FAQ has a heading tree even though the file is YAML: its " +
        "sections are level 1 and its questions level 2, and the response carries renderedFrom."
```

- [ ] **Step 5: Run the tests to verify they pass**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet test DotNetKnowledge.slnx --filter "FullyQualifiedName~DocsToolTests" *> ".scratch/test-tool-$ts.log"
```

Expected: PASS, five new tests plus every existing one.

- [ ] **Step 6: Commit**

```bash
git add src/DotNetKnowledge.Mcp tests/DotNetKnowledge.Mcp.Tests
git commit -m "Declare rendered documents in the tool descriptions and error codes"
```

---

### Task 8: Close the licensing guard's gap for pasted FAQ content

Now that FAQ content can be read and quoted, the guard that stops it being committed has to actually match it. The rule appears twice in the script — the tracked-tree scan and the `--history` scan — and both anchor at column 0.

**Files:**
- Modify: `scripts/verify-no-vendored-content.cs:217` and `scripts/verify-no-vendored-content.cs:294`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing consumed by later tasks.

- [ ] **Step 1: Confirm the gap exists**

The probe's keys are assembled from variables on purpose: written out literally, this plan file would itself become a finding once Step 2 widens the rule.

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
$author = 'ms' + '.author'
$date = 'ms' + '.date'
$lines = @('### YamlMime:FAQ', 'metadata:', "  ${author}: someone", "  ${date}: 01/08/2026", 'sections:', '  - name: General')
Set-Content -Path .scratch/pasted-faq-probe.md -Value $lines
git add -f .scratch/pasted-faq-probe.md
dotnet scripts/verify-no-vendored-content.cs *> ".scratch/verify-gap-$ts.log"
```

Expected: exit 0, "no vendored upstream content" — the guard misses it, because `^ms\.` requires column 0 and the keys are indented two spaces. Read the log to confirm.

- [ ] **Step 2: Allow leading whitespace in both copies of the rule**

`scripts/verify-no-vendored-content.cs` line 217, change:

```csharp
        new Regex(@"^ms\.(author|date|topic):\s*\S", RegexOptions.Multiline),
```

to:

```csharp
        new Regex(@"^[ \t]*ms\.(author|date|topic):\s*\S", RegexOptions.Multiline),
```

and line 294, change:

```csharp
            ("learn-article", @"^ms\.(author|date|topic): ?\S", true),
```

to:

```csharp
            ("learn-article", @"^[ \t]*ms\.(author|date|topic): ?\S", true),
```

A Learn FAQ nests those keys under `metadata:`; a Learn article has them at column 0. One pattern now catches both, and no legitimate tracked file carries an indented `ms.date:` either.

- [ ] **Step 3: Verify the guard now catches it**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet scripts/verify-no-vendored-content.cs *> ".scratch/verify-caught-$ts.log"
```

Expected: **exit 1**, reporting `learn-article` against `.scratch/pasted-faq-probe.md`. Read the log and confirm the finding names that path.

- [ ] **Step 4: Remove the probe and confirm a clean tree**

```powershell
git rm -f --cached .scratch/pasted-faq-probe.md
Remove-Item .scratch/pasted-faq-probe.md
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet scripts/verify-no-vendored-content.cs *> ".scratch/verify-clean-$ts.log"
dotnet scripts/verify-no-vendored-content.cs -- --history *> ".scratch/verify-history-$ts.log"
```

Expected: both exit 0. The `--history` run is the one that proves the widened pattern does not retroactively flag an existing commit; if it reports a finding, stop and report it rather than loosening the rule.

- [ ] **Step 5: Commit**

```bash
git add scripts/verify-no-vendored-content.cs
git commit -m "Catch a pasted Learn FAQ, whose ms.* keys are indented under metadata"
```

---

### Task 9: Standing-record obligations and end-to-end verification

The repository's convention is that a resolved backlog item is deleted, and that a rejected alternative is recorded once so the question is not reopened.

**Files:**
- Delete: `docs/backlog/yaml-source-content-is-unsearchable.md`
- Modify: `docs/backlog/README.md`
- Modify: `docs/decisions.md`
- Modify: `docs/design/mcp-tool-surface.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: everything above.
- Produces: nothing.

- [ ] **Step 1: Delete the backlog item and its index row**

```bash
git rm docs/backlog/yaml-source-content-is-unsearchable.md
```

In `docs/backlog/README.md`, remove this table row entirely:

```markdown
| [YAML content in a synchronized source is unsearchable](yaml-source-content-is-unsearchable.md) | server | Three pipeline stages assume markdown; the cheapest honest fix is narrower than full support and not yet designed |
```

`git log` is the record; do not add a "resolved" note.

- [ ] **Step 2: Add the decisions entry**

`docs/decisions.md` is append-only and newest-first — add this as a new entry at the **top** of the entry list, matching the heading format the file already uses (read the first existing entry and copy its shape, including how it dates entries):

> **YAML support is scoped to `YamlMime:FAQ`, and a rendered document declares itself.**
>
> Of the 13 `.yml` files in the markdown-searchable sources, nine are Azure Pipelines definitions, two are navigation, and two are prose. Microsoft Learn states the schema on line 1, so the gate is content rather than path or source — a Learn FAQ in any future source is picked up with no configuration, and a pipeline definition never is.
>
> Rejected: reading every `.yml`, which the backlog file itself suggested — it would put CI configuration for building Roslyn in front of an agent asking about C#. Rejected: reporting that a FAQ has no outline, also from the backlog file — a Learn FAQ states a two-level tree explicitly in `sections[].name` and `questions[].question`, so there is a real outline to return.
>
> A FAQ is rendered to markdown at the read, which is why the outline, section paths, pager, cursors and truncation reporting needed no new code. The rendering's line numbers do not index the file on disk, so every payload carries `renderedFrom`. Rejected: carrying YamlDotNet source marks through the renderer to report true `.yml` lines — the declaring field already tells an agent not to trust the number against the raw file, at a fraction of the plumbing.
>
> The FAQ's `title` is not rendered as a heading: as an H1 it becomes the ancestor of every heading in the file and prefixes every section path with the document's own name.

- [ ] **Step 3: Update the tool surface document**

In `docs/design/mcp-tool-surface.md`, find the payload descriptions for `search_docs`, `get_doc` and `get_doc_outline` and add `renderedFrom` and `skippedDocuments` to them, stating that a rendered document's line numbers index the rendering. Match the document's existing structure — read it first rather than appending a new section.

- [ ] **Step 4: Update README.md and CLAUDE.md**

In `README.md`'s status summary, note that the document tools now serve Microsoft Learn structured-FAQ documents alongside markdown.

In `CLAUDE.md`, add `DotNetKnowledge.Yaml` to the architecture notes beside `DotNetKnowledge.Markdown`, stating its contract is the mirror — YAML text in, markdown text out — and that YamlDotNet is referenced by no other project. Add it near the existing `Text/DocumentationText.cs` seam paragraph, which describes the analogous "one seam" idea.

- [ ] **Step 5: Full build and test**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet build DotNetKnowledge.slnx *> ".scratch/build-final-$ts.log"
dotnet test DotNetKnowledge.slnx *> ".scratch/test-final-$ts.log"
```

Expected: both exit 0. Read the last ~20 lines of each log and paste them into the completion report. Do not claim a pass without that output.

- [ ] **Step 6: Live smoke over the real cache**

The suites all use fixtures, so nothing so far has touched a real FAQ. Build the server and drive it over redirected stdio (a Git Bash `>` redirect swallows the server's stdout and looks like a server fault — use a redirected-process driver, and see `scripts/probes/` for the existing pattern). Confirm three things against `nuget-docs`:

1. `search_docs` with `source: "nuget-docs"` and a query drawn from a real FAQ answer returns a hit whose `path` is `docs/resources/NuGet-FAQ.yml` and whose `renderedFrom` is `YamlMime:FAQ`.
2. `get_doc_outline` on that path returns 6 level-1 and 27 level-2 entries.
3. `get_doc` with a section path from that outline returns that one question and answer.

Note the query matcher is case-sensitive and Ordinal — `nuget.config` finds nothing because the file writes `NuGet.Config`. Pick the query from the file's actual casing.

If `nuget-docs` is not synced in the local cache, call `sync_source` first; a query tool never downloads.

- [ ] **Step 7: Verify no vendored content, then commit**

```powershell
$ts = Get-Date -Format yyyyMMdd-HHmm
dotnet scripts/verify-no-vendored-content.cs *> ".scratch/verify-final-$ts.log"
```

Expected: exit 0. The smoke test in Step 6 reads real upstream content — make sure none of it was pasted into a tracked file, including this plan's own completion notes.

```bash
git add -A
git commit -m "Retire the YAML backlog item and record the decision"
```

---

## Self-Review

**Spec coverage.** Every section of the spec maps to a task: scope and detection → Task 1; parse and the empty-parse rule → Task 2; render, title dropped, question flattening, verbatim answers → Task 3; `renderedFrom` and `skippedDocuments` → Task 4; `ResolveFullPath` and `ReadDocument` → Task 5; `ReadSearchSource` and render-before-prefilter → Task 6; tool descriptions → Task 7; the `learn-article` rule → Task 8; backlog, decisions, tool surface, README and CLAUDE → Task 9. Ranking is explicitly unchanged per the spec, so it has no task by design.

**Two things the spec left implicit that this plan decides.** The spec says an unparseable FAQ "surfaces" through `get_doc` without naming a wire error code; Task 7 defines `document_unreadable`. The spec lists `DocsToolTests` coverage without saying where its fixtures come from; Task 7 extracts `DocsTestFixtures` so the two test classes share one definition rather than duplicating it.

**Known risk.** Task 2's `ParseRejectsMalformedYaml` assumes YamlDotNet raises `YamlException`. If the log shows another type, widen the catch to that type — never to bare `Exception`, which would swallow the `FaqParseException` the method throws deliberately.
