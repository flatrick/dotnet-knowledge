namespace CSharpFw48Cs80Unsafe.CSharp1.UnsafeCodeAndPointers
{
    // This project sets <AllowUnsafeBlocks>true</AllowUnsafeBlocks>; the mainline
    // projects deliberately do not, which is why this row lives here. Without the
    // switch the compiler reports CS0227.
    public unsafe class PointerArithmetic
    {
        // Taking the address of a local and dereferencing it.
        public static int DereferenceLocal()
        {
            int value = 42;
            int* pointer = &value;
            return *pointer;
        }

        // fixed pins a managed array so a pointer into it stays valid.
        public static int SumFixed(int[] values)
        {
            int total = 0;
            fixed (int* start = values)
            {
                int* current = start;
                for (int i = 0; i < values.Length; i++)
                {
                    total += *current;
                    current++;
                }
            }

            return total;
        }

        // stackalloc allocates from the stack frame rather than the managed heap.
        public static int SumStackAllocated()
        {
            int* buffer = stackalloc int[3];
            buffer[0] = 1;
            buffer[1] = 2;
            buffer[2] = 3;

            int total = 0;
            for (int i = 0; i < 3; i++)
            {
                total += buffer[i];
            }

            return total;
        }

        public static int SizeOfInt()
        {
            return sizeof(int);
        }
    }

    // A fixed-size buffer embeds the array directly in the struct layout.
    public unsafe struct PacketHeader
    {
        public fixed byte Signature[8];
    }
}
