namespace DotNetKnowledge.Corpus.Tests.Toolchains;

[TestClass]
[TestCategory("Unit")]
public sealed class NetFrameworkRuntimeTests
{
    private const string WindowsFolder = @"C:\Windows";

    private static readonly string Mscorlib =
        Path.Combine(WindowsFolder, "Microsoft.NET", "Framework64", "v4.0.30319", "mscorlib.dll");

    [TestMethod]
    public void TryResolveReportsThatFrameworkExecutionRequiresWindows()
    {
        var runtime = new NetFrameworkRuntime(
            () => false,
            () => throw new InvalidOperationException("Windows folder lookup should not run."),
            _ => throw new InvalidOperationException("File probing should not run."));

        var resolved = runtime.TryResolve(out var reason);

        Assert.IsFalse(resolved);
        StringAssert.Contains(reason, "requires Windows");
    }

    [TestMethod]
    public void TryResolveReportsTheExactMscorlibPathItInspected()
    {
        var runtime = new NetFrameworkRuntime(
            () => true,
            () => WindowsFolder,
            _ => false);

        var resolved = runtime.TryResolve(out var reason);

        Assert.IsFalse(resolved);
        StringAssert.Contains(reason, Mscorlib);
    }

    [TestMethod]
    public void TryResolveSucceedsWhenTheInBoxRuntimeIsPresent()
    {
        var runtime = new NetFrameworkRuntime(
            () => true,
            () => WindowsFolder,
            candidate => candidate == Mscorlib);

        var resolved = runtime.TryResolve(out var reason);

        Assert.IsTrue(resolved);
        Assert.AreEqual(string.Empty, reason);
    }
}
