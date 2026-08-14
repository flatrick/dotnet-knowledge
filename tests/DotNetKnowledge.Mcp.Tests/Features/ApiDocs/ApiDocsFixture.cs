using System.Diagnostics;
using System.Text.Json;
using DotNetKnowledge.Mcp.Features.ApiDocs;
using DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;
using DotNetKnowledge.Mcp.Sources;

namespace DotNetKnowledge.Mcp.Tests.Features.ApiDocs;

/// <summary>
/// The synced-source fixtures both API-docs suites read from. A query service can only be built
/// over a real git checkout, so every test here pays for one; sharing the construction keeps the
/// two suites reading the same documents.
/// </summary>
internal static class ApiDocsFixture
{
    private static readonly DateTimeOffset PackageFetchedAt =
        new(2026, 8, 12, 10, 11, 12, TimeSpan.Zero);

    public const string WidgetXml = """
        <Type Name="Widget" FullName="System.Widget">
          <TypeSignature Language="DocId" Value="T:System.Widget" />
          <Members>
            <Member MemberName="Create">
              <MemberSignature Language="C#" Value="public static System.Widget Create(string name);" />
              <MemberSignature Language="DocId" Value="M:System.Widget.Create(System.String)" />
              <Parameters><Parameter Name="name" Type="System.String" /></Parameters>
              <ReturnValue><ReturnType>System.Widget</ReturnType></ReturnValue>
              <Docs>
                <summary>Creates a widget.</summary>
                <param name="name">The widget name.</param>
                <returns>The new widget.</returns>
                <remarks>Names are case-sensitive.</remarks>
              </Docs>
            </Member>
            <Member MemberName="Describe">
              <MemberSignature Language="C#" Value="public string Describe(string name);" />
              <MemberSignature Language="DocId" Value="M:System.Widget.Describe(System.String)" />
              <Parameters><Parameter Name="name" Type="System.String" /></Parameters>
              <Docs>
                <summary>Renders the widget as a <see cref="T:System.String" />.</summary>
                <param name="name">The label applied to <paramref name="name" />.</param>
                <returns>A <see cref="T:System.String" />, or <see langword="null" />.</returns>
                <remarks>See also <see cref="M:System.Widget.Create(System.String)" />.</remarks>
              </Docs>
            </Member>
            <Member MemberName="Convert&lt;TResult&gt;">
              <MemberSignature Language="C#" Value="public TResult Convert&lt;TResult&gt;();" />
              <MemberSignature Language="DocId" Value="M:System.Widget.Convert``1" />
              <Docs><summary>Converts to one type.</summary></Docs>
            </Member>
            <Member MemberName="Convert&lt;TResult,TState&gt;">
              <MemberSignature Language="C#" Value="public TResult Convert&lt;TResult,TState&gt;(TState state);" />
              <MemberSignature Language="DocId" Value="M:System.Widget.Convert``2(``1)" />
              <Docs><summary>Converts with state.</summary></Docs>
            </Member>
          </Members>
        </Type>
        """;

