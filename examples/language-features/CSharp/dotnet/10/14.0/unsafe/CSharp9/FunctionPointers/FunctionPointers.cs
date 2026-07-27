namespace Net10_CSharp14_Unsafe.CSharp9.FunctionPointers
{
    public unsafe class FunctionPointerSamples
    {
        // A function pointer is the raw calli the runtime has always had, now
        // expressible in C#. Unlike a delegate it allocates nothing and carries
        // no target object, which is why it may point only at a static method.
        public static int Apply(delegate*<int, int> operation, int value)
        {
            return operation(value);
        }

        private static int Double(int value)
        {
            return value * 2;
        }

        // &Method takes the address of a static method.
        public static int CallThroughPointer()
        {
            delegate*<int, int> pointer = &Double;
            return Apply(pointer, 21);
        }

        // The calling convention may be stated when interoperating with native
        // code; the unmanaged form suppresses the managed calling sequence.
        public static int UnmanagedConvention(delegate* unmanaged<int, int> operation, int value)
        {
            return operation(value);
        }

        // A delegate does the same job with an allocation and an instance
        // target, which is the trade being made here.
        public static int ViaDelegate(System.Func<int, int> operation, int value)
        {
            return operation(value);
        }
    }
}
