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

    public string GenerationsDirectoryFor(string sourceName) =>
        Path.Combine(Root, ".generations", sourceName);

    public string GenerationDirectoryFor(string sourceName, string generation) =>
        Path.Combine(GenerationsDirectoryFor(sourceName), generation);

    public string RepositoryDirectoryFor(string sourceName, string generation) =>
        Path.Combine(GenerationDirectoryFor(sourceName, generation), "repository");

    public string SupplementsDirectoryFor(string sourceName, string generation) =>
        Path.Combine(GenerationDirectoryFor(sourceName, generation), "supplements");

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
        && state.SchemaVersion == 2
        && !string.IsNullOrWhiteSpace(state.Name)
        && !string.IsNullOrWhiteSpace(state.Repository)
        && !string.IsNullOrWhiteSpace(state.Url)
        && !string.IsNullOrWhiteSpace(state.Ref)
        && state.Commit is { Length: 40 }
        && state.Commit.All(Uri.IsHexDigit)
        && state.FetchedAt != default
        && state.SparsePaths is { Count: > 0 }
        && state.SparsePaths.All(path => !string.IsNullOrWhiteSpace(path))
        && IsSimpleName(state.Generation)
        && state.ApiPackages is not null;

    private static bool IsSimpleName(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && !Path.IsPathRooted(value)
        && !value.Contains(':')
        && !value.Contains(Path.DirectorySeparatorChar)
        && !value.Contains(Path.AltDirectorySeparatorChar)
        && value is not "." and not "..";

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
        var state = TryReadState(sourceName);
        if (state is null)
            return false;

        var directory = RepositoryDirectoryFor(sourceName, state.Generation);
        var hasGitEntry = Directory.Exists(Path.Combine(directory, ".git"))
            || File.Exists(Path.Combine(directory, ".git"));

        return hasGitEntry
            && Directory.Exists(SupplementsDirectoryFor(sourceName, state.Generation));
    }

    private static string ResolveRoot()
    {
        var configured = Environment.GetEnvironmentVariable(CacheEnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(configured))
            return Path.GetFullPath(configured);

        // SpecialFolder.LocalApplicationData maps to %LOCALAPPDATA% on Windows, to
        // $XDG_DATA_HOME (falling back to ~/.local/share) on Linux, and to
        // ~/Library/Application Support on macOS. That is the user *data* directory, not the
        // XDG cache directory, deliberately: a synced pin must survive cache cleaners, because
        // query tools treat an absent source as "call sync_source", never as a silent refetch.
        var baseDirectory = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolderOption.Create);

        if (string.IsNullOrWhiteSpace(baseDirectory))
            baseDirectory = Path.Combine(Path.GetTempPath(), "dotnet-knowledge-cache");

        return Path.Combine(baseDirectory, "dotnet-knowledge", "sources");
    }
}
