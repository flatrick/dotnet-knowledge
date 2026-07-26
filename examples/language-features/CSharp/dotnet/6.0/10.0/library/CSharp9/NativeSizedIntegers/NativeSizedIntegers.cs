namespace CSharpNet6_10.CSharp9.NativeSizedIntegers
{
    public class NativeIntegers
    {
        // nint and nuint are the platform's pointer-width integers: 32 bits on
        // a 32-bit process, 64 on a 64-bit one. They are ordinary numeric types
        // needing no unsafe context.
        public static nint Add(nint left, nint right)
        {
            return left + right;
        }

        public static nuint AddUnsigned(nuint left, nuint right)
        {
            return left + right;
        }

        // The width is a run-time property, so the maximum is not a constant.
        public static nint Maximum()
        {
            return nint.MaxValue;
        }

        // Conversion from int is implicit; the reverse narrows and is explicit.
        public static nint FromInt(int value)
        {
            return value;
        }

        public static int ToInt(nint value)
        {
            return (int)value;
        }

        // In metadata these are IntPtr and UIntPtr carrying a marker
        // attribute, which is why they interoperate with pointer-sized APIs.
        // (sizeof(nint) is unavailable here: it needs an unsafe context,
        // because the size is not a compile-time constant.)
        public static bool IsIntPtrUnderneath()
        {
            nint value = 0;
            return value.GetType() == typeof(System.IntPtr);
        }
    }
}
