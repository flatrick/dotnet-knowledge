using System.Collections.Generic;

namespace Net7_CSharp10_Library.CSharp3.AnonymousTypes
{
    public class AnonymousTypeSamples
    {
        // An anonymous type has no writable name, so var is mandatory.
        public static string Describe()
        {
            var point = new { X = 1, Y = 2 };
            return point.X + "," + point.Y;
        }

        // Two anonymous types sharing property names, types, and order compile
        // to one generated type, and the generated Equals compares by value.
        public static bool StructurallyEqual()
        {
            var first = new { Name = "a", Count = 1 };
            var second = new { Name = "a", Count = 1 };
            return first.Equals(second);
        }

        // The properties are read-only; projection is the usual reason to reach
        // for one.
        public static List<string> ProjectNames(string[] names)
        {
            var projected = new List<string>();
            foreach (string name in names)
            {
                var item = new { Original = name, Length = name.Length };
                projected.Add(item.Original + ":" + item.Length);
            }

            return projected;
        }
    }
}
