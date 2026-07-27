namespace Net6_CSharp10.CSharp8.UnmanagedGenericStructs
{
    // Before C# 8.0 a constructed generic struct was never considered
    // unmanaged, even when its type argument was. Now Pair<int> satisfies an
    // unmanaged constraint, because every field is unmanaged once T is.
    public struct Pair<T>
        where T : unmanaged
    {
        public T First;
        public T Second;

        public Pair(T first, T second)
        {
            First = first;
            Second = second;
        }
    }

    public class Constraints
    {
        public static bool Accepts<T>(T value)
            where T : unmanaged
        {
            return value.Equals(value);
        }

        // The point of the row: a constructed generic struct is accepted here.
        public static bool PassConstructedStruct()
        {
            Pair<int> pair = new Pair<int>(1, 2);
            return Accepts(pair);
        }

        public static bool PassPrimitive()
        {
            return Accepts(42);
        }
    }
}
