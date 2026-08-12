namespace Fixtures;

public class GalleryBase;

public interface IMarker;

public sealed class Marker : GalleryBase, IMarker;

public interface IGallery<T>;

public class NullableBase<T>;

public interface INullableMarker<T>;

public sealed class NullableShape<T> : NullableBase<string?>, INullableMarker<Uri?>
    where T : INullableMarker<string?>;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class GalleryAttribute(Type markerType) : Attribute
{
    public Type MarkerType { get; } = markerType;

    public Type? NamedType { get; set; }
}

/// <summary>Exercises metadata signature decoding.</summary>
/// <typeparam name="T">The gallery value type.</typeparam>
[Gallery(typeof(Marker), NamedType = typeof(Uri))]
public unsafe class SignatureGallery<T> : GalleryBase, IGallery<T>
    where T : GalleryBase, IMarker, new()
{
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

    /// <summary>A nested generic API.</summary>
    /// <typeparam name="TNested">The nested unmanaged type.</typeparam>
    public sealed class Nested<TNested>
        where TNested : unmanaged;

    internal sealed class InternalNested;
}
