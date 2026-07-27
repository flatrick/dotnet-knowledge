namespace Net10_CSharp11_Library.CSharp8.ReadonlyMembers
{
    // readonly may now be applied to individual members of a struct, rather
    // than only to the whole type. A readonly member promises not to mutate
    // the instance, which lets the compiler skip defensive copies when the
    // struct is reached through a readonly field or an in parameter.
    public struct Measurement
    {
        private int _samples;

        public double Total { get; set; }

        public Measurement(double total, int samples)
        {
            Total = total;
            _samples = samples;
        }

        public readonly int Samples
        {
            get { return _samples; }
        }

        public readonly double Average()
        {
            return _samples == 0 ? 0 : Total / _samples;
        }

        // Not readonly: it mutates, and the compiler enforces that difference.
        public void Add(double value)
        {
            Total += value;
            _samples++;
        }

        public readonly override string ToString()
        {
            return "Measurement(" + _samples + ")";
        }
    }

    public class Usage
    {
        // Calling a readonly member through an in parameter copies nothing.
        public static double AverageOf(in Measurement measurement)
        {
            return measurement.Average();
        }
    }
}
