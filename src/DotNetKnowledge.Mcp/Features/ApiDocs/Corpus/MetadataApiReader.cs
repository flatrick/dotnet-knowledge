using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

public static class MetadataApiReader
{
    private const int SchemaVersion = 3;
    private const int MaximumArrayRank = 32;

    private const MethodAttributes ImplicitInterfaceImplementation =
        MethodAttributes.Final | MethodAttributes.Virtual | MethodAttributes.NewSlot;

    private static readonly ApiDocumentation EmptyDocumentation = new(
        null,
        Array.Empty<ApiNamedDocumentation>(),
        Array.Empty<ApiNamedDocumentation>(),
        null,
        null,
        null,
        Array.Empty<ApiNamedDocumentation>());

    private static readonly HashSet<string> SignatureAttributes = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.CompilerGeneratedAttribute",
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.CompilerServices.IsUnmanagedAttribute",
        "System.Runtime.CompilerServices.NullableAttribute",
        "System.Runtime.CompilerServices.NullableContextAttribute",
        "System.Runtime.CompilerServices.TupleElementNamesAttribute",
        // These name the compiler-generated state machine implementing an async or iterator method.
        // The type they point at is a private implementation detail with a name that is not even
        // expressible in C# -- '<ScheduleTask>d__38`1' -- so it is not API, and decoding it aborted
        // every assembly containing a public async method.
        "System.Runtime.CompilerServices.AsyncStateMachineAttribute",
        "System.Runtime.CompilerServices.IteratorStateMachineAttribute",
    };

    private static readonly IReadOnlyDictionary<string, string> OperatorNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["op_Addition"] = "operator +",
            ["op_Subtraction"] = "operator -",
            ["op_Multiply"] = "operator *",
            ["op_Division"] = "operator /",
            ["op_Modulus"] = "operator %",
            ["op_BitwiseAnd"] = "operator &",
            ["op_BitwiseOr"] = "operator |",
            ["op_ExclusiveOr"] = "operator ^",
            ["op_LeftShift"] = "operator <<",
            ["op_RightShift"] = "operator >>",
            ["op_UnsignedRightShift"] = "operator >>>",
            ["op_Equality"] = "operator ==",
            ["op_Inequality"] = "operator !=",
            ["op_GreaterThan"] = "operator >",
            ["op_LessThan"] = "operator <",
            ["op_GreaterThanOrEqual"] = "operator >=",
            ["op_LessThanOrEqual"] = "operator <=",
            ["op_UnaryPlus"] = "operator +",
            ["op_UnaryNegation"] = "operator -",
            ["op_LogicalNot"] = "operator !",
            ["op_OnesComplement"] = "operator ~",
            ["op_Increment"] = "operator ++",
            ["op_Decrement"] = "operator --",
            ["op_True"] = "operator true",
            ["op_False"] = "operator false",
            ["op_Implicit"] = "implicit operator",
            ["op_Explicit"] = "explicit operator",
            ["op_CheckedAddition"] = "operator checked +",
            ["op_CheckedSubtraction"] = "operator checked -",
            ["op_CheckedMultiply"] = "operator checked *",
            ["op_CheckedDivision"] = "operator checked /",
            ["op_CheckedUnaryNegation"] = "operator checked -",
            ["op_CheckedIncrement"] = "operator checked ++",
            ["op_CheckedDecrement"] = "operator checked --",
            ["op_CheckedExplicit"] = "explicit operator checked",
        };

    private static readonly (int Flag, string Name)[] AttributeTargetNames =
    [
        (1, "Assembly"),
        (2, "Module"),
        (4, "Class"),
        (8, "Struct"),
        (16, "Enum"),
        (32, "Constructor"),
        (64, "Method"),
        (128, "Property"),
        (256, "Field"),
        (512, "Event"),
        (1024, "Interface"),
        (2048, "Parameter"),
        (4096, "Delegate"),
        (8192, "ReturnValue"),
        (16384, "GenericParameter"),
    ];

    // A custom attribute blob stores an enum argument in its underlying type's width, so decoding
    // one requires knowing that width. For an enum this assembly defines, it is read from the
    // metadata; for an enum another assembly defines, it cannot be known without resolving that
    // assembly, which this reader will not do. These are the framework enums that appear as
    // attribute arguments, with the widths verified against the running framework rather than
    // assumed -- EventKeywords is Int64 and EventChannel is Byte, so a blanket Int32 would misread
    // the blob and every argument after it.
    private static readonly Dictionary<string, PrimitiveTypeCode> WellKnownEnumUnderlyingTypes =
        new(StringComparer.Ordinal)
        {
            ["System.AttributeTargets"] = PrimitiveTypeCode.Int32,
            ["System.ComponentModel.EditorBrowsableState"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.DebuggerBrowsableState"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.Tracing.EventActivityOptions"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.Tracing.EventChannel"] = PrimitiveTypeCode.Byte,
            ["System.Diagnostics.Tracing.EventCommand"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.Tracing.EventKeywords"] = PrimitiveTypeCode.Int64,
            ["System.Diagnostics.Tracing.EventLevel"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.Tracing.EventManifestOptions"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.Tracing.EventOpcode"] = PrimitiveTypeCode.Int32,
            ["System.Diagnostics.Tracing.EventTask"] = PrimitiveTypeCode.Int32,
            ["System.Reflection.MethodImplAttributes"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.CompilerServices.CompilationRelaxations"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.CompilerServices.MethodCodeType"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.CompilerServices.MethodImplOptions"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.ConstrainedExecution.Cer"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.ConstrainedExecution.Consistency"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.CallingConvention"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.CharSet"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.ClassInterfaceType"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.ComInterfaceType"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.DllImportSearchPath"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.GCHandleType"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.LayoutKind"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.UnmanagedType"] = PrimitiveTypeCode.Int32,
            ["System.Runtime.InteropServices.VarEnum"] = PrimitiveTypeCode.Int32,
            ["System.Security.SecurityRuleSet"] = PrimitiveTypeCode.Byte,
        };

    private static readonly HashSet<string> ByReferenceModifierTypes = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.IsReadOnlyAttribute",
        "System.Runtime.InteropServices.InAttribute",
    };

    private static readonly HashSet<string> InitAccessorModifierTypes = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.IsExternalInit",
    };

    private static readonly HashSet<string> FieldModifierTypes = new(StringComparer.Ordinal)
    {
        "System.Runtime.CompilerServices.IsVolatile",
    };

    private static readonly HashSet<string> UnmanagedConstraintModifierTypes = new(StringComparer.Ordinal)
    {
        "System.Runtime.InteropServices.UnmanagedType",
    };

    private static readonly HashSet<string> NoModifierTypes = new(StringComparer.Ordinal);

    public static ApiCorpus Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        try
        {
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
                throw new InvalidDataException("The stream does not contain managed metadata.");

            var reader = peReader.GetMetadataReader();
            var decoder = new Decoder(reader);
            var types = decoder.ReadTypes();
            return new ApiCorpus(SchemaVersion, types, decoder.Skipped);
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException("The stream does not contain valid ECMA-335 metadata.", exception);
        }
    }

    private sealed class Decoder(MetadataReader reader)
    {
        private readonly MetadataTypeProvider typeProvider = new(reader);
        private readonly List<ApiSkippedDeclaration> skipped = [];

        internal IReadOnlyList<ApiSkippedDeclaration> Skipped => skipped;

        internal ApiCorpusType[] ReadTypes()
        {
            var types = new List<ApiCorpusType>();
            foreach (var handle in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(handle);
                if (reader.GetString(definition.Name) == "<Module>" || !IsVisibleType(handle))
                    continue;

                // A type the reader cannot model costs that type, not the assembly. Only the
                // structural failures above -- unreadable metadata, and the duplicate-identity
                // check below -- still fail the whole read, because those describe an archive that
                // cannot be trusted rather than a declaration shape not met before.
                try
                {
                    types.Add(ReadType(handle, definition));
                }
                catch (InvalidDataException exception)
                {
                    skipped.Add(new ApiSkippedDeclaration(
                        "type",
                        GetSkipTypeName(handle),
                        null,
                        exception.Message));
                }
            }

            var ordered = types.OrderBy(item => item.EcmaId, StringComparer.Ordinal).ToArray();
            RejectDuplicateIds(ordered.Select(item => item.EcmaId), "type");
            RejectDuplicateIds(
                ordered.SelectMany(item => item.Members).Select(item => item.EcmaId),
                "member");
            return ordered;
        }

        private static void RejectDuplicateIds(IEnumerable<string> ids, string kind)
        {
            var duplicate = ids.GroupBy(item => item, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Skip(1).Any());
            if (duplicate is not null)
                throw new InvalidDataException($"Duplicate {kind} ECMA documentation ID '{duplicate.Key}'.");
        }

        private ApiCorpusType ReadType(TypeDefinitionHandle handle, TypeDefinition definition)
        {
            var context = CreateTypeContext(definition);
            var nullableContext = ReadNullableContext(
                definition.GetCustomAttributes(),
                GetInheritedNullableContext(handle));
            var baseType = definition.BaseType.IsNil
                ? null
                : DecodeStructuralType(
                    definition.BaseType,
                    context,
                    ReadNullableFlags(definition.GetCustomAttributes()),
                    nullableContext,
                    "base type");
            var interfaces = definition.GetInterfaceImplementations()
                .Select(item => reader.GetInterfaceImplementation(item))
                .Select(item => DecodeStructuralType(
                    item.Interface,
                    context,
                    ReadNullableFlags(item.GetCustomAttributes()),
                    nullableContext,
                    "interface"))
                .OrderBy(item => item.TypeExpression, StringComparer.Ordinal)
                .ToArray();
            var constraints = ReadConstraints(
                definition.GetGenericParameters(),
                context,
                nullableContext);
            var members = ReadMembers(handle, definition, context, nullableContext)
                .OrderBy(item => item.EcmaId, StringComparer.Ordinal)
                .ToArray();

            return new ApiCorpusType(
                $"T:{GetEcmaTypeName(handle)}",
                GetSourceTypeName(handle, includeNamespace: false),
                GetSourceTypeName(handle, includeNamespace: true),
                baseType,
                interfaces,
                constraints,
                ReadAttributes(definition.GetCustomAttributes()),
                EmptyDocumentation,
                members);
        }

        /// <summary>
        /// Reports the declaring type of a skipped member without going through
        /// <see cref="GetSourceTypeName"/>: the declaration is being recorded precisely because
        /// decoding it failed, so the presentable name may be the thing that threw.
        /// </summary>
        private string GetSkipTypeName(TypeDefinitionHandle handle)
        {
            try
            {
                var definition = reader.GetTypeDefinition(handle);
                var name = reader.GetString(definition.Name);
                var namespaceName = reader.GetString(definition.Namespace);
                return string.IsNullOrEmpty(namespaceName) ? name : $"{namespaceName}.{name}";
            }
            catch (BadImageFormatException)
            {
                return "<unknown>";
            }
        }

        /// <summary>
        /// Runs one member read behind the skip boundary. <paramref name="read"/> returns null when
        /// the member is simply not part of the visible API, which is not a skip and is not
        /// reported.
        /// </summary>
        private void ReadMemberOrSkip(
            List<ApiCorpusMember> members,
            TypeDefinitionHandle typeHandle,
            string kind,
            string name,
            Func<ApiCorpusMember?> read)
        {
            try
            {
                if (read() is { } member)
                    members.Add(member);
            }
            catch (InvalidDataException exception)
            {
                skipped.Add(new ApiSkippedDeclaration(
                    kind,
                    GetSkipTypeName(typeHandle),
                    name,
                    exception.Message));
            }
        }

        private List<ApiCorpusMember> ReadMembers(
            TypeDefinitionHandle typeHandle,
            TypeDefinition definition,
            SignatureContext typeContext,
            byte? nullableContext)
        {
            var members = new List<ApiCorpusMember>();
            var accessorMethods = new HashSet<MethodDefinitionHandle>();
            foreach (var propertyHandle in definition.GetProperties())
            {
                var accessors = reader.GetPropertyDefinition(propertyHandle).GetAccessors();
                if (!accessors.Getter.IsNil)
                    accessorMethods.Add(accessors.Getter);
                if (!accessors.Setter.IsNil)
                    accessorMethods.Add(accessors.Setter);
                foreach (var other in accessors.Others)
                    accessorMethods.Add(other);
            }

            foreach (var eventHandle in definition.GetEvents())
            {
                var accessors = reader.GetEventDefinition(eventHandle).GetAccessors();
                if (!accessors.Adder.IsNil)
                    accessorMethods.Add(accessors.Adder);
                if (!accessors.Remover.IsNil)
                    accessorMethods.Add(accessors.Remover);
                if (!accessors.Raiser.IsNil)
                    accessorMethods.Add(accessors.Raiser);
                foreach (var other in accessors.Others)
                    accessorMethods.Add(other);
            }

            foreach (var handle in definition.GetMethods())
            {
                if (accessorMethods.Contains(handle))
                    continue;

                var method = reader.GetMethodDefinition(handle);
                if (!IsVisible(method.Attributes & MethodAttributes.MemberAccessMask))
                    continue;

                ReadMemberOrSkip(
                    members,
                    typeHandle,
                    "method",
                    reader.GetString(method.Name),
                    () => ReadMethod(typeHandle, method, typeContext, nullableContext));
            }

            foreach (var handle in definition.GetProperties())
            {
                var property = reader.GetPropertyDefinition(handle);
                ReadMemberOrSkip(
                    members,
                    typeHandle,
                    "property",
                    reader.GetString(property.Name),
                    () =>
                    {
                        RejectVisibleOtherAccessors(
                            property.GetAccessors().Others,
                            $"property '{reader.GetString(property.Name)}'");
                        return TryGetPropertyAccessibility(property, out var accessibility)
                            ? ReadProperty(typeHandle, property, accessibility, typeContext, nullableContext)
                            : null;
                    });
            }

            foreach (var handle in definition.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(handle);
                ReadMemberOrSkip(
                    members,
                    typeHandle,
                    "event",
                    reader.GetString(eventDefinition.Name),
                    () =>
                    {
                        var eventAccessors = eventDefinition.GetAccessors();
                        RejectVisibleOtherAccessors(
                            [eventAccessors.Raiser, .. eventAccessors.Others],
                            $"event '{reader.GetString(eventDefinition.Name)}'");
                        return TryGetEventAccessibility(eventDefinition, out var accessibility)
                            ? ReadEvent(typeHandle, eventDefinition, accessibility, typeContext, nullableContext)
                            : null;
                    });
            }

            foreach (var handle in definition.GetFields())
            {
                var field = reader.GetFieldDefinition(handle);
                if (!IsVisible(field.Attributes & FieldAttributes.FieldAccessMask))
                    continue;

                ReadMemberOrSkip(
                    members,
                    typeHandle,
                    "field",
                    reader.GetString(field.Name),
                    () => ReadField(typeHandle, field, typeContext, nullableContext));
            }

            return members;
        }

        private ApiCorpusMember ReadMethod(
            TypeDefinitionHandle typeHandle,
            MethodDefinition method,
            SignatureContext typeContext,
            byte? containingNullableContext)
        {
            var methodParameters = method.GetGenericParameters()
                .Select(item => reader.GetGenericParameter(item))
                .OrderBy(item => item.Index)
                .Select(item => reader.GetString(item.Name))
                .ToImmutableArray();
            var context = typeContext with { MethodParameters = methodParameters };
            var signature = method.DecodeSignature(typeProvider, context);
            var rawName = reader.GetString(method.Name);
            var isConstructor = rawName is ".ctor" or ".cctor";
            // The CLI marks an operator with SpecialName; the name alone does not make one. Roslyn's
            // own SyntaxList<T> and SeparatedSyntaxList<T> ship a public static 'op_Implicit'
            // without it, so treating every op_-prefixed method as an operator both misdescribed an
            // ordinary method and rejected the whole assembly for failing operator rules.
            var isOperator = !isConstructor
                && rawName.StartsWith("op_", StringComparison.Ordinal)
                && (method.Attributes & MethodAttributes.SpecialName) != 0;
            if (isConstructor)
                ValidateConstructor(typeHandle, method, signature, methodParameters.Length, rawName);
            else if (isOperator)
                ValidateOperator(typeHandle, method, signature, methodParameters.Length, rawName);
            var parametersBySequence = method.GetParameters()
                .ToDictionary(item => (int)reader.GetParameter(item).SequenceNumber);
            var nullableContext = ReadNullableContext(method.GetCustomAttributes(), containingNullableContext);

            var parameterUses = new List<ApiTypeUse>(signature.ParameterTypes.Length);
            var renderedParameters = new List<string>(signature.ParameterTypes.Length);
            for (var index = 0; index < signature.ParameterTypes.Length; index++)
            {
                var hasParameter = parametersBySequence.TryGetValue(index + 1, out var parameterHandle);
                var parameter = hasParameter ? reader.GetParameter(parameterHandle) : default;
                var parameterName = !hasParameter || parameter.Name.IsNil
                    ? $"arg{index}"
                    : reader.GetString(parameter.Name);
                CustomAttributeHandleCollection? attributes = hasParameter
                    ? parameter.GetCustomAttributes()
                    : null;
                var parameterAttributes = hasParameter ? parameter.Attributes : 0;
                var annotatedType = ApplyNullable(
                    signature.ParameterTypes[index],
                    ReadNullableFlags(attributes),
                    nullableContext);
                ValidateModifiers(
                    annotatedType,
                    ByReferenceModifierTypes,
                    $"parameter '{parameterName}'",
                    requireByReference: true);
                var tupleNames = ReadTupleNames(attributes);
                var modifier = GetParameterModifier(
                    annotatedType,
                    parameterAttributes,
                    HasAttribute(attributes, "System.ParamArrayAttribute"));
                var displayType = RenderCSharp(UnwrapByReference(annotatedType), tupleNames);
                var parameterText = string.IsNullOrEmpty(modifier)
                    ? $"{displayType} {parameterName}"
                    : $"{modifier} {displayType} {parameterName}";
                renderedParameters.Add(parameterText);
                parameterUses.Add(new ApiTypeUse(
                    parameterName,
                    string.IsNullOrEmpty(modifier) ? displayType : $"{modifier} {displayType}",
                    CollectTypeNames(annotatedType)));
            }

            var hasReturnParameter = parametersBySequence.TryGetValue(0, out var returnParameterHandle);
            var returnParameter = hasReturnParameter ? reader.GetParameter(returnParameterHandle) : default;
            CustomAttributeHandleCollection? returnAttributes = hasReturnParameter
                ? returnParameter.GetCustomAttributes()
                : null;
            var returnParameterAttributes = hasReturnParameter ? returnParameter.Attributes : 0;
            var returnType = ApplyNullable(
                signature.ReturnType,
                ReadNullableFlags(returnAttributes),
                nullableContext);
            ValidateModifiers(
                returnType,
                ByReferenceModifierTypes,
                $"return type of '{reader.GetString(method.Name)}'",
                requireByReference: true);
            var returnTupleNames = ReadTupleNames(returnAttributes);
            var isConversion = isOperator && IsConversionOperator(rawName);
            var kind = isConstructor ? "constructor" : isOperator ? "operator" : "method";
            var displayName = isConstructor
                ? StripArity(reader.GetString(reader.GetTypeDefinition(typeHandle).Name))
                : GetMethodDisplayName(rawName, returnType, returnTupleNames);
            if (!isConstructor && !isOperator && methodParameters.Length > 0)
                displayName += $"<{string.Join(", ", methodParameters)}>";

            var modifiers = GetMethodModifiers(method.Attributes);
            var returnPrefix = GetReturnPrefix(returnType, returnParameterAttributes);
            var returnText = isConstructor || isConversion
                ? string.Empty
                : returnPrefix
                    + RenderCSharp(UnwrapByReference(returnType), returnTupleNames)
                    + " ";
            var constraints = ReadConstraints(
                method.GetGenericParameters(),
                context,
                nullableContext);
            var constraintText = RenderConstraintClauses(
                method.GetGenericParameters(),
                context,
                nullableContext);
            var csharpSignature = $"{modifiers}{returnText}{displayName}({string.Join(", ", renderedParameters)}){constraintText};";

            return new ApiCorpusMember(
                BuildMethodEcmaId(typeHandle, method, signature),
                isConstructor ? StripArity(reader.GetString(reader.GetTypeDefinition(typeHandle).Name)) : rawName,
                kind,
                csharpSignature,
                parameterUses,
                IsVoid(returnType) || isConstructor
                    ? null
                    : new ApiTypeUse(
                        null,
                        returnPrefix + RenderCSharp(UnwrapByReference(returnType), returnTupleNames),
                        CollectTypeNames(returnType)),
                constraints,
                ReadAttributes(method.GetCustomAttributes()),
                EmptyDocumentation);
        }

        private ApiCorpusMember ReadProperty(
            TypeDefinitionHandle typeHandle,
            PropertyDefinition property,
            string accessibility,
            SignatureContext context,
            byte? containingNullableContext)
        {
            var signature = property.DecodeSignature(typeProvider, context);
            var nullableContext = ReadNullableContext(property.GetCustomAttributes(), containingNullableContext);
            var returnType = ApplyNullable(
                signature.ReturnType,
                ReadNullableFlags(property.GetCustomAttributes()),
                nullableContext);
            var tupleNames = ReadTupleNames(property.GetCustomAttributes());
            var accessors = property.GetAccessors();
            var name = reader.GetString(property.Name);
            ValidatePropertyAccessors(name, accessors, signature, context);
            var parameterDefinitions = GetPropertyParameterDefinitions(accessors);
            var parameterUses = new List<ApiTypeUse>();
            var parameterTexts = new List<string>();
            for (var index = 0; index < signature.ParameterTypes.Length; index++)
            {
                var hasParameter = parameterDefinitions.TryGetValue(index + 1, out var parameterHandle);
                var parameter = hasParameter ? reader.GetParameter(parameterHandle) : default;
                var parameterName = !hasParameter || parameter.Name.IsNil
                    ? $"arg{index}"
                    : reader.GetString(parameter.Name);
                CustomAttributeHandleCollection? parameterAttributes = hasParameter
                    ? parameter.GetCustomAttributes()
                    : null;
                var parameterType = ApplyNullable(
                    signature.ParameterTypes[index],
                    ReadNullableFlags(parameterAttributes),
                    nullableContext);
                ValidateModifiers(
                    parameterType,
                    ByReferenceModifierTypes,
                    $"property parameter '{parameterName}'",
                    requireByReference: true);
                var display = RenderCSharp(parameterType, ReadTupleNames(parameterAttributes));
                parameterTexts.Add($"{display} {parameterName}");
                parameterUses.Add(new ApiTypeUse(parameterName, display, CollectTypeNames(parameterType)));
            }

            var isIndexer = signature.ParameterTypes.Length > 0;
            var displayName = isIndexer ? $"this[{string.Join(", ", parameterTexts)}]" : name;
            var declarationModifiers = GetAccessorDeclarationModifiers(
                typeHandle,
                [accessors.Getter, accessors.Setter],
                $"property '{name}'");
            var accessorText = RenderPropertyAccessors(accessors, accessibility, context);
            ValidateModifiers(
                returnType,
                ByReferenceModifierTypes,
                $"return type of property '{name}'",
                requireByReference: true);
            var returnPrefix = GetReturnPrefix(returnType, 0);
            var returnDisplay = RenderCSharp(UnwrapByReference(returnType), tupleNames);
            var csharpSignature = $"{accessibility} {declarationModifiers}{returnPrefix}{returnDisplay} {displayName} {{ {accessorText} }}";
            var ecmaParameters = signature.ParameterTypes.Length == 0
                ? string.Empty
                : $"({string.Join(",", signature.ParameterTypes.Select(RenderEcmaType))})";

            return new ApiCorpusMember(
                $"P:{GetEcmaTypeName(typeHandle)}.{EscapeMemberName(name)}{ecmaParameters}",
                name,
                isIndexer ? "indexer" : "property",
                csharpSignature,
                parameterUses,
                new ApiTypeUse(null, returnPrefix + returnDisplay, CollectTypeNames(returnType)),
                Array.Empty<ApiTypeUse>(),
                ReadAttributes(property.GetCustomAttributes()),
                EmptyDocumentation);
        }

        private ApiCorpusMember ReadEvent(
            TypeDefinitionHandle typeHandle,
            EventDefinition eventDefinition,
            string accessibility,
            SignatureContext context,
            byte? containingNullableContext)
        {
            var eventType = ApplyNullable(
                DecodeEntityType(eventDefinition.Type, context),
                ReadNullableFlags(eventDefinition.GetCustomAttributes()),
                ReadNullableContext(eventDefinition.GetCustomAttributes(), containingNullableContext));
            ValidateModifiers(eventType, NoModifierTypes, $"event '{reader.GetString(eventDefinition.Name)}'");
            var accessors = eventDefinition.GetAccessors();
            var name = reader.GetString(eventDefinition.Name);
            ValidateEventAccessors(name, accessors, eventType, context);
            var declarationModifiers = GetAccessorDeclarationModifiers(
                typeHandle,
                [accessors.Adder, accessors.Remover],
                $"event '{name}'");
            var display = RenderCSharp(eventType, ReadTupleNames(eventDefinition.GetCustomAttributes()));

            return new ApiCorpusMember(
                $"E:{GetEcmaTypeName(typeHandle)}.{EscapeMemberName(name)}",
                name,
                "event",
                $"{accessibility} {declarationModifiers}event {display} {name};",
                Array.Empty<ApiTypeUse>(),
                new ApiTypeUse(null, display, CollectTypeNames(eventType)),
                Array.Empty<ApiTypeUse>(),
                ReadAttributes(eventDefinition.GetCustomAttributes()),
                EmptyDocumentation);
        }

        private ApiCorpusMember ReadField(
            TypeDefinitionHandle typeHandle,
            FieldDefinition field,
            SignatureContext context,
            byte? containingNullableContext)
        {
            var fieldType = ApplyNullable(
                field.DecodeSignature(typeProvider, context),
                ReadNullableFlags(field.GetCustomAttributes()),
                ReadNullableContext(field.GetCustomAttributes(), containingNullableContext));
            var name = reader.GetString(field.Name);
            ValidateRootModifiers(fieldType, FieldModifierTypes, $"field '{name}'");
            var modifiers = new StringBuilder(GetAccessibility(field.Attributes & FieldAttributes.FieldAccessMask));
            modifiers.Append(' ');
            if ((field.Attributes & FieldAttributes.Literal) != 0)
                modifiers.Append("const ");
            else
            {
                if ((field.Attributes & FieldAttributes.Static) != 0)
                    modifiers.Append("static ");
                if ((field.Attributes & FieldAttributes.InitOnly) != 0)
                    modifiers.Append("readonly ");
                if (HasModifier(fieldType, "System.Runtime.CompilerServices.IsVolatile"))
                    modifiers.Append("volatile ");
            }

            var display = RenderCSharp(fieldType, ReadTupleNames(field.GetCustomAttributes()));
            return new ApiCorpusMember(
                $"F:{GetEcmaTypeName(typeHandle)}.{EscapeMemberName(name)}",
                name,
                "field",
                $"{modifiers}{display} {name};",
                Array.Empty<ApiTypeUse>(),
                new ApiTypeUse(null, display, CollectTypeNames(fieldType)),
                Array.Empty<ApiTypeUse>(),
                ReadAttributes(field.GetCustomAttributes()),
                EmptyDocumentation);
        }

        private List<ApiTypeUse> ReadConstraints(
            GenericParameterHandleCollection handles,
            SignatureContext context,
            byte? nullableContext)
        {
            var result = new List<ApiTypeUse>();
            foreach (var handle in handles)
            {
                var parameter = reader.GetGenericParameter(handle);
                var parameterName = reader.GetString(parameter.Name);
                var isUnmanaged = HasAttribute(
                    parameter.GetCustomAttributes(),
                    "System.Runtime.CompilerServices.IsUnmanagedAttribute");
                var parameterAttributes = parameter.Attributes;
                var isValueType = (parameterAttributes
                    & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
                if (isValueType)
                {
                    result.Add(new ApiTypeUse(
                        parameterName,
                        isUnmanaged ? "unmanaged" : "struct",
                        Array.Empty<string>()));
                }
                else if ((parameterAttributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                {
                    var parameterNullable = ReadNullableFlags(parameter.GetCustomAttributes());
                    result.Add(new ApiTypeUse(
                        parameterName,
                        IsNullableClassConstraint(parameterNullable, parameterName) ? "class?" : "class",
                        Array.Empty<string>()));
                }
                foreach (var constraintHandle in parameter.GetConstraints())
                {
                    var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                    var type = ApplyNullable(
                        DecodeEntityType(constraint.Type, context),
                        ReadNullableFlags(constraint.GetCustomAttributes()),
                        nullableContext);
                    if (isUnmanaged)
                    {
                        ValidateRootModifiers(
                            type,
                            UnmanagedConstraintModifierTypes,
                            $"constraint on '{parameterName}'",
                            "System.ValueType",
                            requireModifier: true);
                    }
                    else
                    {
                        ValidateModifiers(type, NoModifierTypes, $"constraint on '{parameterName}'");
                    }
                    if ((parameter.Attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0
                        && StripModifiers(type).CanonicalName == "System.ValueType")
                    {
                        continue;
                    }

                    result.Add(new ApiTypeUse(
                        parameterName,
                        RenderCSharp(type, null),
                        CollectTypeNames(type)));
                }
                if (!isValueType
                    && (parameterAttributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
                {
                    result.Add(new ApiTypeUse(
                        parameterName,
                        "new()",
                        Array.Empty<string>()));
                }
            }

            return result;
        }

        private ApiTypeUse DecodeStructuralType(
            EntityHandle handle,
            SignatureContext context,
            NullableTransform? nullableFlags,
            byte? nullableContext,
            string position)
        {
            var type = ApplyNullable(
                DecodeEntityType(handle, context),
                nullableFlags,
                nullableContext);
            ValidateModifiers(type, NoModifierTypes, position);
            return new ApiTypeUse(null, RenderCSharp(type, null), CollectTypeNames(type));
        }

        private string RenderConstraintClauses(
            GenericParameterHandleCollection handles,
            SignatureContext context,
            byte? nullableContext)
        {
            var clauses = new List<string>();
            foreach (var handle in handles)
            {
                var parameter = reader.GetGenericParameter(handle);
                var parts = new List<string>();
                var attributes = parameter.Attributes;
                var isValueType = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
                var isUnmanaged = HasAttribute(
                    parameter.GetCustomAttributes(),
                    "System.Runtime.CompilerServices.IsUnmanagedAttribute");
                if (isValueType)
                {
                    parts.Add(isUnmanaged
                        ? "unmanaged"
                        : "struct");
                }
                else if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
                {
                    var parameterNullable = ReadNullableFlags(parameter.GetCustomAttributes());
                    parts.Add(IsNullableClassConstraint(
                        parameterNullable,
                        reader.GetString(parameter.Name)) ? "class?" : "class");
                }

                foreach (var constraintHandle in parameter.GetConstraints())
                {
                    var definition = reader.GetGenericParameterConstraint(constraintHandle);
                    var constraint = ApplyNullable(
                        DecodeEntityType(definition.Type, context),
                        ReadNullableFlags(definition.GetCustomAttributes()),
                        nullableContext);
                    if (isUnmanaged)
                    {
                        ValidateRootModifiers(
                            constraint,
                            UnmanagedConstraintModifierTypes,
                            $"constraint on '{reader.GetString(parameter.Name)}'",
                            "System.ValueType",
                            requireModifier: true);
                    }
                    else
                    {
                        ValidateModifiers(
                            constraint,
                            NoModifierTypes,
                            $"constraint on '{reader.GetString(parameter.Name)}'");
                    }
                    if (isValueType && StripModifiers(constraint).CanonicalName == "System.ValueType")
                        continue;
                    parts.Add(RenderCSharp(constraint, null));
                }

                if (!isValueType && (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0)
                    parts.Add("new()");
                if (parts.Count > 0)
                    clauses.Add($" where {reader.GetString(parameter.Name)} : {string.Join(", ", parts)}");
            }

            return string.Concat(clauses);
        }

        private ApiAttributeUse[] ReadAttributes(CustomAttributeHandleCollection handles)
        {
            var attributes = new List<ApiAttributeUse>();
            foreach (var handle in handles)
            {
                var attribute = reader.GetCustomAttribute(handle);
                var attributeType = GetAttributeTypeName(attribute);
                if (SignatureAttributes.Contains(attributeType))
                    continue;

                CustomAttributeValue<DecodedType> value;
                try
                {
                    value = attribute.DecodeValue(typeProvider);
                }
                catch (Exception exception) when (exception is BadImageFormatException or InvalidOperationException)
                {
                    throw new InvalidDataException($"Could not decode attribute '{attributeType}'.", exception);
                }

                var arguments = new List<string>();
                var argumentTypeNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (var argument in value.FixedArguments)
                {
                    arguments.Add(RenderAttributeArgument(argument.Type, argument.Value, argumentTypeNames));
                }

                foreach (var argument in value.NamedArguments)
                {
                    arguments.Add($"{argument.Name} = {RenderAttributeArgument(argument.Type, argument.Value, argumentTypeNames)}");
                }

                var applicationType = RemoveAttributeSuffix(GetAttributeDisplayType(attribute));
                var application = arguments.Count == 0
                    ? $"[{applicationType}]"
                    : $"[{applicationType}({string.Join(", ", arguments)})]";
                attributes.Add(new ApiAttributeUse(
                    application,
                    attributeType,
                    argumentTypeNames.OrderBy(item => item, StringComparer.Ordinal).ToArray()));
            }

            return attributes
                .OrderBy(item => item.AttributeType, StringComparer.Ordinal)
                .ThenBy(item => item.Application, StringComparer.Ordinal)
                .ToArray();
        }

        private string RenderAttributeArgument(
            DecodedType argumentType,
            object? argumentValue,
            ISet<string> argumentTypeNames)
        {
            if (argumentType.CanonicalName == "System.AttributeTargets")
            {
                argumentTypeNames.Add(argumentType.CanonicalName);
                return RenderAttributeTargets(argumentValue);
            }

            if (argumentType.CanonicalName == "System.Type")
            {
                if (argumentValue is null)
                    return "null";
                if (argumentValue is not DecodedType type)
                    throw new InvalidDataException("A System.Type attribute argument had an unsupported encoding.");
                foreach (var name in CollectTypeNames(type))
                    argumentTypeNames.Add(name);
                return $"typeof({RenderCSharp(type, null)})";
            }

            if (TryGetLocalEnum(argumentType, out var enumDefinition))
            {
                argumentTypeNames.Add(argumentType.CanonicalName!);
                return RenderLocalEnum(argumentType, enumDefinition, argumentValue);
            }

            if (argumentValue is ImmutableArray<CustomAttributeTypedArgument<DecodedType>> elements)
            {
                return $"new[] {{ {string.Join(", ", elements.Select(item => RenderAttributeArgument(item.Type, item.Value, argumentTypeNames)))} }}";
            }

            // An enum another assembly defines. Its member names cannot be recovered without
            // resolving that assembly, so the value renders as the number the blob actually holds,
            // but the type is known and is recorded -- otherwise a search by argument type would
            // report the attribute as having none.
            if (argumentType.Kind == DecodedTypeKind.Named
                && argumentType.CanonicalName is { } externalEnumName
                && WellKnownEnumUnderlyingTypes.ContainsKey(externalEnumName))
            {
                argumentTypeNames.Add(externalEnumName);
            }

            return argumentValue switch
            {
                null => "null",
                bool boolean => boolean ? "true" : "false",
                char character => $"'{EscapeCharacter(character)}'",
                string text => $"\"{EscapeString(text)}\"",
                float number => number.ToString("R", CultureInfo.InvariantCulture) + "F",
                double number => number.ToString("R", CultureInfo.InvariantCulture) + "D",
                decimal number => number.ToString(CultureInfo.InvariantCulture) + "M",
                sbyte or byte or short or ushort or int or uint or long or ulong =>
                    Convert.ToString(argumentValue, CultureInfo.InvariantCulture)!,
                _ => throw new InvalidDataException(
                    $"Attribute argument type '{argumentValue.GetType().FullName}' is unsupported."),
            };
        }

        private static string RemoveAttributeSuffix(string attributeType)
        {
            var genericArguments = attributeType.IndexOf('<');
            var suffixEnd = genericArguments < 0 ? attributeType.Length : genericArguments;
            const string suffix = "Attribute";
            return suffixEnd >= suffix.Length
                && attributeType.AsSpan(suffixEnd - suffix.Length, suffix.Length)
                    .SequenceEqual(suffix)
                    ? attributeType.Remove(suffixEnd - suffix.Length, suffix.Length)
                    : attributeType;
        }

        private bool TryGetLocalEnum(DecodedType type, out TypeDefinition definition)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                if (GetDefinitionTypeName(handle) != type.CanonicalName)
                    continue;
                definition = reader.GetTypeDefinition(handle);
                return GetEntityTypeName(definition.BaseType) == "System.Enum";
            }
            definition = default;
            return false;
        }

        private string RenderLocalEnum(
            DecodedType enumType,
            TypeDefinition definition,
            object? value)
        {
            if (value is null)
                throw new InvalidDataException($"Enum '{enumType.CanonicalName}' cannot be null.");
            var numericValue = GetIntegralBits(value);
            var constants = definition.GetFields()
                .Select(handle => reader.GetFieldDefinition(handle))
                .Where(field => (field.Attributes & FieldAttributes.Literal) != 0)
                .Select(field => (
                    Name: reader.GetString(field.Name),
                    Value: ReadIntegralConstant(field.GetDefaultValue())))
                .OrderBy(item => item.Value)
                .ToArray();
            var exact = constants.FirstOrDefault(item => item.Value == numericValue);
            if (exact.Name is not null)
                return $"{enumType.CanonicalName}.{exact.Name}";

            if (HasAttribute(definition.GetCustomAttributes(), "System.FlagsAttribute"))
            {
                var remaining = numericValue;
                var parts = new List<string>();
                foreach (var constant in constants.Where(item => item.Value != 0))
                {
                    if ((remaining & constant.Value) != constant.Value)
                        continue;
                    parts.Add($"{enumType.CanonicalName}.{constant.Name}");
                    remaining &= ~constant.Value;
                }
                if (remaining == 0 && parts.Count > 0)
                    return string.Join(" | ", parts);
            }

            return $"({enumType.CanonicalName}){Convert.ToString(value, CultureInfo.InvariantCulture)}";
        }

        private ulong ReadIntegralConstant(ConstantHandle handle)
        {
            if (handle.IsNil)
                throw new InvalidDataException("An enum literal has no constant value.");
            var constant = reader.GetConstant(handle);
            var blob = reader.GetBlobReader(constant.Value);
            return constant.TypeCode switch
            {
                ConstantTypeCode.SByte => unchecked((ulong)blob.ReadSByte()),
                ConstantTypeCode.Byte => blob.ReadByte(),
                ConstantTypeCode.Int16 => unchecked((ulong)blob.ReadInt16()),
                ConstantTypeCode.UInt16 => blob.ReadUInt16(),
                ConstantTypeCode.Int32 => unchecked((ulong)blob.ReadInt32()),
                ConstantTypeCode.UInt32 => blob.ReadUInt32(),
                ConstantTypeCode.Int64 => unchecked((ulong)blob.ReadInt64()),
                ConstantTypeCode.UInt64 => blob.ReadUInt64(),
                _ => throw new InvalidDataException(
                    $"Enum constant type '{constant.TypeCode}' is unsupported."),
            };
        }

        private static ulong GetIntegralBits(object value) => value switch
        {
            sbyte number => unchecked((ulong)number),
            byte number => number,
            short number => unchecked((ulong)number),
            ushort number => number,
            int number => unchecked((ulong)number),
            uint number => number,
            long number => unchecked((ulong)number),
            ulong number => number,
            _ => throw new InvalidDataException(
                $"Enum value type '{value.GetType().FullName}' is unsupported."),
        };

        private static string RenderAttributeTargets(object? value)
        {
            if (value is null)
                throw new InvalidDataException("System.AttributeTargets cannot be null.");
            var remaining = Convert.ToInt32(value, CultureInfo.InvariantCulture);
            if (remaining == 32767)
                return "System.AttributeTargets.All";

            var parts = new List<string>();
            foreach (var (flag, name) in AttributeTargetNames)
            {
                if ((remaining & flag) == 0)
                    continue;
                parts.Add($"System.AttributeTargets.{name}");
                remaining &= ~flag;
            }

            if (parts.Count == 0 || remaining != 0)
                throw new InvalidDataException($"System.AttributeTargets value '{value}' is unsupported.");
            return string.Join(" | ", parts);
        }

        private string GetAttributeTypeName(CustomAttribute attribute) => attribute.Constructor.Kind switch
        {
            HandleKind.MethodDefinition => GetDefinitionTypeName(
                reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType()),
            HandleKind.MemberReference => GetEntityTypeName(
                reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent),
            _ => throw new InvalidDataException(
                $"Attribute constructor handle '{attribute.Constructor.Kind}' is unsupported."),
        };

        private string GetEntityTypeName(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetDefinitionTypeName((TypeDefinitionHandle)handle),
            HandleKind.TypeReference => GetReferenceTypeName((TypeReferenceHandle)handle),
            HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                .DecodeSignature(typeProvider, SignatureContext.Empty).CanonicalName
                ?? throw new InvalidDataException("A generic attribute type has no canonical identity."),
            _ => throw new InvalidDataException($"Attribute type handle '{handle.Kind}' is unsupported."),
        };

        private string GetAttributeDisplayType(CustomAttribute attribute)
        {
            var declaringType = attribute.Constructor.Kind switch
            {
                HandleKind.MethodDefinition => (EntityHandle)reader.GetMethodDefinition(
                    (MethodDefinitionHandle)attribute.Constructor).GetDeclaringType(),
                HandleKind.MemberReference => reader.GetMemberReference(
                    (MemberReferenceHandle)attribute.Constructor).Parent,
                _ => throw new InvalidDataException(
                    $"Attribute constructor handle '{attribute.Constructor.Kind}' is unsupported."),
            };
            return declaringType.Kind == HandleKind.TypeSpecification
                ? RenderCSharp(
                    reader.GetTypeSpecification((TypeSpecificationHandle)declaringType)
                        .DecodeSignature(typeProvider, SignatureContext.Empty),
                    null)
                : RenderNamedType(GetEntityTypeName(declaringType));
        }

        private DecodedType DecodeEntityType(EntityHandle handle, SignatureContext context) => handle.Kind switch
        {
            HandleKind.TypeDefinition => typeProvider.GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeReference => typeProvider.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                rawTypeKind: 0),
            HandleKind.TypeSpecification => reader.GetTypeSpecification((TypeSpecificationHandle)handle)
                .DecodeSignature(typeProvider, context),
            _ => throw new InvalidDataException($"Type handle '{handle.Kind}' is unsupported."),
        };

        private bool IsVisibleType(TypeDefinitionHandle handle)
        {
            var definition = reader.GetTypeDefinition(handle);
            var visibility = definition.Attributes & TypeAttributes.VisibilityMask;
            var directlyVisible = visibility is TypeAttributes.Public
                or TypeAttributes.NestedPublic
                or TypeAttributes.NestedFamily
                or TypeAttributes.NestedFamORAssem;
            if (!directlyVisible)
                return false;

            var declaringType = definition.GetDeclaringType();
            return declaringType.IsNil || IsVisibleType(declaringType);
        }

        private static bool IsVisible(MethodAttributes access) => access is MethodAttributes.Public
            or MethodAttributes.Family
            or MethodAttributes.FamORAssem;

        private static bool IsVisible(FieldAttributes access) => access is FieldAttributes.Public
            or FieldAttributes.Family
            or FieldAttributes.FamORAssem;

        private bool TryGetPropertyAccessibility(PropertyDefinition property, out string accessibility)
        {
            var accessors = property.GetAccessors();
            return TryGetMostVisibleAccessibility([accessors.Getter, accessors.Setter], out accessibility);
        }

        private bool TryGetEventAccessibility(EventDefinition eventDefinition, out string accessibility)
        {
            var accessors = eventDefinition.GetAccessors();
            return TryGetMostVisibleAccessibility(
                [accessors.Adder, accessors.Remover],
                out accessibility);
        }

        private void RejectVisibleOtherAccessors(
            IEnumerable<MethodDefinitionHandle> handles,
            string position)
        {
            foreach (var handle in handles.Where(handle => !handle.IsNil))
            {
                var method = reader.GetMethodDefinition(handle);
                if (IsVisible(method.Attributes & MethodAttributes.MemberAccessMask))
                {
                    throw new InvalidDataException(
                        $"Visible accessor '{reader.GetString(method.Name)}' on {position} cannot be represented as C#.");
                }
            }
        }

        private bool TryGetMostVisibleAccessibility(
            IEnumerable<MethodDefinitionHandle> handles,
            out string accessibility)
        {
            var bestRank = -1;
            accessibility = string.Empty;
            foreach (var handle in handles)
            {
                if (handle.IsNil)
                    continue;
                var access = reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask;
                var rank = GetVisibilityRank(access);
                if (rank <= bestRank)
                    continue;
                bestRank = rank;
                accessibility = GetAccessibility(access);
            }

            return bestRank >= 0;
        }

        private static int GetVisibilityRank(MethodAttributes access) => access switch
        {
            MethodAttributes.Public => 3,
            MethodAttributes.FamORAssem => 2,
            MethodAttributes.Family => 1,
            _ => -1,
        };

        private static string GetAccessibility(MethodAttributes access) => access switch
        {
            MethodAttributes.Public => "public",
            MethodAttributes.FamORAssem => "protected internal",
            MethodAttributes.Family => "protected",
            _ => throw new InvalidDataException($"Method accessibility '{access}' is not part of the visible API."),
        };

        private static string GetAccessibility(FieldAttributes access) => access switch
        {
            FieldAttributes.Public => "public",
            FieldAttributes.FamORAssem => "protected internal",
            FieldAttributes.Family => "protected",
            _ => throw new InvalidDataException($"Field accessibility '{access}' is not part of the visible API."),
        };

        private static string GetMethodModifiers(MethodAttributes attributes)
        {
            var result = new StringBuilder(GetAccessibility(attributes & MethodAttributes.MemberAccessMask));
            result.Append(' ');
            if ((attributes & MethodAttributes.Static) != 0)
                result.Append("static ");
            if ((attributes & MethodAttributes.Abstract) != 0)
                result.Append("abstract ");
            else if ((attributes & MethodAttributes.Virtual) != 0)
            {
                if ((attributes & MethodAttributes.Final) != 0)
                    result.Append("sealed override ");
                else if ((attributes & MethodAttributes.NewSlot) != 0)
                    result.Append("virtual ");
                else
                    result.Append("override ");
            }
            if ((attributes & MethodAttributes.PinvokeImpl) != 0)
                result.Append("extern ");
            return result.ToString();
        }

        private string RenderPropertyAccessors(
            PropertyAccessors accessors,
            string propertyAccessibility,
            SignatureContext context)
        {
            var parts = new List<string>();
            if (IsVisibleAccessor(accessors.Getter))
                parts.Add(RenderAccessor("get", accessors.Getter, propertyAccessibility, context));
            if (IsVisibleAccessor(accessors.Setter))
                parts.Add(RenderAccessor("set", accessors.Setter, propertyAccessibility, context));
            return string.Join(' ', parts);
        }

        private void ValidatePropertyAccessors(
            string propertyName,
            PropertyAccessors accessors,
            MethodSignature<DecodedType> propertySignature,
            SignatureContext context)
        {
            if (propertySignature.Header.Kind != SignatureKind.Property
                || propertySignature.Header.HasExplicitThis)
            {
                throw new InvalidDataException(
                    $"Property '{propertyName}' has an unsupported signature calling convention.");
            }
            if (accessors.Getter.IsNil && accessors.Setter.IsNil)
                throw new InvalidDataException($"Property '{propertyName}' has no getter or setter.");
            if (!accessors.Getter.IsNil)
            {
                var getter = reader.GetMethodDefinition(accessors.Getter);
                var getterName = reader.GetString(getter.Name);
                if (getterName != $"get_{propertyName}")
                    throw new InvalidDataException($"Property getter '{getterName}' has an unsupported name.");
                var getterSignature = getter.DecodeSignature(typeProvider, context);
                ValidateAccessorHeader(
                    getter,
                    getterSignature,
                    getterName,
                    propertySignature.Header.IsInstance);
                ValidateSignatureType(
                    getterSignature.ReturnType,
                    propertySignature.ReturnType,
                    $"return type of '{getterName}'");
                ValidateAccessorParameters(
                    getterSignature.ParameterTypes,
                    propertySignature.ParameterTypes,
                    getterName);
            }

            if (!accessors.Setter.IsNil)
            {
                var setter = reader.GetMethodDefinition(accessors.Setter);
                var setterName = reader.GetString(setter.Name);
                if (setterName != $"set_{propertyName}")
                    throw new InvalidDataException($"Property setter '{setterName}' has an unsupported name.");
                var setterSignature = setter.DecodeSignature(typeProvider, context);
                ValidateAccessorHeader(
                    setter,
                    setterSignature,
                    setterName,
                    propertySignature.Header.IsInstance);
                ValidateRootModifiers(
                    setterSignature.ReturnType,
                    InitAccessorModifierTypes,
                    $"return type of '{setterName}'",
                    "System.Void");
                if (setterSignature.ParameterTypes.Length != propertySignature.ParameterTypes.Length + 1)
                    throw new InvalidDataException($"Property setter '{setterName}' has an incompatible parameter count.");
                for (var index = 0; index < propertySignature.ParameterTypes.Length; index++)
                {
                    ValidateSignatureType(
                        setterSignature.ParameterTypes[index],
                        propertySignature.ParameterTypes[index],
                        $"parameter {index} of '{setterName}'");
                }
                ValidateSignatureType(
                    setterSignature.ParameterTypes[^1],
                    propertySignature.ReturnType,
                    $"value parameter of '{setterName}'");
            }
        }

        private void ValidateEventAccessors(
            string eventName,
            EventAccessors accessors,
            DecodedType eventType,
            SignatureContext context)
        {
            if (accessors.Adder.IsNil || accessors.Remover.IsNil)
                throw new InvalidDataException($"Event '{eventName}' must have both an adder and remover.");
            var addAccess = reader.GetMethodDefinition(accessors.Adder).Attributes
                & MethodAttributes.MemberAccessMask;
            var removeAccess = reader.GetMethodDefinition(accessors.Remover).Attributes
                & MethodAttributes.MemberAccessMask;
            if (addAccess != removeAccess)
                throw new InvalidDataException($"Event '{eventName}' accessors have incompatible visibility.");

            ValidateEventAccessor(accessors.Adder, $"add_{eventName}", eventType, context);
            ValidateEventAccessor(accessors.Remover, $"remove_{eventName}", eventType, context);
        }

        private void ValidateEventAccessor(
            MethodDefinitionHandle handle,
            string expectedName,
            DecodedType eventType,
            SignatureContext context)
        {
            var method = reader.GetMethodDefinition(handle);
            var name = reader.GetString(method.Name);
            if (name != expectedName)
                throw new InvalidDataException($"Event accessor '{name}' has an unsupported name.");
            var signature = method.DecodeSignature(typeProvider, context);
            ValidateAccessorHeader(method, signature, name);
            ValidateRootModifiers(
                signature.ReturnType,
                NoModifierTypes,
                $"return type of '{name}'",
                "System.Void");
            if (signature.ParameterTypes.Length != 1)
                throw new InvalidDataException($"Event accessor '{name}' has an incompatible parameter count.");
            ValidateSignatureType(signature.ParameterTypes[0], eventType, $"parameter of '{name}'");
        }

        private static void ValidateAccessorHeader(
            MethodDefinition method,
            MethodSignature<DecodedType> signature,
            string name,
            bool? declarationIsInstance = null)
        {
            if ((method.Attributes & MethodAttributes.SpecialName) == 0)
                throw new InvalidDataException($"Accessor '{name}' is not marked special-name.");
            if (signature.GenericParameterCount != 0)
                throw new InvalidDataException($"Accessor '{name}' cannot be generic.");
            if (signature.Header.Kind != SignatureKind.Method
                || signature.Header.CallingConvention != SignatureCallingConvention.Default
                || signature.Header.HasExplicitThis)
            {
                throw new InvalidDataException(
                    $"Accessor '{name}' has an unsupported calling convention.");
            }
            var isStatic = (method.Attributes & MethodAttributes.Static) != 0;
            if (signature.Header.IsInstance == isStatic)
                throw new InvalidDataException($"Accessor '{name}' has an incompatible calling convention.");
            if (declarationIsInstance is not null
                && signature.Header.IsInstance != declarationIsInstance.Value)
            {
                throw new InvalidDataException(
                    $"Accessor '{name}' has staticness incompatible with its property declaration.");
            }
        }

        private static void ValidateAccessorParameters(
            ImmutableArray<DecodedType> actual,
            ImmutableArray<DecodedType> expected,
            string name)
        {
            if (actual.Length != expected.Length)
                throw new InvalidDataException($"Accessor '{name}' has an incompatible parameter count.");
            for (var index = 0; index < actual.Length; index++)
                ValidateSignatureType(actual[index], expected[index], $"parameter {index} of '{name}'");
        }

        private static void ValidateSignatureType(
            DecodedType actual,
            DecodedType expected,
            string position)
        {
            if (!HaveSameSignatureShape(actual, expected))
                throw new InvalidDataException($"The {position} does not match its declaration.");
        }

        private static bool HaveSameSignatureShape(DecodedType left, DecodedType right)
        {
            if (left.Kind != right.Kind
                || left.CanonicalName != right.CanonicalName
                || left.GenericIndex != right.GenericIndex
                || left.IsMethodGenericParameter != right.IsMethodGenericParameter
                || left.IsValueType != right.IsValueType
                || left.IsRequiredModifier != right.IsRequiredModifier
                || left.IsSzArray != right.IsSzArray
                || left.ModifierType?.CanonicalName != right.ModifierType?.CanonicalName
                || left.ArrayShape.Rank != right.ArrayShape.Rank
                || !SequenceEqual(left.ArrayShape.Sizes, right.ArrayShape.Sizes)
                || !SequenceEqual(left.ArrayShape.LowerBounds, right.ArrayShape.LowerBounds)
                || left.TypeArguments.Length != right.TypeArguments.Length)
            {
                return false;
            }
            if (left.ElementType is null != (right.ElementType is null))
                return false;
            if (left.ElementType is not null
                && !HaveSameSignatureShape(left.ElementType, right.ElementType!))
            {
                return false;
            }
            for (var index = 0; index < left.TypeArguments.Length; index++)
            {
                if (!HaveSameSignatureShape(left.TypeArguments[index], right.TypeArguments[index]))
                    return false;
            }
            return true;
        }

        private static bool SequenceEqual<T>(ImmutableArray<T> left, ImmutableArray<T> right) =>
            left.IsDefaultOrEmpty
                ? right.IsDefaultOrEmpty
                : !right.IsDefault && left.AsSpan().SequenceEqual(right.AsSpan());

        private bool IsVisibleAccessor(MethodDefinitionHandle handle) => !handle.IsNil
            && IsVisible(reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask);

        private string RenderAccessor(
            string keyword,
            MethodDefinitionHandle handle,
            string propertyAccessibility,
            SignatureContext context)
        {
            var method = reader.GetMethodDefinition(handle);
            var access = method.Attributes & MethodAttributes.MemberAccessMask;
            var accessibility = GetAccessibility(access);
            if (keyword == "set")
            {
                var signature = method.DecodeSignature(typeProvider, context);
                ValidateRootModifiers(
                    signature.ReturnType,
                    InitAccessorModifierTypes,
                    "property setter return type",
                    "System.Void");
                foreach (var parameterType in signature.ParameterTypes)
                {
                    ValidateModifiers(
                        parameterType,
                        NoModifierTypes,
                        "property setter parameter");
                }
                if (HasModifier(
                    signature.ReturnType,
                    "System.Runtime.CompilerServices.IsExternalInit"))
                {
                    keyword = "init";
                }
            }
            return accessibility == propertyAccessibility ? $"{keyword};" : $"{accessibility} {keyword};";
        }

        /// <summary>
        /// Collapses the flag set the compiler emits for an implicit interface implementation to
        /// "no modifier", which is what the C# declaration says.
        /// </summary>
        /// <remarks>
        /// Measured against known source rather than assumed — <c>Final|Virtual|NewSlot</c> is what
        /// <c>public int Value { get; set; }</c> emits on a class implementing an interface that
        /// declares <c>Value</c>. It is not <c>sealed override</c>, which is <c>Final|Virtual</c>
        /// <em>without</em> <c>NewSlot</c>, because an override reuses its base slot and a new slot
        /// is by definition not one. Rendering the two alike claimed an override relationship that
        /// does not exist, and comparing the raw flags rejected an interface-implementing getter
        /// beside an ordinary setter.
        /// <para>
        /// Restricted to class declarations: no interface case was observed, and a sealed default
        /// interface member is a different question left alone.
        /// </para>
        /// </remarks>
        private static MethodAttributes CollapseImplicitInterfaceImplementation(
            MethodAttributes semantics,
            bool isInterface) =>
            !isInterface && semantics == ImplicitInterfaceImplementation
                ? default
                : semantics;

        private string GetAccessorDeclarationModifiers(
            TypeDefinitionHandle typeHandle,
            IEnumerable<MethodDefinitionHandle> handles,
            string position)
        {
            const MethodAttributes semanticMask = MethodAttributes.Static
                | MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot
                | MethodAttributes.PinvokeImpl;
            var declaringType = reader.GetTypeDefinition(typeHandle);
            var isInterface = (declaringType.Attributes & TypeAttributes.Interface) != 0;
            var present = handles.Where(handle => !handle.IsNil).ToArray();
            if (present.Length == 0)
                throw new InvalidDataException($"The {position} has no getter, setter, adder, or remover.");

            // Only the visible accessors decide the declaration's modifiers. A private setter beside
            // a public getter is not part of the rendered declaration, and comparing its vtable flags
            // rejected the ordinary '{ get; private set; }' on an interface-implementing property.
            var visible = present
                .Where(handle => IsVisible(
                    reader.GetMethodDefinition(handle).Attributes & MethodAttributes.MemberAccessMask))
                .ToArray();
            var considered = visible.Length > 0 ? visible : present;

            var semantics = considered
                .Select(handle => CollapseImplicitInterfaceImplementation(
                    reader.GetMethodDefinition(handle).Attributes & semanticMask,
                    isInterface))
                .Distinct()
                .ToArray();
            if (semantics.Length != 1)
                throw new InvalidDataException($"The accessors for {position} have incompatible modifiers.");

            var attributes = semantics[0];
            var isStatic = (attributes & MethodAttributes.Static) != 0;
            var isAbstract = (attributes & MethodAttributes.Abstract) != 0;
            var isVirtual = (attributes & MethodAttributes.Virtual) != 0;
            var isFinal = (attributes & MethodAttributes.Final) != 0;
            var isNewSlot = (attributes & MethodAttributes.NewSlot) != 0;
            if ((attributes & MethodAttributes.PinvokeImpl) != 0
                || isAbstract && !isVirtual
                || isFinal && !isVirtual
                || isStatic && (isFinal || isNewSlot && !isVirtual)
                || isStatic && isVirtual && !isInterface)
            {
                throw new InvalidDataException(
                    isStatic && isVirtual && !isInterface
                        ? $"Static virtual accessors for {position} require an interface declaring type."
                        : $"The accessors for {position} have unsupported modifiers ({attributes}).");
            }

            if (isStatic)
            {
                if (isAbstract)
                    return "static abstract ";
                if (isVirtual)
                    return "static virtual ";
                return "static ";
            }
            if (isAbstract)
                return isNewSlot ? "abstract " : "abstract override ";
            if (!isVirtual)
                return string.Empty;
            if (isFinal)
                return "sealed override ";
            return isNewSlot ? "virtual " : "override ";
        }

        private Dictionary<int, ParameterHandle> GetPropertyParameterDefinitions(PropertyAccessors accessors)
        {
            var accessor = !accessors.Getter.IsNil ? accessors.Getter : accessors.Setter;
            if (accessor.IsNil)
                return [];
            return reader.GetMethodDefinition(accessor).GetParameters()
                .Where(item => reader.GetParameter(item).SequenceNumber > 0)
                .ToDictionary(item => (int)reader.GetParameter(item).SequenceNumber);
        }

        private string BuildMethodEcmaId(
            TypeDefinitionHandle typeHandle,
            MethodDefinition method,
            MethodSignature<DecodedType> signature)
        {
            var name = EscapeMemberName(reader.GetString(method.Name));
            if (signature.GenericParameterCount > 0)
                name += $"``{signature.GenericParameterCount}";
            var parameters = signature.ParameterTypes.Length == 0
                ? string.Empty
                : $"({string.Join(",", signature.ParameterTypes.Select(RenderEcmaType))})";
            var conversionReturn = IsConversionOperator(reader.GetString(method.Name))
                ? $"~{RenderEcmaType(signature.ReturnType)}"
                : string.Empty;
            return $"M:{GetEcmaTypeName(typeHandle)}.{name}{parameters}{conversionReturn}";
        }

        private void ValidateConstructor(
            TypeDefinitionHandle typeHandle,
            MethodDefinition method,
            MethodSignature<DecodedType> signature,
            int declaredGenericParameterCount,
            string name)
        {
            var typeName = GetSourceTypeName(typeHandle, includeNamespace: true);
            var attributes = method.Attributes;
            var isStatic = (attributes & MethodAttributes.Static) != 0;
            if ((attributes & (MethodAttributes.SpecialName | MethodAttributes.RTSpecialName))
                != (MethodAttributes.SpecialName | MethodAttributes.RTSpecialName))
            {
                throw new InvalidDataException($"The constructor '{typeName}.{name}' lacks constructor metadata flags.");
            }
            const MethodAttributes incompatibleFlags = MethodAttributes.Abstract
                | MethodAttributes.Virtual
                | MethodAttributes.Final
                | MethodAttributes.NewSlot;
            if ((attributes & incompatibleFlags) != 0)
            {
                throw new InvalidDataException(
                    $"The constructor '{typeName}.{name}' has constructor-incompatible method flags.");
            }
            if (!IsPlainVoid(signature.ReturnType))
                throw new InvalidDataException($"Constructor '{typeName}' must return System.Void.");
            if (signature.GenericParameterCount != 0 || declaredGenericParameterCount != 0)
                throw new InvalidDataException($"The constructor '{typeName}.{name}' cannot be generic.");
            if (signature.Header.CallingConvention != SignatureCallingConvention.Default)
                throw new InvalidDataException($"The constructor '{typeName}.{name}' has an unsupported calling convention.");

            if (name == ".ctor")
            {
                if (isStatic || !signature.Header.IsInstance)
                    throw new InvalidDataException($"Instance constructor '{typeName}.ctor' must be an instance method.");
                return;
            }

            if (!isStatic
                || signature.Header.IsInstance
                || signature.ParameterTypes.Length != 0
                || (attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Private)
                throw new InvalidDataException($"Type initializer '{typeName}.cctor' must be static and parameterless.");
        }

        private void ValidateOperator(
            TypeDefinitionHandle typeHandle,
            MethodDefinition method,
            MethodSignature<DecodedType> signature,
            int declaredGenericParameterCount,
            string name)
        {
            if (!OperatorNames.ContainsKey(name))
                throw new InvalidDataException($"Operator metadata name '{name}' is unsupported.");
            var attributes = method.Attributes;
            if ((attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public
                || (attributes & MethodAttributes.Static) == 0
                || (attributes & MethodAttributes.SpecialName) == 0
                || signature.Header.IsInstance
                || signature.Header.CallingConvention != SignatureCallingConvention.Default)
            {
                throw new InvalidDataException($"Operator '{name}' must be a public static special-name method.");
            }
            if (signature.GenericParameterCount != 0 || declaredGenericParameterCount != 0)
                throw new InvalidDataException($"Operator '{name}' cannot be generic.");
            if (IsPlainVoid(signature.ReturnType))
                throw new InvalidDataException($"Operator '{name}' cannot return System.Void.");
            // An operand may be passed by read-only reference: 'in' is legal on an operator and is
            // the only by-reference form that is -- plain 'ref', 'out' and 'ref readonly' are all
            // CS0631. Roslyn's own Workspaces assembly ships 'in' operands, and rejecting them
            // rejected the entire package. A by-reference return stays rejected, because a
            // by-reference operator return does not parse in any C# version.
            if (ContainsByReference(signature.ReturnType))
            {
                throw new InvalidDataException(
                    $"Operator '{name}' cannot have a by-reference return type.");
            }
            for (var index = 0; index < signature.ParameterTypes.Length; index++)
            {
                if (ContainsByReference(signature.ParameterTypes[index])
                    && !IsReadOnlyOperand(method, index, signature.ParameterTypes[index]))
                {
                    throw new InvalidDataException(
                        $"Operator '{name}' cannot have a by-reference parameter type unless it is read-only.");
                }
            }

            var expectedParameterCount = UnaryOperatorNames.Contains(name) ? 1 : 2;
            if (signature.ParameterTypes.Length != expectedParameterCount)
            {
                throw new InvalidDataException(
                    $"Operator '{name}' requires {expectedParameterCount} parameter(s).");
            }

            var declaringType = GetDeclaredSelfType(typeHandle);
            var isConversion = IsConversionOperator(name);
            // An 'in' operand reaches here as a by-reference type, so the operand shape is compared
            // against what it references rather than against the reference itself.
            var hasDeclaringParameter = signature.ParameterTypes.Any(type =>
                HaveSameSignatureShape(UnwrapByReference(type), declaringType));
            if (isConversion
                ? !hasDeclaringParameter && !HaveSameSignatureShape(signature.ReturnType, declaringType)
                : !hasDeclaringParameter)
            {
                throw new InvalidDataException(
                    $"Operator '{name}' must convert to, convert from, or operate on '{RenderEcmaType(declaringType)}'.");
            }
            if (isConversion && HaveSameSignatureShape(signature.ParameterTypes[0], signature.ReturnType))
                throw new InvalidDataException($"Conversion operator '{name}' must change type.");
            if (IncrementOperatorNames.Contains(name)
                && !HaveSameSignatureShape(signature.ReturnType, declaringType))
            {
                throw new InvalidDataException(
                    $"Increment or decrement operator '{name}' must return '{RenderEcmaType(declaringType)}'.");
            }
            if (BooleanOperatorNames.Contains(name)
                && (signature.ReturnType.Kind != DecodedTypeKind.Named
                    || signature.ReturnType.CanonicalName != "System.Boolean"))
            {
                throw new InvalidDataException($"Boolean operator '{name}' must return System.Boolean.");
            }
        }

        private DecodedType GetDeclaredSelfType(TypeDefinitionHandle handle)
        {
            var definition = reader.GetTypeDefinition(handle);
            var canonicalName = GetEcmaTypeName(handle);
            var baseTypeName = definition.BaseType.IsNil
                ? null
                : GetEntityTypeName(definition.BaseType);
            var isValueType = baseTypeName is "System.ValueType" or "System.Enum";
            var arguments = definition.GetGenericParameters()
                .Select(item => reader.GetGenericParameter(item))
                .OrderBy(item => item.Index)
                .Select(item => new DecodedType(
                    DecodedTypeKind.GenericParameter,
                    GenericName: reader.GetString(item.Name),
                    GenericIndex: item.Index))
                .ToImmutableArray();
            return arguments.Length == 0
                ? new DecodedType(DecodedTypeKind.Named, canonicalName, IsValueType: isValueType)
                : new DecodedType(
                    DecodedTypeKind.GenericInstance,
                    canonicalName,
                    IsValueType: isValueType,
                    TypeArguments: arguments);
        }

        private static readonly HashSet<string> UnaryOperatorNames = new(StringComparer.Ordinal)
        {
            "op_UnaryPlus",
            "op_UnaryNegation",
            "op_LogicalNot",
            "op_OnesComplement",
            "op_Increment",
            "op_Decrement",
            "op_True",
            "op_False",
            "op_Implicit",
            "op_Explicit",
            "op_CheckedUnaryNegation",
            "op_CheckedIncrement",
            "op_CheckedDecrement",
            "op_CheckedExplicit",
        };

        private static readonly HashSet<string> IncrementOperatorNames = new(StringComparer.Ordinal)
        {
            "op_Increment",
            "op_Decrement",
            "op_CheckedIncrement",
            "op_CheckedDecrement",
        };

        private static readonly HashSet<string> BooleanOperatorNames = new(StringComparer.Ordinal)
        {
            "op_True",
            "op_False",
            "op_Equality",
            "op_Inequality",
            "op_GreaterThan",
            "op_LessThan",
            "op_GreaterThanOrEqual",
            "op_LessThanOrEqual",
        };

        private string GetEcmaTypeName(TypeDefinitionHandle handle)
        {
            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var declaringType = definition.GetDeclaringType();
            if (!declaringType.IsNil)
                return $"{GetEcmaTypeName(declaringType)}.{name}";
            var @namespace = reader.GetString(definition.Namespace);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }

        private string GetSourceTypeName(TypeDefinitionHandle handle, bool includeNamespace)
        {
            var definition = reader.GetTypeDefinition(handle);
            var metadataName = reader.GetString(definition.Name);
            var ownArity = ReadArity(metadataName);
            var genericParameters = definition.GetGenericParameters()
                .Select(item => reader.GetGenericParameter(item))
                .OrderBy(item => item.Index)
                .Select(item => reader.GetString(item.Name))
                .ToArray();
            var ownParameters = ownArity == 0
                ? []
                : genericParameters.Skip(Math.Max(0, genericParameters.Length - ownArity)).ToArray();
            var name = StripArity(metadataName)
                + (ownParameters.Length == 0 ? string.Empty : $"<{string.Join(", ", ownParameters)}>");
            if (!includeNamespace)
                return name;
            var declaringType = definition.GetDeclaringType();
            if (!declaringType.IsNil)
                return $"{GetSourceTypeName(declaringType, includeNamespace)}.{name}";
            var @namespace = reader.GetString(definition.Namespace);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }

        private SignatureContext CreateTypeContext(TypeDefinition definition) => new(
            definition.GetGenericParameters()
                .Select(item => reader.GetGenericParameter(item))
                .OrderBy(item => item.Index)
                .Select(item => reader.GetString(item.Name))
                .ToImmutableArray(),
            ImmutableArray<string>.Empty);

        private byte? GetInheritedNullableContext(TypeDefinitionHandle typeHandle)
        {
            var declaringType = reader.GetTypeDefinition(typeHandle).GetDeclaringType();
            if (!declaringType.IsNil)
            {
                return ReadNullableContext(
                    reader.GetTypeDefinition(declaringType).GetCustomAttributes(),
                    GetInheritedNullableContext(declaringType));
            }
            return ReadNullableContext(reader.GetModuleDefinition().GetCustomAttributes(), null);
        }

        private byte? ReadNullableContext(CustomAttributeHandleCollection handles, byte? fallback)
        {
            foreach (var handle in handles)
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (GetAttributeTypeName(attribute) != "System.Runtime.CompilerServices.NullableContextAttribute")
                    continue;
                var value = attribute.DecodeValue(typeProvider);
                if (value.FixedArguments.Length == 1 && value.FixedArguments[0].Value is byte flag)
                    return ValidateNullableFlag(flag, "NullableContextAttribute");
                throw new InvalidDataException("NullableContextAttribute had an unsupported encoding.");
            }
            return fallback;
        }

        private NullableTransform? ReadNullableFlags(CustomAttributeHandleCollection? handles)
        {
            if (handles is null)
                return null;
            foreach (var handle in handles.Value)
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (GetAttributeTypeName(attribute) != "System.Runtime.CompilerServices.NullableAttribute")
                    continue;
                var value = attribute.DecodeValue(typeProvider);
                if (value.FixedArguments.Length != 1)
                    throw new InvalidDataException("NullableAttribute had an unsupported encoding.");
                if (value.FixedArguments[0].Value is byte single)
                    return new NullableTransform(
                        [ValidateNullableFlag(single, "NullableAttribute")],
                        IsSingle: true);
                if (value.FixedArguments[0].Value is ImmutableArray<CustomAttributeTypedArgument<DecodedType>> values)
                {
                    return new NullableTransform(
                        values.Select(item => ValidateNullableFlag(
                            (byte)item.Value!,
                            "NullableAttribute")).ToArray(),
                        IsSingle: false);
                }
                throw new InvalidDataException("NullableAttribute had an unsupported encoding.");
            }
            return null;
        }

        private static byte ValidateNullableFlag(byte flag, string attributeName)
        {
            if (flag > 2)
                throw new InvalidDataException($"{attributeName} contains invalid flag '{flag}'.");
            return flag;
        }

        private static bool IsNullableClassConstraint(
            NullableTransform? transform,
            string parameterName)
        {
            if (transform is null)
                return false;
            if (transform.Flags.Length != 1)
            {
                throw new InvalidDataException(
                    $"NullableAttribute supplied {transform.Flags.Length} flags for class constraint '{parameterName}'.");
            }
            return transform.Flags[0] == 2;
        }

        private string?[]? ReadTupleNames(CustomAttributeHandleCollection? handles)
        {
            if (handles is null)
                return null;
            foreach (var handle in handles.Value)
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (GetAttributeTypeName(attribute) != "System.Runtime.CompilerServices.TupleElementNamesAttribute")
                    continue;
                var value = attribute.DecodeValue(typeProvider);
                if (value.FixedArguments.Length == 1
                    && value.FixedArguments[0].Value is ImmutableArray<CustomAttributeTypedArgument<DecodedType>> values)
                {
                    return values.Select(item => item.Value as string).ToArray();
                }
                throw new InvalidDataException("TupleElementNamesAttribute had an unsupported encoding.");
            }
            return null;
        }

        private bool HasAttribute(CustomAttributeHandleCollection handles, string typeName) =>
            HasAttribute((CustomAttributeHandleCollection?)handles, typeName);

        private bool HasAttribute(CustomAttributeHandleCollection? handles, string typeName)
        {
            if (handles is null)
                return false;
            foreach (var handle in handles.Value)
            {
                if (GetAttributeTypeName(reader.GetCustomAttribute(handle)) == typeName)
                    return true;
            }
            return false;
        }

        private static DecodedType ApplyNullable(
            DecodedType type,
            NullableTransform? transform,
            byte? context)
        {
            var index = 0;
            var result = ApplyNullable(type, transform, context, ref index);
            if (transform is { IsSingle: false } && index != transform.Flags.Length)
            {
                throw new InvalidDataException(
                    $"NullableAttribute supplied {transform.Flags.Length} flags for {index} signature positions.");
            }
            return result;
        }

        private static DecodedType ApplyNullable(
            DecodedType type,
            NullableTransform? transform,
            byte? context,
            ref int index)
        {
            if (type.Kind is DecodedTypeKind.ByReference or DecodedTypeKind.Pointer or DecodedTypeKind.Modified)
                return type with { ElementType = ApplyNullable(type.ElementType!, transform, context, ref index) };

            if (type.Kind == DecodedTypeKind.GenericInstance
                && type.CanonicalName == "System.Nullable`1")
            {
                var nullableArguments = ImmutableArray.CreateBuilder<DecodedType>(type.TypeArguments.Length);
                foreach (var argument in type.TypeArguments)
                    nullableArguments.Add(ApplyNullable(argument, transform, context, ref index));
                return type with { TypeArguments = nullableArguments.MoveToImmutable() };
            }

            // A non-generic value type is skipped entirely by the encoding: it can never be
            // annotated, so the compiler emits no flag for it and consuming one here would shift
            // every later position by one. A *generic* value type is not skipped -- it carries an
            // explicit 0 ahead of its arguments -- so only the argument-less case returns early.
            // https://github.com/dotnet/roslyn/blob/main/docs/features/nullable-metadata.md
            if (type.Kind == DecodedTypeKind.Named && type.IsValueType)
                return type;

            byte annotation;
            if (transform is { IsSingle: true })
            {
                annotation = transform.Flags[0];
            }
            else if (transform is not null)
            {
                if (index >= transform.Flags.Length)
                {
                    throw new InvalidDataException(
                        $"NullableAttribute does not supply every signature position; missing '{type.CanonicalName ?? type.Kind.ToString()}'.");
                }
                annotation = transform.Flags[index++];
            }
            else
            {
                annotation = context ?? 0;
            }
            if (type.Kind == DecodedTypeKind.Array)
            {
                return type with
                {
                    NullableAnnotation = annotation,
                    ElementType = ApplyNullable(type.ElementType!, transform, context, ref index),
                };
            }

            if (type.Kind == DecodedTypeKind.GenericInstance)
            {
                var arguments = ImmutableArray.CreateBuilder<DecodedType>(type.TypeArguments.Length);
                foreach (var argument in type.TypeArguments)
                    arguments.Add(ApplyNullable(argument, transform, context, ref index));
                return type with { NullableAnnotation = annotation, TypeArguments = arguments.MoveToImmutable() };
            }

            return type with { NullableAnnotation = annotation };
        }

        private static string RenderCSharp(DecodedType type, IReadOnlyList<string?>? tupleNames)
        {
            var tupleIndex = 0;
            var result = RenderCSharp(type, tupleNames, ref tupleIndex);
            if (tupleNames is not null && tupleIndex != tupleNames.Count)
            {
                throw new InvalidDataException(
                    $"TupleElementNamesAttribute supplied {tupleNames.Count} names for {tupleIndex} tuple elements.");
            }
            return result;
        }

        private static string RenderCSharp(
            DecodedType type,
            IReadOnlyList<string?>? tupleNames,
            ref int tupleIndex)
        {
            switch (type.Kind)
            {
                case DecodedTypeKind.ByReference:
                    throw new InvalidDataException(
                        "A nested or otherwise unconsumed by-reference type cannot be rendered as C#.");
                case DecodedTypeKind.Modified:
                    return RenderCSharp(type.ElementType!, tupleNames, ref tupleIndex);
                case DecodedTypeKind.Pointer:
                    return RenderCSharp(type.ElementType!, tupleNames, ref tupleIndex) + "*";
                case DecodedTypeKind.Array:
                {
                    string suffix;
                    if (type.IsSzArray)
                    {
                        if (type.ArrayShape.Rank != 1
                            || type.ArrayShape.Sizes.Length > 0
                            || type.ArrayShape.LowerBounds.Length > 0)
                        {
                            throw new InvalidDataException(
                                "An SZ array signature has an invalid rank, size, or lower-bound shape.");
                        }
                        suffix = "[]";
                    }
                    else
                    {
                        if (type.ArrayShape.Rank == 1)
                        {
                            throw new InvalidDataException(
                                "A rank-one non-SZ array signature cannot be rendered as C# without losing its metadata shape.");
                        }
                        if (type.ArrayShape.Rank is < 2 or > MaximumArrayRank
                            || type.ArrayShape.Sizes.Length > 0
                            || type.ArrayShape.LowerBounds.Length != type.ArrayShape.Rank
                            || type.ArrayShape.LowerBounds.Any(bound => bound != 0))
                        {
                            throw new InvalidDataException(
                                $"A multidimensional array signature with invalid rank, explicit sizes, or lower bounds cannot be rendered as C# "
                                + $"(rank {type.ArrayShape.Rank}, sizes [{string.Join(',', type.ArrayShape.Sizes)}], "
                                + $"lower bounds [{string.Join(',', type.ArrayShape.LowerBounds)}]).");
                        }
                        suffix = $"[{new string(',', type.ArrayShape.Rank - 1)}]";
                    }
                    var element = RenderCSharp(type.ElementType!, tupleNames, ref tupleIndex);
                    return element + suffix + NullableSuffix(type);
                }
                case DecodedTypeKind.GenericParameter:
                    return type.GenericName! + NullableSuffix(type);
                case DecodedTypeKind.GenericInstance:
                {
                    if (type.CanonicalName == "System.Nullable`1" && type.TypeArguments.Length == 1)
                        return RenderCSharp(type.TypeArguments[0], tupleNames, ref tupleIndex) + "?";
                    if (IsValueTuple(type.CanonicalName))
                    {
                        var tupleArguments = ExpandTupleArguments(type).ToArray();
                        var directNameIndex = tupleIndex;
                        tupleIndex += tupleArguments.Length;
                        if (tupleNames is not null && tupleIndex > tupleNames.Count)
                        {
                            throw new InvalidDataException(
                                "TupleElementNamesAttribute does not supply every tuple element name.");
                        }
                        var elements = new List<string>();
                        for (var index = 0; index < tupleArguments.Length; index++)
                        {
                            var rendered = RenderCSharp(tupleArguments[index], tupleNames, ref tupleIndex);
                            var tupleName = tupleNames is not null
                                ? tupleNames[directNameIndex + index]
                                : null;
                            elements.Add(string.IsNullOrEmpty(tupleName) ? rendered : $"{rendered} {tupleName}");
                        }
                        return $"({string.Join(", ", elements)})" + NullableSuffix(type);
                    }

                    var arguments = new string[type.TypeArguments.Length];
                    for (var index = 0; index < type.TypeArguments.Length; index++)
                        arguments[index] = RenderCSharp(type.TypeArguments[index], tupleNames, ref tupleIndex);
                    return RenderConstructedType(type, arguments, ", ", "<", ">")
                        + NullableSuffix(type);
                }
                case DecodedTypeKind.Named:
                    return RenderNamedType(type.CanonicalName!) + NullableSuffix(type);
                default:
                    throw new InvalidDataException($"Signature type kind '{type.Kind}' is unsupported.");
            }
        }

        private static string RenderEcmaType(DecodedType type) => type.Kind switch
        {
            DecodedTypeKind.ByReference => RenderEcmaType(type.ElementType!) + "@",
            DecodedTypeKind.Modified => RenderEcmaType(type.ElementType!),
            DecodedTypeKind.Pointer => RenderEcmaType(type.ElementType!) + "*",
            DecodedTypeKind.Array when type.IsSzArray => RenderEcmaType(type.ElementType!) + "[]",
            DecodedTypeKind.Array => RenderEcmaType(type.ElementType!) + RenderEcmaArrayShape(type.ArrayShape),
            DecodedTypeKind.GenericParameter => type.IsMethodGenericParameter
                ? $"``{type.GenericIndex}"
                : $"`{type.GenericIndex}",
            DecodedTypeKind.GenericInstance => RenderConstructedType(
                type,
                type.TypeArguments.Select(RenderEcmaType).ToArray(),
                ",",
                "{",
                "}"),
            DecodedTypeKind.Named => type.CanonicalName!,
            _ => throw new InvalidDataException($"Signature type kind '{type.Kind}' is unsupported."),
        };

        private static string RenderConstructedType(
            DecodedType type,
            IReadOnlyList<string> renderedArguments,
            string argumentSeparator,
            string openArguments,
            string closeArguments)
        {
            var segments = type.CanonicalName!.Split('.');
            var argumentIndex = 0;
            for (var segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
            {
                var segment = segments[segmentIndex];
                var arityMarker = segment.LastIndexOf('`');
                if (arityMarker < 0)
                    continue;
                if (!int.TryParse(segment[(arityMarker + 1)..], out var arity) || arity < 1)
                    throw new InvalidDataException($"Generic type segment '{segment}' has an invalid arity.");
                if (argumentIndex + arity > type.TypeArguments.Length)
                {
                    throw new InvalidDataException(
                        $"Constructed type '{type.CanonicalName}' does not provide its declared generic arguments.");
                }

                var segmentArguments = renderedArguments
                    .Skip(argumentIndex)
                    .Take(arity);
                segments[segmentIndex] = segment[..arityMarker]
                    + openArguments
                    + string.Join(argumentSeparator, segmentArguments)
                    + closeArguments;
                argumentIndex += arity;
            }

            if (argumentIndex != type.TypeArguments.Length)
            {
                throw new InvalidDataException(
                    $"Constructed type '{type.CanonicalName}' has {type.TypeArguments.Length} generic arguments but declares {argumentIndex}.");
            }
            return string.Join('.', segments);
        }

        private static string RenderEcmaArrayShape(ArrayShape shape)
        {
            var dimensions = new string[shape.Rank];
            for (var index = 0; index < dimensions.Length; index++)
            {
                var lowerBound = index < shape.LowerBounds.Length ? shape.LowerBounds[index] : 0;
                var size = index < shape.Sizes.Length ? shape.Sizes[index] : 0;
                dimensions[index] = size > 0 ? $"{lowerBound}:{size}" : $"{lowerBound}:";
            }
            return $"[{string.Join(",", dimensions)}]";
        }

        private static string[] CollectTypeNames(DecodedType type)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            CollectTypeNames(type, names);
            return names.OrderBy(item => item, StringComparer.Ordinal).ToArray();
        }

        private static void CollectTypeNames(DecodedType type, ISet<string> names)
        {
            if (type.Kind is DecodedTypeKind.Named or DecodedTypeKind.GenericInstance)
                names.Add(StripArityFromQualifiedName(type.CanonicalName!));
            if (type.ElementType is not null)
                CollectTypeNames(type.ElementType, names);
            foreach (var argument in type.TypeArguments)
                CollectTypeNames(argument, names);
        }

        private static string GetParameterModifier(
            DecodedType type,
            ParameterAttributes attributes,
            bool isParamArray)
        {
            if (!IsByReference(type))
                return isParamArray ? "params" : string.Empty;
            if ((attributes & ParameterAttributes.Out) != 0)
                return "out";
            if ((attributes & ParameterAttributes.In) != 0 || IsReadOnlyByReference(type))
                return "in";
            return "ref";
        }

        private static string GetReturnPrefix(DecodedType type, ParameterAttributes attributes)
        {
            if (!IsByReference(type))
                return string.Empty;
            return (attributes & ParameterAttributes.In) != 0 || IsReadOnlyByReference(type)
                ? "ref readonly "
                : "ref ";
        }

        // On a non-virtual method the compiler encodes 'in' as a plain by-reference type and records
        // the read-only-ness on the parameter row -- ParameterAttributes.In plus [IsReadOnly] -- so
        // the modreq form alone misses it. Both spellings are accepted because a virtual or
        // interface member does carry the modreq, where it is part of the signature's identity.
        private bool IsReadOnlyOperand(MethodDefinition method, int index, DecodedType type)
        {
            if (IsReadOnlyByReference(type))
                return true;

            foreach (var handle in method.GetParameters())
            {
                var parameter = reader.GetParameter(handle);
                if (parameter.SequenceNumber != index + 1)
                    continue;

                return (parameter.Attributes & ParameterAttributes.In) != 0
                    || HasAttribute(
                        parameter.GetCustomAttributes(),
                        "System.Runtime.CompilerServices.IsReadOnlyAttribute");
            }

            return false;
        }

        private static bool IsReadOnlyByReference(DecodedType type) =>
            HasModifier(type, "System.Runtime.InteropServices.InAttribute")
            || HasModifier(type, "System.Runtime.CompilerServices.IsReadOnlyAttribute");

        private static bool HasModifier(DecodedType type, string name)
        {
            if (type.Kind == DecodedTypeKind.Modified && type.ModifierType?.CanonicalName == name)
                return true;
            return type.ElementType is not null && HasModifier(type.ElementType, name);
        }

        private static void ValidateModifiers(
            DecodedType type,
            IReadOnlySet<string> allowedModifiers,
            string position,
            bool requireByReference = false)
        {
            ValidateModifiers(
                type,
                allowedModifiers,
                position,
                requireByReference,
                onByReferencePath: IsByReference(type));
        }

        private static void ValidateModifiers(
            DecodedType type,
            IReadOnlySet<string> allowedModifiers,
            string position,
            bool requireByReference,
            bool onByReferencePath)
        {
            if (type.Kind == DecodedTypeKind.Modified)
            {
                var modifier = type.ModifierType?.CanonicalName
                    ?? throw new InvalidDataException($"A modifier on {position} has no type identity.");
                if (!allowedModifiers.Contains(modifier)
                    || !type.IsRequiredModifier
                    || requireByReference && !onByReferencePath)
                {
                    throw new InvalidDataException(
                        $"Signature modifier '{modifier}' on {position} is unsupported.");
                }
            }

            if (type.ElementType is not null)
            {
                var elementOnByReferencePath = type.Kind switch
                {
                    DecodedTypeKind.Modified => onByReferencePath,
                    DecodedTypeKind.ByReference => true,
                    _ => false,
                };
                ValidateModifiers(
                    type.ElementType,
                    allowedModifiers,
                    position,
                    requireByReference,
                    elementOnByReferencePath);
            }
            foreach (var argument in type.TypeArguments)
            {
                ValidateModifiers(
                    argument,
                    allowedModifiers,
                    position,
                    requireByReference,
                    onByReferencePath: false);
            }
        }

        private static void ValidateRootModifiers(
            DecodedType type,
            HashSet<string> allowedModifiers,
            string position,
            string? requiredUnderlyingCanonicalName = null,
            bool requireModifier = false)
        {
            var current = type;
            var foundModifier = false;
            while (current.Kind == DecodedTypeKind.Modified)
            {
                foundModifier = true;
                var modifier = current.ModifierType?.CanonicalName
                    ?? throw new InvalidDataException($"A modifier on {position} has no type identity.");
                if (!allowedModifiers.Contains(modifier) || !current.IsRequiredModifier)
                {
                    throw new InvalidDataException(
                        $"Signature modifier '{modifier}' on {position} is unsupported.");
                }
                current = current.ElementType!;
            }

            ValidateModifiers(current, NoModifierTypes, position);
            if (requireModifier && !foundModifier)
                throw new InvalidDataException($"The required signature modifier on {position} is missing.");
            if (requiredUnderlyingCanonicalName is not null
                && current.CanonicalName != requiredUnderlyingCanonicalName)
            {
                throw new InvalidDataException(
                    $"The signature modifier on {position} has unsupported underlying type '{current.CanonicalName}'.");
            }
        }

        private static bool IsByReference(DecodedType type)
        {
            while (type.Kind == DecodedTypeKind.Modified)
                type = type.ElementType!;
            return type.Kind == DecodedTypeKind.ByReference;
        }

        private static bool ContainsByReference(DecodedType type) =>
            type.Kind == DecodedTypeKind.ByReference
            || type.ElementType is not null && ContainsByReference(type.ElementType)
            || type.TypeArguments.Any(ContainsByReference);

        private static DecodedType UnwrapByReference(DecodedType type)
        {
            while (type.Kind == DecodedTypeKind.Modified)
                type = type.ElementType!;
            return type.Kind == DecodedTypeKind.ByReference ? type.ElementType! : type;
        }

        private static DecodedType StripModifiers(DecodedType type)
        {
            while (type.Kind == DecodedTypeKind.Modified)
                type = type.ElementType!;
            return type;
        }

        private static bool IsVoid(DecodedType type)
        {
            while (type.Kind == DecodedTypeKind.Modified)
                type = type.ElementType!;
            return type.Kind == DecodedTypeKind.Named && type.CanonicalName == "System.Void";
        }

        private static bool IsPlainVoid(DecodedType type) =>
            type.Kind == DecodedTypeKind.Named && type.CanonicalName == "System.Void";

        private static string GetMethodDisplayName(
            string metadataName,
            DecodedType returnType,
            IReadOnlyList<string?>? tupleNames)
        {
            if (!OperatorNames.TryGetValue(metadataName, out var operatorName))
                return metadataName;
            if (IsConversionOperator(metadataName))
                return $"{operatorName} {RenderCSharp(UnwrapByReference(returnType), tupleNames)}";
            return operatorName;
        }

        private static bool IsConversionOperator(string metadataName) =>
            metadataName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit";

        private static string NullableSuffix(DecodedType type) =>
            type.NullableAnnotation == 2 && !type.IsValueType ? "?" : string.Empty;

        private static string RenderNamedType(string canonicalName) => canonicalName switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Char" => "char",
            "System.Decimal" => "decimal",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.IntPtr" => "nint",
            "System.UIntPtr" => "nuint",
            "System.Object" => "object",
            "System.String" => "string",
            "System.Void" => "void",
            _ => StripArityFromQualifiedName(canonicalName),
        };

        private static bool IsValueTuple(string? canonicalName) =>
            canonicalName is not null
            && canonicalName.StartsWith("System.ValueTuple`", StringComparison.Ordinal);

        private static IEnumerable<DecodedType> ExpandTupleArguments(DecodedType tuple)
        {
            if (tuple.TypeArguments.Length != 8 || tuple.CanonicalName != "System.ValueTuple`8")
                return tuple.TypeArguments;
            var result = tuple.TypeArguments.Take(7).ToList();
            var rest = tuple.TypeArguments[7];
            if (rest.Kind != DecodedTypeKind.GenericInstance || !IsValueTuple(rest.CanonicalName))
                throw new InvalidDataException("An eight-element ValueTuple has a non-tuple rest element.");
            result.AddRange(ExpandTupleArguments(rest));
            return result;
        }

        private static string EscapeMemberName(string name) => name
            .Replace('.', '#')
            .Replace('<', '{')
            .Replace('>', '}');

        private static string StripArity(string name)
        {
            var marker = name.IndexOf('`');
            return marker < 0 ? name : name[..marker];
        }

        private static int ReadArity(string name)
        {
            var marker = name.LastIndexOf('`');
            return marker < 0
                ? 0
                : int.Parse(name.AsSpan(marker + 1), CultureInfo.InvariantCulture);
        }

        private static string StripArityFromQualifiedName(string name)
        {
            var result = new StringBuilder(name.Length);
            for (var index = 0; index < name.Length; index++)
            {
                if (name[index] != '`')
                {
                    result.Append(name[index]);
                    continue;
                }
                index++;
                while (index < name.Length && char.IsDigit(name[index]))
                    index++;
                index--;
            }
            return result.ToString();
        }

        private static string EscapeString(string value) => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        private static string EscapeCharacter(char value) => value switch
        {
            '\\' => "\\\\",
            '\'' => "\\'",
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ => value.ToString(),
        };

        private string GetDefinitionTypeName(TypeDefinitionHandle handle)
        {
            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var declaringType = definition.GetDeclaringType();
            if (!declaringType.IsNil)
                return $"{GetDefinitionTypeName(declaringType)}.{name}";
            var @namespace = reader.GetString(definition.Namespace);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }

        private string GetReferenceTypeName(TypeReferenceHandle handle)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                return $"{GetReferenceTypeName((TypeReferenceHandle)reference.ResolutionScope)}.{name}";
            var @namespace = reader.GetString(reference.Namespace);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }
    }

    private sealed class MetadataTypeProvider(MetadataReader reader) :
        ISignatureTypeProvider<DecodedType, SignatureContext>,
        ICustomAttributeTypeProvider<DecodedType>
    {
        public DecodedType GetArrayType(DecodedType elementType, ArrayShape shape) => new(
            DecodedTypeKind.Array,
            ElementType: elementType,
            ArrayShape: shape,
            IsSzArray: false,
            IsValueType: false);

        public DecodedType GetByReferenceType(DecodedType elementType) => new(
            DecodedTypeKind.ByReference,
            ElementType: elementType,
            IsValueType: false);

        public DecodedType GetFunctionPointerType(MethodSignature<DecodedType> signature) =>
            throw new InvalidDataException("Function-pointer signatures are unsupported.");

        public DecodedType GetGenericInstantiation(
            DecodedType genericType,
            ImmutableArray<DecodedType> typeArguments) => new(
                DecodedTypeKind.GenericInstance,
                genericType.CanonicalName,
                IsValueType: genericType.IsValueType,
                TypeArguments: typeArguments);

        public DecodedType GetGenericMethodParameter(SignatureContext genericContext, int index) => new(
            DecodedTypeKind.GenericParameter,
            GenericName: GetGenericName(genericContext.MethodParameters, index, "method"),
            GenericIndex: index,
            IsMethodGenericParameter: true);

        public DecodedType GetGenericTypeParameter(SignatureContext genericContext, int index) => new(
            DecodedTypeKind.GenericParameter,
            GenericName: GetGenericName(genericContext.TypeParameters, index, "type"),
            GenericIndex: index);

        public DecodedType GetModifiedType(
            DecodedType modifier,
            DecodedType unmodifiedType,
            bool isRequired) => new(
                DecodedTypeKind.Modified,
                ElementType: unmodifiedType,
                ModifierType: modifier,
                IsRequiredModifier: isRequired,
                IsValueType: unmodifiedType.IsValueType);

        public DecodedType GetPinnedType(DecodedType elementType) =>
            throw new InvalidDataException("Pinned public signatures are unsupported.");

        public DecodedType GetPointerType(DecodedType elementType) => new(
            DecodedTypeKind.Pointer,
            ElementType: elementType,
            IsValueType: true);

        public DecodedType GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            var (name, isValueType) = typeCode switch
            {
                PrimitiveTypeCode.Boolean => ("System.Boolean", true),
                PrimitiveTypeCode.Byte => ("System.Byte", true),
                PrimitiveTypeCode.SByte => ("System.SByte", true),
                PrimitiveTypeCode.Char => ("System.Char", true),
                PrimitiveTypeCode.Int16 => ("System.Int16", true),
                PrimitiveTypeCode.UInt16 => ("System.UInt16", true),
                PrimitiveTypeCode.Int32 => ("System.Int32", true),
                PrimitiveTypeCode.UInt32 => ("System.UInt32", true),
                PrimitiveTypeCode.Int64 => ("System.Int64", true),
                PrimitiveTypeCode.UInt64 => ("System.UInt64", true),
                PrimitiveTypeCode.Single => ("System.Single", true),
                PrimitiveTypeCode.Double => ("System.Double", true),
                PrimitiveTypeCode.IntPtr => ("System.IntPtr", true),
                PrimitiveTypeCode.UIntPtr => ("System.UIntPtr", true),
                PrimitiveTypeCode.Object => ("System.Object", false),
                PrimitiveTypeCode.String => ("System.String", false),
                PrimitiveTypeCode.TypedReference => ("System.TypedReference", true),
                PrimitiveTypeCode.Void => ("System.Void", true),
                _ => throw new InvalidDataException($"Primitive signature type '{typeCode}' is unsupported."),
            };
            return new DecodedType(DecodedTypeKind.Named, name, IsValueType: isValueType);
        }

        public DecodedType GetSZArrayType(DecodedType elementType) => new(
            DecodedTypeKind.Array,
            ElementType: elementType,
            ArrayShape: new ArrayShape(1, ImmutableArray<int>.Empty, ImmutableArray<int>.Empty),
            IsSzArray: true,
            IsValueType: false);

        public DecodedType GetTypeFromDefinition(
            MetadataReader metadataReader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
        {
            var definition = metadataReader.GetTypeDefinition(handle);
            return new DecodedType(
                DecodedTypeKind.Named,
                GetDefinitionTypeName(handle),
                IsValueType: rawTypeKind == (byte)SignatureTypeKind.ValueType
                    || IsKnownValueType(metadataReader.GetString(definition.Namespace), metadataReader.GetString(definition.Name)));
        }

        public DecodedType GetTypeFromReference(
            MetadataReader metadataReader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var reference = metadataReader.GetTypeReference(handle);
            return new DecodedType(
                DecodedTypeKind.Named,
                GetReferenceTypeName(handle),
                IsValueType: rawTypeKind == (byte)SignatureTypeKind.ValueType
                    || IsKnownValueType(metadataReader.GetString(reference.Namespace), metadataReader.GetString(reference.Name)));
        }

        public DecodedType GetTypeFromSpecification(
            MetadataReader metadataReader,
            SignatureContext genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind) => metadataReader.GetTypeSpecification(handle)
                .DecodeSignature(this, genericContext);

        public DecodedType GetTypeFromSerializedName(string name)
        {
            if (name.IndexOfAny(['[', ']', '*', '&', '`']) >= 0)
            {
                throw new InvalidDataException(
                    $"Serialized System.Type argument '{name}' uses an unsupported compound type form.");
            }
            var canonicalName = StripAssemblyQualification(name).Replace('+', '.');
            return new DecodedType(DecodedTypeKind.Named, canonicalName);
        }

        public PrimitiveTypeCode GetUnderlyingEnumType(DecodedType type)
        {
            // This assembly's own definition wins: a target framework without a given framework
            // enum is routinely served by a locally compiled polyfill of the same name, and the
            // local metadata is authoritative about the local shape.
            foreach (var handle in reader.TypeDefinitions)
            {
                if (GetDefinitionTypeName(handle) != type.CanonicalName)
                    continue;
                var definition = reader.GetTypeDefinition(handle);
                foreach (var fieldHandle in definition.GetFields())
                {
                    var field = reader.GetFieldDefinition(fieldHandle);
                    if (reader.GetString(field.Name) != "value__")
                        continue;
                    var underlying = field.DecodeSignature(this, SignatureContext.Empty);
                    return GetPrimitiveTypeCode(underlying.CanonicalName);
                }
            }
            if (type.CanonicalName is { } canonicalName
                && WellKnownEnumUnderlyingTypes.TryGetValue(canonicalName, out var wellKnown))
            {
                return wellKnown;
            }

            throw new InvalidDataException(
                $"Cannot determine the underlying type of external enum '{type.CanonicalName}' without resolving its assembly.");
        }

        public DecodedType GetSystemType() => new(DecodedTypeKind.Named, "System.Type");

        public bool IsSystemType(DecodedType type) => type.CanonicalName == "System.Type";

        private string GetDefinitionTypeName(TypeDefinitionHandle handle)
        {
            var definition = reader.GetTypeDefinition(handle);
            var name = reader.GetString(definition.Name);
            var declaringType = definition.GetDeclaringType();
            if (!declaringType.IsNil)
                return $"{GetDefinitionTypeName(declaringType)}.{name}";
            var @namespace = reader.GetString(definition.Namespace);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }

        private string GetReferenceTypeName(TypeReferenceHandle handle)
        {
            var reference = reader.GetTypeReference(handle);
            var name = reader.GetString(reference.Name);
            if (reference.ResolutionScope.Kind == HandleKind.TypeReference)
                return $"{GetReferenceTypeName((TypeReferenceHandle)reference.ResolutionScope)}.{name}";
            var @namespace = reader.GetString(reference.Namespace);
            return string.IsNullOrEmpty(@namespace) ? name : $"{@namespace}.{name}";
        }

        private static string GetGenericName(ImmutableArray<string> names, int index, string owner)
        {
            if ((uint)index >= (uint)names.Length)
                throw new InvalidDataException($"A signature references missing {owner} generic parameter {index}.");
            return names[index];
        }

        private static bool IsKnownValueType(string @namespace, string name) =>
            @namespace == "System" && name is "Decimal" or "DateTime" or "Guid" or "ValueType"
            || @namespace == "System" && name.StartsWith("ValueTuple`", StringComparison.Ordinal);

        private static string StripAssemblyQualification(string name)
        {
            var depth = 0;
            for (var index = 0; index < name.Length; index++)
            {
                depth += name[index] switch { '[' => 1, ']' => -1, _ => 0 };
                if (name[index] == ',' && depth == 0)
                    return name[..index];
            }
            return name;
        }

        private static PrimitiveTypeCode GetPrimitiveTypeCode(string? name) => name switch
        {
            "System.Byte" => PrimitiveTypeCode.Byte,
            "System.SByte" => PrimitiveTypeCode.SByte,
            "System.Int16" => PrimitiveTypeCode.Int16,
            "System.UInt16" => PrimitiveTypeCode.UInt16,
            "System.Int32" => PrimitiveTypeCode.Int32,
            "System.UInt32" => PrimitiveTypeCode.UInt32,
            "System.Int64" => PrimitiveTypeCode.Int64,
            "System.UInt64" => PrimitiveTypeCode.UInt64,
            _ => throw new InvalidDataException($"Enum underlying type '{name}' is unsupported."),
        };
    }

    private sealed record DecodedType
    {
        internal DecodedType(
            DecodedTypeKind Kind,
            string? CanonicalName = null,
            string? GenericName = null,
            int GenericIndex = 0,
            bool IsMethodGenericParameter = false,
            bool IsValueType = false,
            DecodedType? ElementType = null,
            ImmutableArray<DecodedType> TypeArguments = default,
            ArrayShape ArrayShape = default,
            DecodedType? ModifierType = null,
            bool IsRequiredModifier = false,
            bool IsSzArray = false,
            byte NullableAnnotation = 0)
        {
            this.Kind = Kind;
            this.CanonicalName = CanonicalName;
            this.GenericName = GenericName;
            this.GenericIndex = GenericIndex;
            this.IsMethodGenericParameter = IsMethodGenericParameter;
            this.IsValueType = IsValueType;
            this.ElementType = ElementType;
            this.TypeArguments = TypeArguments.IsDefault ? ImmutableArray<DecodedType>.Empty : TypeArguments;
            this.ArrayShape = ArrayShape;
            this.ModifierType = ModifierType;
            this.IsRequiredModifier = IsRequiredModifier;
            this.IsSzArray = IsSzArray;
            this.NullableAnnotation = NullableAnnotation;
        }

        internal DecodedTypeKind Kind { get; init; }
        internal string? CanonicalName { get; init; }
        internal string? GenericName { get; init; }
        internal int GenericIndex { get; init; }
        internal bool IsMethodGenericParameter { get; init; }
        internal bool IsValueType { get; init; }
        internal DecodedType? ElementType { get; init; }
        internal ImmutableArray<DecodedType> TypeArguments { get; init; }
        internal ArrayShape ArrayShape { get; init; }
        internal DecodedType? ModifierType { get; init; }
        internal bool IsRequiredModifier { get; init; }
        internal bool IsSzArray { get; init; }
        internal byte NullableAnnotation { get; init; }
    }

    private enum DecodedTypeKind
    {
        Named,
        GenericParameter,
        GenericInstance,
        Array,
        Pointer,
        ByReference,
        Modified,
    }

    private sealed record SignatureContext(
        ImmutableArray<string> TypeParameters,
        ImmutableArray<string> MethodParameters)
    {
        internal static SignatureContext Empty { get; } = new(
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty);
    }

    private sealed record NullableTransform(byte[] Flags, bool IsSingle);
}
