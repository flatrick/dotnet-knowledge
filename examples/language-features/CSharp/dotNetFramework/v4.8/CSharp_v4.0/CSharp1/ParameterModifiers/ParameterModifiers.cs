namespace Net48_CSharp4_Library.CSharp1.ParameterModifiers
{
    public class Statistics
    {
        // params: callable as Sum(1, 2, 3) or Sum(new int[] { 1, 2, 3 }).
        public static int Sum(params int[] values)
        {
            int total = 0;
            for (int i = 0; i < values.Length; i++)
            {
                total += values[i];
            }

            return total;
        }

        // out: the caller need not initialize, the callee must assign before returning.
        public static void MinMax(int[] values, out int min, out int max)
        {
            min = values[0];
            max = values[0];
            for (int i = 1; i < values.Length; i++)
            {
                if (values[i] < min)
                {
                    min = values[i];
                }

                if (values[i] > max)
                {
                    max = values[i];
                }
            }
        }

        // ref: passed in already initialized, modified in place.
        public static void Double(ref int value)
        {
            value *= 2;
        }
    }
}
