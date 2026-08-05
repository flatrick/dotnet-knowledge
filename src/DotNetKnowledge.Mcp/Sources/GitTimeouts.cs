namespace DotNetKnowledge.Mcp.Sources;

/// <summary>
/// The ceiling for each <see cref="GitCommandKind"/>. A timeout that fires on a healthy repository
/// is a worse defect than an unbounded hang, so both values carry deliberate margin.
/// </summary>
internal sealed record GitTimeouts(TimeSpan Quick, TimeSpan Bulk)
{
    /// <summary>
    /// Ten seconds for metadata commands. Fifteen minutes for bulk ones — roughly five times the
    /// measured worst case, a 2 min 57 s clone of dotnet-api-docs at 806 MB.
    /// </summary>
    public static GitTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(15));

    public TimeSpan For(GitCommandKind kind) => kind == GitCommandKind.Quick ? Quick : Bulk;
}
