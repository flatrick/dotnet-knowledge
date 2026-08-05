namespace DotNetKnowledge.Mcp.Sources;

/// <summary>How long a git command is expected to take, which selects its timeout tier.</summary>
internal enum GitCommandKind
{
    /// <summary>Reads metadata and touches few files: rev-parse, config, status.</summary>
    Quick,

    /// <summary>Transfers or writes the whole working tree: clone, fetch, sparse-checkout, checkout.</summary>
    Bulk,
}
