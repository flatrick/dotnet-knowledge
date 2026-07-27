using System;

namespace Net10_CSharp13_Library.CSharp12.LambdaOptionalParameters
{
    public class OptionalParameters
    {
        // A lambda parameter may have a default value, so the delegate it
        // produces carries the default with it. Before C# 12.0 defaults were
        // only expressible on methods and local functions.
        public static int WithDefault()
        {
            var increment = (int value, int by = 1) => value + by;
            return increment(41);
        }

        // The natural delegate type generated for it carries the default, so
        // calling through the variable honors it.
        public static int ExplicitArgument()
        {
            var increment = (int value, int by = 1) => value + by;
            return increment(40, 2);
        }

        // params is allowed on a lambda too, from the same version.
        public static int WithParams()
        {
            var total = (params int[] values) =>
            {
                int sum = 0;
                foreach (int value in values)
                {
                    sum += value;
                }

                return sum;
            };
            return total(1, 2, 3);
        }
    }
}
