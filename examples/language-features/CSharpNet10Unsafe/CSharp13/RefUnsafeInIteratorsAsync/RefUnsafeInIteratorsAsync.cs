using System.Collections.Generic;
using System.Threading.Tasks;

namespace CSharpNet10Unsafe.CSharp13.RefUnsafeInIteratorsAsync
{
    public class RefInIterators
    {
        // Iterators and async methods forbade ref locals and unsafe blocks
        // outright, because their bodies are rewritten into a state machine and
        // a reference cannot be stored in a field. C# 13.0 relaxes that: the
        // constructs are allowed wherever they do not have to survive a yield
        // or an await.
        public static IEnumerable<int> Doubled(int[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                // A ref local inside an iterator, used and finished with before
                // the yield — so it never needs to live in the state machine.
                ref int slot = ref values[i];
                int doubled = slot * 2;
                yield return doubled;
            }
        }

        // An unsafe block inside an iterator, likewise confined to one step.
        // Note the explicit block: an `unsafe` MODIFIER on an iterator method
        // does not establish the context in the rewritten body (CS0214), so the
        // block form is the one that works here.
        public static IEnumerable<int> FirstBytes(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
            {
                int value;
                unsafe
                {
                    fixed (byte* pointer = data)
                    {
                        value = pointer[i];
                    }
                }

                yield return value;
            }
        }

        // The same relaxation applies to async methods.
        public static async Task<int> SumAsync(int[] values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; i++)
            {
                ref int slot = ref values[i];
                total += slot;
                await Task.Yield();
            }

            return total;
        }
    }
}
