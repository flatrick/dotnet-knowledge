namespace DotNetKnowledge.Corpus.Tests.CSharpScripts;

[TestClass]
[TestCategory("Unit")]
public sealed class CsiToolchainTests
{
    [TestMethod]
    public void TryResolveReportsThatThePinnedNet472HostRequiresWindows()
    {
        using var output = new TemporaryOutput();
        var toolchain = new CsiToolchain(() => false, _ => throw new InvalidOperationException("Version lookup should not run."));

        var resolved = toolchain.TryResolve(output.DirectoryPath, out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        Assert.AreEqual(
            "Pinned csi 5.6.0 is unavailable: the Microsoft.Net.Compilers.Toolset net472 host requires Windows.",
            reason);
    }

    [TestMethod]
    public void TryResolveReportsTheExactMissingRestoredPath()
    {
        using var output = new TemporaryOutput();
        var expectedPath = Path.Combine(output.DirectoryPath, "Csi", "csi.exe");
        var toolchain = new CsiToolchain(() => true, _ => throw new InvalidOperationException("Version lookup should not run."));

        var resolved = toolchain.TryResolve(output.DirectoryPath, out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        Assert.AreEqual(
            $"Pinned csi 5.6.0 executable was not found at '{expectedPath}'. Restore the test project to copy the net472 toolset assets.",
            reason);
    }

    [TestMethod]
    public void TryResolveReportsTheRequiredVersionWhenTheRestoredHostDoesNotMatch()
    {
        using var output = new TemporaryOutput(createCsi: true);
        var expectedPath = Path.Combine(output.DirectoryPath, "Csi", "csi.exe");
        var toolchain = new CsiToolchain(() => true, _ => "5.5.0");

        var resolved = toolchain.TryResolve(output.DirectoryPath, out var path, out var reason);

        Assert.IsFalse(resolved);
        Assert.AreEqual(string.Empty, path);
        Assert.AreEqual(
            $"Pinned csi executable '{expectedPath}' must report product version 5.6.0, but reported '5.5.0'.",
            reason);
    }

    [TestMethod]
    [DataRow("5.6.01")]
    [DataRow("5.6.00-preview")]
    [DataRow("5.6.012.3")]
    public void TryResolveRejectsVersionsWhoseNumericComponentOnlySharesTheExpectedPrefix(string productVersion)
    {
        using var output = new TemporaryOutput(createCsi: true);
        var toolchain = new CsiToolchain(() => true, _ => productVersion);

        var resolved = toolchain.TryResolve(output.DirectoryPath, out _, out _);

        Assert.IsFalse(resolved);
    }

    [TestMethod]
    [DataRow("5.6.0")]
    [DataRow("5.6.0-2.26352.2+abcdef")]
    [DataRow("5.6.0 (Roslyn)")]
    public void TryResolveAcceptsTheExactVersionWithAnOptionalNonDigitSuffix(string productVersion)
    {
        using var output = new TemporaryOutput(createCsi: true);
        var expectedPath = Path.Combine(output.DirectoryPath, "Csi", "csi.exe");
        var toolchain = new CsiToolchain(() => true, _ => productVersion);

        var resolved = toolchain.TryResolve(output.DirectoryPath, out var path, out var reason);

        Assert.IsTrue(resolved);
        Assert.AreEqual(expectedPath, path);
        Assert.AreEqual(string.Empty, reason);
    }

    private sealed class TemporaryOutput : IDisposable
    {
        public TemporaryOutput(bool createCsi = false)
        {
            DirectoryPath = Path.Combine(Path.GetTempPath(), $"dotnet-knowledge-csi-{Guid.NewGuid():N}");
            Directory.CreateDirectory(DirectoryPath);
            if (createCsi)
            {
                var csiDirectory = Path.Combine(DirectoryPath, "Csi");
                Directory.CreateDirectory(csiDirectory);
                File.WriteAllBytes(Path.Combine(csiDirectory, "csi.exe"), []);
            }
        }

        public string DirectoryPath { get; }

        public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);
    }
}
