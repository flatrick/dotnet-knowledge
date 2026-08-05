namespace DotNetKnowledge.Mcp.Sources;

/// <summary>How long a git command is expected to take, which selects its timeout tier.</summary>
internal enum GitCommandKind
{
    /// <summary>Reads metadata and touches few files: rev-parse, config.</summary>
    Quick,

    /// <summary>
    /// Reads the whole working tree without transferring anything: <c>status</c>. Its cost scales
    /// with the checkout rather than with the network, so it does not belong on the metadata tier
    /// even though it writes nothing.
    /// </summary>
    Walk,

    /// <summary>Transfers or writes the whole working tree: clone, fetch, sparse-checkout, checkout.</summary>
    Bulk,
}
