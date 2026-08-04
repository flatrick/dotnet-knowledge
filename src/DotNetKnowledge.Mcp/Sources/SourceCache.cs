using System.Text.Json;

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

    public SourceCache()
        : this(ResolveRoot())
    {
    }

    public SourceCache(string root) => Root = Path.GetFullPath(root);

    public string Root { get; }

    public string DirectoryFor(string sourceName) => Path.Combine(Root, sourceName);

    public string StatePathFor(string sourceName) =>
        Path.Combine(Root, ".state", sourceName + ".json");

    public SourceSyncState? TryReadState(string sourceName)
    {
        var path = StatePathFor(sourceName);
        if (!File.Exists(path))
            return null;

        try
        {
            var state = JsonSerializer.Deserialize<SourceSyncState>(File.ReadAllText(path));
            return IsComplete(state) ? state : null;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or IOException)
        {
            return null;
        }
    }

    private static bool IsComplete(SourceSyncState? state) =>
        state is not null
        && state.SchemaVersion == 1
        && !string.IsNullOrWhiteSpace(state.Name)
        && !string.IsNullOrWhiteSpace(state.Repository)
        && !string.IsNullOrWhiteSpace(state.Url)
        && !string.IsNullOrWhiteSpace(state.Ref)
        && state.Commit is { Length: 40 }
        && state.Commit.All(Uri.IsHexDigit)
        && state.FetchedAt != default
        && state.SparsePaths is { Count: > 0 }
        && state.SparsePaths.All(path => !string.IsNullOrWhiteSpace(path));

    public void WriteState(string sourceName, SourceSyncState state)
    {
        var directory = Path.GetDirectoryName(StatePathFor(sourceName))!;
        Directory.CreateDirectory(directory);
        var destination = StatePathFor(sourceName);
        var temporary = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(state));
            File.Move(temporary, destination, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    /// <summary>
    /// True when the source has both a Git repository and structurally complete synchronization
    /// metadata. Full commit, configuration, and worktree validation is performed by
    /// <see cref="SourceSynchronizer.TryGetCurrentStateAsync"/>.
    /// </summary>
    public bool IsSynced(string sourceName)
    {
        var directory = DirectoryFor(sourceName);
        var hasGitEntry = Directory.Exists(Path.Combine(directory, ".git"))
            || File.Exists(Path.Combine(directory, ".git"));

        return hasGitEntry && TryReadState(sourceName) is not null;
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
