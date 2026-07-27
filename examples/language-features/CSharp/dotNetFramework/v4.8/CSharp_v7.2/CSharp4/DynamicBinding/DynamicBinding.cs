using System.Collections.Generic;

namespace Net48_CSharp7_2_Library.CSharp4.DynamicBinding
{
    public class Greeter
    {
        public string Greet(string name)
        {
            return "hello " + name;
        }
    }

    public class DynamicSamples
    {
        // dynamic defers member binding to run time: the compiler emits a call
        // site here instead of resolving Greet now.
        public static string CallResolvedAtRuntime()
        {
            dynamic greeter = new Greeter();
            return greeter.Greet("world");
        }

        // Operators are dispatched dynamically too.
        public static int AddDynamically(dynamic left, dynamic right)
        {
            return left + right;
        }

        // A dynamic value converts implicitly to any type; the conversion is
        // checked when it runs, not when it compiles.
        public static int ConvertImplicitly()
        {
            dynamic value = 41;
            int converted = value;
            return converted + 1;
        }

        // dynamic is erased to object in metadata, so it stores anywhere object
        // stores.
        public static List<object> Store()
        {
            dynamic value = "text";
            List<object> items = new List<object>();
            items.Add(value);
            return items;
        }
    }
}
