namespace Net6_CSharp10.CSharp7.Discards
{
    public class Measurement
    {
        public int Width { get; }

        public int Height { get; }

        public Measurement(int width, int height)
        {
            Width = width;
            Height = height;
        }

        public void Deconstruct(out int width, out int height)
        {
            width = Width;
            height = Height;
        }
    }

    public class DiscardSamples
    {
        // A discard is a write-only placeholder: no variable is declared, so
        // nothing is allocated and the name cannot be read back.
        public static bool IsNumeric(string text)
        {
            return int.TryParse(text, out _);
        }

        // Discarding one half of a deconstruction.
        public static int WidthOnly(Measurement measurement)
        {
            var (width, _) = measurement;
            return width;
        }

        public static int SecondOfPair()
        {
            (_, int second) = Pair();
            return second;
        }

        // A standalone discard assignment states "this result is deliberately
        // ignored", which a bare expression statement cannot express.
        public static int IgnoreResult()
        {
            _ = Compute();
            return 0;
        }

        private static (int, int) Pair()
        {
            return (1, 2);
        }

        private static int Compute()
        {
            return 42;
        }
    }
}
