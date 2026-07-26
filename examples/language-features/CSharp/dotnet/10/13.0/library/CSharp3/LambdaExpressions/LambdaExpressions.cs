using System;
using System.Collections.Generic;

namespace CSharpNet10Latest.CSharp3.LambdaExpressions
{
    public class LambdaSamples
    {
        // Expression lambda: the body is a single expression.
        public static Func<int, int> Square()
        {
            return value => value * value;
        }

        // Statement lambda: the body is a block and needs explicit returns.
        public static Func<int, string> Classify()
        {
            return value =>
            {
                if (value < 0)
                {
                    return "negative";
                }

                return "non-negative";
            };
        }

        // Parameter types may be written out when inference is not enough.
        public static Func<int, int, int> Add()
        {
            return (int left, int right) => left + right;
        }

        // No parameters, and a capture of an enclosing local.
        public static Func<int> Incrementer(int start)
        {
            int current = start;
            return () => current + 1;
        }

        public static List<int> FilterOdd(List<int> values)
        {
            return values.FindAll(value => value % 2 != 0);
        }
    }
}
