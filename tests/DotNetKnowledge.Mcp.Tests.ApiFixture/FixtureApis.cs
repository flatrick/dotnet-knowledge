namespace Fixtures;

#nullable enable

public class NullableContextSource
{
    public string? Value => null;
}

#nullable disable

public class DeclaringContextProbe
{
    public class Nested
    {
        public string Value = string.Empty;
    }
}

public class ModuleContextProbe
{
    public string Value = string.Empty;
}

public class AccessorProbeType;

public class AccessorSourceType;

#nullable restore

public class GalleryBase;

public interface IMarker;

public sealed class Marker : GalleryBase, IMarker;

public interface IGallery<T>;

public class NullableBase<T>;

public interface INullableMarker<T>;

public interface IHierarchy<T>;

public sealed class HierarchyShape :
    NullableBase<(string Name, Uri? Link)>,
    IHierarchy<GenericOuter<string>.GenericInner<Uri?>>;

public class GenericOuter<TOuter>
{
    public class GenericInner<TInner>;
}

public class ConstraintGallery<TStruct, TUnmanaged, TClass, TNew>
    where TStruct : struct
    where TUnmanaged : unmanaged
    where TClass : class
    where TNew : class, new();

public abstract class AccessorBase
{
    public abstract int AbstractProperty { get; }

    public virtual int VirtualProperty { get; set; }

    public abstract event EventHandler? AbstractEvent;

    public virtual event EventHandler? VirtualEvent
    {
        add { }
        remove { }
    }

    public event Action? OtherEvent
    {
        add { }
        remove { }
    }
}

public class AccessorOverride : AccessorBase
{
    public override int AbstractProperty => 0;

    public sealed override int VirtualProperty { get; set; }

    public override event EventHandler? AbstractEvent
    {
        add { }
        remove { }
    }

    public sealed override event EventHandler? VirtualEvent
    {
        add { }
        remove { }
    }
}

public interface IImplicitAccessors
{
    int ImplicitProperty { get; }

    int PrivateSetterProperty { get; }

    event EventHandler? ImplicitEvent;

    void ImplicitMethod();
}

// A method implementing an interface carries the same Final|Virtual|NewSlot the accessors do, and
// the same absence of a modifier. Equals, Dispose and GetEnumerator are the shapes this reaches in
// practice, so the fixture uses them rather than an invented name.
public sealed class ImplicitMethods : IEquatable<ImplicitMethods>, IDisposable
{
    public bool Equals(ImplicitMethods? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => Equals(obj as ImplicitMethods);

    public override int GetHashCode() => 0;

    public void Dispose()
    {
    }
}

public class ImplicitMethodBase
{
    public virtual void ImplicitMethod()
    {
    }
}

// The guard: an override that ALSO satisfies an interface reuses its base slot, so it is not a new
// slot and must still read as an override. Collapsing on Final|Virtual alone would swallow it.
public class OverrideThatImplements : ImplicitMethodBase, IImplicitAccessors
{
    public override void ImplicitMethod()
    {
    }

    public int ImplicitProperty => 0;

    public int PrivateSetterProperty => 0;

    public event EventHandler? ImplicitEvent;
}

// Implicitly implementing an interface emits Final|Virtual|NewSlot on the accessor that
// participates, while an added setter stays plain. Both declarations are ordinary C# carrying no
// modifier: the reader rendered them "sealed override", and rejected the mixed pair outright.
public class ImplicitAccessors : IImplicitAccessors
{
    public int ImplicitProperty { get; set; }

    public int PrivateSetterProperty { get; private set; }

    public event EventHandler? ImplicitEvent;

    public void ImplicitMethod()
    {
    }
}

public interface IStaticAccessors
{
    static abstract int AbstractProperty { get; }

    static virtual int VirtualProperty => 0;

    static abstract event EventHandler? AbstractEvent;

    static virtual event EventHandler? VirtualEvent
    {
        add { }
        remove { }
    }
}

public sealed class ConstructorProbe(System.Text.Encoding source)
{
    private readonly System.Text.Encoding source = source;

