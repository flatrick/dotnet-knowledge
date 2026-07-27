using System.Runtime.CompilerServices;

namespace Net10_CSharpLatest_Unsafe.CSharp9.SkipLocalsInit
{
    // SkipLocalsInit tells the compiler to omit the .locals init flag, so the
    // runtime does not zero a method's stack space before it runs. It is an
    // attribute rather than a pointer construct, yet it still requires
    // /unsafe — the compiler rejects it otherwise (CS0227), because skipping
    // the zeroing is only observable, and only sound, in code that can read
    // uninitialized stack.
    [SkipLocalsInit]
    public class FastPath
    {
        // Every local in this type is left unzeroed. The saving matters only
        // for large stackallocs in hot paths; ordinary code should not use it.
        public static int SumStack()
        {
            System.Span<int> buffer = stackalloc int[64];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = i;
            }

            int total = 0;
            foreach (int value in buffer)
            {
                total += value;
            }

            return total;
        }
    }

    public class Scoped
    {
        // The attribute may also be applied to a single method rather than a
        // whole type, which is the narrower and safer choice.
        [SkipLocalsInit]
        public static int SingleMethod()
        {
            System.Span<byte> buffer = stackalloc byte[16];
            buffer[0] = 1;
            return buffer[0];
        }
    }
}