    public const string WidgetKitXml = """
        <Type Name="WidgetKit" FullName="System.WidgetKit">
          <TypeSignature Language="DocId" Value="T:System.WidgetKit" />
          <Base>
            <BaseTypeName>System.WidgetBase</BaseTypeName>
          </Base>
          <Interfaces>
            <Interface>
              <InterfaceName>System.IWidget</InterfaceName>
            </Interface>
          </Interfaces>
          <Members>
            <Member MemberName="Combine">
              <MemberSignature Language="C#" Value="public static string Combine(string[] parts);" />
              <MemberSignature Language="DocId" Value="M:System.WidgetKit.Combine(System.String[])" />
              <Parameters><Parameter Name="parts" Type="System.String[]" /></Parameters>
              <ReturnValue><ReturnType>System.String</ReturnType></ReturnValue>
            </Member>
            <Member MemberName="JoinAll">
              <MemberSignature Language="C#" Value="public static string JoinAll(System.Collections.Generic.IEnumerable&lt;string&gt; parts);" />
              <MemberSignature Language="DocId" Value="M:System.WidgetKit.JoinAll(System.Collections.Generic.IEnumerable{System.String})" />
              <Parameters><Parameter Name="parts" Type="System.Collections.Generic.IEnumerable&lt;System.String&gt;" /></Parameters>
            </Member>
            <Member MemberName="Borrow">
              <MemberSignature Language="C#" Value="public static ref string Borrow();" />
              <MemberSignature Language="DocId" Value="M:System.WidgetKit.Borrow" />
              <ReturnValue><ReturnType>System.String&amp;</ReturnType></ReturnValue>
            </Member>
            <Member MemberName="CompareWith">
              <MemberSignature Language="C#" Value="public static int CompareWith(System.StringComparer comparer);" />
              <MemberSignature Language="DocId" Value="M:System.WidgetKit.CompareWith(System.StringComparer)" />
              <Parameters><Parameter Name="comparer" Type="System.StringComparer" /></Parameters>
            </Member>
          </Members>
        </Type>
        """;

    /// <summary>
    /// A generic type carrying the two structural uses that live outside <c>Base</c>,
    /// <c>Interfaces</c>, <c>Parameters</c> and <c>ReturnValue</c>: generic constraints and
    /// attribute applications, each at both type and member level. The member's attribute names a
    /// type in its arguments rather than as the attribute itself, which is the distinction
    /// <c>isExact</c> reports for an attribute hit.
    /// </summary>
    public const string WidgetPolicyXml = """
        <Type Name="WidgetPolicy&lt;TWidget&gt;" FullName="System.WidgetPolicy&lt;TWidget&gt;">
          <TypeSignature Language="DocId" Value="T:System.WidgetPolicy`1" />
          <TypeParameters>
            <TypeParameter Name="TWidget">
              <Constraints>
                <ParameterAttribute>DefaultConstructorConstraint</ParameterAttribute>
                <BaseTypeName>System.WidgetPolicyBase</BaseTypeName>
                <InterfaceName>System.IWidgetPolicy</InterfaceName>
              </Constraints>
            </TypeParameter>
          </TypeParameters>
          <Attributes>
            <Attribute>
              <AttributeName Language="C#">[System.WidgetMarker]</AttributeName>
              <AttributeName Language="F#">[&lt;System.WidgetMarker&gt;]</AttributeName>
            </Attribute>
          </Attributes>
          <Members>
            <Member MemberName="Adapt&lt;TState&gt;">
              <MemberSignature Language="C#" Value="public static void Adapt&lt;TState&gt;(TState state);" />
              <MemberSignature Language="DocId" Value="M:System.WidgetPolicy`1.Adapt``1(``0)" />
              <TypeParameters>
                <TypeParameter Name="TState">
                  <Constraints>
                    <BaseTypeName>System.WidgetState</BaseTypeName>
                  </Constraints>
                </TypeParameter>
              </TypeParameters>
              <Attributes>
                <Attribute>
                  <AttributeName Language="C#">[System.WidgetMarker(typeof(System.String))]</AttributeName>
                  <AttributeName Language="F#">[&lt;System.WidgetMarker(typeof(System.String))&gt;]</AttributeName>
                </Attribute>
              </Attributes>
              <Parameters><Parameter Name="state" Type="TState" /></Parameters>
            </Member>
          </Members>
        </Type>
        """;

    /// <summary>
    /// The de-suffixed sibling of <see cref="WidgetTraitAttributeXml"/>: a class that shares the
    /// name C# spells the attribute with, which is what makes an application of the attribute
    /// ambiguous to read.
    /// </summary>
    private const string WidgetTraitXml = """
        <Type Name="WidgetTrait" FullName="System.WidgetTrait">
          <TypeSignature Language="DocId" Value="T:System.WidgetTrait" />
        </Type>
        """;

