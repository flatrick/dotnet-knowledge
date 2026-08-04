using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace DotNetKnowledge.CSharpScriptHost;

internal sealed class BclMetadataResolver : MetadataReferenceResolver
{
    private const string AllowedAssemblyName = "System.Xml.Linq";

    public override ImmutableArray<PortableExecutableReference> ResolveReference(
        string reference,
        string? baseFilePath,
        MetadataReferenceProperties properties)
    {
        _ = baseFilePath;
        return string.Equals(reference, AllowedAssemblyName, StringComparison.Ordinal)
            ? [MetadataReference.CreateFromFile(typeof(System.Xml.Linq.XDocument).Assembly.Location, properties)]
            : [];
    }

    public override bool Equals(object? other) => other is BclMetadataResolver;

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(AllowedAssemblyName);
}
