namespace Net10_CSharp12_Library.CSharp12.PrimaryConstructors
{
    // C# 9.0 gave records a primary constructor. C# 12.0 extends it to every
    // class and struct — but without the record behavior: no properties are
    // generated, and no value equality. The parameters are simply in scope for
    // the whole body.
    public class Repository(string connectionString, int retries)
    {
        // A parameter may be captured by a member, which is what makes it
        // available after construction.
        private readonly string _connectionString = connectionString;

        public int Retries
        {
            get { return retries; }
        }

        public string Describe()
        {
            return _connectionString + ":" + retries;
        }
    }

    // A struct may have one too, and a second constructor must chain to it.
    public struct Range(int start, int end)
    {
        public Range(int end)
            : this(0, end)
        {
        }

        public int Length
        {
            get { return end - start; }
        }
    }

    // Note the difference from a record: no Equals, no ToString listing the
    // members, no deconstruction. Only the constructor is generated.
    public class Usage
    {
        public static string Build()
        {
            return new Repository("server", 3).Describe();
        }

        public static int Measure()
        {
            return new Range(5).Length;
        }
    }
}
