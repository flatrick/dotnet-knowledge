namespace Net48_CSharp1_Unsafe.CSharp1.UnsafeCodeAndPointers
{
    // This project sets <AllowUnsafeBlocks>true</AllowUnsafeBlocks>; the mainline
    // projects deliberately do not, which is why this row lives here. Without the
    // switch the compiler reports CS0227.
    //
    // Everything below is the C# 1.0 unsafe surface: pointer types, address-of,
    // dereference, pointer arithmetic, ->, void*, fixed and stackalloc. Fixed-size
    // buffers (public fixed byte Signature[8]) look like they belong here and do
    // not - they arrived in C# 2.0, and at <LangVersion>1</LangVersion> the
    // compiler rejects them with CS8022. They are shown by the C# 7.3
    // FixedBufferIndexing row instead.
    public unsafe class PointerArithmetic
    {
        // Taking the address of a local and dereferencing it. A local is a fixed
        // variable - its storage cannot move - so no pinning is needed.
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

        // A string is the other built-in type fixed knows how to pin: the pointer
        // addresses the character data directly, with no copy.
        public static int CountCharacters(string text)
        {
            int count = 0;
            fixed (char* start = text)
            {
                char* current = start;
                while (*current != '\0')
                {
                    count++;
                    current++;
                }
            }

            return count;
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

        // void* is the untyped pointer: any pointer converts to it implicitly, and
        // back only through an explicit cast.
        public static int RoundTripThroughVoidPointer()
        {
            int value = 7;
            int* typed = &value;
            void* untyped = typed;
            return *(int*)untyped;
        }

        // sizeof reports the unmanaged size of a type. In C# 1.0 it is an unsafe-
        // only operator; C# 2.0 later allowed it on the primitive types outside an
        // unsafe context.
        public static int SizeOfInt()
        {
            return sizeof(int);
        }
    }

    public struct Point
    {
        public int X;
        public int Y;
    }

    public unsafe class PointerMemberAccess
    {
        // -> reads a member through a pointer: p->X is shorthand for (*p).X. The
        // parameter is a value parameter of struct type, so it already lives in
        // the stack frame and needs no fixed statement.
        public static int Sum(Point point)
        {
            Point* pointer = &point;
            return pointer->X + pointer->Y;
        }

        // Pointer subtraction yields the element count between two pointers, not
        // the byte distance.
        public static long DistanceInElements(int[] values)
        {
            fixed (int* start = values)
            {
                int* end = start + values.Length;
                return end - start;
            }
        }
    }
}
