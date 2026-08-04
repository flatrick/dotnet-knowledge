using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DotNetKnowledge.CSharpScriptHost;

internal sealed class RestrictedSourceResolver : SourceReferenceResolver
{
    private readonly SourceFileResolver _resolver;
    private readonly string _root;
    private readonly StringComparer _pathComparer;

    public RestrictedSourceResolver(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);

        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        _pathComparer = OperatingSystem.IsWindows()
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;
        _resolver = new SourceFileResolver(ImmutableArray<string>.Empty, _root);
    }

    public override string? NormalizePath(string path, string? baseFilePath) =>
        Restrict(_resolver.NormalizePath(path, baseFilePath));

    public override string? ResolveReference(string path, string? baseFilePath) =>
        Restrict(_resolver.ResolveReference(path, baseFilePath));

    public override Stream OpenRead(string resolvedPath)
    {
        var restrictedPath = Restrict(resolvedPath)
            ?? throw new FileNotFoundException("Source reference is outside the scenario directory.", resolvedPath);
        return _resolver.OpenRead(restrictedPath);
    }

    public override bool Equals(object? other) =>
        other is RestrictedSourceResolver resolver && _pathComparer.Equals(_root, resolver._root);

    public override int GetHashCode() => _pathComparer.GetHashCode(_root);

    private string? Restrict(string? candidate)
    {
        if (candidate is null)
        {
            return null;
        }

        var canonicalCandidate = Path.GetFullPath(candidate);
        var relative = Path.GetRelativePath(_root, canonicalCandidate);
        return Path.IsPathRooted(relative) ||
            relative == ".." ||
            relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                ? null
                : canonicalCandidate;
    }
}
