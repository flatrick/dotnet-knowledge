namespace Net7_CSharp10.CSharp10.ParameterlessStructConstructors
{
    // C# 10.0 allows a struct to declare a parameterless constructor and to
    // give its fields initializers. Both were errors before, because every
    // struct was required to have a zeroing default.
    public struct Options
    {
        // A field initializer on a struct — also new in C# 10.0.
        public int Retries = 3;

        public string Region { get; set; }

        public Options()
        {
            Region = "global";
        }

        public Options(string region)
        {
            Region = region;
        }
    }

    public class Usage
    {
        // new Options() runs the declared constructor, so Retries is 3.
        public static int ViaNew()
        {
            Options options = new Options();
            return options.Retries;
        }

        // default does NOT run it: it produces the all-zero value, so Retries
        // is 0. That difference is the trap the feature introduces, and the
        // reason to be careful adding a parameterless constructor to a struct.
        public static int ViaDefault()
        {
            Options options = default;
            return options.Retries;
        }
    }
}