    public int ConstructorReturnSource(System.Text.Encoding value) =>
        ReferenceEquals(source, value) ? 1 : 0;
}

public sealed class NullableShape<T> : NullableBase<string?>, INullableMarker<Uri?>
    where T : INullableMarker<string?>;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class GalleryAttribute(Type markerType) : Attribute
{
    public Type MarkerType { get; } = markerType;

    public Type? NamedType { get; set; }
}

[Flags]
public enum FixtureOptions
{
    None = 0,
    First = 1,
    Second = 2,
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class OptionsAttribute(FixtureOptions options) : Attribute
{
    public FixtureOptions Options { get; } = options;
}

[AttributeUsage(AttributeTargets.Class)]
public sealed class GenericTagAttribute<T> : Attribute;

/// <summary>Exercises metadata signature decoding.</summary>
/// <typeparam name="T">The gallery value type.</typeparam>
[Gallery(typeof(Marker), NamedType = typeof(Uri))]
[Options(FixtureOptions.First | FixtureOptions.Second)]
[GenericTag<int>]
public unsafe class SignatureGallery<T> : GalleryBase, IGallery<T>
    where T : GalleryBase, IMarker, new()
{
    static SignatureGallery() { }

    private (string Name, T Value)? borrowed;

    /// <summary>Creates a gallery.</summary>
    /// <param name="capacity">The initial capacity.</param>
    public SignatureGallery(int capacity)
    {
        Count = capacity;
    }

    /// <summary>Gets the fixture numbers.</summary>
    public static readonly int[] Numbers = [];

    /// <summary>Stores an unmanaged pointer.</summary>
    public int* Pointer;

    /// <summary>Gets or sets the item count.</summary>
    public int Count { get; set; }

    /// <summary>Gets a value whose setter is not visible API.</summary>
    public string PublicGetter { get; private set; } = string.Empty;

    /// <summary>Gets or initializes a value.</summary>
    public string Initial { get; init; } = string.Empty;

    /// <summary>Gets the text at an index.</summary>
    /// <param name="index">The item index.</param>
    /// <value>The formatted index.</value>
    public string this[int index] => index.ToString();

    /// <summary>Occurs when the gallery changes.</summary>
    public event EventHandler? Changed;

    public AccessorProbeType AccessorProbe => null!;

    public AccessorSourceType AccessorSource => null!;

    /// <summary>Borrows a tuple containing the value.</summary>
    /// <param name="value">The value to borrow.</param>
    /// <returns>A borrowed tuple.</returns>
    public ref readonly (string Name, T Value)? Borrow(in T value)
    {
        borrowed = (nameof(value), value);
        return ref borrowed;
    }

    /// <summary>Transforms a marker into URI slots.</summary>
    /// <param name="input">The marker.</param>
    /// <param name="pointer">The source pointer.</param>
    /// <param name="current">The current value.</param>
    /// <param name="label">The value label.</param>
    /// <param name="result">The resulting value.</param>
    /// <returns>The URI slots.</returns>
    /// <remarks>Preserves every signature position needed by the fixture.</remarks>
    [Gallery(typeof(Marker), NamedType = typeof(Uri))]
    public Uri?[] Transform(
        Marker input,
        int* pointer,
        ref T current,
        in string label,
        out T result)
    {
        _ = input;
        _ = pointer;
        _ = label;
        result = current;
        return [];
    }

    /// <summary>Returns a constrained value.</summary>
    /// <typeparam name="TResult">The stream type.</typeparam>
    /// <param name="value">The stream value.</param>
    /// <returns>The same value.</returns>
    public TResult Constrain<TResult>(TResult value)
        where TResult : Stream, IDisposable, new() => value;

    public TResult NullableConstrain<TResult>(TResult value)
        where TResult : INullableMarker<string?> => value;

    public TReference NullableClass<TReference>(TReference value)
        where TReference : class? => value;

    public void Collect(params string[] values) => _ = values;

    public int[] ArrayShapeProbe(int[] values, string marker)
    {
        _ = marker;
        return values;
    }

    public int[,] MultiDimensionalArrayProbe(int[,] matrix, string[,,] cube)
    {
        _ = cube;
        return matrix;
    }

    public int[,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,] RankThirtyTwoArrayProbe(
        int[,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,,] values) => values;

    public int[] NestedByRefProbe(int[] values, string marker)
    {
        _ = marker;
        return values;
    }

    public ((string First, string Second) Pair, string Tail) NestedTuple(
        ((string First, string Second) Pair, string Tail) value) => value;

    public List<string?> NullableTransformProbe(List<string> value)
    {
        _ = value;
        return [];
    }

    public string? NullableTransformSource(List<string> value)
    {
        _ = value;
        return null;
    }

    public int DuplicateA(int value) => value;

    public int DuplicateB(int value) => value;

    public static void Xctor() { }

    public void Xcctor() { }

    public static void Ycctor(int value) => _ = value;

    public void Zctor<TValue>() { }

    public int BogusApi(int value) => value;

    public SignatureGallery<T> IncrementApi(SignatureGallery<T> value) => value;

    public static SignatureGallery<T> AdditionApi(SignatureGallery<T> value) => value;

    public static byte ExplicitApi() => 0;

    public static int IncrementReturnSource(SignatureGallery<T> value)
    {
        _ = value;
        return 0;
    }

    public static SignatureGallery<T> WrappedOperatorSource(
        List<SignatureGallery<T>> left,
        int right)
    {
        _ = left;
        _ = right;
        return null!;
    }

    public static SignatureGallery<T> RefOperatorSource(
        ref SignatureGallery<T> left,
        int right)
    {
        _ = right;
        return left;
    }

    public static SignatureGallery<T> RefParameterOperatorSource(
        ref int left,
        SignatureGallery<T> right)
    {
        _ = left;
        return right;
    }

    public static ref byte RefConversionReturnSource(SignatureGallery<T> value)
    {
        _ = value;
        throw new NotSupportedException();
    }

    public static SignatureGallery<Marker> WrongConstructedOperatorSource(
        SignatureGallery<Marker> value) => value;

    /// <summary>Exercises constructed nested generic identities.</summary>
    /// <param name="value">The nested value.</param>
    /// <returns>A differently constructed nested value.</returns>
    public GenericOuter<string>.GenericInner<int> UseNested(
        GenericOuter<Uri>.GenericInner<long> value) => new();

    [Obsolete]
    public string PublicOnly() => string.Empty;

    protected string ProtectedOnly() => string.Empty;

    protected internal string ProtectedInternalOnly() => string.Empty;

    internal string InternalOnly() => string.Empty;

    private string PrivateOnly() => string.Empty;

    private protected string PrivateProtectedOnly() => string.Empty;

    /// <summary>Adds two galleries.</summary>
    /// <param name="left">The left gallery.</param>
    /// <param name="right">The right gallery.</param>
    /// <returns>The selected gallery.</returns>
    public static SignatureGallery<T> operator +(
        SignatureGallery<T> left,
        SignatureGallery<T> right) => left;

    /// <summary>Converts a gallery to its count.</summary>
    public static implicit operator int(SignatureGallery<T> value) => value.Count;

    /// <summary>Converts a count to a gallery.</summary>
    public static explicit operator SignatureGallery<T>(int value) => new(value);

    /// <summary>Converts a gallery to a byte.</summary>
    public static explicit operator byte(SignatureGallery<T> value) => (byte)value.Count;

    /// <summary>Converts a gallery to a checked byte.</summary>
    public static explicit operator checked byte(SignatureGallery<T> value) =>
        checked((byte)value.Count);

    /// <summary>Increments a gallery.</summary>
    public static SignatureGallery<T> operator ++(SignatureGallery<T> value) => value;

    /// <summary>Increments a gallery in a checked context.</summary>
    public static SignatureGallery<T> operator checked ++(SignatureGallery<T> value) => value;

    /// <summary>Decrements a gallery.</summary>
    public static SignatureGallery<T> operator --(SignatureGallery<T> value) => value;

    /// <summary>Decrements a gallery in a checked context.</summary>
    public static SignatureGallery<T> operator checked --(SignatureGallery<T> value) => value;

    public static bool operator true(SignatureGallery<T> value) => value is not null;

    public static bool operator false(SignatureGallery<T> value) => value is null;

    /// <summary>A nested generic API.</summary>
    /// <typeparam name="TNested">The nested unmanaged type.</typeparam>
    public sealed class Nested<TNested>
        where TNested : unmanaged;

    internal sealed class InternalNested;
}

/// <summary>
/// Shapes that real Microsoft assemblies contain and repository-authored fixtures did not.
/// Every member here is legal C# that the compiler accepts; each one previously aborted the whole
/// corpus build, which cost the coverage of an entire package rather than of one member.
/// </summary>
public readonly struct InteropShapeGallery
{
    private readonly int _value;

    /// <summary>Creates a gallery.</summary>
    /// <param name="value">The value.</param>
    public InteropShapeGallery(int value) => _value = value;

    /// <summary>The value.</summary>
    public int Value => _value;

    // 'in' is the one by-reference form an operator may take: 'ref', 'out' and 'ref readonly' are
    // all CS0631, and a by-reference return does not parse. Roslyn's own Workspaces assembly ships
    // these, so rejecting them rejected the package.
    /// <summary>Compares two galleries.</summary>
    /// <param name="left">The left gallery.</param>
    /// <param name="right">The right gallery.</param>
    /// <returns>True when equal.</returns>
    public static bool operator ==(in InteropShapeGallery left, in InteropShapeGallery right) =>
        left.Value == right.Value;

    /// <summary>Compares two galleries for inequality.</summary>
    /// <param name="left">The left gallery.</param>
    /// <param name="right">The right gallery.</param>
    /// <returns>True when unequal.</returns>
    public static bool operator !=(in InteropShapeGallery left, in InteropShapeGallery right) =>
        !(left == right);

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is InteropShapeGallery other && other.Value == Value;

    /// <inheritdoc/>
    public override int GetHashCode() => Value;

    // The argument's type is an enum defined in another assembly, so its underlying type cannot be
    // found among this assembly's own type definitions. [EditorBrowsable] is ubiquitous in shipping
    // libraries.
    /// <summary>A member hidden from editor completion.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public void Hidden()
    {
    }

    // A nullable-annotated signature carrying an external enum as a type argument. Value types take
    // no NullableAttribute entry, so a decoder that expects one for every position runs out.
    /// <summary>Looks up days by name.</summary>
    /// <returns>The lookup, or null.</returns>
    public System.Collections.Generic.Dictionary<string, System.DayOfWeek>? Lookup() => null;
}
