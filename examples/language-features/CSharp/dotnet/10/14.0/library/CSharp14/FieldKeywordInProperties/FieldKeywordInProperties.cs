namespace Net10_CSharp14_Library.CSharp14.FieldKeywordInProperties
{
    public class Person
    {
        // The `field` keyword names the compiler-generated backing field inside
        // an accessor, so a property can add validation without the author
        // declaring a backing field by hand and keeping the two in sync.
        public string Name
        {
            get;
            set => field = value ?? string.Empty;
        }

        // Only one accessor needs a body; the other stays auto-implemented.
        public int Age
        {
            get => field;
            set => field = value < 0 ? 0 : value;
        }

        // An initializer still applies to the same generated field.
        public string Region { get; set => field = value?.ToUpperInvariant(); } = "GLOBAL";

        public Person(string name)
        {
            Name = name;
        }
    }

    public class Usage
    {
        // The setter normalizes null to empty, so this returns 0 rather than
        // throwing.
        public static int NullBecomesEmpty()
        {
            return new Person(null).Name.Length;
        }
    }
}
