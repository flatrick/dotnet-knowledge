using System;

namespace CSharpNet10Latest.CSharp11.NumericIntPtr
{
    public class NumericPointerSized
    {
        // C# 9.0 introduced nint as a distinct language type layered over
        // IntPtr. C# 11.0 unified them: nint IS System.IntPtr, and IntPtr
        // gained the numeric operators, so no conversion sits between them.
        public static bool SameType()
        {
            return typeof(nint) == typeof(IntPtr);
        }

        // Arithmetic directly on IntPtr, which C# 9.0 required nint for.
        public static IntPtr AddPointers(IntPtr left, IntPtr right)
        {
            return left + right;
        }

        // The two spellings are interchangeable, with no cast.
        public static nint Mixed(IntPtr value)
        {
            nint native = value;
            return native + 1;
        }

        public static IntPtr BackAgain(nint value)
        {
            IntPtr pointer = value;
            return pointer;
        }
    }
}
