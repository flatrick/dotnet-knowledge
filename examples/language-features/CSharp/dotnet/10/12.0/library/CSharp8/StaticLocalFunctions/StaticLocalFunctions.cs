namespace Net10_CSharp12_Library.CSharp8.StaticLocalFunctions
{
    public class StaticLocals
    {
        private const int DefaultScale = 2;

        // A static local function cannot capture locals or parameters from its
        // enclosing scope; static members and constants stay reachable. The
        // modifier turns an unintended capture into a compile error, which is
        // what it is for — a non-static local function that never escapes
        // already allocates nothing.
        public static int Factorial(int n)
        {
            static int Compute(int value)
            {
                return value <= 1 ? 1 : value * Compute(value - 1);
            }

            return Compute(n);
        }

        // Everything varying is passed in; the constant is reached directly,
        // which a static local function is still allowed to do.
        public static int SumScaled(int[] values, int scale)
        {
            static int Scale(int value, int factor)
            {
                return value * factor * DefaultScale;
            }

            int total = 0;
            foreach (int value in values)
            {
                total += Scale(value, scale);
            }

            return total;
        }

        // A non-static local function may still capture, and is the right
        // choice when capture is what you want.
        public static int SumWithCapture(int[] values)
        {
            int total = 0;

            void Add(int value)
            {
                total += value;
            }

            foreach (int value in values)
            {
                Add(value);
            }

            return total;
        }
    }
}
