using System.Collections.Immutable;
using System.Globalization;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

namespace DotNetKnowledge.Mcp.Features.ApiDocs.Corpus;

public static class MetadataApiReader
{
    private const int SchemaVersion = 1;

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
            return new ApiCorpus(SchemaVersion, decoder.ReadTypes());
        }
        catch (BadImageFormatException exception)
        {
            throw new InvalidDataException("The stream does not contain valid ECMA-335 metadata.", exception);
        }
    }

    private sealed class Decoder(MetadataReader reader)
    {
        private readonly MetadataTypeProvider typeProvider = new(reader);

        internal ApiCorpusType[] ReadTypes()
        {
            var types = new List<ApiCorpusType>();
            foreach (var handle in reader.TypeDefinitions)
            {
                var definition = reader.GetTypeDefinition(handle);
                if (reader.GetString(definition.Name) == "<Module>" || !IsVisibleType(handle))
                    continue;

                types.Add(ReadType(handle, definition));
            }

            return types.OrderBy(item => item.EcmaId, StringComparer.Ordinal).ToArray();
        }

        private ApiCorpusType ReadType(TypeDefinitionHandle handle, TypeDefinition definition)
        {
            var context = CreateTypeContext(definition);
            var nullableContext = ReadNullableContext(definition.GetCustomAttributes(), null);
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
                .OrderBy(item => item, StringComparer.Ordinal)
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

        private IEnumerable<ApiCorpusMember> ReadMembers(
            TypeDefinitionHandle typeHandle,
            TypeDefinition definition,
            SignatureContext typeContext,
            byte? nullableContext)
        {
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
                if (IsVisible(method.Attributes & MethodAttributes.MemberAccessMask))
                    yield return ReadMethod(typeHandle, method, typeContext, nullableContext);
            }

            foreach (var handle in definition.GetProperties())
            {
                var property = reader.GetPropertyDefinition(handle);
                if (TryGetPropertyAccessibility(property, out var accessibility))
                    yield return ReadProperty(typeHandle, property, accessibility, typeContext, nullableContext);
            }

            foreach (var handle in definition.GetEvents())
            {
                var eventDefinition = reader.GetEventDefinition(handle);
                if (TryGetEventAccessibility(eventDefinition, out var accessibility))
                    yield return ReadEvent(typeHandle, eventDefinition, accessibility, typeContext, nullableContext);
            }

            foreach (var handle in definition.GetFields())
            {
                var field = reader.GetFieldDefinition(handle);
                if (IsVisible(field.Attributes & FieldAttributes.FieldAccessMask))
                    yield return ReadField(typeHandle, field, typeContext, nullableContext);
            }
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
            var rawName = reader.GetString(method.Name);
            var isConstructor = rawName is ".ctor" or ".cctor";
            var isOperator = rawName.StartsWith("op_", StringComparison.Ordinal);
            var kind = isConstructor ? "constructor" : isOperator ? "operator" : "method";
            var displayName = isConstructor
                ? StripArity(reader.GetString(reader.GetTypeDefinition(typeHandle).Name))
                : GetMethodDisplayName(rawName, returnType, returnTupleNames);
            if (!isConstructor && !isOperator && methodParameters.Length > 0)
                displayName += $"<{string.Join(", ", methodParameters)}>";

            var modifiers = GetMethodModifiers(method.Attributes);
            var returnPrefix = GetReturnPrefix(returnType, returnParameterAttributes);
            var returnText = isConstructor
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
            var name = reader.GetString(property.Name);
            var displayName = isIndexer ? $"this[{string.Join(", ", parameterTexts)}]" : name;
            var staticText = IsStaticAccessor(accessors.Getter, accessors.Setter) ? "static " : string.Empty;
            var accessorText = RenderPropertyAccessors(accessors, accessibility, context);
            ValidateModifiers(
                returnType,
                ByReferenceModifierTypes,
                $"return type of property '{name}'",
                requireByReference: true);
            var returnPrefix = GetReturnPrefix(returnType, 0);
            var returnDisplay = RenderCSharp(UnwrapByReference(returnType), tupleNames);
            var csharpSignature = $"{accessibility} {staticText}{returnPrefix}{returnDisplay} {displayName} {{ {accessorText} }}";
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
            var staticText = IsStaticAccessor(accessors.Adder, accessors.Remover) ? "static " : string.Empty;
            var name = reader.GetString(eventDefinition.Name);
            var display = RenderCSharp(eventType, ReadTupleNames(eventDefinition.GetCustomAttributes()));

            return new ApiCorpusMember(
                $"E:{GetEcmaTypeName(typeHandle)}.{EscapeMemberName(name)}",
                name,
                "event",
                $"{accessibility} {staticText}event {display} {name};",
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
            }

            return result;
        }

        private string DecodeStructuralType(
            EntityHandle handle,
            SignatureContext context,
            byte[]? nullableFlags,
            byte? nullableContext,
            string position)
        {
            var type = ApplyNullable(
                DecodeEntityType(handle, context),
                nullableFlags,
                nullableContext);
            ValidateModifiers(type, NoModifierTypes, position);
            return RenderCSharp(type, null);
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
                    parts.Add(parameterNullable is [2, ..] ? "class?" : "class");
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

                var applicationType = attributeType.EndsWith("Attribute", StringComparison.Ordinal)
                    ? attributeType[..^"Attribute".Length]
                    : attributeType;
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

        private static string RenderAttributeArgument(
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

            if (argumentValue is ImmutableArray<CustomAttributeTypedArgument<DecodedType>> elements)
            {
                return $"new[] {{ {string.Join(", ", elements.Select(item => RenderAttributeArgument(item.Type, item.Value, argumentTypeNames)))} }}";
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
            _ => throw new InvalidDataException($"Attribute type handle '{handle.Kind}' is unsupported."),
        };

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
            return TryGetMostVisibleAccessibility([accessors.Getter, accessors.Setter, .. accessors.Others], out accessibility);
        }

        private bool TryGetEventAccessibility(EventDefinition eventDefinition, out string accessibility)
        {
            var accessors = eventDefinition.GetAccessors();
            return TryGetMostVisibleAccessibility(
                [accessors.Adder, accessors.Remover, accessors.Raiser, .. accessors.Others],
                out accessibility);
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

        private bool IsStaticAccessor(params MethodDefinitionHandle[] handles) => handles
            .Where(handle => !handle.IsNil)
            .Select(handle => reader.GetMethodDefinition(handle))
            .Any(method => (method.Attributes & MethodAttributes.Static) != 0);

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
            var conversionReturn = reader.GetString(method.Name) is "op_Implicit" or "op_Explicit"
                ? $"~{RenderEcmaType(signature.ReturnType)}"
                : string.Empty;
            return $"M:{GetEcmaTypeName(typeHandle)}.{name}{parameters}{conversionReturn}";
        }

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

        private byte? ReadNullableContext(CustomAttributeHandleCollection handles, byte? fallback)
        {
            foreach (var handle in handles)
            {
                var attribute = reader.GetCustomAttribute(handle);
                if (GetAttributeTypeName(attribute) != "System.Runtime.CompilerServices.NullableContextAttribute")
                    continue;
                var value = attribute.DecodeValue(typeProvider);
                if (value.FixedArguments.Length == 1 && value.FixedArguments[0].Value is byte flag)
                    return flag;
                throw new InvalidDataException("NullableContextAttribute had an unsupported encoding.");
            }
            return fallback;
        }

        private byte[]? ReadNullableFlags(CustomAttributeHandleCollection? handles)
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
                    return [single];
                if (value.FixedArguments[0].Value is ImmutableArray<CustomAttributeTypedArgument<DecodedType>> values)
                    return values.Select(item => (byte)item.Value!).ToArray();
                throw new InvalidDataException("NullableAttribute had an unsupported encoding.");
            }
            return null;
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

        private static DecodedType ApplyNullable(DecodedType type, byte[]? flags, byte? context)
        {
            var index = 0;
            return ApplyNullable(type, flags, context, ref index);
        }

        private static DecodedType ApplyNullable(
            DecodedType type,
            byte[]? flags,
            byte? context,
            ref int index)
        {
            if (type.Kind is DecodedTypeKind.ByReference or DecodedTypeKind.Pointer or DecodedTypeKind.Modified)
                return type with { ElementType = ApplyNullable(type.ElementType!, flags, context, ref index) };

            var annotation = flags is not null && index < flags.Length ? flags[index++] : context ?? 0;
            if (type.Kind == DecodedTypeKind.Array)
            {
                return type with
                {
                    NullableAnnotation = annotation,
                    ElementType = ApplyNullable(type.ElementType!, flags, context, ref index),
                };
            }

            if (type.Kind == DecodedTypeKind.GenericInstance)
            {
                var arguments = ImmutableArray.CreateBuilder<DecodedType>(type.TypeArguments.Length);
                foreach (var argument in type.TypeArguments)
                    arguments.Add(ApplyNullable(argument, flags, context, ref index));
                return type with { NullableAnnotation = annotation, TypeArguments = arguments.MoveToImmutable() };
            }

            return type with { NullableAnnotation = annotation };
        }

        private static string RenderCSharp(DecodedType type, IReadOnlyList<string?>? tupleNames)
        {
            var tupleIndex = 0;
            return RenderCSharp(type, tupleNames, ref tupleIndex);
        }

        private static string RenderCSharp(
            DecodedType type,
            IReadOnlyList<string?>? tupleNames,
            ref int tupleIndex)
        {
            switch (type.Kind)
            {
                case DecodedTypeKind.ByReference:
                case DecodedTypeKind.Modified:
                    return RenderCSharp(type.ElementType!, tupleNames, ref tupleIndex);
                case DecodedTypeKind.Pointer:
                    return RenderCSharp(type.ElementType!, tupleNames, ref tupleIndex) + "*";
                case DecodedTypeKind.Array:
                {
                    if (type.ArrayShape.Rank < 1
                        || type.ArrayShape.Sizes.Length > 0
                        || type.ArrayShape.LowerBounds.Length > 0)
                    {
                        throw new InvalidDataException(
                            "An array signature with explicit sizes or lower bounds cannot be rendered as C#.");
                    }
                    var element = RenderCSharp(type.ElementType!, tupleNames, ref tupleIndex);
                    var suffix = type.ArrayShape.Rank == 1
                        ? "[]"
                        : $"[{new string(',', type.ArrayShape.Rank - 1)}]";
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
                        var elements = new List<string>();
                        foreach (var argument in ExpandTupleArguments(type))
                        {
                            var rendered = RenderCSharp(argument, tupleNames, ref tupleIndex);
                            var tupleName = tupleNames is not null && tupleIndex < tupleNames.Count
                                ? tupleNames[tupleIndex]
                                : null;
                            tupleIndex++;
                            elements.Add(string.IsNullOrEmpty(tupleName) ? rendered : $"{rendered} {tupleName}");
                        }
                        return $"({string.Join(", ", elements)})" + NullableSuffix(type);
                    }

                    var genericName = RenderNamedType(type.CanonicalName!);
                    var arguments = new string[type.TypeArguments.Length];
                    for (var index = 0; index < type.TypeArguments.Length; index++)
                        arguments[index] = RenderCSharp(type.TypeArguments[index], tupleNames, ref tupleIndex);
                    return $"{genericName}<{string.Join(", ", arguments)}>" + NullableSuffix(type);
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
            DecodedTypeKind.Array when type.ArrayShape.Rank == 1 => RenderEcmaType(type.ElementType!) + "[]",
            DecodedTypeKind.Array => RenderEcmaType(type.ElementType!) + RenderEcmaArrayShape(type.ArrayShape),
            DecodedTypeKind.GenericParameter => type.IsMethodGenericParameter
                ? $"``{type.GenericIndex}"
                : $"`{type.GenericIndex}",
            DecodedTypeKind.GenericInstance =>
                $"{StripArityFromQualifiedName(type.CanonicalName!)}{{{string.Join(",", type.TypeArguments.Select(RenderEcmaType))}}}",
            DecodedTypeKind.Named => type.CanonicalName!,
            _ => throw new InvalidDataException($"Signature type kind '{type.Kind}' is unsupported."),
        };

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
            if (foundModifier
                && requiredUnderlyingCanonicalName is not null
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

        private static string GetMethodDisplayName(
            string metadataName,
            DecodedType returnType,
            IReadOnlyList<string?>? tupleNames)
        {
            if (!OperatorNames.TryGetValue(metadataName, out var operatorName))
                return metadataName;
            if (metadataName is "op_Implicit" or "op_Explicit" or "op_CheckedExplicit")
                return $"{operatorName} {RenderCSharp(UnwrapByReference(returnType), tupleNames)}";
            return operatorName;
        }

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
            if (type.CanonicalName == "System.AttributeTargets")
                return PrimitiveTypeCode.Int32;

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
}