    /// <summary>
    /// The colliding half: applied as <c>[System.WidgetTrait]</c>, while a class of exactly that
    /// name also exists.
    /// </summary>
    private const string WidgetTraitAttributeXml = """
        <Type Name="WidgetTraitAttribute" FullName="System.WidgetTraitAttribute">
          <TypeSignature Language="DocId" Value="T:System.WidgetTraitAttribute" />
          <Base>
            <BaseTypeName>System.Attribute</BaseTypeName>
          </Base>
        </Type>
        """;

    /// <summary>
    /// The non-colliding half: applied as <c>[System.WidgetSeal]</c>, and nothing else is named
    /// <c>System.WidgetSeal</c>, so the short form has one reading.
    /// </summary>
    private const string WidgetSealAttributeXml = """
        <Type Name="WidgetSealAttribute" FullName="System.WidgetSealAttribute">
          <TypeSignature Language="DocId" Value="T:System.WidgetSealAttribute" />
          <Base>
            <BaseTypeName>System.Attribute</BaseTypeName>
          </Base>
        </Type>
        """;

    /// <summary>
    /// A type decorated with both attributes, and taking the colliding class as a parameter, so a
    /// query for that class has structural hits of its own alongside the applications that are not
    /// its.
    /// </summary>
    private const string TraitedWidgetXml = """
        <Type Name="TraitedWidget" FullName="System.TraitedWidget">
          <TypeSignature Language="DocId" Value="T:System.TraitedWidget" />
          <Attributes>
            <Attribute>
              <AttributeName Language="C#">[System.WidgetTrait]</AttributeName>
              <AttributeName Language="F#">[&lt;System.WidgetTrait&gt;]</AttributeName>
            </Attribute>
            <Attribute>
              <AttributeName Language="C#">[System.WidgetSeal]</AttributeName>
              <AttributeName Language="F#">[&lt;System.WidgetSeal&gt;]</AttributeName>
            </Attribute>
          </Attributes>
          <Members>
            <Member MemberName="Apply">
              <MemberSignature Language="C#" Value="public void Apply(System.WidgetTrait trait);" />
              <MemberSignature Language="DocId" Value="M:System.TraitedWidget.Apply(System.WidgetTrait)" />
              <Parameters><Parameter Name="trait" Type="System.WidgetTrait" /></Parameters>
            </Member>
          </Members>
        </Type>
        """;

    /// <summary>
    /// A type one namespace below <c>System</c>, so a pattern naming <c>System</c> has both a
    /// direct member and a descendant to tell apart. Its name shares no substring with the types
    /// in <c>System</c>, and <c>System.Widgets</c> is a whole segment that <c>Widget</c> must not
    /// match.
    /// </summary>
    public const string GadgetXml = """
        <Type Name="Gadget" FullName="System.Widgets.Gadget">
          <TypeSignature Language="DocId" Value="T:System.Widgets.Gadget" />
        </Type>
        """;

    /// <summary>
    /// A type whose fully-qualified name is <c>System.Widget.Create</c>, so that one symbol string
    /// resolves as a type in the source holding this file and as <c>System.Widget</c>'s member
    /// <c>Create</c> in the source holding <see cref="WidgetXml"/>.
    /// </summary>
    private const string CreateTypeXml = """
        <Type Name="Create" FullName="System.Widget.Create">
          <TypeSignature Language="DocId" Value="T:System.Widget.Create" />
          <Members>
            <Member MemberName="Invoke">
              <MemberSignature Language="C#" Value="public void Invoke();" />
              <MemberSignature Language="DocId" Value="M:System.Widget.Create.Invoke" />
              <Docs><summary>Runs the created widget.</summary></Docs>
            </Member>
          </Members>
        </Type>
        """;

    public static async Task<ApiDocsQueryService> CreateWidgetServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");

