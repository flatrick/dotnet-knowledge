using System.Collections.Generic;

namespace Net10_CSharpLatest_Library.CSharp14.ExtensionMethodsAndProperties
{
    // C# 3.0 extension methods put `this` on the first parameter, which allowed
    // methods only. C# 14.0 introduces an extension BLOCK, which names the
    // receiver once and can then declare properties and static members too.
    public static class SequenceExtensions
    {
        extension<T>(IEnumerable<T> source)
        {
            // An extension PROPERTY — impossible before C# 14.0.
            public bool IsEmpty
            {
                get
                {
                    foreach (T unused in source)
                    {
                        return false;
                    }

                    return true;
                }
            }

            // An extension method inside the same block, with the receiver
            // taken from the block header rather than a `this` parameter.
            public int CountItems()
            {
                int count = 0;
                foreach (T unused in source)
                {
                    count++;
                }

                return count;
            }
        }
    }

    public class Usage
    {
        public static bool EmptyCheck()
        {
            return new List<int>().IsEmpty;
        }

        public static int Count()
        {
            return new List<int> { 1, 2, 3 }.CountItems();
        }
    }
}
