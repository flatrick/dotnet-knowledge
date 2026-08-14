using System.Buffers;
using System.Globalization;
using System.Text;

namespace DotNetKnowledge.Mcp.Features.ApiDocs;

internal static class ApiDeclarationId
{
    private const int MaximumSyntaxDepth = 64;
    private const int MaximumArrayRank = 32;

    internal static bool IsCanonicalTypeId(string? value) =>
        IsCanonicalPrefixedId(value, 'T') && IsCanonicalQualifiedTypeName(value.AsSpan(2));

    internal static bool IsCanonicalMemberId(string? value, string typeId)
    {
        if (value is not { Length: > 2 }
            || value[1] != ':'
            || value[0] is not ('M' or 'P' or 'F' or 'E')
            || !HasNoWhitespaceOrControl(value)
            || !IsCanonicalTypeId(typeId))
        {
            return false;
        }

        var owner = typeId.AsSpan(2);
        var body = value.AsSpan(2);
        if (!body.StartsWith(owner, StringComparison.Ordinal)
            || body.Length <= owner.Length + 1
            || body[owner.Length] != '.')
        {
            return false;
        }

        return new MemberIdParser(body[(owner.Length + 1)..], value[0]).Parse();
    }

    internal static bool IsCanonicalNamespaceName(string? value) =>
        value is { Length: > 0 }
        && HasNoWhitespaceOrControl(value)
        && AllSegments(value.AsSpan(), IsIdentifier);

    internal static bool IsCanonicalTypeName(string? value) =>
        value is { Length: > 0 }
        && HasNoWhitespaceOrControl(value)
        && IsCanonicalQualifiedTypeName(value.AsSpan());

    internal static bool IsIdentifier(string segment) => IsIdentifier(segment.AsSpan());

