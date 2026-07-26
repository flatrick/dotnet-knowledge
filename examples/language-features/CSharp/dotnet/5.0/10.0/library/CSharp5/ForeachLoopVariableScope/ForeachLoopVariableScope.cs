using System;
using System.Collections.Generic;

namespace CSharpNet6_10.CSharp5.ForeachLoopVariableScope
{
    public class LoopCapture
    {
        // Since C# 5.0 the foreach iteration variable is a fresh variable on
        // every pass, so each closure captures that pass's value. Before C# 5.0
        // one variable was shared by the whole loop and every closure observed
        // the final value.
        public static List<Func<int>> CaptureEachElement(int[] values)
        {
            List<Func<int>> captured = new List<Func<int>>();
            foreach (int value in values)
            {
                captured.Add(() => value);
            }

            return captured;
        }

        public static List<int> Invoke(int[] values)
        {
            List<int> results = new List<int>();
            foreach (Func<int> capture in CaptureEachElement(values))
            {
                results.Add(capture());
            }

            return results;
        }

        // A for loop's variable is still declared once and shared across every
        // iteration, so these closures all observe the same variable — the
        // C# 5.0 change deliberately did not touch this case.
        public static List<Func<int>> ForLoopSharesOneVariable(int count)
        {
            List<Func<int>> captured = new List<Func<int>>();
            for (int i = 0; i < count; i++)
            {
                captured.Add(() => i);
            }

            return captured;
        }
    }
}
