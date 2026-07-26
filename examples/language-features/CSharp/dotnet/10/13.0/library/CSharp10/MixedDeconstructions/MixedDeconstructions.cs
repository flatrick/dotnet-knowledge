namespace CSharpNet10Latest.CSharp10.MixedDeconstructions
{
    public class MixedDeconstruction
    {
        // Before C# 10.0 a deconstruction had to either declare every variable
        // or assign to every one; mixing the two in a single deconstruction was
        // an error. Now an existing variable and a new one may appear together.
        public static int DeclareAndAssign()
        {
            int existing = 0;
            (existing, int fresh) = (1, 2);
            return existing + fresh;
        }

        // The same with var on the newly declared element.
        public static string MixedWithVar()
        {
            string name = string.Empty;
            (name, var count) = ("item", 3);
            return name + count;
        }

        // A discard may take either role.
        public static int WithDiscard()
        {
            int kept = 0;
            (kept, _) = (5, 6);
            return kept;
        }
    }
}