    internal static bool HasNoWhitespaceOrControl(string value)
    {
        var remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var consumed);
            if (status != OperationStatus.Done
                || Rune.IsWhiteSpace(rune)
                || Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control)
            {
                return false;
            }
            remaining = remaining[consumed..];
        }
        return true;
    }

    private static bool IsCanonicalPrefixedId(string? value, char prefix) =>
        value is { Length: > 2 }
        && value[0] == prefix
        && value[1] == ':'
        && HasNoWhitespaceOrControl(value);

    private static bool IsCanonicalQualifiedTypeName(ReadOnlySpan<char> body) =>
        !body.IsEmpty && AllSegments(body, IsCanonicalTypeSegment);

    private static bool AllSegments(
        ReadOnlySpan<char> value,
        Func<ReadOnlySpan<char>, bool> predicate)
    {
        while (true)
        {
            var separator = value.IndexOf('.');
            var segment = separator < 0 ? value : value[..separator];
            if (!predicate(segment))
                return false;
            if (separator < 0)
                return true;
            value = value[(separator + 1)..];
        }
    }

    private static bool IsCanonicalTypeSegment(ReadOnlySpan<char> segment)
    {
        var marker = segment.IndexOf('`');
        if (marker < 0)
            return IsMetadataIdentifier(segment);
        return marker > 0
            && segment[(marker + 1)..].IndexOf('`') < 0
            && IsMetadataIdentifier(segment[..marker])
            && IsCanonicalPositiveInteger(segment[(marker + 1)..]);
    }

    private static bool IsIdentifier(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty
            || !TryReadRune(segment, out var first, out var consumed)
            || !IsIdentifierStart(first))
        {
            return false;
        }

        segment = segment[consumed..];
        while (!segment.IsEmpty)
        {
            if (!TryReadRune(segment, out var rune, out consumed) || !IsIdentifierPart(rune))
                return false;
            segment = segment[consumed..];
        }
        return true;
    }

    // C#-expressible metadata names use the identifier categories below. Compilers also emit
    // angle-bracket/dollar names such as <>c and <Clone>$; those characters are legal in CLR
    // metadata and occur in the pinned documentation IDs, so they are accepted only inside one
    // balanced metadata-name segment. Documentation-ID punctuation remains grammar, not a name.
    private static bool IsMetadataIdentifier(ReadOnlySpan<char> segment)
    {
        if (segment.IsEmpty)
            return false;
        var angleDepth = 0;
        var sawNameContent = false;
        var atStart = true;
        while (!segment.IsEmpty)
        {
            if (segment[0] == '<')
            {
                if (++angleDepth > MaximumSyntaxDepth)
                    return false;
                sawNameContent = true;
                atStart = false;
                segment = segment[1..];
                continue;
            }
            if (segment[0] == '>')
            {
                if (angleDepth == 0)
                    return false;
                angleDepth--;
                segment = segment[1..];
                continue;
            }
            if (segment[0] == '$')
            {
                sawNameContent = true;
                atStart = false;
                segment = segment[1..];
                continue;
            }
            if (!TryReadRune(segment, out var rune, out var consumed)
                || atStart && !IsIdentifierStart(rune)
                || !atStart && !IsIdentifierPart(rune))
            {
                return false;
            }
            sawNameContent = true;
            atStart = false;
            segment = segment[consumed..];
        }
        return sawNameContent && angleDepth == 0;
    }

    private static bool TryReadRune(ReadOnlySpan<char> value, out Rune rune, out int consumed) =>
        Rune.DecodeFromUtf16(value, out rune, out consumed) == OperationStatus.Done;

    private static bool IsIdentifierStart(Rune rune)
    {
        if (rune.Value == '_')
            return true;
        return Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.UppercaseLetter or
            UnicodeCategory.LowercaseLetter or
            UnicodeCategory.TitlecaseLetter or
            UnicodeCategory.ModifierLetter or
            UnicodeCategory.OtherLetter or
            UnicodeCategory.LetterNumber;
    }

    private static bool IsIdentifierPart(Rune rune) =>
        IsIdentifierStart(rune)
        || Rune.GetUnicodeCategory(rune) is
            UnicodeCategory.NonSpacingMark or
            UnicodeCategory.SpacingCombiningMark or
            UnicodeCategory.DecimalDigitNumber or
            UnicodeCategory.ConnectorPunctuation or
            UnicodeCategory.Format;

    private static bool IsCanonicalPositiveInteger(ReadOnlySpan<char> digits) =>
        IsCanonicalPositiveIntegerText(digits)
        && int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out _);

    private static bool IsCanonicalNonnegativeInteger(ReadOnlySpan<char> digits) =>
        digits is ['0']
        || IsCanonicalPositiveInteger(digits);

    private static bool IsCanonicalSignedInteger(ReadOnlySpan<char> digits) =>
        IsCanonicalNonnegativeInteger(digits)
        || digits is ['-', .. var magnitude]
        && IsCanonicalPositiveIntegerText(magnitude)
        && int.TryParse(digits, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _);

    private static bool IsCanonicalPositiveIntegerText(ReadOnlySpan<char> digits) =>
        !digits.IsEmpty
        && digits[0] is >= '1' and <= '9'
        && digits.IndexOfAnyExceptInRange('0', '9') < 0;

    private ref struct MemberIdParser
    {
        private readonly ReadOnlySpan<char> _text;
        private readonly char _category;
        private int _position;

        internal MemberIdParser(ReadOnlySpan<char> text, char category)
        {
            _text = text;
            _category = category;
        }

        internal bool Parse()
        {
            var delimiter = IndexOfTopLevelDelimiter(_text);
            if (delimiter < 0)
                delimiter = _text.Length;
            var name = _text[..delimiter];
            if (!TryValidateMemberName(name, _category, out var simpleName, out var hasMethodArity))
                return false;
            if (hasMethodArity && _category != 'M')
                return false;
            _position = delimiter;

            var hasParameters = false;
            var parameterCount = 0;
            if (TryConsume('('))
            {
                if (_category is not ('M' or 'P') || Peek() == ')')
                    return false;
                hasParameters = true;
                while (true)
                {
                    if (!ParseType(0, allowCustomModifiers: true))
                        return false;
                    parameterCount++;
                    if (TryConsume(','))
                    {
                        if (Peek() == ')' || Peek() == ',')
                            return false;
                        continue;
                    }
                    if (!TryConsume(')'))
                        return false;
                    break;
                }
            }

            if (_category is 'F' or 'E' && hasParameters)
                return false;
            if (TryConsume('~'))
            {
                if (_category != 'M'
                    || hasMethodArity
                    || !hasParameters
                    || parameterCount != 1
                    || !IsConversionName(simpleName)
                    || !ParseType(0, allowCustomModifiers: true))
                {
                    return false;
                }
            }
            return _position == _text.Length;
        }

        private bool ParseType(int depth, bool allowCustomModifiers)
        {
            if (depth >= MaximumSyntaxDepth || _position >= _text.Length)
                return false;

            if (Remaining.StartsWith("=FUNC:".AsSpan(), StringComparison.Ordinal))
            {
                _position += 6;
                if (!ParseType(depth + 1, allowCustomModifiers: false))
                    return false;
                if (TryConsume('('))
                {
                    if (Peek() == ')')
                        return false;
                    while (true)
                    {
                        if (!ParseType(depth + 1, allowCustomModifiers: true))
                            return false;
                        if (TryConsume(','))
                            continue;
                        if (!TryConsume(')'))
                            return false;
                        break;
                    }
                }
            }
            else if (TryConsume('`'))
            {
                TryConsume('`');
                var start = _position;
                while (char.IsAsciiDigit(Peek()))
                    _position++;
                if (!IsCanonicalNonnegativeInteger(_text[start.._position]))
                    return false;
            }
            else if (!ParseNamedType(depth))
            {
                return false;
            }

            while (true)
            {
                if (TryConsume('*'))
                    continue;
                if (Peek() == '[')
                {
                    if (!ParseArrayShape())
                        return false;
                    continue;
                }
                break;
            }
            TryConsume('@');
            if (allowCustomModifiers)
            {
                while (TryConsume('|'))
                {
                    if (!ParseType(depth + 1, allowCustomModifiers: false))
                        return false;
                }
            }
            return true;
        }

        private bool ParseNamedType(int depth)
        {
            while (true)
            {
                var start = _position;
                while (_position < _text.Length && !IsTypeNameDelimiter(_text[_position]))
                    _position++;
                if (start == _position)
                    return false;
                var segment = _text[start.._position];
                var hasArity = segment.Contains('`');
                if (!IsCanonicalTypeSegment(segment))
                    return false;

                if (TryConsume('{'))
                {
                    if (hasArity || Peek() == '}')
                        return false;
                    while (true)
                    {
                        if (!ParseType(depth + 1, allowCustomModifiers: true))
                            return false;
                        if (TryConsume(','))
                        {
                            if (Peek() == '}' || Peek() == ',')
                                return false;
                            continue;
                        }
                        if (!TryConsume('}'))
                            return false;
                        break;
                    }
                }

                if (!TryConsume('.'))
                    return true;
            }
        }

        private bool ParseArrayShape()
        {
            _position++;
            if (TryConsume(']'))
                return true;
            var rank = 0;
            while (true)
            {
                var lowerBoundStart = _position;
                TryConsume('-');
                while (char.IsAsciiDigit(Peek()))
                    _position++;
                if (!IsCanonicalSignedInteger(_text[lowerBoundStart.._position]) || !TryConsume(':'))
                    return false;
                var sizeStart = _position;
                while (char.IsAsciiDigit(Peek()))
                    _position++;
                if (sizeStart != _position
                    && !IsCanonicalPositiveInteger(_text[sizeStart.._position]))
                {
                    return false;
                }
                if (++rank > MaximumArrayRank)
                    return false;
                if (TryConsume(','))
                {
                    if (Peek() == ']')
                        return false;
                    continue;
                }
                return TryConsume(']');
            }
        }

        private ReadOnlySpan<char> Remaining => _text[_position..];

        private char Peek() => _position < _text.Length ? _text[_position] : '\0';

        private bool TryConsume(char character)
        {
            if (Peek() != character)
                return false;
            _position++;
            return true;
        }

        private static bool IsTypeNameDelimiter(char character) =>
            character is '.' or '{' or '}' or '[' or ']' or '*' or '@' or '|' or ',' or ')' or '(' or '~';

        private static int IndexOfTopLevelDelimiter(ReadOnlySpan<char> value)
        {
            Span<char> closers = stackalloc char[MaximumSyntaxDepth];
            var depth = 0;
            for (var index = 0; index < value.Length; index++)
            {
                var character = value[index];
                if (character is '<' or '{' or '[')
                {
                    if (depth == closers.Length)
                        return -2;
                    closers[depth++] = character switch { '<' => '>', '{' => '}', _ => ']' };
                    continue;
                }
                if (character is '>' or '}' or ']')
                {
                    if (depth == 0 || closers[--depth] != character)
                        return -2;
                    continue;
                }
                if (depth == 0 && character is '(' or '~')
                    return index;
            }
            return depth == 0 ? -1 : -2;
        }

        private static bool TryValidateMemberName(
            ReadOnlySpan<char> name,
            char category,
            out ReadOnlySpan<char> simpleName,
            out bool hasMethodArity)
        {
            simpleName = default;
            hasMethodArity = false;
            if (name.IsEmpty)
                return false;
            if (name is "#ctor" or "#cctor")
            {
                simpleName = name;
                return category == 'M';
            }

            var arityMarker = name.LastIndexOf("``".AsSpan(), StringComparison.Ordinal);
            if (arityMarker >= 0)
            {
                if (!IsCanonicalPositiveInteger(name[(arityMarker + 2)..]))
                    return false;
                name = name[..arityMarker];
                hasMethodArity = true;
            }
            if (name.IsEmpty || !ValidateMemberNameCore(name))
                return false;
            var separator = name.LastIndexOf('#');
            simpleName = separator < 0 ? name : name[(separator + 1)..];
            return !simpleName.IsEmpty;
        }

        private static bool ValidateMemberNameCore(ReadOnlySpan<char> name)
        {
            Span<char> closers = stackalloc char[MaximumSyntaxDepth];
            var depth = 0;
            var componentHasContent = false;
            var expectingStart = true;
            for (var index = 0; index < name.Length;)
            {
                var character = name[index];
                if (character is '<' or '{' or '[')
                {
                    if (depth == closers.Length)
                        return false;
                    closers[depth++] = character switch { '<' => '>', '{' => '}', _ => ']' };
                    componentHasContent = true;
                    expectingStart = false;
                    index++;
                    continue;
                }
                if (character is '>' or '}' or ']')
                {
                    if (depth == 0 || closers[--depth] != character)
                        return false;
                    index++;
                    continue;
                }
                if (character == '#' && depth == 0)
                {
                    if (!componentHasContent)
                        return false;
                    componentHasContent = false;
                    expectingStart = true;
                    index++;
                    continue;
                }
                if (character == ',')
                {
                    if (depth == 0 || index == 0 || name[index - 1] is '<' or '{' or '[' or ',')
                        return false;
                    expectingStart = true;
                    index++;
                    continue;
                }
                if (character == '`')
                {
                    if (depth == 0)
                        return false;
                    var start = ++index;
                    if (index < name.Length && name[index] == '`')
                    {
                        index++;
                        start++;
                    }
                    while (index < name.Length && char.IsAsciiDigit(name[index]))
                        index++;
                    if (!IsCanonicalNonnegativeInteger(name[start..index]))
                        return false;
                    componentHasContent = true;
                    expectingStart = false;
                    continue;
                }
                if (character == '$')
                {
                    componentHasContent = true;
                    expectingStart = false;
                    index++;
                    continue;
                }
                if (depth > 0 && character is '#' or '.' or '*' or '@' or ':' or '=' or '|')
                {
                    index++;
                    continue;
                }
                if (!TryReadRune(name[index..], out var rune, out var consumed)
                    || expectingStart && !IsIdentifierStart(rune)
                    || !expectingStart && !IsIdentifierPart(rune))
                {
                    return false;
                }
                componentHasContent = true;
                expectingStart = false;
                index += consumed;
            }
            return depth == 0 && componentHasContent;
        }

        private static bool IsConversionName(ReadOnlySpan<char> name) =>
            name is "op_Implicit" or "op_Explicit" or "op_CheckedImplicit" or "op_CheckedExplicit";
    }
}
