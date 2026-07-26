using DotNetKnowledge.Corpus.Tests.Execution;

namespace DotNetKnowledge.Corpus.Tests.Probes;

internal sealed class ProbeResult : IDisposable
{
    private readonly string ownedRoot;
    private int disposed;

    public ProbeResult(
        ProcessResult process,
        IReadOnlyList<string> diagnostics,
        string ownedRoot,
        string projectDirectory,
        string globalJsonPath,
        string projectPath)
    {
        Process = process;
        Diagnostics = diagnostics;
        this.ownedRoot = ownedRoot;
        ProjectDirectory = projectDirectory;
        GlobalJsonPath = globalJsonPath;
        ProjectPath = projectPath;
    }

    public ProcessResult Process { get; }

    public IReadOnlyList<string> Diagnostics { get; }

    public string ProjectDirectory { get; }

    public string GlobalJsonPath { get; }

    public string ProjectPath { get; }

    public string CompleteOutput =>
        $"Standard output:{Environment.NewLine}{Process.StandardOutput}{Environment.NewLine}" +
        $"Standard error:{Environment.NewLine}{Process.StandardError}";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 0)
        {
            DeleteOwnedDirectory(ownedRoot, ProjectDirectory);
        }
    }

    internal static void DeleteOwnedDirectory(string ownedRoot, string probeDirectory)
    {
        var resolvedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(ownedRoot));
        var resolvedProbe = Path.TrimEndingDirectorySeparator(Path.GetFullPath(probeDirectory));
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        var ownedPrefix = resolvedRoot + Path.DirectorySeparatorChar;

        if (!resolvedProbe.StartsWith(ownedPrefix, comparison))
        {
            throw new InvalidOperationException(
                $"Refusing to delete probe directory outside the owned temporary root: {resolvedProbe}");
        }

        if (Directory.Exists(resolvedProbe))
        {
            Directory.Delete(resolvedProbe, recursive: true);
        }
    }
}
