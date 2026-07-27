using System;

namespace Net10_CSharp10_Library.CSharp11.NumericIntPtr
{
    // This row demonstrates numeric IntPtr behavior as it is available when
    // compiling against reference assemblies that advertise the capability.
    // Runtime verification: CSharp11.NumericIntPtr
    // It cannot demonstrate a C# 10-to-11 boundary inside this corpus.
    //
    // C# 11 made nint an alias for System.IntPtr. The compiler enables that
    // behavior when the target framework's reference assemblies define the
    // RuntimeFeature.NumericIntPtr capability. It does not use LangVersion as
    // the only switch. The SDK 7 compiler accepts this source against net7.0
    // at C# 11, while the SDK 10 compiler accepts it against net7.0 and
    // net10.0 even when LangVersion is lowered to 10.
    //
    // Compiled in isolation with that compiler, this source fails against
    // net6.0 with CS0266 for the implicit conversion, CS0019 for multiplication,
    // and CS9135 for the constant patterns. It compiles cleanly against
    // net10.0 at both C# 10 and C# 11.
    //
    // The examples below therefore prove that IntPtr is numeric in this target,
    // but not which historical compiler or language pin first accepted it.
    public static class NumericPointerSized
    {
        // An int implicitly converts to IntPtr as it does to the nint alias.
        public static IntPtr FromConstant()
        {
            IntPtr value = 42;
            return value;
        }

        // IntPtr participates directly in the predefined numeric operators.
        public static IntPtr Multiply(IntPtr left, IntPtr right)
        {
            return left * right;
        }

        // Numeric constants can be matched directly against an IntPtr input.
        public static string Classify(IntPtr value)
        {
            return value switch
            {
                0 => "zero",
                1 => "one",
                _ => "other"
            };
        }
    }
}
