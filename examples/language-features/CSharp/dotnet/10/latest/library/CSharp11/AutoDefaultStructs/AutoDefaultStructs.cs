namespace Net10_CSharpLatest_Library.CSharp11.AutoDefaultStructs
{
    // Before C# 11.0 a struct constructor had to definitely assign every field,
    // or the compiler reported an error. Now any field the constructor leaves
    // alone is initialized to its default automatically — and the compiler
    // warns about nothing, because the behavior is defined.
    public struct Reading
    {
        public int Sensor;

        public double Value;

        public string Label;

        // Assigns only one of the three fields. Sensor and Label are
        // auto-defaulted to 0 and null.
        public Reading(double value)
        {
            Value = value;
        }

        // Assigning all three remains perfectly ordinary.
        public Reading(int sensor, double value, string label)
        {
            Sensor = sensor;
            Value = value;
            Label = label;
        }
    }

    public class Usage
    {
        public static int UnassignedFieldIsDefault()
        {
            Reading reading = new Reading(1.5);
            return reading.Sensor;
        }

        public static bool UnassignedReferenceIsNull()
        {
            Reading reading = new Reading(1.5);
            return reading.Label == null;
        }
    }
}
