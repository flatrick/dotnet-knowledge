namespace CSharpFw48Cs73.CSharp2.StaticClasses
{
    // A static class cannot be instantiated, inherited from, or used as a type
    // argument; the compiler enforces that every member is static.
    public static class MathHelpers
    {
        public const double GoldenRatio = 1.618033988749895d;

        private static int _callCount;

        public static int CallCount
        {
            get { return _callCount; }
        }

        public static int Square(int value)
        {
            _callCount++;
            return value * value;
        }

        public static int Clamp(int value, int low, int high)
        {
            _callCount++;
            if (value < low)
            {
                return low;
            }

            return value > high ? high : value;
        }
    }
}
