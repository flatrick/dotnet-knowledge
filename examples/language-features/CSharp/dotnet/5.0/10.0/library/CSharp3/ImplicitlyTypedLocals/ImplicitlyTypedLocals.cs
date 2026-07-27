using System.Collections.Generic;

namespace Net5_CSharp10.CSharp3.ImplicitlyTypedLocals
{
    public class VarSamples
    {
        // var infers the local's type from its initializer. The local is still
        // statically typed; the inferred type is fixed at compile time.
        public static int InferredInt()
        {
            var value = 42;
            return value;
        }

        public static string InferredString()
        {
            var text = "inferred";
            return text.ToUpperInvariant();
        }

        // var earns its keep when the type name is long or cannot be written out.
        public static int CountEntries()
        {
            var lookup = new Dictionary<string, List<int>>();
            lookup.Add("first", new List<int>());
            return lookup.Count;
        }

        public static int SumAll(int[] values)
        {
            var total = 0;
            foreach (var value in values)
            {
                total += value;
            }

            return total;
        }
    }
}
