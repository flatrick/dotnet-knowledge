using DotNetKnowledge.Mcp.Text;

namespace DotNetKnowledge.Mcp.Tests.Text;

[TestClass]
public sealed class DocumentationTextTests
{
    [TestMethod]
    [DataRow("<xref:System.String>", "System.String")]
    [DataRow("<xref:System.Buffers.ReadOnlySequence`1>", "System.Buffers.ReadOnlySequence`1")]
    [DataRow("<xref:System.Reflection.Binder?displayProperty=nameWithType>", "System.Reflection.Binder")]
    [DataRow("<xref:System.Threading.Tasks.Task.Wait*?displayProperty=nameWithType>", "System.Threading.Tasks.Task.Wait")]
    [DataRow("<xref:System.String.IndexOfAny(System.Char%5B%5D,System.Int32)>", "System.String.IndexOfAny(System.Char[],System.Int32)")]
    [DataRow("<xref:System.AppDomainUnloadedException.%23ctor*>", "System.AppDomainUnloadedException.#ctor")]
    public void NormalizeResolvesEveryXrefFormTheCorpusUses(string input, string expected) =>
        Assert.AreEqual(expected, DocumentationText.Normalize(input, collapseWhitespace: true));

    [TestMethod]
    public void NormalizeFoldsTheSourcesOwnLineWrappingButKeepsParagraphs()
    {
        // roslyn-api-docs wraps and indents its prose; the indentation is a fact about the file.
        const string wrapped = "Register an action to be executed at completion,\n"
            + "            which will operate on the model.\n\n"
            + "            A second paragraph.";

        Assert.AreEqual(
            "Register an action to be executed at completion, which will operate on the model.\n\nA second paragraph.",
            DocumentationText.Normalize(wrapped, collapseWhitespace: true));
    }

    [TestMethod]
    public void NormalizeLeavesMarkdownBodiesAlone()
    {
        // Folding this would run the fence, the code and the closing fence onto one line.
        const string markdown = "## Remarks\n\n```csharp\nvar x = 1;\nvar y = 2;\n```\n\n- first\n- second";

        var result = DocumentationText.Normalize(markdown, collapseWhitespace: false);

        Assert.AreEqual(markdown, result);
        StringAssert.Contains(result!, "\nvar y = 2;\n");
    }

    [TestMethod]
    public void NormalizeResolvesReferencesInsideMarkdownEvenWithoutCollapsing()
    {
        // The xref form only ever appears in markdown bodies, so skipping it there would leave the
        // whole feature unreachable.
        Assert.AreEqual(
            "Using a System.String is not efficient.",
            DocumentationText.Normalize("Using a <xref:System.String> is not efficient.", collapseWhitespace: false));
    }

    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   \n\t ")]
    [DataRow("To be added.")]
    public void NormalizeReturnsNullForAbsentAndPlaceholderText(string? input) =>
        Assert.IsNull(DocumentationText.Normalize(input, collapseWhitespace: true));

    [TestMethod]
    public void BudgetReportsTruncationInsteadOfMarkingTheText()
    {
        var (shortText, shortTruncated) = DocumentationText.Budget("abcde", 5);
        Assert.AreEqual("abcde", shortText);
        Assert.IsFalse(shortTruncated, "text exactly at the budget is not truncated.");

        var (longText, longTruncated) = DocumentationText.Budget("abcdef", 5);
        Assert.AreEqual("abcde", longText);
        Assert.IsTrue(longTruncated);

        // No ellipsis: a caller cannot tell one this server added from one the source wrote.
        Assert.IsFalse(longText.EndsWith('…'));
    }

    [TestMethod]
    public void BudgetRejectsANonPositiveBudget() =>
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => DocumentationText.Budget("abc", 0));
}
