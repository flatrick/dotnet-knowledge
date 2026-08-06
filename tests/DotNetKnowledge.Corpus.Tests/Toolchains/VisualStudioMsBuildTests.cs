namespace DotNetKnowledge.Corpus.Tests.Toolchains;

[TestClass]
[TestCategory("Unit")]
public sealed class VisualStudioMsBuildTests
{
    private const string ProgramFilesX86 = @"C:\Program Files (x86)";

    private static readonly string VswherePath =
        Path.Combine(ProgramFilesX86, "Microsoft Visual Studio", "Installer", "vswhere.exe");

    [TestMethod]
    public void TryResolveReportsThatVisualStudioMsBuildRequiresWindows()
    {
        var toolchain = new VisualStudioMsBuild(
            () => false,
            () => throw new InvalidOperationException("Program Files lookup should not run."),
            _ => throw new InvalidOperationException("File probing should not run."),
            (_, _) => throw new InvalidOperationException("vswhere should not run."));

        var resolved = toolchain.TryResolve(out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        StringAssert.Contains(reason, "requires Windows");
    }

    [TestMethod]
    public void TryResolveReportsTheExactVswherePathItInspected()
    {
        var toolchain = new VisualStudioMsBuild(
            () => true,
            () => ProgramFilesX86,
            _ => false,
            (_, _) => throw new InvalidOperationException("vswhere should not run."));

        var resolved = toolchain.TryResolve(out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        StringAssert.Contains(reason, VswherePath);
    }

    [TestMethod]
    public void TryResolveReportsTheQueryAndItsOutputWhenNoMsBuildIsNamed()
    {
        var toolchain = new VisualStudioMsBuild(
            () => true,
            () => ProgramFilesX86,
            candidate => candidate == VswherePath,
            (_, _) => string.Empty);

        var resolved = toolchain.TryResolve(out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        StringAssert.Contains(reason, VswherePath);
        StringAssert.Contains(reason, VisualStudioMsBuild.VswhereArguments);
        StringAssert.Contains(reason, "(nothing)");
    }

    [TestMethod]
    public void TryResolveReportsTheNamedPathWhenVswhereNamesOneThatDoesNotExist()
    {
        const string missing = @"C:\Program Files\Microsoft Visual Studio\18\MSBuild\Current\Bin\MSBuild.exe";
        var toolchain = new VisualStudioMsBuild(
            () => true,
            () => ProgramFilesX86,
            candidate => candidate == VswherePath,
            (_, _) => missing);

        var resolved = toolchain.TryResolve(out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        StringAssert.Contains(reason, missing);
    }

    [TestMethod]
    public void TryResolveTakesTheFirstExistingPathVswhereNames()
    {
        const string missing = @"C:\stale\MSBuild.exe";
        const string present = @"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe";
        var toolchain = new VisualStudioMsBuild(
            () => true,
            () => ProgramFilesX86,
            candidate => candidate == VswherePath || candidate == present,
            (_, _) => $"{missing}\r\n{present}\r\n");

        var resolved = toolchain.TryResolve(out var path, out var reason);

        Assert.IsTrue(resolved);
        Assert.AreEqual(present, path);
        Assert.AreEqual(string.Empty, reason);
    }

    [TestMethod]
    public void TryResolveReportsAVswhereThatCouldNotBeRun()
    {
        var toolchain = new VisualStudioMsBuild(
            () => true,
            () => ProgramFilesX86,
            candidate => candidate == VswherePath,
            (_, _) => throw new System.ComponentModel.Win32Exception(5, "Access is denied."));

        var resolved = toolchain.TryResolve(out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        StringAssert.Contains(reason, VswherePath);
        StringAssert.Contains(reason, "Access is denied.");
    }

    /// <summary>
    /// The duplication with <c>scripts/verify-feature-floors.cs</c> is accepted — a single-file
    /// script and a test project cannot share code — but a silent divergence in the query is not.
    /// The script spells the same value as a C# literal, so the backslashes are doubled there.
    /// </summary>
    [TestMethod]
    public void TryResolveUsesTheSameVswhereQueryAsTheFloorProbeScript()
    {
        var script = File.ReadAllText(Path.Combine(RepositoryRoot(), "scripts", "verify-feature-floors.cs"));

        StringAssert.Contains(
            script,
            VisualStudioMsBuild.VswhereArguments.Replace("\\", "\\\\", StringComparison.Ordinal));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "sources.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
