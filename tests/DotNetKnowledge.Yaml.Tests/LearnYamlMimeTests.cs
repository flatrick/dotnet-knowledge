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