        // A second type whose name also contains "Widget" so a search for that pattern has more
        // than one match. lookup_api's exact-name resolution never matches this file, so it is
        // invisible to every lookup test that shares this fixture; it exists so
        // search_api("Widget", limit: 1) has a real page boundary to hand a cursor across, and it
        // carries the structural shapes find_api_references reads — a base type, an interface, and
        // parameters whose types are compound rather than bare.
        var pin = await CreateRepositoryAsync(
            repository,
            [
                ("xml/System/Widget.xml", WidgetXml),
                ("xml/System/WidgetKit.xml", WidgetKitXml),
                ("xml/System/WidgetPolicy`1.xml", WidgetPolicyXml),
                ("xml/System.Widgets/Gadget.xml", GadgetXml),
            ]);
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        return await CreateServiceAsync(root, catalogPath, ["dotnet-api-docs"]);
    }

    public static async Task<RepositoryApiDocsBackend> CreateWidgetRepositoryBackendAsync(string root)
        => await CreateRepositoryBackendAsync(
            root,
            [("xml/System/Widget.xml", WidgetXml)]);

    public static async Task<RepositoryApiDocsBackend> CreateRepositoryBackendAsync(
        string root,
        IReadOnlyList<(string RelativePath, string Contents)> files)
    {
        var repository = Path.Combine(root, "origin");
        var pin = await CreateRepositoryAsync(repository, files);
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        await synchronizer.SyncAsync("dotnet-api-docs", requestedRef: null, CancellationToken.None);
        return await synchronizer.ReadCurrentSourceAsync(
            "dotnet-api-docs",
            snapshot => new RepositoryApiDocsBackend("dotnet-api-docs", snapshot),
            CancellationToken.None);
    }

    public static async Task<PackageApiDocsBackend> CreatePackageBackendAsync(
        string root,
        string framework = "net10.0",
        bool reverseFixtureInsertion = false)
    {
        var supplements = Path.Combine(root, "supplements");
        var corpusDirectory = Path.Combine(supplements, "fixture-package");
        Directory.CreateDirectory(corpusDirectory);
        var definition = PackageDefinition();
        await PackageApiCorpusStore.WriteAsync(
            Path.Combine(corpusDirectory, "net10.0.json"),
            definition,
            "net10.0",
            PackageApiCorpusBuilder.NormalizeForStorage(
                PerturbFixtureCorpus(PackageCorpus(), reverseFixtureInsertion)),
            CancellationToken.None);
        await PackageApiCorpusStore.WriteAsync(
            Path.Combine(corpusDirectory, "net8.0.json"),
            definition,
            "net8.0",
            PackageApiCorpusBuilder.NormalizeForStorage(PerturbFixtureCorpus(new ApiCorpus(2,
            [
                new ApiCorpusType(
                    "T:System.LegacyWidget", "LegacyWidget", "System.LegacyWidget", null,
                    [], [], [], EmptyPackageDocumentation(), []),
                new ApiCorpusType(
                    "T:System.WidgetTrait", "WidgetTrait", "System.WidgetTrait", null,
                    [], [], [], EmptyPackageDocumentation(), []),
                new ApiCorpusType(
                    "T:System.LegacyTraitedWidget", "LegacyTraitedWidget", "System.LegacyTraitedWidget", null,
                    [], [],
                    [new ApiAttributeUse("[System.WidgetTrait]", "System.WidgetTraitAttribute", [])],
                    EmptyPackageDocumentation(), []),
            ]), reverseFixtureInsertion)),
            CancellationToken.None);

        var packageState = PackageState();
        var snapshot = PackageSnapshot(root, definition, packageState);
        return new PackageApiDocsBackend(snapshot, definition.PackageId, framework);
    }

    public static SourceSnapshot PackageSnapshot(
        string root,
        ApiPackageDefinition? definition = null,
        ApiPackageSyncState? packageState = null,
        string sourceRef = "pinned",
        IReadOnlyList<ApiPackageDefinition>? configuredPackages = null,
        IReadOnlyList<ApiPackageSyncState>? synchronizedPackages = null)
    {
        definition ??= PackageDefinition();
        packageState ??= PackageState();
        const string sourceCommit = "0123456789abcdef0123456789abcdef01234567";
        var sourceDefinition = new SourceDefinition(
            "test/roslyn-api-docs", "https://example.invalid/roslyn", sourceCommit,
            "main", ["dotnet/xml"], "Test API docs.", configuredPackages ?? [definition]);
        var sourceState = new SourceSyncState(
            SourceSyncState.CurrentSchemaVersion,
            "roslyn-api-docs", sourceDefinition.Repository, sourceDefinition.Url, sourceRef,
            sourceCommit, PackageFetchedAt, sourceDefinition.Sparse, "generation-1",
            synchronizedPackages ?? [packageState]);
        var cache = new SourceCache(Path.Combine(root, ".fixture-source-cache"));
        cache.WriteState(sourceState.Name, sourceState);
        sourceState = cache.TryReadState(sourceState.Name)
            ?? throw new InvalidOperationException("The package fixture source state is not production-valid.");
        return new SourceSnapshot(
            sourceDefinition,
            sourceState,
            root,
            Path.Combine(root, "repository"),
            Path.Combine(root, "supplements"));
    }

    public static ApiPackageDefinition PackageDefinition() => new(
        "Fixture.Package",
        "Fixture.Package",
        "https://feed.test/v3/index.json",
        "1.2.3",
        Convert.ToBase64String(Enumerable.Repeat((byte)0x5a, 64).ToArray()),
        "net10.0");

    public static ApiPackageSyncState PackageState(string corpusDirectory = "fixture-package") => new(
        "Fixture.Package",
        "Fixture.Package",
        "1.2.3",
        Convert.ToBase64String(Enumerable.Repeat((byte)0x5a, 64).ToArray()),
        "https://feed.test/v3/index.json",
        PackageFetchedAt,
        "net10.0",
        ["net10.0", "net8.0"],
        corpusDirectory);

    public static async Task WritePackageCorpusAsync(
        string root,
        ApiPackageSyncState state,
        string framework)
    {
        var directory = Path.Combine(root, "supplements", state.CorpusDirectory);
        Directory.CreateDirectory(directory);
        await PackageApiCorpusStore.WriteAsync(
            Path.Combine(directory, framework + ".json"),
            PackageDefinition().WithSynchronizedContent(state.Version, state.Sha512),
            framework,
            PackageApiCorpusBuilder.NormalizeForStorage(PackageCorpus()),
            CancellationToken.None);
    }

    private static ApiCorpus PackageCorpus()
    {
        var longSummary = "Needle " + new string('x', 340);
        return new ApiCorpus(2,
        [
            new ApiCorpusType(
                "T:System.StringDerived", "StringDerived", "System.StringDerived",
                new ApiTypeUse(null, "string", ["System.String"]),
                [new ApiTypeUse(
                    null,
                    "System.Collections.Generic.IEnumerable<string>",
                    ["System.Collections.Generic.IEnumerable", "System.String"])],
                [new ApiTypeUse("T", "string", ["System.String"])],
                [new ApiAttributeUse(
                    "[System.WidgetMarker(typeof(string))]",
                    "System.WidgetMarkerAttribute",
                    ["System.String"]),
                 new ApiAttributeUse(
                     "[System.WidgetMarker(typeof(string))]",
                     "System.WidgetMarkerAttribute",
                     ["System.String"])],
                new ApiDocumentation(
                    longSummary,
                    [],
                    [new ApiNamedDocumentation("T", "Needle type parameter.")],
                    null,
                    "Needle value.",
                    "Needle remarks.",
                    [new ApiNamedDocumentation("T:System.Exception", "Needle exception.")]),
                [
                    new ApiCorpusMember(
                        "M:System.StringDerived.Transform(System.String[])",
                        "Transform",
                        "Method",
                        "public ref string Transform(string[] values);",
                        [new ApiTypeUse("values", "string[]", ["System.String"])],
                        new ApiTypeUse(null, "ref string", ["System.String"]),
                        [],
                        [],
                        new ApiDocumentation(
                            "Transforms a Needle value.",
                            [new ApiNamedDocumentation("values", "Needle values.")],
                            [],
                            "Returns the Needle result.",
                            null,
                            null,
                            [])),
                ]),
            new ApiCorpusType(
                "T:System.Box`1", "Box<T>", "System.Box<T>", null,
                [], [], [], EmptyPackageDocumentation(), []),
            new ApiCorpusType(
                "T:Fixtures.Outer`1", "Outer<T>", "Fixtures.Outer<T>", null,
                [], [], [], EmptyPackageDocumentation(), []),
            new ApiCorpusType(
                "T:Fixtures.Outer`1.Inner`1", "Inner<U>", "Fixtures.Outer<T>.Inner<U>", null,
                [], [], [], EmptyPackageDocumentation(), []),
            new ApiCorpusType(
                "T:Fixtures.HierarchyConsumer", "HierarchyConsumer", "Fixtures.HierarchyConsumer",
                new ApiTypeUse(
                    null,
                    "Fixtures.NullableBase<(string, System.Uri?)>",
                    ["Fixtures.NullableBase", "System.String", "System.Uri", "System.ValueTuple"]),
                [new ApiTypeUse(
                    null,
                    "Fixtures.IHierarchy<Fixtures.Outer<string>.Inner<System.Uri?>>",
                    ["Fixtures.IHierarchy", "Fixtures.Outer.Inner", "System.String", "System.Uri"])],
                [], [], EmptyPackageDocumentation(),
                [
                    new ApiCorpusMember(
                        "M:Fixtures.HierarchyConsumer.Use(System.Int32*,Fixtures.Outer{System.String}.Inner{System.Uri})",
                        "Use",
                        "Method",
                        "public void Use(int* pointer, Fixtures.Outer<string>.Inner<System.Uri?> nested);",
                        [
                            new ApiTypeUse("pointer", "int*", ["System.Int32"]),
                            new ApiTypeUse(
                                "nested",
                                "Fixtures.Outer<string>.Inner<System.Uri?>",
                                ["Fixtures.Outer.Inner", "System.String", "System.Uri"]),
                        ],
                        null, [], [], EmptyPackageDocumentation()),
                ]),
            new ApiCorpusType(
                "T:System.Widget", "Widget", "System.Widget", null, [], [], [],
                EmptyPackageDocumentation(),
                [
                    new ApiCorpusMember(
                        "M:System.Widget.Create(System.String)",
                        "Create",
                        "Method",
                        "public static System.Widget Create(string name);",
                        [new ApiTypeUse("name", "string", ["System.String"])],
                        new ApiTypeUse(null, "System.Widget", ["System.Widget"]),
                        [],
                        [],
                        new ApiDocumentation(
                            "Creates a widget.",
                            [new ApiNamedDocumentation("name", "The widget name.")],
                            [],
                            "The new widget.",
                            null,
                            "Names are case-sensitive.",
                            [])),
                    new ApiCorpusMember(
                        "M:System.Widget.Convert``1",
                        "Convert<TResult>",
                        "Method",
                        "public TResult Convert<TResult>();",
                        [], null, [], [], EmptyPackageDocumentation()),
                ]),
            new ApiCorpusType(
                "T:System.Widgets.Gadget", "Gadget", "System.Widgets.Gadget", null,
                [], [], [], EmptyPackageDocumentation(), []),
            new ApiCorpusType(
                "T:System.WidgetTrait", "WidgetTrait", "System.WidgetTrait", null,
                [], [], [], EmptyPackageDocumentation(), []),
            new ApiCorpusType(
                "T:System.WidgetTraitAttribute", "WidgetTraitAttribute", "System.WidgetTraitAttribute",
                new ApiTypeUse(null, "System.Attribute", ["System.Attribute"]),
                [], [], [], EmptyPackageDocumentation(), []),
            new ApiCorpusType(
                "T:System.WidgetSealAttribute", "WidgetSealAttribute", "System.WidgetSealAttribute",
                new ApiTypeUse(null, "System.Attribute", ["System.Attribute"]),
                [], [], [], EmptyPackageDocumentation(), []),
            new ApiCorpusType(
                "T:System.TraitedWidget", "TraitedWidget", "System.TraitedWidget", null,
                [], [],
                [
                    new ApiAttributeUse("[System.WidgetTrait]", "System.WidgetTraitAttribute", []),
                    new ApiAttributeUse("[System.WidgetSeal]", "System.WidgetSealAttribute", []),
                ],
                EmptyPackageDocumentation(),
                [
                    new ApiCorpusMember(
                        "M:System.TraitedWidget.Apply(System.WidgetTrait)",
                        "Apply",
                        "Method",
                        "public void Apply(System.WidgetTrait trait);",
                        [new ApiTypeUse("trait", "System.WidgetTrait", ["System.WidgetTrait"])],
                        null, [], [], EmptyPackageDocumentation()),
                ]),
        ]);
    }

    private static ApiCorpus PerturbFixtureCorpus(ApiCorpus corpus, bool reverse)
    {
        if (!reverse)
            return corpus;
        return corpus with
        {
            Types = corpus.Types.Reverse().Select(type => type with
            {
                Interfaces = type.Interfaces.Reverse().Select(ReverseTypeNames).ToArray(),
                Constraints = type.Constraints.Reverse().Select(ReverseTypeNames).ToArray(),
                Attributes = type.Attributes.Reverse().Select(attribute => attribute with
                {
                    ArgumentTypeNames = attribute.ArgumentTypeNames.Reverse().ToArray(),
                }).ToArray(),
                Members = type.Members.Reverse().Select(member => member with
                {
                    Constraints = member.Constraints.Reverse().Select(ReverseTypeNames).ToArray(),
                    Attributes = member.Attributes.Reverse().Select(attribute => attribute with
                    {
                        ArgumentTypeNames = attribute.ArgumentTypeNames.Reverse().ToArray(),
                    }).ToArray(),
                }).ToArray(),
            }).ToArray(),
        };
    }

    private static ApiTypeUse ReverseTypeNames(ApiTypeUse use) => use with
    {
        TypeNames = use.TypeNames.Reverse().ToArray(),
    };

    private static ApiDocumentation EmptyPackageDocumentation() =>
        new(null, [], [], null, null, null, []);

    /// <summary>
    /// A source holding both an attribute type whose C# short form names nothing else and one whose
    /// short form is also a class, which are the two readings an application has to be resolved
    /// against. Kept apart from <see cref="CreateWidgetServiceAsync"/> so the name-search fixtures
    /// stay what they are.
    /// </summary>
    public static async Task<ApiDocsQueryService> CreateAttributeSiblingServiceAsync(string root)
    {
        var repository = Path.Combine(root, "origin");
        var pin = await CreateRepositoryAsync(
            repository,
            [
                ("xml/System/WidgetTrait.xml", WidgetTraitXml),
                ("xml/System/WidgetTraitAttribute.xml", WidgetTraitAttributeXml),
                ("xml/System/WidgetSealAttribute.xml", WidgetSealAttributeXml),
                ("xml/System/TraitedWidget.xml", TraitedWidgetXml),
            ]);
        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(catalogPath, repository, pin);
        return await CreateServiceAsync(root, catalogPath, ["dotnet-api-docs"]);
    }

    /// <summary>
    /// Both API sources synced, holding documents that overlap: <c>System.Widget.Create</c> is a
    /// member in <c>dotnet-api-docs</c> and a type in <c>roslyn-api-docs</c>. The configured pair
    /// has disjoint namespace trees, so nothing but a fixture reaches the disagreement.
    /// </summary>
    /// <remarks>
    /// <c>roslyn-api-docs</c> roots its documents at <c>dotnet/xml</c> rather than <c>xml</c>,
    /// which is what <c>ApiDocsQueryService.ApiRootSegments</c> declares.
    /// </remarks>
    public static async Task<ApiDocsQueryService> CreateOverlappingSourcesServiceAsync(string root)
    {
        var dotnetRepository = Path.Combine(root, "origin");
        var dotnetPin = await CreateRepositoryAsync(
            dotnetRepository,
            [
                ("xml/System/Widget.xml", WidgetXml),
                ("xml/System/WidgetKit.xml", WidgetKitXml),
                ("xml/System/WidgetPolicy`1.xml", WidgetPolicyXml),
            ]);
        var roslynRepository = Path.Combine(root, "roslyn-origin");
        var roslynPin = await CreateRepositoryAsync(
            roslynRepository,
            [("dotnet/xml/System.Widget/Create.xml", CreateTypeXml)]);

        var catalogPath = Path.Combine(root, "sources.json");
        await WriteCatalogAsync(
            catalogPath,
            [
                ApiSource("dotnet-api-docs", dotnetRepository, dotnetPin, "xml"),
                ApiSource("roslyn-api-docs", roslynRepository, roslynPin, "dotnet/xml"),
            ]);
        return await CreateServiceAsync(root, catalogPath, ["dotnet-api-docs", "roslyn-api-docs"]);
    }

    public static Task WriteCatalogAsync(string path, string repository, string pin) =>
        WriteCatalogAsync(path, [ApiSource("dotnet-api-docs", repository, pin, "xml")]);

    public static async Task<string> RunGitAsync(string? workingDirectory, params string[] arguments)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };

        foreach (var argument in arguments)
            process.StartInfo.ArgumentList.Add(argument);

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        Assert.AreEqual(0, process.ExitCode, $"git {string.Join(' ', arguments)} failed: {stderr}");
        return stdout;
    }

    public static void DeleteDirectory(string path)
    {
        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            File.SetAttributes(file, FileAttributes.Normal);

        Directory.Delete(path, recursive: true);
    }

    private static async Task<ApiDocsQueryService> CreateServiceAsync(
        string root,
        string catalogPath,
        IReadOnlyList<string> sourceNames)
    {
        var catalog = new SourceCatalog(catalogPath);
        var cache = new SourceCache(Path.Combine(root, "cache"));
        var synchronizer = new SourceSynchronizer(catalog, cache);
        foreach (var sourceName in sourceNames)
            await synchronizer.SyncAsync(sourceName, requestedRef: null, CancellationToken.None);

        return new ApiDocsQueryService(catalog, cache, synchronizer);
    }

    private static async Task<string> CreateRepositoryAsync(
        string repository,
        IReadOnlyList<(string RelativePath, string Content)> files)
    {
        Directory.CreateDirectory(repository);
        await RunGitAsync(null, "init", "--initial-branch=main", repository);
        await RunGitAsync(repository, "config", "user.email", "tests@example.invalid");
        await RunGitAsync(repository, "config", "user.name", "Tests");
        foreach (var (relativePath, content) in files)
        {
            var target = Path.Combine(repository, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            await File.WriteAllTextAsync(target, content);
        }

        await RunGitAsync(repository, "add", ".");
        await RunGitAsync(repository, "commit", "-m", "docs");
        return (await RunGitAsync(repository, "rev-parse", "HEAD")).Trim();
    }

    private static async Task WriteCatalogAsync(
        string path,
        IReadOnlyList<KeyValuePair<string, object>> sources)
    {
        var document = new
        {
            schemaVersion = 1,
            sources = sources.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal),
        };

        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(document));
    }

    private static KeyValuePair<string, object> ApiSource(
        string name,
        string repository,
        string pin,
        string sparse) =>
        new(name, new
        {
            repository = "test/" + name,
            url = repository,
            pin,
            head = "main",
            sparse = new[] { sparse },
            purpose = "Test API docs.",
        });
}
