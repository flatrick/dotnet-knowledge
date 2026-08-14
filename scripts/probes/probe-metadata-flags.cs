#!/usr/bin/env dotnet
#:property Nullable=enable
#:property JsonSerializerIsReflectionEnabledByDefault=true
#:project ../../src/DotNetKnowledge.Mcp/DotNetKnowledge.Mcp.csproj

// Not an MCP server. It prints, for every member of an assembly, the metadata flags the compiler
// emitted beside the signature the SHIPPED MetadataApiReader renders from them.
//
// The question it answers: does the reader's interpretation of a metadata shape match what the C#
// compiler actually emits for known source? Pointed at metadata-flag-truth/, whose every
// declaration records the modifier it was written with, the three columns can be compared directly
// and any disagreement is a defect.
//
// This exists because the reader twice rendered Final|Virtual|NewSlot as 'sealed override'. That
// flag set is an implicit interface implementation and carries no modifier at all; 'sealed override'
// is Final|Virtual WITHOUT NewSlot. Both times the mistake came from reasoning about what compilers
// ought to emit instead of compiling something and looking. docs/gotchas.md carries the rule.
//
//   dotnet build scripts/probes/metadata-flag-truth/metadata-flag-truth.csproj
//   dotnet run --file scripts/probes/probe-metadata-flags.cs
//   dotnet run --file scripts/probes/probe-metadata-flags.cs -- --assembly <path-to-dll>
//
// It never downloads and never loads an assembly for execution.

using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

const MethodAttributes SemanticMask = MethodAttributes.Static
    | MethodAttributes.Abstract
    | MethodAttributes.Virtual
    | MethodAttributes.Final
    | MethodAttributes.NewSlot
    | MethodAttributes.PinvokeImpl;

var assemblyPath = Path.Combine(
    "scripts", "probes", "metadata-flag-truth", "bin", "Debug", "net10.0", "metadata-flag-truth.dll");
for (var index = 0; index < args.Length - 1; index++)
{
    if (string.Equals(args[index], "--assembly", StringComparison.Ordinal))
        assemblyPath = args[index + 1];
}

if (!File.Exists(assemblyPath))
{
    Console.Error.WriteLine($"No assembly at '{assemblyPath}'.");
    Console.Error.WriteLine(
        "Build the fixture first: dotnet build scripts/probes/metadata-flag-truth/metadata-flag-truth.csproj");
    return 2;
}

// The rendered signature comes from the shipped reader, so this reports what the server would say.
using var corpusStream = File.OpenRead(assemblyPath);
var corpus = MetadataApiReader.Read(corpusStream);
var rendered = corpus.Types.ToDictionary(
    type => type.FullName,
    type => type.Members.ToLookup(member => member.Name, StringComparer.Ordinal),
    StringComparer.Ordinal);

Console.WriteLine($"assembly: {assemblyPath}");
Console.WriteLine($"skipped : {corpus.Skipped.Count}");
foreach (var declaration in corpus.Skipped)
    Console.WriteLine($"    SKIP {declaration.Kind} {declaration.DeclaringType}.{declaration.Name}: {declaration.Reason}");

Console.WriteLine();

using var stream = File.OpenRead(assemblyPath);
using var peReader = new PEReader(stream);
var reader = peReader.GetMetadataReader();

foreach (var typeHandle in reader.TypeDefinitions)
{
    var definition = reader.GetTypeDefinition(typeHandle);
    var name = reader.GetString(definition.Name);
    if (name == "<Module>")
        continue;

    var namespaceName = reader.GetString(definition.Namespace);
    var fullName = string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
    Console.WriteLine(fullName);

    // Accessors are reported under their property or event, because that is the declaration whose
    // modifiers they decide, and reporting them twice obscures which renderer produced what.
    var accessors = new Dictionary<MethodDefinitionHandle, string>();
    foreach (var propertyHandle in definition.GetProperties())
    {
        var property = reader.GetPropertyDefinition(propertyHandle);
        var propertyAccessors = property.GetAccessors();
        foreach (var handle in new[] { propertyAccessors.Getter, propertyAccessors.Setter })
        {
            if (!handle.IsNil)
                accessors[handle] = reader.GetString(property.Name);
        }
    }

    foreach (var eventHandle in definition.GetEvents())
    {
        var declaration = reader.GetEventDefinition(eventHandle);
        var eventAccessors = declaration.GetAccessors();
        foreach (var handle in new[] { eventAccessors.Adder, eventAccessors.Remover })
        {
            if (!handle.IsNil)
                accessors[handle] = reader.GetString(declaration.Name);
        }
    }

    foreach (var methodHandle in definition.GetMethods())
    {
        var method = reader.GetMethodDefinition(methodHandle);
        var methodName = reader.GetString(method.Name);
        if (methodName is ".ctor" or ".cctor")
            continue;

        var owner = accessors.TryGetValue(methodHandle, out var declaringMember) ? declaringMember : methodName;
        var access = method.Attributes & MethodAttributes.MemberAccessMask;
        var semantics = method.Attributes & SemanticMask;
        // Overloads share a name, so every signature under it is printed rather than the first:
        // silently showing one overload's rendering against another's flags is the exact class of
        // mistake this probe exists to catch.
        var signatures = rendered.TryGetValue(fullName, out var members)
            ? members[owner].Select(item => item.Signature).ToArray()
            : [];
        Console.WriteLine($"    {methodName,-46} {access,-12} {(semantics == 0 ? "(none)" : semantics.ToString())}");
        if (signatures.Length == 0)
            Console.WriteLine("        renders: (not in corpus)");
        else if (signatures.Length == 1)
            Console.WriteLine($"        renders: {signatures[0]}");
        else
        {
            Console.WriteLine($"        renders ({signatures.Length} overloads share this name):");
            foreach (var signature in signatures.OrderBy(item => item, StringComparer.Ordinal))
                Console.WriteLine($"            {signature}");
        }
    }

    Console.WriteLine();
}

return 0;
