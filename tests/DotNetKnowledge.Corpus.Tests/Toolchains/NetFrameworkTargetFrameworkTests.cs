namespace DotNetKnowledge.Corpus.Tests.Toolchains;

[TestClass]
[TestCategory("Unit")]
public sealed class NetFrameworkTargetFrameworkTests
{
    [TestMethod]
    [DataRow("net48")]
    [DataRow("net472")]
    [DataRow("net40")]
    public void IsFrameworkRecognizesAnUndottedNumericSuffix(string targetFramework)
    {
        Assert.IsTrue(NetFrameworkTargetFramework.IsFramework(targetFramework));
    }

    [TestMethod]
    [DataRow("net7.0")]
    [DataRow("net10.0")]
    [DataRow("net5.0")]
    [DataRow("netstandard2.0")]
    [DataRow("net")]
    public void IsFrameworkRejectsACoreClrOrMalformedMoniker(string targetFramework)
    {
        Assert.IsFalse(NetFrameworkTargetFramework.IsFramework(targetFramework));
    }
}
