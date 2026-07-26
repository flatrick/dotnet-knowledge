namespace DotNetKnowledge.Mcp.Sources;

/// <summary>
/// Where fetched upstream sources live on disk, and what state each one is in.
/// </summary>
/// <remarks>
/// The cache deliberately sits outside any repository, in a per-user directory, so one download
/// serves every repository and every git worktree on the machine. That is the concrete advantage
/// over the git-submodule arrangement this replaces, where content was per-clone and each new
/// worktree needed its own <c>git submodule update --init</c>.
/// <para>
/// Override with <c>DOTNET_KNOWLEDGE_CACHE</c> when a caller needs the sources somewhere specific.
/// </para>
/// </remarks>
public sealed class SourceCache
{
    public const string CacheEnvironmentVariable = "DOTNET_KNOWLEDGE_CACHE";

    public string Root { get; } = ResolveRoot();

    public string DirectoryFor(string sourceName) => Path.Combine(Root, sourceName);

    /// <summary>
    /// True when the source has been cloned. A directory containing a <c>.git</c> entry is the
    /// signal; an empty or partially-created directory counts as not synced, because a half-fetched
    /// source answers queries with plausible-looking absences rather than errors.
    /// </summary>
    public bool IsSynced(string sourceName)
    {
        var directory = DirectoryFor(sourceName);
        return Directory.Exists(Path.Combine(directory, ".git"))
            || File.Exists(Path.Combine(directory, ".git"));
    }

    private static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable(CacheEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        // SpecialFolder.LocalApplicationData maps to %LOCALAPPDATA% on Windows and to
        // $XDG_CACHE_HOME (falling back to ~/.local/share or ~/.cache) elsewhere.
        var baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = Path.Combine(Path.GetTempPath(), "dotnet-knowledge-cache");

        return Path.Combine(baseDirectory, "dotnet-knowledge", "sources");
    }
}
