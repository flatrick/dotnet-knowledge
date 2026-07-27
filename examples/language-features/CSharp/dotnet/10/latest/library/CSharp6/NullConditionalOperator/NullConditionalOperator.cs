using System;

namespace Net10_CSharpLatest_Library.CSharp6.NullConditionalOperator
{
    public class Node
    {
        public Node Next { get; set; }

        public string Name { get; set; }

        public int[] Values { get; set; }
    }

    public class NullConditional
    {
        // ?. yields null instead of throwing when the left operand is null.
        public static string NameOrNull(Node node)
        {
            return node?.Name;
        }

        // Chained: the whole expression short-circuits at the first null, so
        // Next is never dereferenced when node is null.
        public static string ChainedName(Node node)
        {
            return node?.Next?.Name;
        }

        // ?[ ] is the indexer form. The result is lifted to int? because the
        // expression must have somewhere to put "there was no value".
        public static int? FirstValue(Node node)
        {
            return node?.Values?[0];
        }

        // ?? supplies the fallback, which is how the lifted type is usually
        // collapsed back to a plain one.
        public static int CountOrZero(Node node)
        {
            return node?.Values?.Length ?? 0;
        }

        public static event EventHandler Changed;

        // The canonical use: the delegate is read once into a temporary, so a
        // concurrent unsubscribe between the null check and the call cannot
        // turn this into a NullReferenceException.
        public static void RaiseChanged()
        {
            Changed?.Invoke(null, EventArgs.Empty);
        }
    }
}
