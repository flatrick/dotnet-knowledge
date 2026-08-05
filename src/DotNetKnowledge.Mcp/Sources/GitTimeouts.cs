namespace DotNetKnowledge.Mcp.Sources;

/// <summary>
/// The ceiling for each <see cref="GitCommandKind"/>. A timeout that fires on a healthy repository
/// is a worse defect than an unbounded hang, so every value carries deliberate margin.
/// </summary>
internal sealed record GitTimeouts(TimeSpan Quick, TimeSpan Walk, TimeSpan Bulk)
{
    /// <summary>
    /// Ten seconds for metadata commands. Two minutes for whole-tree reads. Fifteen minutes for
    /// bulk ones — roughly five times the measured worst case, a 2 min 57 s clone of
    /// dotnet-api-docs at 806 MB.
    /// <para>
    /// The <see cref="Walk"/> tier exists because <c>git status --untracked-files=all</c> over a
    /// 13,485-file checkout measured 0.101 s warm, 0.54 s cold on a fast disk, and 3.34 s on a
    /// slower machine — then exceeded ten seconds on that same machine when real-time anti-virus
    /// scanned the tree for the first time. Its cost is a property of the checkout and of the host,
    /// not of the command, so it needs a ceiling sized for the tree rather than for a metadata read.
    /// </para>
    /// </summary>
    public static GitTimeouts Default { get; } =
        new(TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(15));

    public TimeSpan For(GitCommandKind kind) => kind switch
    {
        GitCommandKind.Quick => Quick,
        GitCommandKind.Walk => Walk,
        _ => Bulk,
    };
}
